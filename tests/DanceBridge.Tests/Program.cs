using DanceBridge.Core;
void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
if(args.Contains("--hardware")){
 using var adapter=new DanceBridge.Adapter();adapter.Open();
 var watch=System.Diagnostics.Stopwatch.StartNew();int count=0;PadState[] states=[];
 while(watch.Elapsed.TotalSeconds<5){states=ReportParser.Parse(adapter.Read());count++;}
 Console.WriteLine($"Read {count} valid reports in {watch.Elapsed.TotalSeconds:F2}s");
 for(int p=0;p<4;p++)Console.WriteLine($"Port {p+1}: {states[p]}");
 return;
}
byte[] report=new byte[37];report[0]=0x21;report[1]=0x10;report[2]=0xF0;report[3]=1;
var ports=ReportParser.Parse(report);Check(ports[0].Buttons.HasFlag(PadButton.Left|PadButton.Right|PadButton.Up|PadButton.Down),"opposite arrows preserved");Check(ports[0].Buttons.HasFlag(PadButton.Start),"second button byte");Check(!ports[1].Connected,"disconnected port");report[10]=0x20;report[11]=1;Check(ReportParser.Parse(report)[1].Buttons==PadButton.A,"independent second port");report[1]=0;Check(ReportParser.Parse(report)[0].Buttons==PadButton.None,"ignore stale disconnected buttons");
try{ReportParser.Parse(new byte[36]);throw new Exception("accepted malformed");}catch(FormatException){Check(true,"reject truncated report");}report[0]=0;try{ReportParser.Parse(report);throw new Exception("accepted header");}catch(FormatException){Check(true,"reject wrong header");}
var events=new List<(int,bool)>();var router=new KeyRouter((key,down)=>events.Add((key,down)));router.Update([37,37]);router.Update([37]);Check(events.Count==1,"shared mapping remains held");router.Update([38]);Check(events.SequenceEqual(new[]{(37,true),(37,false),(38,true)}),"release before press");router.Release();router.Release();Check(events.Count==4&&events[^1]==(38,false),"idempotent cleanup");
new Profile().Validate();try{new Profile{Ports=[]}.Validate();throw new Exception("accepted invalid profile");}catch(FormatException){Check(true,"profile validation");}
Check(WiiDdrLayout.Panels.Count == 8 && WiiDdrLayout.Panels.Select(p=>p.Source).Distinct().Count()==8,"eight distinct Wii DDR inputs");
Check(WiiDdrLayout.Panels.Single(p=>p.Source==PadButton.B) is {Row:1,Column:0} && WiiDdrLayout.Panels.Single(p=>p.Source==PadButton.A) is {Row:1,Column:2},"B and A at correct top corners");
Check(!WiiDdrLayout.Panels.Any(p=>p.Row==2&&p.Column==1) && !WiiDdrLayout.Panels.Any(p=>p.Row==3&&p.Column!=1),"center and lower corners are inactive");
Check(WiiDdrLayout.Panels.Single(p=>p.Source==PadButton.Z).Row==0 && WiiDdrLayout.Panels.Single(p=>p.Source==PadButton.Start).Row==0,"minus and plus on separate top strip");
var defaults=new Profile();Check(defaults.Ports[0][PadButton.A]==13 && defaults.Ports[0][PadButton.Z]==27,"Wii menu key defaults");
FeatureTests.Run(Check);
Console.WriteLine("All checks passed.");
