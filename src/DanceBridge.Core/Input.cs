namespace DanceBridge.Core;
[Flags] public enum PadButton { None=0,A=1,B=2,X=4,Y=8,Left=16,Right=32,Down=64,Up=128,Start=256,Z=512,R=1024,L=2048 }
public record PadState(bool Connected, PadButton Buttons);
public static class ReportParser {
 public static PadState[] Parse(ReadOnlySpan<byte> data) {
  if(data.Length!=37 || data[0]!=0x21) throw new FormatException("Expected a 37-byte WUP-028 input report (0x21).");
  var ports=new PadState[4];
  for(int p=0;p<4;p++){ int i=1+p*9; bool connected=(data[i]&0x30)!=0; ports[p]=new(connected,connected?(PadButton)(data[i+1]|((data[i+2]&15)<<8)):PadButton.None); }
  return ports;
 }
}
public sealed class KeyRouter(Action<int,bool> emit) {
 private HashSet<int> held=[];
 public void Update(IEnumerable<int> desired){var next=desired.ToHashSet(); foreach(var key in held.Except(next).ToArray()){emit(key,false);held.Remove(key);} foreach(var key in next.Except(held).ToArray()){emit(key,true);held.Add(key);} }
 public void Release(){List<Exception> errors=[];foreach(var key in held.ToArray()){try{emit(key,false);held.Remove(key);}catch(Exception ex){errors.Add(ex);}}if(errors.Count>0)throw new AggregateException("Some generated keys could not be released.",errors);}
}
public sealed class Profile {
 public int Version {get;set;}=2;
 public Guid Id {get;set;}=Guid.NewGuid();
 public string Name {get;set;}="My dance mat";
 public bool[] EnabledPorts {get;set;}=[true,false,false,false];
 public string[] PlayerNames {get;set;}=["Player 1","Player 2","Player 3","Player 4"];
 public string? GamePath {get;set;}
 public Dictionary<PadButton,PadButton>[] Sources {get;set;}=Enumerable.Range(0,4).Select(_=>Enum.GetValues<PadButton>().Where(b=>b!=PadButton.None).ToDictionary(b=>b,b=>b)).ToArray();
 public Dictionary<PadButton,int>[] Ports {get;set;}=Enumerable.Range(0,4).Select(_=>new Dictionary<PadButton,int>{{PadButton.Left,0x25},{PadButton.Up,0x26},{PadButton.Right,0x27},{PadButton.Down,0x28},{PadButton.Start,0x0D},{PadButton.A,0x0D},{PadButton.Z,0x1B},{PadButton.B,0x1B}}).ToArray();
 public PadButton PhysicalButtons(int port,PadButton raw){PadButton result=PadButton.None;foreach(var (panel,source) in Sources[port])if(source!=PadButton.None&&raw.HasFlag(source))result|=panel;return result;}
 public IEnumerable<int> DesiredKeys(PadState[] raw)=>raw.SelectMany((s,p)=>s.Connected&&EnabledPorts[p]?Ports[p].Where(m=>PhysicalButtons(p,s.Buttons).HasFlag(m.Key)).Select(m=>m.Value):[]);
 public void BindPanel(int port,PadButton panel,PadButton raw){var old=Sources[port].GetValueOrDefault(panel);foreach(var other in Sources[port].Where(k=>k.Key!=panel&&k.Value==raw&&raw!=PadButton.None).Select(k=>k.Key).ToArray())Sources[port][other]=old;Sources[port][panel]=raw;}
 public void ApplyPlayerPreset(int port){int[][] keys=[[0x25,0x28,0x26,0x27],[0x41,0x53,0x57,0x44],[0x4A,0x4B,0x49,0x4C],[0x46,0x47,0x54,0x48]];PadButton[] directions=[PadButton.Left,PadButton.Down,PadButton.Up,PadButton.Right];for(int i=0;i<4;i++)Ports[port][directions[i]]=keys[port][i];}
 public IReadOnlyList<string> DirectionConflicts(){var owners=new Dictionary<int,List<string>>();for(int p=0;p<4;p++)if(EnabledPorts[p])foreach(var d in new[]{PadButton.Left,PadButton.Down,PadButton.Up,PadButton.Right})if(Ports[p].TryGetValue(d,out int key)){if(!owners.ContainsKey(key))owners[key]=[];owners[key].Add($"{PlayerNames[p]} {d}");}return owners.Values.Where(v=>v.Count>1).Select(v=>string.Join(" / ",v)).ToArray();}
 public void Validate(){if(Version!=2||Id==Guid.Empty||string.IsNullOrWhiteSpace(Name)||Name.Length>80||Ports is null||Ports.Length!=4||Ports.Any(p=>p is null||p.Any(k=>!Enum.IsDefined(k.Key)||k.Key==PadButton.None||k.Value<1||k.Value>254))||Sources is null||Sources.Length!=4||Sources.Any(p=>p is null||p.Any(k=>!Enum.IsDefined(k.Key)||k.Key==PadButton.None||!Enum.IsDefined(k.Value))||p.Values.Where(v=>v!=PadButton.None).Distinct().Count()!=p.Values.Count(v=>v!=PadButton.None))||EnabledPorts is null||EnabledPorts.Length!=4||PlayerNames is null||PlayerNames.Length!=4||PlayerNames.Any(n=>string.IsNullOrWhiteSpace(n)||n.Length>40)||GamePath is not null&&(!Path.IsPathFullyQualified(GamePath)||!GamePath.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)))throw new FormatException("Invalid profile data or unsupported profile version.");}
}
