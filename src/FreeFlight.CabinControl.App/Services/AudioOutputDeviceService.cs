using System.Runtime.InteropServices;

namespace FreeFlight.CabinControl.App.Services;

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault)
{
    public string DisplayName => IsDefault ? $"{Name} (Default)" : Name;

    public override string ToString() => DisplayName;
}

public interface IAudioOutputDeviceService
{
    IReadOnlyList<AudioOutputDevice> GetActiveOutputDevices();
}

public sealed class AudioOutputDeviceService : IAudioOutputDeviceService
{
    private const uint DeviceStateActive = 0x00000001;

    public IReadOnlyList<AudioOutputDevice> GetActiveOutputDevices()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MmDeviceEnumeratorComObject();
            var defaultDeviceId = string.Empty;
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out defaultDevice) >= 0
                && defaultDevice is not null
                && defaultDevice.GetId(out var detectedDefaultDeviceId) >= 0)
            {
                defaultDeviceId = detectedDefaultDeviceId;
            }
            ThrowIfFailed(enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out collection));
            ThrowIfFailed(collection.GetCount(out var count));

            var devices = new List<AudioOutputDevice>((int)count);
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                IPropertyStore? properties = null;
                try
                {
                    ThrowIfFailed(collection.Item(index, out device));
                    ThrowIfFailed(device.GetId(out var id));
                    ThrowIfFailed(device.OpenPropertyStore(StorageAccessMode.Read, out properties));

                    var friendlyNameKey = PropertyKeys.DeviceFriendlyName;
                    ThrowIfFailed(properties.GetValue(ref friendlyNameKey, out var value));
                    try
                    {
                        var name = value.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            devices.Add(new AudioOutputDevice(
                                id,
                                name,
                                string.Equals(id, defaultDeviceId, StringComparison.OrdinalIgnoreCase)));
                        }
                    }
                    finally
                    {
                        value.Clear();
                    }
                }
                finally
                {
                    ReleaseComObject(properties);
                    ReleaseComObject(device);
                }
            }

            return devices
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            ReleaseComObject(defaultDevice);
            ReleaseComObject(collection);
            ReleaseComObject(enumerator);
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    private enum StorageAccessMode : uint
    {
        Read = 0
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MmDeviceEnumeratorComObject;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParameters, out IntPtr instance);

        [PreserveSig]
        int OpenPropertyStore(StorageAccessMode accessMode, out IPropertyStore properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    private static class PropertyKeys
    {
        public static PropertyKey DeviceFriendlyName =>
            new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        private readonly ushort _variantType;

        [FieldOffset(8)]
        private readonly IntPtr _pointerValue;

        public string GetString() => _variantType == 31 && _pointerValue != IntPtr.Zero
            ? Marshal.PtrToStringUni(_pointerValue) ?? string.Empty
            : string.Empty;

        public void Clear() => PropVariantClear(ref this);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);
    }
}
