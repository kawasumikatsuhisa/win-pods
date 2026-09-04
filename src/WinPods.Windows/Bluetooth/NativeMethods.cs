using System.Runtime.InteropServices;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// bthprops.cpl (Bluetooth API) の P/Invoke 定義。
/// </summary>
/// <remarks>
/// クラシック Bluetooth のオーディオプロファイル (A2DP / HFP) を能動的に
/// 接続・切断する公開 WinRT API は存在しないため、Win32 の
/// <c>BluetoothSetServiceState</c> を使う。
/// </remarks>
internal static partial class NativeMethods
{
    private const string BluetoothApis = "bthprops.cpl";

    internal const int ERROR_SUCCESS = 0;

    internal const uint BLUETOOTH_SERVICE_DISABLE = 0x00;
    internal const uint BLUETOOTH_SERVICE_ENABLE = 0x01;

    /// <summary>Advanced Audio Distribution Profile (A2DP) Sink。</summary>
    internal static readonly Guid AudioSinkServiceClass = new("0000110B-0000-1000-8000-00805F9B34FB");

    /// <summary>Hands-Free Profile (HFP)。</summary>
    internal static readonly Guid HandsFreeServiceClass = new("0000111E-0000-1000-8000-00805F9B34FB");

    /// <summary>Headset Profile (HSP)。HFP を持たない古いデバイス向け。</summary>
    internal static readonly Guid HeadsetServiceClass = new("00001108-0000-1000-8000-00805F9B34FB");

    [StructLayout(LayoutKind.Sequential)]
    internal struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        internal uint dwSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEMTIME
    {
        internal ushort wYear;
        internal ushort wMonth;
        internal ushort wDayOfWeek;
        internal ushort wDay;
        internal ushort wHour;
        internal ushort wMinute;
        internal ushort wSecond;
        internal ushort wMilliseconds;
    }

    private const int BLUETOOTH_MAX_NAME_SIZE = 248;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BLUETOOTH_DEVICE_INFO
    {
        internal uint dwSize;
        internal ulong Address;
        internal uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool fConnected;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool fAuthenticated;
        internal SYSTEMTIME stLastSeen;
        internal SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BLUETOOTH_MAX_NAME_SIZE)]
        internal string szName;
    }

    [LibraryImport(BluetoothApis, SetLastError = true)]
    internal static partial IntPtr BluetoothFindFirstRadio(
        ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp,
        out IntPtr phRadio);

    [LibraryImport(BluetoothApis, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [LibraryImport(BluetoothApis, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BluetoothFindRadioClose(IntPtr hFind);

    /// <summary>アドレスを設定した構造体を渡すと、残りのフィールドを埋めて返す。</summary>
    [DllImport(BluetoothApis, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport(BluetoothApis, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint BluetoothSetServiceState(
        IntPtr hRadio,
        ref BLUETOOTH_DEVICE_INFO pbtdi,
        ref Guid pGuidService,
        uint dwServiceFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);
}
