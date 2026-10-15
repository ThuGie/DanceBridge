using DanceBridge.Core;
using DanceBridge.DriverServices;
public static class FeatureTests
{
    public static void Run(Action<bool,string> check)
    {
        var legacy=ProfileStore.Parse("""{"Name":"Legacy","Ports":[{"Left":37},{},{},{}]}""");
        check(legacy.EnabledPorts.All(p=>p)&&legacy.Ports[0][PadButton.Left]==37,"legacy keyboard mappings and port behavior preserved");
        var profile=new Profile();profile.BindPanel(0,PadButton.Left,PadButton.A);
        check(profile.PhysicalButtons(0,PadButton.A)==PadButton.Left&&profile.PhysicalButtons(0,PadButton.Left)==PadButton.A,"physical calibration swaps conflicting signal assignments");
        profile.BindPanel(0,PadButton.Z,PadButton.None);profile.Validate();
        check(!profile.PhysicalButtons(0,PadButton.Z).HasFlag(PadButton.Z),"unavailable menu panel can be skipped");
        check(ProfileStore.Clone(profile).Sources[0][PadButton.Left]==PadButton.A,"calibration survives profile export/import");
        profile.EnabledPorts=[true,false,false,false];
        var raw=new[]{new PadState(true,PadButton.A),new PadState(true,PadButton.Up),new PadState(false,0),new PadState(false,0)};
        check(profile.DesiredKeys(raw).SequenceEqual(new[]{37}),"disabled players do not produce keys");
        profile.EnabledPorts[1]=true;check(profile.DirectionConflicts().Count>0,"shared multiplayer directional mappings detected");
        profile.ApplyPlayerPreset(1);check(profile.DirectionConflicts().Count==0,"separate player preset resolves conflicts");
        var directory=Path.Combine(Path.GetTempPath(),"DanceBridge-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"profile.json"),"""{"Name":"Migrated","Ports":[{"Up":38},{},{},{}]}""");
        var store=new ProfileStore(directory);var library=store.Load();
        check(library.Active.Name=="Migrated"&&File.Exists(Path.Combine(directory,"legacy-profile.backup.json")),"profile library migration preserves original backup");
        var copy=ProfileStore.Clone(library.Active);copy.Id=Guid.NewGuid();copy.Name="Copy";library.Profiles.Add(copy);library.ActiveId=copy.Id;store.Save(library);
        check(store.Load().Active.Name=="Copy"&&store.Load().Profiles.Count==2,"profile library and active selection persist");
        var health=new MatHealth();health.Observe(PadButton.None);
        foreach(var expected in MatHealth.Steps){health.Observe(expected);health.Observe(PadButton.None);}
        var now=DateTimeOffset.Parse("2026-10-15T12:00:00Z");
        for(int i=0;i<5;i++){health.Observe(PadButton.Left,now.AddMilliseconds(i*200));health.Observe(PadButton.None,now.AddMilliseconds(i*200+50));}
        health.Observe(PadButton.None,now.AddSeconds(1));health.Observe(PadButton.Up,now.AddSeconds(2));health.Observe(PadButton.None,now.AddSeconds(4.1));
        check(health.Finished&&health.Passed.All(p=>p)&&health.RapidPassed&&health.HoldPassed,"health session verifies panels, opposite directions, rapid taps and hold");
        var skipped=new MatHealth();skipped.Observe(PadButton.Left);check(skipped.Step==0,"stuck initial input must be released before testing");skipped.Skip();check(!skipped.Passed[0],"skipped health check is not reported as passed");
        var released=new List<int>();bool fail=true;var router=new KeyRouter((key,down)=>{if(!down){if(key==37&&fail)throw new IOException("simulated");released.Add(key);}});router.Update([37,38]);try{router.Release();}catch(AggregateException){}check(released.Contains(38),"cleanup attempts other keys after one release fails");fail=false;router.Release();check(released.Contains(37),"failed releases remain eligible for retry");
        check(DriverManagement.IsDeviceSpecificPackage("oem10.inf","DeviceID=VID_057E&PID_0337"),"device-specific OEM package eligible for further removal checks");
        check(!DriverManagement.IsDeviceSpecificPackage("winusb.inf","VID_057E&PID_0337")&&!DriverManagement.IsDeviceSpecificPackage("oem10.inf","VID_057E&PID_0337 VID_1234&PID_5678")&&!DriverManagement.IsDeviceSpecificPackage("../oem10.inf","VID_057E&PID_0337"),"shared inbox, unrelated hardware and path traversal rejected");
    }
}
