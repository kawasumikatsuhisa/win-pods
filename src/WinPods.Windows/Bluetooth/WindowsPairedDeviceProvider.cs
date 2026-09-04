using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using WinPods.Core.Abstractions;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// WinRT の <see cref="DeviceInformation"/> を使ってペアリング済みの
/// クラシック Bluetooth オーディオデバイスを列挙する。
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsPairedDeviceProvider : IPairedDeviceProvider
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PairedDevice>> GetPairedAudioDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);

        DeviceInformationCollection found = await DeviceInformation
            .FindAllAsync(selector)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var devices = new List<PairedDevice>(found.Count);

        foreach (DeviceInformation info in found)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using BluetoothDevice? device = await BluetoothDevice
                .FromIdAsync(info.Id)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (device is null)
            {
                continue;
            }

            // ヘッドセット・イヤホンの類だけに絞る。
            if (device.ClassOfDevice.MajorClass != BluetoothMajorClass.AudioVideo)
            {
                continue;
            }

            devices.Add(new PairedDevice(
                device.BluetoothAddress,
                string.IsNullOrEmpty(device.Name) ? info.Name : device.Name,
                device.ConnectionStatus == BluetoothConnectionStatus.Connected));
        }

        return devices;
    }
}
