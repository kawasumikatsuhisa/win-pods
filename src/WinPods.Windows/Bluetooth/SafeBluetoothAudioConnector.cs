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
/// KSPROPERTY_ONESHOT_RECONNECT / DISCONNECT を送り、再生 Endpoint に加えて
/// HFP の録音 Endpoint も起こす。これにより Teams 等からマイクが見える状態を
/// Windows の Bluetooth 設定から接続した場合に近づける。
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class SafeBluetoothAudioConnector : IAudioProfileConnector
{
    // Windows は接続要求を受け付けてから Bluetooth audio endpoint を ACTIVE にするまで
    // かなり時間がかかる場合がある。実機では 15 秒付近で接続完了するケースが
    // あったため、余裕を持って待機し、タイムアウト境界でも最終確認する。
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(8);
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

            IReadOnlyList<BluetoothAudioEndpointController.Endpoint> endpoints =
                BluetoothAudioEndpointController.FindMatchingEndpoints(deviceName);

            if (endpoints.Count == 0)
            {
                return AudioProfileOperationResult.Failure(
                    $"{deviceName} の Bluetooth オーディオエンドポイントが見つかりませんでした");
            }

            if (AreRequiredEndpointsActive(endpoints))
            {
                return AudioProfileOperationResult.Success();
            }

            bool requestAccepted = false;

            // A2DP / HFP の再生側だけでなく HFP の録音側にも再接続要求を送る。
            // 会社 PC などでは再生だけ ACTIVE になり、Teams のマイク候補に
            // AirPods が現れないケースがあるため、同一デバイスの Endpoint 群を
            // 一つの接続単位として扱う。
            foreach (BluetoothAudioEndpointController.Endpoint endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id))
                {
                    continue;
                }

                requestAccepted |= BluetoothAudioEndpointController.Connect(endpoint.Id);
            }

            if (!requestAccepted && !AreRequiredEndpointsActive(endpoints))
            {
                return AudioProfileOperationResult.Failure(
                    "Bluetooth オーディオドライバーへ接続要求を送れませんでした");
            }

            DateTime deadline = DateTime.UtcNow + ConnectTimeout;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (AreRequiredEndpointsActive(endpoints))
                {
                    return AudioProfileOperationResult.Success();
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            // タイムアウト境界で Windows 側の状態反映と競合するケースを吸収する。
            await Task.Delay(FinalActivationGrace, cancellationToken).ConfigureAwait(false);
            if (AreRequiredEndpointsActive(endpoints))
            {
                return AudioProfileOperationResult.Success();
            }

            bool renderActive = IsAnyDirectionActive(
                endpoints,
                BluetoothAudioEndpointController.EndpointDirection.Render);
            bool captureExists = endpoints.Any(endpoint =>
                endpoint.Direction == BluetoothAudioEndpointController.EndpointDirection.Capture);
            bool captureActive = IsAnyDirectionActive(
                endpoints,
                BluetoothAudioEndpointController.EndpointDirection.Capture);

            if (renderActive && captureExists && !captureActive)
            {
                return AudioProfileOperationResult.Failure(
                    "スピーカーは接続されましたが、AirPods のマイクが有効になりませんでした");
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

            IReadOnlyList<BluetoothAudioEndpointController.Endpoint> endpoints =
                BluetoothAudioEndpointController.FindMatchingEndpoints(deviceName);

            if (endpoints.Count == 0)
            {
                return AudioProfileOperationResult.Failure(
                    $"{deviceName} の Bluetooth オーディオエンドポイントが見つかりませんでした");
            }

            if (!endpoints.Any(endpoint => BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id)))
            {
                return AudioProfileOperationResult.Success();
            }

            bool requestAccepted = false;
            foreach (BluetoothAudioEndpointController.Endpoint endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id))
                {
                    continue;
                }

                requestAccepted |= BluetoothAudioEndpointController.Disconnect(endpoint.Id);
            }

            if (!requestAccepted)
            {
                return AudioProfileOperationResult.Failure(
                    "Bluetooth オーディオドライバーへ切断要求を送れませんでした");
            }

            DateTime deadline = DateTime.UtcNow + DisconnectTimeout;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!endpoints.Any(endpoint => BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id)))
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

            IReadOnlyList<BluetoothAudioEndpointController.Endpoint> endpoints =
                BluetoothAudioEndpointController.FindMatchingEndpoints(deviceName);

            if (endpoints.Count == 0)
            {
                return null;
            }

            // UI 上の「接続済み」は、通常の音声再生が可能な Render Endpoint を基準にする。
            // Capture がまだ上がっていない場合でも接続そのものまで未接続扱いにはしない。
            IReadOnlyList<BluetoothAudioEndpointController.Endpoint> renderEndpoints = endpoints
                .Where(endpoint => endpoint.Direction == BluetoothAudioEndpointController.EndpointDirection.Render)
                .ToArray();

            IEnumerable<BluetoothAudioEndpointController.Endpoint> candidates =
                renderEndpoints.Count > 0 ? renderEndpoints : endpoints;

            return candidates.Any(endpoint => BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id));
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

    private static bool AreRequiredEndpointsActive(
        IReadOnlyList<BluetoothAudioEndpointController.Endpoint> endpoints)
    {
        bool renderExists = endpoints.Any(endpoint =>
            endpoint.Direction == BluetoothAudioEndpointController.EndpointDirection.Render);
        bool captureExists = endpoints.Any(endpoint =>
            endpoint.Direction == BluetoothAudioEndpointController.EndpointDirection.Capture);

        bool renderActive = !renderExists || IsAnyDirectionActive(
            endpoints,
            BluetoothAudioEndpointController.EndpointDirection.Render);
        bool captureActive = !captureExists || IsAnyDirectionActive(
            endpoints,
            BluetoothAudioEndpointController.EndpointDirection.Capture);

        return renderActive && captureActive;
    }

    private static bool IsAnyDirectionActive(
        IReadOnlyList<BluetoothAudioEndpointController.Endpoint> endpoints,
        BluetoothAudioEndpointController.EndpointDirection direction)
    {
        return endpoints
            .Where(endpoint => endpoint.Direction == direction)
            .Any(endpoint => BluetoothAudioEndpointController.IsEndpointActive(endpoint.Id));
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
