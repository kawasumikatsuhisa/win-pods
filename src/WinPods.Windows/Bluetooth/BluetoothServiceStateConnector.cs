using System.Globalization;
using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using WinPods.Core.Abstractions;
using static WinPods.Windows.Bluetooth.NativeMethods;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// ペアリング済みオーディオデバイスの接続・切断を行う。
/// </summary>
/// <remarks>
/// <para>
/// クラシック Bluetooth のオーディオプロファイル (A2DP / HFP) を能動的に接続する
/// 公開 WinRT API は存在しないため、Win32 の <c>BluetoothSetServiceState</c> を使う。
/// </para>
/// <para>
/// <b>この API の危険性</b>: <c>BLUETOOTH_SERVICE_DISABLE</c> は一時的な切断ではなく、
/// そのデバイスからオーディオプロファイルの<b>登録を外す</b>操作である。通常は
/// <c>BLUETOOTH_SERVICE_ENABLE</c> で戻せるが、ENABLE が ERROR_INVALID_PARAMETER(87)
/// を返す環境 (Windows 11 build 26200 で確認) では戻せず、Windows がそのデバイスを
/// オーディオ機器として扱えなくなり再ペアリングが必要になる。
/// そのため切断は、ENABLE が成功する = 元に戻せると確認できた場合しか実行しない。
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class BluetoothServiceStateConnector : IAudioProfileConnector
{
    /// <summary>プロファイル操作のあと、リンクが確立するのを待つ時間。</summary>
    private static readonly TimeSpan LinkSettleTimeout = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan LinkSettlePollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>プロファイルの状態変更の結果。</summary>
    /// <param name="AllSucceeded">対象プロファイルすべてで ERROR_SUCCESS だったか。</param>
    /// <param name="Detail">プロファイル別の結果 (UI 表示・切り分け用)。</param>
    private readonly record struct ServiceStateOutcome(bool AllSucceeded, string Detail);

    /// <summary>
    /// 接続・切断の対象にするプロファイル。既定では A2DP と HFP の両方。
    /// </summary>
    public IReadOnlyList<Guid> ServiceClasses { get; init; } =
    [
        AudioSinkServiceClass,
        HandsFreeServiceClass,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// ENABLE は登録を追加する方向の操作なので、失敗しても状態を壊さない。
    /// 効く手法が環境によって違うため、順に試して最初に実際に接続できたものを採る。
    /// </remarks>
    public async Task<AudioProfileOperationResult> ConnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default)
    {
        var attempts = new List<string>();

        // 1) ラジオハンドルを指定してプロファイルを有効化する (標準的な方法)
        ServiceStateOutcome perRadio = await Task.Run(
            () => SetServiceStatePerRadio(deviceAddress, enable: true), cancellationToken).ConfigureAwait(false);
        attempts.Add($"radio {perRadio.Detail}");

        if (await WaitForConnectionAsync(deviceAddress, expected: true, cancellationToken).ConfigureAwait(false))
        {
            return AudioProfileOperationResult.Success();
        }

        // 2) hRadio に NULL を渡す (すべてのローカルラジオを対象にする呼び方)
        ServiceStateOutcome allRadios = await Task.Run(
            () => SetServiceStateOnAllRadios(deviceAddress, enable: true), cancellationToken).ConfigureAwait(false);
        attempts.Add($"null-radio {allRadios.Detail}");

        if (await WaitForConnectionAsync(deviceAddress, expected: true, cancellationToken).ConfigureAwait(false))
        {
            return AudioProfileOperationResult.Success();
        }

        // 3) SDP 問い合わせで ACL リンクを張らせ、オーディオドライバの自動接続を促す
        attempts.Add($"sdp {await ForceLinkAsync(deviceAddress, cancellationToken).ConfigureAwait(false)}");

        if (await WaitForConnectionAsync(deviceAddress, expected: true, cancellationToken).ConfigureAwait(false))
        {
            return AudioProfileOperationResult.Success();
        }

        return AudioProfileOperationResult.Failure(string.Join(" / ", attempts));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 元に戻せることを確認できない限り実行しない。クラスのコメントを参照。
    /// </remarks>
    public async Task<AudioProfileOperationResult> DisconnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default)
    {
        // 先に ENABLE を試し、この環境でプロファイルを再登録できるかを確かめる。
        // ENABLE は追加方向の操作なので、ここで実行しても壊れない。
        ServiceStateOutcome probe = await Task.Run(
            () => SetServiceStatePerRadio(deviceAddress, enable: true), cancellationToken).ConfigureAwait(false);

        if (!probe.AllSucceeded)
        {
            return AudioProfileOperationResult.Failure(
                "この環境では切断できません。オーディオプロファイルを元に戻せず、" +
                $"再ペアリングが必要になるためです ({probe.Detail})");
        }

        ServiceStateOutcome disable = await Task.Run(
            () => SetServiceStatePerRadio(deviceAddress, enable: false), cancellationToken).ConfigureAwait(false);

        return await WaitForConnectionAsync(deviceAddress, expected: false, cancellationToken).ConfigureAwait(false)
            ? AudioProfileOperationResult.Success()
            : AudioProfileOperationResult.Failure(disable.Detail);
    }

    /// <inheritdoc />
    public Task<bool?> IsConnectedAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                bool? connected = null;

                BluetoothRadios.ForEach(radio =>
                {
                    if (BluetoothRadios.GetDeviceInfo(radio, deviceAddress, out BLUETOOTH_DEVICE_INFO info) != ERROR_SUCCESS)
                    {
                        // このラジオでは見つからなかっただけかもしれないので次を試す。
                        return false;
                    }

                    connected = info.fConnected;
                    return true;
                });

                return connected;
            },
            cancellationToken);

    /// <summary>ラジオを列挙し、デバイスが属するラジオでプロファイルの状態を変更する。</summary>
    private ServiceStateOutcome SetServiceStatePerRadio(ulong deviceAddress, bool enable)
    {
        ServiceStateOutcome outcome = default;
        bool deviceFound = false;
        uint lastLookupError = ERROR_NOT_FOUND;

        int radioCount = BluetoothRadios.ForEach(radio =>
        {
            uint lookupError = BluetoothRadios.GetDeviceInfo(radio, deviceAddress, out BLUETOOTH_DEVICE_INFO info);

            if (lookupError != ERROR_SUCCESS)
            {
                lastLookupError = lookupError;
                return false;
            }

            deviceFound = true;
            outcome = ApplyServiceStates(radio, ref info, enable);
            return true;
        });

        if (radioCount == 0)
        {
            return new ServiceStateOutcome(false, "アダプタなし");
        }

        return deviceFound
            ? outcome
            : new ServiceStateOutcome(false, $"デバイス未検出 {BluetoothRadios.DescribeError(lastLookupError)}");
    }

    /// <summary>hRadio に NULL を渡して、すべてのローカルラジオを対象に操作する。</summary>
    private ServiceStateOutcome SetServiceStateOnAllRadios(ulong deviceAddress, bool enable)
    {
        uint lookupError = BluetoothRadios.GetDeviceInfo(IntPtr.Zero, deviceAddress, out BLUETOOTH_DEVICE_INFO info);

        return lookupError != ERROR_SUCCESS
            ? new ServiceStateOutcome(false, $"デバイス未検出 {BluetoothRadios.DescribeError(lookupError)}")
            : ApplyServiceStates(IntPtr.Zero, ref info, enable);
    }

    private ServiceStateOutcome ApplyServiceStates(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, bool enable)
    {
        uint flags = enable ? BLUETOOTH_SERVICE_ENABLE : BLUETOOTH_SERVICE_DISABLE;
        var details = new List<string>(ServiceClasses.Count);
        bool allSucceeded = true;

        foreach (Guid serviceClass in ServiceClasses)
        {
            Guid service = serviceClass;
            uint result = BluetoothSetServiceState(radio, ref info, ref service, flags);

            if (result != ERROR_SUCCESS)
            {
                allSucceeded = false;
            }

            details.Add($"{DescribeService(serviceClass)} {BluetoothRadios.DescribeError(result)}");
        }

        return new ServiceStateOutcome(allSucceeded, string.Join(", ", details));
    }

    /// <summary>
    /// SDP (RFCOMM サービス) を問い合わせて ACL リンクを張らせる。
    /// オーディオプロファイルが「デバイスが見えたら繋ぐ」設定になっている場合、
    /// これをきっかけに Windows 側が接続することがある。
    /// </summary>
    private static async Task<string> ForceLinkAsync(ulong deviceAddress, CancellationToken cancellationToken)
    {
        try
        {
            using BluetoothDevice? device = await BluetoothDevice
                .FromBluetoothAddressAsync(deviceAddress)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (device is null)
            {
                return "デバイスを開けません";
            }

            RfcommDeviceServicesResult services = await device
                .GetRfcommServicesAsync(BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{services.Error} (services={services.Services.Count}, status={device.ConnectionStatus})");
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>期待した接続状態になるまで短時間待つ。</summary>
    private async Task<bool> WaitForConnectionAsync(
        ulong deviceAddress,
        bool expected,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + LinkSettleTimeout;

        do
        {
            if (await IsConnectedAsync(deviceAddress, cancellationToken).ConfigureAwait(false) == expected)
            {
                return true;
            }

            await Task.Delay(LinkSettlePollInterval, cancellationToken).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return await IsConnectedAsync(deviceAddress, cancellationToken).ConfigureAwait(false) == expected;
    }

    private static string DescribeService(Guid serviceClass)
    {
        if (serviceClass == AudioSinkServiceClass)
        {
            return "A2DP";
        }

        if (serviceClass == HandsFreeServiceClass)
        {
            return "HFP";
        }

        return serviceClass == HeadsetServiceClass ? "HSP" : serviceClass.ToString();
    }
}
