using System.Runtime.InteropServices;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// bthprops.cpl (Bluetooth API) の P/Invoke 定義。
/// </summary>
/// <remarks>
/// <c>LibraryImport</c> (ソースジェネレータ) ではなく <c>DllImport</c> を使っているのは、
/// 前者が <c>AllowUnsafeBlocks</c> を要求するのと、<c>BLUETOOTH_DEVICE_INFO</c> が
/// 固定長文字列を含む非 blittable 構造体でどのみち従来のマーシャリングが必要なため。
/// </remarks>
internal static class NativeMethods
{
    private const string BluetoothApis = "bthprops.cpl";

    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_NOT_FOUND = 1168;
    internal const uint ERROR_MORE_DATA = 234;

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

    [DllImport(BluetoothApis, SetLastError = true)]
    internal static extern IntPtr BluetoothFindFirstRadio(
        ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp,
        out IntPtr phRadio);

    [DllImport(BluetoothApis, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport(BluetoothApis, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindRadioClose(IntPtr hFind);

    /// <summary>アドレスを設定した構造体を渡すと、残りのフィールドを埋めて返す。</summary>
    [DllImport(BluetoothApis, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

    /// <summary>
    /// Windows がそのデバイスに対して認識しているサービスを列挙する。
    /// <paramref name="pGuidServices"/> に null を渡すと必要な個数だけが返る
    /// (戻り値は ERROR_MORE_DATA)。
    /// </summary>
    [DllImport(BluetoothApis, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint BluetoothEnumerateInstalledServices(
        IntPtr hRadio,
        ref BLUETOOTH_DEVICE_INFO pbtdi,
        ref uint pcServiceInout,
        [Out] Guid[]? pGuidServices);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);
}
