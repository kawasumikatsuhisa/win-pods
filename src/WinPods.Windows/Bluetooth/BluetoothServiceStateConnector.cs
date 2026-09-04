using System.Runtime.InteropServices;
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
/// <b>未検証</b>: 実機 (Windows + AirPods) での動作確認はこれから。
/// 特に「A2DP だけ接続して HFP は繋がない」ときの挙動は要確認。
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
    public Task<bool> ConnectAsync(ulong deviceAddress, CancellationToken cancellationToken = default) =>
        Task.Run(() => SetServiceState(deviceAddress, enable: true), cancellationToken);

    /// <inheritdoc />
    public Task<bool> DisconnectAsync(ulong deviceAddress, CancellationToken cancellationToken = default) =>
        Task.Run(() => SetServiceState(deviceAddress, enable: false), cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsConnectedAsync(ulong deviceAddress, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                bool connected = false;

                ForEachRadio(radio =>
                {
                    if (!TryGetDeviceInfo(radio, deviceAddress, out BLUETOOTH_DEVICE_INFO info))
                    {
                        return false;
                    }

                    connected = info.fConnected;
                    return true;
                });

                return connected;
            },
            cancellationToken);

    private bool SetServiceState(ulong deviceAddress, bool enable)
    {
        bool anySucceeded = false;

        ForEachRadio(radio =>
        {
            if (!TryGetDeviceInfo(radio, deviceAddress, out BLUETOOTH_DEVICE_INFO info))
            {
                return false;
            }

            uint flags = enable ? BLUETOOTH_SERVICE_ENABLE : BLUETOOTH_SERVICE_DISABLE;

            foreach (Guid serviceClass in ServiceClasses)
            {
                Guid service = serviceClass;

                if (BluetoothSetServiceState(radio, ref info, ref service, flags) == ERROR_SUCCESS)
                {
                    anySucceeded = true;
                }
            }

            // デバイスが見つかったラジオで処理したので探索を終了する。
            return true;
        });

        return anySucceeded;
    }

    private static bool TryGetDeviceInfo(IntPtr radio, ulong deviceAddress, out BLUETOOTH_DEVICE_INFO info)
    {
        info = new BLUETOOTH_DEVICE_INFO
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
            Address = deviceAddress,
            szName = string.Empty,
        };

        return BluetoothGetDeviceInfo(radio, ref info) == ERROR_SUCCESS;
    }

    /// <summary>
    /// すべての Bluetooth ラジオを順に処理する。
    /// <paramref name="action"/> が true を返した時点で探索を打ち切る。
    /// </summary>
    private static void ForEachRadio(Func<IntPtr, bool> action)
    {
        var findParams = new BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>(),
        };

        IntPtr find = BluetoothFindFirstRadio(ref findParams, out IntPtr radio);

        if (find == IntPtr.Zero)
        {
            return;
        }

        try
        {
            do
            {
                try
                {
                    if (action(radio))
                    {
                        return;
                    }
                }
                finally
                {
                    CloseHandle(radio);
                }
            }
            while (BluetoothFindNextRadio(find, out radio));
        }
        finally
        {
            BluetoothFindRadioClose(find);
        }
    }
}
