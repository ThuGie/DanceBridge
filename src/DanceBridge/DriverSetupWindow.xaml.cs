using DanceBridge.DriverServices;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
namespace DanceBridge;
public partial class DriverSetupWindow : Window
{
 readonly Action<bool> suspendAdapter;
 bool busy;
 AdapterDriver? device;
 public DriverSetupWindow(Action<bool> suspend)
 {
  InitializeComponent();suspendAdapter=suspend;
  Explanation.Text="Repair reinstalls the adapter-specific WinUSB binding. Rollback asks Windows to restore its previous driver, if available. Removal deletes only an eligible device-specific package, leaving Microsoft's shared WinUSB system driver installed.";
  Loaded+=async(_,_)=>await RefreshDevice();Closing+=(_,e)=>{if(busy)e.Cancel=true;};
 }
 async Task RefreshDevice()
 {
  SetBusy(true);DeviceStatus.Text="Inspecting adapter…";
  try
  {
   var result=await Task.Run(()=>{var adapter=DriverLibrary.Inspect();return(adapter,DriverManagement.CanRemove(adapter));});
   device=result.adapter;DeviceStatus.Text="Native GameCube adapter · 057E:0337";
   DriverStatus.Text=$"Driver: {(device.Service.Length==0?"Not installed":device.Service)}\nPackage: {device.InfPath} · {device.Provider}\nVersion: {device.Version}\nDevice: {device.DeviceId}";
   RemovalReason.Text=result.Item2.Reason;Install.IsEnabled=!device.Ready;Repair.IsEnabled=device.Ready;Rollback.IsEnabled=device.Ready;Remove.IsEnabled=result.Item2.Allowed;
   Progress.Text=device.Ready?"WinUSB is ready. No installation is required for normal use.":"Install WinUSB to use this adapter. Windows will request administrator approval.";
  }
  catch(Exception ex){device=null;DeviceStatus.Text="Adapter unavailable";DriverStatus.Text=ex.Message;RemovalReason.Text="Connect one native-compatible adapter, then refresh detection.";}
  finally{busy=false;RefreshButton.IsEnabled=true;if(device is null){Install.IsEnabled=false;Repair.IsEnabled=false;Rollback.IsEnabled=false;Remove.IsEnabled=false;}}
 }
 void SetBusy(bool value){busy=value;RefreshButton.IsEnabled=!value;Install.IsEnabled=false;Repair.IsEnabled=false;Rollback.IsEnabled=false;Remove.IsEnabled=false;}
 async void Refresh_Click(object sender,RoutedEventArgs e)=>await RefreshDevice();
 async void Install_Click(object sender,RoutedEventArgs e)=>await Run("--install","Install WinUSB for this adapter? A device-specific setup certificate will be generated.");
 async void Repair_Click(object sender,RoutedEventArgs e)=>await Run("--repair","Reinstall WinUSB for this adapter? Its binding will be replaced and the adapter may briefly disconnect.");
 async void Rollback_Click(object sender,RoutedEventArgs e)=>await Run("--rollback","Restore the previous driver recorded by Windows? DanceBridge may stop reading the adapter afterward. Windows may report that no backup is available.");
 async void Remove_Click(object sender,RoutedEventArgs e)=>await Run("--remove","Remove this adapter's device-specific driver package? DanceBridge may stop reading it until WinUSB is installed again. Microsoft's shared WinUSB driver is not removed.");
 async Task Run(string operation,string confirmation)
 {
  if(device is null||busy)return;
  if(System.Windows.MessageBox.Show(this,confirmation+"\n\nTarget: "+device.DeviceId+"\nPackage: "+device.InfPath,"Confirm driver operation",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
  var expected=device;SetBusy(true);suspendAdapter(true);string outcome="";
  try
  {
   await Task.Delay(600);var helper=Path.Combine(AppContext.BaseDirectory,"DanceBridge.DriverSetup.exe");if(!File.Exists(helper))throw new FileNotFoundException("The bundled setup helper is missing. Extract the full package.");
   var start=new ProcessStartInfo(helper){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add(operation);start.ArgumentList.Add(expected.DeviceId);start.ArgumentList.Add(expected.InfPath);
   using var process=Process.Start(start)??throw new IOException("Windows could not start setup.");Progress.Text="Working… keep the adapter connected.";await process.WaitForExitAsync();
   if(process.ExitCode is not (0 or 3010))throw new IOException("The operation did not complete. Review the setup message, then refresh detection.");
   outcome=process.ExitCode==3010?"Operation completed. Restart Windows to finish.":"Operation completed. Detection refreshed. Reconnect the adapter if necessary.";
  }
  catch(Win32Exception ex)when(ex.NativeErrorCode==1223){outcome="Administrator approval was cancelled. No operation was started.";}
  catch(Exception ex){outcome=ex.Message;}
  finally{suspendAdapter(false);busy=false;await RefreshDevice();Progress.Text=outcome;}
 }
 void Close_Click(object sender,RoutedEventArgs e){if(!busy)Close();}
}
