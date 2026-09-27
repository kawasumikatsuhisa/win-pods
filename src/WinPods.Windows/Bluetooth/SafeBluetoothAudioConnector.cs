using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using WinPods.Core.Abstractions;
using static WinPods.Windows.Bluetooth.NativeMethods;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// Windows の Bluetooth 構成を変更せず、安全な範囲だけで接続状態を扱うコネクタ。
/// </summary>
/// <remarks>
/// <para>
/// <c>BluetoothSetServiceState</c> は一時的な接続 / 切断 API ではなく、
/// Bluetooth サービスに対応するドライバーの有効化 / 無効化を行う API である。
/// そのため本クラスでは使用しない。
/// </para>
/// <para>
/// Windows の公開 API には、任意の Classic Bluetooth A2DP Sink へ確実に
/// 接続・切断する API がない。接続時はデバイスを開いて SDP を更新することで
/// Windows の自動接続を促し、実際に接続されたかを確認する。
/// 切断は構成を壊す可能性のある代替手段を使わず、未サポートとして返す。
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class SafeBluetoothAudioConnector : IAudioProfileConnector
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    /// <inheritdoc />
    public async Task<AudioProfileOperationResult> ConnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default)
    {
        if (await IsConnectedAsync(deviceAddress, cancellationToken).ConfigureAwait(false) == true)
        {
            return AudioProfileOperationResult.Success();
        }

        try
        {
            // BluetoothDevice を開き、キャッシュを使わずサービスを問い合わせる。
            // これはデバイス / ドライバー構成を変更せず、ACL リンクと Windows の
            // 自動接続を促すための best-effort 操作。
            using BluetoothDevice? device = await BluetoothDevice
                .FromBluetoothAddressAsync(deviceAddress)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (device is null)
            {
                return AudioProfileOperationResult.Failure("Bluetooth デバイスを開けませんでした");
            }

            _ = await device
                .GetRfcommServicesAsync(BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            DateTime deadline = DateTime.UtcNow + ConnectTimeout;

            do
            {
                if (await IsConnectedAsync(deviceAddress, cancellationToken).ConfigureAwait(false) == true)
                {
                    return AudioProfileOperationResult.Success();
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return AudioProfileOperationResult.Failure(
                "Windows の公開 API では接続を開始できませんでした。Bluetooth 設定から接続してください");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AudioProfileOperationResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<AudioProfileOperationResult> DisconnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(AudioProfileOperationResult.Failure(
            "安全に切断できる公開 API がないため、Bluetooth 設定から切断してください"));
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
                        return false;
                    }

                    connected = info.fConnected;
                    return true;
                });

                return connected;
            },
            cancellationToken);
}
