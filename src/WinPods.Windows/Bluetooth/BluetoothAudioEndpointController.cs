using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// Bluetooth オーディオエンドポイントのドライバーへ Kernel Streaming 経由で
/// 再接続 / 切断要求を送る。
/// </summary>
/// <remarks>
/// Bluetooth スタックの ACL 接続ではなく、実際のオーディオエンドポイントを対象にする。
/// AirPods などの A2DP デバイスでは、エンドポイントが ACTIVE になった時点を
/// 「オーディオ接続済み」と判定する。
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class BluetoothAudioEndpointController
{
    private static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

    private const uint KsPropertyOneShotReconnect = 0;
    private const uint KsPropertyOneShotDisconnect = 1;
    private const uint KsPropertyTypeGet = 0x00000001;
    private const uint KsPropertyTypeBasicSupport = 0x00000200;

    private const int ClsctxAll = 23;
    private const int DeviceStateActive = 0x00000001;
    private const int DeviceStateUnplugged = 0x00000008;
    private const int StgmRead = 0;

    internal sealed record Endpoint(string Id, string Name, bool IsActive);

    public static IReadOnlyList<Endpoint> GetBluetoothAudioEndpoints()
    {
        var result = new List<Endpoint>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

        try
        {
            enumerator.EnumAudioEndpoints(
                EDataFlow.Render,
                DeviceStateActive | DeviceStateUnplugged,
                out IMMDeviceCollection collection);

            try
            {
                collection.GetCount(out int count);

                for (int i = 0; i < count; i++)
                {
                    collection.Item(i, out IMMDevice device);

                    try
                    {
                        if (!IsBluetoothEndpoint(device))
                        {
                            continue;
                        }

                        device.GetId(out string id);
                        device.GetState(out int state);
                        result.Add(new Endpoint(id, GetFriendlyName(device), state == DeviceStateActive));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }

        return result;
    }

    public static Endpoint? FindBestEndpoint(string deviceName)
    {
        IReadOnlyList<Endpoint> endpoints = GetBluetoothAudioEndpoints();
        string target = NormalizeName(deviceName);

        return endpoints
            .Where(endpoint => NamesMatch(NormalizeName(endpoint.Name), target))
            .OrderByDescending(endpoint => endpoint.IsActive)
            .FirstOrDefault();
    }

    public static bool Connect(string endpointId) => SendOneShot(endpointId, KsPropertyOneShotReconnect);

    public static bool Disconnect(string endpointId) => SendOneShot(endpointId, KsPropertyOneShotDisconnect);

    public static bool IsEndpointActive(string endpointId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

        try
        {
            if (enumerator.GetDevice(endpointId, out IMMDevice device) < 0 || device is null)
            {
                return false;
            }

            try
            {
                device.GetState(out int state);
                return state == DeviceStateActive;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static bool NamesMatch(string endpointName, string targetName)
    {
        if (string.Equals(endpointName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return endpointName.Contains(targetName, StringComparison.OrdinalIgnoreCase)
            || targetName.Contains(endpointName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string name)
    {
        string value = name.Trim();

        // Windows の Bluetooth オーディオエンドポイントは
        // "Headphones (AirPods Pro)" / "Headset (AirPods Pro)" のようになる。
        int open = value.IndexOf('(');
        int close = value.LastIndexOf(')');

        if (open >= 0 && close > open)
        {
            value = value[(open + 1)..close];
        }

        int suffix = value.IndexOf(" - ", StringComparison.Ordinal);
        if (suffix > 0)
        {
            value = value[..suffix];
        }

        return value.Trim();
    }

    private static bool SendOneShot(string endpointId, uint propertyId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

        try
        {
            if (enumerator.GetDevice(endpointId, out IMMDevice endpoint) < 0 || endpoint is null)
            {
                return false;
            }

            try
            {
                IKsControl? ks = GetKsControl(endpoint);
                if (ks is null)
                {
                    return false;
                }

                try
                {
                    var property = new KsProperty
                    {
                        Set = KsPropSetIdBtAudio,
                        Id = propertyId,
                        Flags = KsPropertyTypeGet,
                    };

                    int hr = ks.KsProperty(
                        ref property,
                        (uint)Marshal.SizeOf<KsProperty>(),
                        IntPtr.Zero,
                        0,
                        out _);

                    return hr >= 0;
                }
                finally
                {
                    Marshal.ReleaseComObject(ks);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(endpoint);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static bool IsBluetoothEndpoint(IMMDevice endpoint)
    {
        IKsControl? ks = null;

        try
        {
            ks = GetKsControl(endpoint);
            if (ks is null)
            {
                return false;
            }

            var property = new KsProperty
            {
                Set = KsPropSetIdBtAudio,
                Id = KsPropertyOneShotReconnect,
                Flags = KsPropertyTypeBasicSupport,
            };

            IntPtr buffer = Marshal.AllocHGlobal(64);
            try
            {
                return ks.KsProperty(
                    ref property,
                    (uint)Marshal.SizeOf<KsProperty>(),
                    buffer,
                    64,
                    out _) >= 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (ks is not null)
            {
                Marshal.ReleaseComObject(ks);
            }
        }
    }

    private static IKsControl? GetKsControl(IMMDevice endpoint)
    {
        Guid iidTopology = typeof(IDeviceTopology).GUID;
        if (endpoint.Activate(ref iidTopology, ClsctxAll, IntPtr.Zero, out object topologyObject) < 0)
        {
            return null;
        }

        var topology = (IDeviceTopology)topologyObject;

        try
        {
            topology.GetConnectorCount(out uint count);

            for (uint i = 0; i < count; i++)
            {
                topology.GetConnector(i, out IConnector connector);

                try
                {
                    connector.IsConnected(out bool connected);
                    if (!connected)
                    {
                        continue;
                    }

                    connector.GetConnectedTo(out IConnector other);

                    try
                    {
                        var part = (IPart)other;
                        part.GetTopologyObject(out IDeviceTopology otherTopology);

                        try
                        {
                            otherTopology.GetDeviceId(out string deviceId);
                            IKsControl? ks = ActivateKsControl(deviceId);
                            if (ks is not null)
                            {
                                return ks;
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(otherTopology);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(other);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(connector);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(topology);
        }

        return null;
    }

    private static IKsControl? ActivateKsControl(string deviceId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

        try
        {
            if (enumerator.GetDevice(deviceId, out IMMDevice device) < 0 || device is null)
            {
                return null;
            }

            try
            {
                Guid iid = typeof(IKsControl).GUID;
                if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object ksObject) < 0 || ksObject is null)
                {
                    return null;
                }

                return (IKsControl)ksObject;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(StgmRead, out IPropertyStore store);

        try
        {
            PropertyKey key = PkeyDeviceFriendlyName;
            store.GetValue(ref key, out PropVariant value);

            try
            {
                return Marshal.PtrToStringUni(value.PointerValue) ?? "(unknown)";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static PropertyKey PkeyDeviceFriendlyName = new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public short VariantType;

        [FieldOffset(8)]
        public IntPtr PointerValue;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    private enum EDataFlow
    {
        Render,
        Capture,
        All,
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int count);
        int Item(int index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        int OpenPropertyStore(int access, out IPropertyStore properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int index, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }

    [ComImport]
    [Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        int GetConnectorCount(out uint count);
        int GetConnector(uint index, out IConnector connector);
        int GetSubunitCount(out uint count);
        int GetSubunit(uint index, out IntPtr subunit);
        int GetPartById(uint id, out IntPtr part);
        int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetSignalPath(IntPtr from, IntPtr to, bool rejectMixedPaths, out IntPtr parts);
    }

    [ComImport]
    [Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        int GetType(out int connectorType);
        int GetDataFlow(out int dataFlow);
        int ConnectTo(IConnector other);
        int Disconnect();
        int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        int GetConnectedTo(out IConnector other);
        int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id, out IntPtr reserved);
        int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport]
    [Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPart
    {
        int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int GetLocalId(out uint id);
        int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetPartType(out int partType);
        int GetSubType(out Guid subType);
        int GetControlInterfaceCount(out uint count);
        int GetControlInterface(uint index, out IntPtr controlInterface);
        int EnumPartsIncoming(out IntPtr parts);
        int EnumPartsOutgoing(out IntPtr parts);
        int GetTopologyObject(out IDeviceTopology topology);
    }

    [ComImport]
    [Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig]
        int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
        [PreserveSig]
        int KsMethod(IntPtr method, uint methodLength, IntPtr data, uint dataLength, out uint bytesReturned);
        [PreserveSig]
        int KsEvent(IntPtr eventData, uint eventLength, IntPtr data, uint dataLength, out uint bytesReturned);
    }
}
