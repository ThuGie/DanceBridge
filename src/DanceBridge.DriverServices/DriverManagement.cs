using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace DanceBridge.DriverServices;

public record RemovalCheck(bool Allowed,string Reason);
public static class DriverManagement
{
    public static RemovalCheck CanRemove(AdapterDriver device)
    {
        try { ValidateRemoval(device); return new(true,"Only this adapter uses the device-specific package."); }
        catch(Exception ex) { return new(false,ex.Message); }
    }
    public static bool IsDeviceSpecificPackage(string infName,string contents) => Regex.IsMatch(infName,@"^oem\d+\.inf$",RegexOptions.IgnoreCase) && contents.Contains("VID_057E&PID_0337",StringComparison.OrdinalIgnoreCase) && Regex.Matches(contents,@"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}(?:&MI_[0-9A-F]{2})?",RegexOptions.IgnoreCase).Cast<Match>().All(m=>m.Value.Equals("VID_057E&PID_0337",StringComparison.OrdinalIgnoreCase)) && !contents.Contains("MS_COMP_",StringComparison.OrdinalIgnoreCase);
    static string InfFile(string name)=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"INF",name);
    static void ValidateRemoval(AdapterDriver device)
    {
        if(!device.Ready)throw new IOException("Only the adapter’s WinUSB binding can be removed here.");
        if(!Regex.IsMatch(device.InfPath,@"^oem\d+\.inf$",RegexOptions.IgnoreCase))throw new IOException("The Microsoft inbox driver is shared and will not be removed.");
        if(!IsDeviceSpecificPackage(device.InfPath,File.ReadAllText(InfFile(device.InfPath))))throw new IOException("The package is not exclusively targeted to the native GameCube adapter.");
        var set=SetupDiGetClassDevs(IntPtr.Zero,null,IntPtr.Zero,4);if(set==new IntPtr(-1))throw Error();
        try
        {
            for(uint index=0;;index++)
            {
                var data=new DeviceData{Size=Marshal.SizeOf<DeviceData>()};
                if(!SetupDiEnumDeviceInfo(set,index,ref data)){if(Marshal.GetLastWin32Error()!=259)throw Error();break;}
                var buffer=new byte[2048];
                if(!SetupDiGetDeviceRegistryProperty(set,ref data,9,out _,buffer,(uint)buffer.Length,out _)){if(Marshal.GetLastWin32Error()!=13)throw Error();continue;}
                var key=Encoding.Unicode.GetString(buffer).TrimEnd('\0');
                using var driver=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\"+key);
                if(!string.Equals(driver?.GetValue("InfPath") as string,device.InfPath,StringComparison.OrdinalIgnoreCase))continue;
                var id=new StringBuilder(2048);if(!SetupDiGetDeviceInstanceId(set,ref data,id,2048,out _))throw Error();
                if(!id.ToString().Equals(device.DeviceId,StringComparison.OrdinalIgnoreCase))throw new IOException("This package is also used by another registered device. Removal is blocked to protect that device.");
            }
        }
        finally{SetupDiDestroyDeviceInfoList(set);}
    }
    public static bool Remove(AdapterDriver expected)
    {
        var current=VerifyUnchanged(expected);ValidateRemoval(current);
        if(!DiUninstallDriver(IntPtr.Zero,InfFile(current.InfPath),0,out bool reboot))throw Error();
        return reboot;
    }
    public static bool Rollback(AdapterDriver expected)
    {
        var current=VerifyUnchanged(expected);
        if(!current.Ready)throw new IOException("Only the adapter’s current WinUSB driver can be rolled back here.");
        var set=SetupDiCreateDeviceInfoList(IntPtr.Zero,IntPtr.Zero);if(set==new IntPtr(-1))throw Error();
        try
        {
            var data=new DeviceData{Size=Marshal.SizeOf<DeviceData>()};
            if(!SetupDiOpenDeviceInfo(set,current.DeviceId,IntPtr.Zero,0,ref data))throw Error();
            if(!DiRollbackDriver(set,ref data,IntPtr.Zero,0,out bool reboot)){if(Marshal.GetLastWin32Error()==259)throw new IOException("Windows has no previous driver available for this adapter. The current driver was not removed.");throw Error();}
            return reboot;
        }
        finally{SetupDiDestroyDeviceInfoList(set);}
    }
    public static AdapterDriver VerifyUnchanged(AdapterDriver expected)
    {
        var current=DriverLibrary.Inspect();
        if(!current.DeviceId.Equals(expected.DeviceId,StringComparison.OrdinalIgnoreCase)||!current.InfPath.Equals(expected.InfPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Adapter or driver changed. Refresh setup and try again.");
        return current;
    }
    static Win32Exception Error()=>new(Marshal.GetLastWin32Error());
    [StructLayout(LayoutKind.Sequential)]struct DeviceData{public int Size;public Guid Class;public uint DevInst;public IntPtr Reserved;}
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(IntPtr guid,string? enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)]static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr guid,IntPtr parent);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiOpenDeviceInfo(IntPtr set,string id,IntPtr parent,uint flags,ref DeviceData data);
    [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref DeviceData data);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set,ref DeviceData data,uint property,out uint type,byte[] buffer,uint length,out uint required);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInstanceId(IntPtr set,ref DeviceData data,StringBuilder id,uint size,out uint required);
    [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("newdev.dll",EntryPoint="DiUninstallDriverW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool DiUninstallDriver(IntPtr parent,string path,uint flags,out bool reboot);
    [DllImport("newdev.dll",SetLastError=true)]static extern bool DiRollbackDriver(IntPtr set,ref DeviceData data,IntPtr parent,uint flags,out bool reboot);
}
