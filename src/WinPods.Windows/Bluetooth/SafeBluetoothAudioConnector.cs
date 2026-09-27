using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using WinPods.Core.Abstractions;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// Bluetooth オーディオドライバーのエンドポイントを使って接続 / 切断する。
/// </summary>
/// <remarks>
/// <para>
/// RFCOMM のサービス問い合わせは ACL リンクを一時的に張るだけで、A2DP 接続を
/// 確立しない。従来実装はその瞬間の fConnected を成功扱いしていたため、
/// 「接続したように見えてすぐ切れる」状態になっていた。
/// </para>
/// <para>
/// 現在は Bluetooth オーディオエンドポイントの KS filter に
/// KSPROPERTY_ONESHOT_RECONNECT / DISCONNECT を送り、エンドポイントが ACTIVE に
/// なったかどうかで実際のオーディオ接続を判定する。
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class SafeBluetoothAudioConnector : IAudioProfileConnector
{
    // Windows は接続要求を受け付けてから A2DP endpoint を ACTIVE にするまで
    // かなり時間がかかる場合がある。実機では 15 秒付近で接続完了するケースが
    // あったため、余裕を持って待機し、タイムアウト境界でも最終確認する。
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FinalActivationGrace = TimeSpan.FromSeconds(2);

    public async Task<AudioProfileOperationResult> ConnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? deviceName = await GetDeviceNameAsync(deviceAddress, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return AudioProfileOperationResult.Failure("Bluetooth デバイス名を取得できませんでした");
            }

            BluetoothAudioEndpointController.Endpoint? endpoint =
                BluetoothAudioEndpointController.FindBestEndpoint(deviceName);

            if (endpoint is null)
            {
                return AudioProfileOperationResult.Failure(
                    $"{deviceName} の Bluetooth オーディオエンドポイントが見つかりませんでした");
            }

            if (endpoint.IsActive)
            {
                return AudioProfileOperationResult.Success();
            }

            if (!BluetoothAudioEndpointController.Connect(endpoint.Id))
            {
                return AudioProfileOperationResult.Failure(
                    "Bluetooth オーディオドライバーへ接続要求を送れませんでした");
            }

            DateTime deadline = DateTime.UtcNow + ConnectTimeout;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id))
                {
                    return AudioProfileOperationResult.Success();
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            // タイムアウト境界で Windows 側の状態反映と競合するケースを吸収する。
            await Task.Delay(FinalActivationGrace, cancellationToken).ConfigureAwait(false);
            if (BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id))
            {
                return AudioProfileOperationResult.Success();
            }

            return AudioProfileOperationResult.Failure(
                "接続要求は送信しましたが、オーディオエンドポイントが有効になりませんでした");
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

    public async Task<AudioProfileOperationResult> DisconnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? deviceName = await GetDeviceNameAsync(deviceAddress, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return AudioProfileOperationResult.Failure("Bluetooth デバイス名を取得できませんでした");
            }

            BluetoothAudioEndpointController.Endpoint? endpoint =
                BluetoothAudioEndpointController.FindBestEndpoint(deviceName);

            if (endpoint is null)
            {
                return AudioProfileOperationResult.Failure(
                    $"{deviceName} の Bluetooth オーディオエンドポイントが見つかりませんでした");
            }

            if (!endpoint.IsActive)
            {
                return AudioProfileOperationResult.Success();
            }

            if (!BluetoothAudioEndpointController.Disconnect(endpoint.Id))
            {
                return AudioProfileOperationResult.Failure(
                    "Bluetooth オーディオドライバーへ切断要求を送れませんでした");
            }

            DateTime deadline = DateTime.UtcNow + DisconnectTimeout;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id))
                {
                    return AudioProfileOperationResult.Success();
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return AudioProfileOperationResult.Failure(
                "切断要求は送信しましたが、オーディオエンドポイントがまだ有効です");
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

    public async Task<bool?> IsConnectedAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? deviceName = await GetDeviceNameAsync(deviceAddress, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return null;
            }

            BluetoothAudioEndpointController.Endpoint? endpoint =
                BluetoothAudioEndpointController.FindBestEndpoint(deviceName);

            return endpoint?.IsActive;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetDeviceNameAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken)
    {
        using BluetoothDevice? device = await BluetoothDevice
            .FromBluetoothAddressAsync(deviceAddress)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        return device?.Name;
    }
}
