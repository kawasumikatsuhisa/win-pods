using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static WinPods.Windows.Bluetooth.NativeMethods;

namespace WinPods.Windows.Bluetooth;

/// <summary>ローカルの Bluetooth ラジオ (アダプタ) を列挙するヘルパ。</summary>
[SupportedOSPlatform("windows")]
internal static class BluetoothRadios
{
    /// <summary>
    /// すべての Bluetooth ラジオを順に処理する。
    /// <paramref name="action"/> が true を返した時点で探索を打ち切る。
    /// </summary>
    /// <returns>列挙できたラジオの数。</returns>
    internal static int ForEach(Func<IntPtr, bool> action)
    {
        var findParams = new BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>(),
        };

        IntPtr find = BluetoothFindFirstRadio(ref findParams, out IntPtr radio);

        if (find == IntPtr.Zero)
        {
            return 0;
        }

        int count = 0;

        try
        {
            do
            {
                count++;

                try
                {
                    if (action(radio))
                    {
                        return count;
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

        return count;
    }

    /// <summary>
    /// アドレスを指定してデバイス情報を取得する。戻り値は Win32 のエラーコード。
    /// </summary>
    internal static uint GetDeviceInfo(IntPtr radio, ulong deviceAddress, out BLUETOOTH_DEVICE_INFO info)
    {
        info = new BLUETOOTH_DEVICE_INFO
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
            Address = deviceAddress,
            szName = string.Empty,
        };

        return BluetoothGetDeviceInfo(radio, ref info);
    }

    /// <summary>Win32 のエラーコードを、原因の切り分けに使える文字列にする。</summary>
    internal static string DescribeError(uint code)
    {
        string name = code switch
        {
            0 => "ERROR_SUCCESS",
            5 => "ERROR_ACCESS_DENIED",
            87 => "ERROR_INVALID_PARAMETER",
            234 => "ERROR_MORE_DATA",
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
}
