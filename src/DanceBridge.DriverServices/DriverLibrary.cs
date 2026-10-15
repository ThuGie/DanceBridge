using System.Runtime.InteropServices;
namespace DanceBridge.DriverServices;

public record AdapterDriver(string DeviceId, string Description, string Service)
{
    public bool Ready => Service.Equals("WinUSB", StringComparison.OrdinalIgnoreCase);
    public string InfPath { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Version { get; init; } = "";
}

/// <summary>Restricted integration with libwdi: WUP-028, inbox WinUSB, Windows x64 only.</summary>
public static class DriverLibrary
{
    public const string InterfaceGuid = "{B1541DF6-41F2-453B-A05B-B81FAAEB102E}";
    static DriverLibrary()
    {
        NativeLibrary.SetDllImportResolver(typeof(DriverLibrary).Assembly, (name, _, _) =>
            name == "dancebridge_wdi" ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "drivers", "libwdi.dll")) : IntPtr.Zero);
    }

    public static AdapterDriver Inspect() => WithAdapter((_, device) => Snapshot(device));

    public static void Prepare(string directory, bool unsignedPreview, string? expectedDeviceId = null)
    {
        WithAdapter((pointer, device) =>
        {
            if(expectedDeviceId is not null && !Text(device.DeviceId).Equals(expectedDeviceId,StringComparison.OrdinalIgnoreCase))throw new IOException("The connected adapter changed. Refresh setup before continuing.");
            Directory.CreateDirectory(directory);
            var vendor = Marshal.StringToCoTaskMemUTF8("DanceBridge");
            var guid = Marshal.StringToCoTaskMemUTF8(InterfaceGuid);
            try
            {
                var options = new PrepareOptions { DriverType = 0, Vendor = vendor, Guid = guid, DisableCat = unsignedPreview ? 1 : 0, DisableSigning = unsignedPreview ? 1 : 0 };
                Check(wdi_prepare_driver(pointer, directory, "dancebridge-wup028.inf", ref options));
            }
            finally { Marshal.FreeCoTaskMem(vendor); Marshal.FreeCoTaskMem(guid); }
            return true;
        });
    }

    public static void Install(string directory, string? expectedDeviceId = null)
    {
        WithAdapter((pointer, device) =>
        {
            if(expectedDeviceId is not null && !Text(device.DeviceId).Equals(expectedDeviceId,StringComparison.OrdinalIgnoreCase))throw new IOException("The connected adapter changed. Refresh setup before continuing.");
            var options = new InstallOptions { PendingTimeout = 120000 };
            Check(wdi_install_driver(pointer, directory, "dancebridge-wup028.inf", ref options));
            return true;
        });
    }

    static AdapterDriver Snapshot(Device device)
    {
        var id=Text(device.DeviceId);
        using var node=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\"+id);
        var key=node?.GetValue("Driver") as string;
        using var driver=key is null?null:Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\"+key);
        return new(id,Text(device.Description),Text(device.Driver)){InfPath=driver?.GetValue("InfPath") as string??"",Provider=driver?.GetValue("ProviderName") as string??"",Version=driver?.GetValue("DriverVersion") as string??""};
    }
    static string Text(IntPtr value) => Marshal.PtrToStringUTF8(value) ?? "";
    static T WithAdapter<T>(Func<IntPtr, Device, T> action)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10) || RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Driver setup supports Windows 10/11 x64.");
        var options = new ListOptions { ListAll = 1, Trim = 1 };
        Check(wdi_create_list(out var list, ref options));
        try
        {
            var matches = new List<(IntPtr Pointer, Device Device)>();
            for (var pointer = list; pointer != IntPtr.Zero;)
            {
                var device = Marshal.PtrToStructure<Device>(pointer);
                if (device.Vid == 0x057E && device.Pid == 0x0337 && device.Composite == 0) matches.Add((pointer, device));
                pointer = device.Next;
            }
            if (matches.Count == 0) throw new IOException("Connect the WUP-028 adapter's data USB cable, then try again.");
            if (matches.Count != 1) throw new IOException("Connect only one WUP-028 adapter during driver setup.");
            return action(matches[0].Pointer, matches[0].Device);
        }
        finally { wdi_destroy_list(list); }
    }
    static void Check(int code)
    {
        if (code != 0) throw new IOException($"Driver setup failed ({code}): {Text(wdi_strerror(code))}");
    }
    [StructLayout(LayoutKind.Sequential)] struct ListOptions { public int ListAll, ListHubs, Trim; }
    [StructLayout(LayoutKind.Sequential)] struct Device
    {
        public IntPtr Next;
        public ushort Vid, Pid;
        public int Composite;
        public byte Interface;
        public IntPtr Description, Driver, DeviceId, HardwareId, CompatibleId, UpperFilter;
        public ulong DriverVersion;
    }
    [StructLayout(LayoutKind.Sequential)] struct PrepareOptions
    {
        public int DriverType;
        public IntPtr Vendor, Guid;
        public int DisableCat, DisableSigning;
        public IntPtr CertificateSubject;
        public int Wcid, ExternalInf;
    }
    [StructLayout(LayoutKind.Sequential)] struct InstallOptions { public IntPtr Window; public int Filter; public uint PendingTimeout; }
    [DllImport("dancebridge_wdi")] static extern int wdi_create_list(out IntPtr list, ref ListOptions options);
    [DllImport("dancebridge_wdi")] static extern int wdi_destroy_list(IntPtr list);
    [DllImport("dancebridge_wdi")] static extern IntPtr wdi_strerror(int code);
    [DllImport("dancebridge_wdi")] static extern int wdi_prepare_driver(IntPtr device, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref PrepareOptions options);
    [DllImport("dancebridge_wdi")] static extern int wdi_install_driver(IntPtr device, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref InstallOptions options);
}
