using DanceBridge.DriverServices;
using System.Security.Principal;
using System.Text.Json;
string[] commands=["--inspect","--stage-preview","--removal-check","--install","--repair","--rollback","--remove"];
if(args.Length is not (1 or 3)||!commands.Contains(args[0])){Console.Error.WriteLine("Expected an inspection or adapter-specific driver operation.");return 2;}
try
{
 var device=DriverLibrary.Inspect();
 if(args[0]=="--inspect"){Console.WriteLine(JsonSerializer.Serialize(device));return 0;}
 if(args[0]=="--removal-check"){Console.WriteLine(JsonSerializer.Serialize(DriverManagement.CanRemove(device)));return 0;}
 if(args[0]=="--stage-preview"){var preview=Path.Combine(Path.GetTempPath(),"DanceBridge-preview-"+Guid.NewGuid().ToString("N"));DriverLibrary.Prepare(preview,true);Console.WriteLine(preview);return 0;}
 if(args[0]=="--install"&&device.Ready){Console.WriteLine("WinUSB already installed. No changes needed.");return 0;}
 if(args.Length!=3||!device.DeviceId.Equals(args[1],StringComparison.OrdinalIgnoreCase)||!device.InfPath.Equals(args[2],StringComparison.OrdinalIgnoreCase))throw new IOException("Adapter or driver changed. Refresh setup before continuing.");
 using var identity=WindowsIdentity.GetCurrent();
 if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Administrator approval is required for driver changes.");
 var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"DanceBridge","Operation-"+Guid.NewGuid().ToString("N"));
 Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"previous-device.json"),JsonSerializer.Serialize(device));
 bool reboot=false;
 try
 {
  if(args[0]=="--remove")reboot=DriverManagement.Remove(device);
  else if(args[0]=="--rollback")reboot=DriverManagement.Rollback(device);
  else{DriverLibrary.Prepare(directory,false,device.DeviceId);DriverManagement.VerifyUnchanged(device);DriverLibrary.Install(directory,device.DeviceId);if(!DriverLibrary.Inspect().Ready)throw new IOException("Reconnect the adapter or restart Windows to activate WinUSB.");}
  File.WriteAllText(Path.Combine(directory,"result.txt"),args[0]+" completed."+(reboot?" Restart required.":""));
 }
 catch(Exception ex){File.WriteAllText(Path.Combine(directory,"error.txt"),ex.ToString());throw;}
 if(reboot)NativeMessage.Show("The operation completed. Restart Windows to finish; DanceBridge will not restart it automatically.");
 return reboot?3010:0;
}
catch(Exception exception)
{
 Console.Error.WriteLine(exception.Message);
 if(args[0] is "--install" or "--repair" or "--remove" or "--rollback")NativeMessage.Show(exception.Message+"\n\nNo Windows driver-signature or security settings have been disabled.");
 return 1;
}
static class NativeMessage
{
 public static void Show(string text)=>MessageBox(IntPtr.Zero,text,"DanceBridge driver management",0x40);
 [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern int MessageBox(IntPtr hwnd,string text,string caption,uint type);
}
