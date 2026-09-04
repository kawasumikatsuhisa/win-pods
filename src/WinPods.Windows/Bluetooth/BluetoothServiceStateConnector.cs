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

                ForEachRadio(radio =>
                {
                    if (GetDeviceInfo(radio, deviceAddress, out BLUETOOTH_DEVICE_INFO info) != ERROR_SUCCESS)
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
        bool anyRadio = false;

        ForEachRadio(radio =>
        {
            anyRadio = true;

            uint lookupError = GetDeviceInfo(radio, deviceAddress, out BLUETOOTH_DEVICE_INFO info);

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
                    failures.Add($"{DescribeService(serviceClass)} {DescribeError(result)}");
                }
            }

            // デバイスが見つかったラジオで処理したので探索を終了する。
            return true;
        });

        if (!anyRadio)
        {
            return AudioProfileOperationResult.Failure("Bluetooth アダプタが見つかりません");
        }

        if (!deviceFound)
        {
            return AudioProfileOperationResult.Failure(
                $"デバイスが見つかりません {DescribeError(lastLookupError)}");
        }

        return anySucceeded
            ? AudioProfileOperationResult.Success()
            : AudioProfileOperationResult.Failure(string.Join(" / ", failures));
    }

    private static uint GetDeviceInfo(IntPtr radio, ulong deviceAddress, out BLUETOOTH_DEVICE_INFO info)
    {
        info = new BLUETOOTH_DEVICE_INFO
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
            Address = deviceAddress,
            szName = string.Empty,
        };

        return BluetoothGetDeviceInfo(radio, ref info);
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

    /// <summary>Win32 のエラーコードを、原因の切り分けに使える文字列にする。</summary>
    private static string DescribeError(uint code)
    {
        string name = code switch
        {
            0 => "ERROR_SUCCESS",
            5 => "ERROR_ACCESS_DENIED",
            87 => "ERROR_INVALID_PARAMETER",
            258 => "WAIT_TIMEOUT",
            1167 => "ERROR_DEVICE_NOT_CONNECTED",
            1168 => "ERROR_NOT_FOUND",
            1219 => "ERROR_SESSION_CREDENTIAL_CONFLICT",
            1223 => "ERROR_CANCELLED",
            1359 => "ERROR_INTERNAL_ERROR",
            _ => "Win32",
        };

        return $"{name}({code})";
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
