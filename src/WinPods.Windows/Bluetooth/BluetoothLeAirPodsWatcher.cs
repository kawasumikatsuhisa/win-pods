using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Windows.Devices.Bluetooth.Advertisement;
using WinPods.Core.Abstractions;
using WinPods.Core.Proximity;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// <see cref="BluetoothLEAdvertisementWatcher"/> で BLE アドバタイズメントを監視し、
/// Apple Proximity Pairing メッセージだけを取り出す。
/// </summary>
/// <remarks>
/// <para>
/// AirPods は接続中でもバッテリー情報を BLE アドバタイズメントで撒き続けるため、
/// GATT 接続をせずに残量を取得できる。
/// </para>
/// <para>
/// <see cref="AdvertisementReceived"/> は WinRT のスレッドプールから発火する。
/// UI で使う場合は Dispatcher へマーシャリングすること。
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class BluetoothLeAirPodsWatcher : IAirPodsAdvertisementWatcher
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private bool _disposed;

    public BluetoothLeAirPodsWatcher()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            // Proximity Pairing はスキャン応答ではなくアドバタイズ本体に載るが、
            // Active スキャンのほうが取りこぼしが少ない。
            ScanningMode = BluetoothLEScanningMode.Active,
        };

        _watcher.Received += OnAdvertisementReceived;
    }

    /// <inheritdoc />
    public event EventHandler<AirPodsAdvertisement>? AdvertisementReceived;

    /// <inheritdoc />
    public bool IsRunning => _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started;

    /// <summary>
    /// これより弱い信号のアドバタイズメントを無視する閾値 (dBm)。
    /// null の場合はフィルタしない。
    /// </summary>
    public short? MinimumRssi { get; set; }

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started)
        {
            return;
        }

        _watcher.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started)
        {
            _watcher.Stop();
        }
    }

    private void OnAdvertisementReceived(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (MinimumRssi is { } threshold && args.RawSignalStrengthInDBm < threshold)
        {
            return;
        }

        foreach (BluetoothLEManufacturerData section in args.Advertisement.ManufacturerData)
        {
            if (section.CompanyId != ProximityPairingParser.AppleCompanyId)
            {
                continue;
            }

            byte[] data = section.Data.ToArray();

            if (!ProximityPairingParser.TryParse(data, out ProximityPairingMessage? message))
            {
                continue;
            }

            AdvertisementReceived?.Invoke(
                this,
                new AirPodsAdvertisement(
                    args.BluetoothAddress,
                    args.RawSignalStrengthInDBm,
                    args.Timestamp,
                    message));

            return;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.Received -= OnAdvertisementReceived;
        Stop();
    }
}
