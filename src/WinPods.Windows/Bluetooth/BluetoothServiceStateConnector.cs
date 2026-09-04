using System.Runtime.Versioning;
using WinPods.Core.Abstractions;
using static WinPods.Windows.Bluetooth.NativeMethods;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// Win32 の <c>BluetoothSetServiceState</c> で A2DP / HFP の接続・切断を行う。
/// </summary>
/// <remarks>
/// <para>
/// 「ワンクリック接続」の実体はこれ。Windows の設定アプリが行っているのと同じことを
/// プロファイル単位で実行する。対象デバイスはペアリング済みである必要がある。
/// </para>
/// <para>
/// 失敗したときは原因の切り分けができるよう、Win32 のエラーコードを
/// <see cref="AudioProfileOperationResult.Detail"/> に載せて返す。
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class BluetoothServiceStateConnector : IAudioProfileConnector
{
    /// <summary>
    /// 接続・切断の対象にするプロファイル。既定では A2DP と HFP の両方。
    /// </summary>
    public IReadOnlyList<Guid> ServiceClasses { get; init; } =
    [
        AudioSinkServiceClass,
        HandsFreeServiceClass,
    ];

    /// <inheritdoc />
    public Task<AudioProfileOperationResult> ConnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SetServiceState(deviceAddress, enable: true), cancellationToken);

    /// <inheritdoc />
    public Task<AudioProfileOperationResult> DisconnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SetServiceState(deviceAddress, enable: false), cancellationToken);

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

    private AudioProfileOperationResult SetServiceState(ulong deviceAddress, bool enable)
    {
        var failures = new List<string>();
        bool deviceFound = false;
        bool anySucceeded = false;
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
            uint flags = enable ? BLUETOOTH_SERVICE_ENABLE : BLUETOOTH_SERVICE_DISABLE;

            foreach (Guid serviceClass in ServiceClasses)
            {
                Guid service = serviceClass;
                uint result = BluetoothSetServiceState(radio, ref info, ref service, flags);

                if (result == ERROR_SUCCESS)
                {
                    anySucceeded = true;
                }
                else
                {
                    failures.Add($"{DescribeService(serviceClass)} {BluetoothRadios.DescribeError(result)}");
                }
            }

            // デバイスが見つかったラジオで処理したので探索を終了する。
            return true;
        });

        if (radioCount == 0)
        {
            return AudioProfileOperationResult.Failure("Bluetooth アダプタが見つかりません");
        }

        if (!deviceFound)
        {
            return AudioProfileOperationResult.Failure(
                $"デバイスが見つかりません {BluetoothRadios.DescribeError(lastLookupError)}");
        }

        return anySucceeded
            ? AudioProfileOperationResult.Success()
            : AudioProfileOperationResult.Failure(string.Join(" / ", failures));
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
