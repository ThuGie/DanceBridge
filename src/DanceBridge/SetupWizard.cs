using DanceBridge.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using System.IO;
using System.Text.Json;
namespace DanceBridge;

public sealed class SetupWizard : Window
{
    readonly MainWindow main;
    readonly object sync = new();
    readonly bool healthOnly;
    readonly TextBlock title = new() { FontSize = 25, FontWeight = FontWeights.SemiBold };
    readonly TextBlock progress = new() { Margin = new Thickness(0,12,0,20) };
    readonly TextBlock prompt = new() { FontSize=21,TextWrapping=TextWrapping.Wrap,MinHeight=55,Margin=new Thickness(0,14,0,10) };
    readonly TextBlock detail = new() { TextWrapping=TextWrapping.Wrap,MinHeight=60 };
    readonly ComboBox ports = new() { Width=180,HorizontalAlignment=System.Windows.HorizontalAlignment.Left };
    readonly Button next = new() { Content="Begin calibration" };
    readonly Button skip = new() { Content="Skip / not available",Margin=new Thickness(0,0,8,0) };
    readonly Button driver = new() { Content="Driver setup",Margin=new Thickness(0,12,0,0) };
    readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromMilliseconds(50) };
    readonly Grid mat = new() { Margin=new Thickness(0,12,0,12),MaxWidth=420,Height=210 };
    readonly ProgressBar completion = new() { Height=7,Maximum=20,Margin=new Thickness(0,8,0,8) };
    readonly ProgressBar gesture = new() { Height=10,Maximum=1,Margin=new Thickness(0,8,0,8) };
    readonly Dictionary<PadButton,Border> tiles = new();
    readonly TextBlock feedback = new() { FontSize=16,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };
    MatHealth health = new();
    readonly Button back = new() { Content="Previous panel",Margin=new Thickness(0,0,8,0) };
    readonly PadButton[] panels=[PadButton.Left,PadButton.Down,PadButton.Up,PadButton.Right,PadButton.B,PadButton.A,PadButton.Z,PadButton.Start];
    int stage, port, panelIndex;
    bool awaitRelease=true,waitingForKey,disconnected;
    PadButton captured, lastRaw;
    string calibrationMessage="";
    readonly HashSet<Key> assignmentKeysDown = new();
    DateTimeOffset lastChange=DateTimeOffset.UtcNow;
    public Profile Result { get; }
    readonly string draftPath;
    string lastDraft="";
    sealed class Draft { public Profile Profile {get;set;}=new();public int Panel {get;set;}public int Stage {get;set;}public int Port {get;set;} }
    public SetupWizard(MainWindow main,Profile profile,int port,bool healthOnly)
    {
        this.main=main;Result=profile;this.port=port;this.healthOnly=healthOnly;
        draftPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DanceBridge","setup-"+profile.Id+".json");
        if(!healthOnly&&File.Exists(draftPath))try{var draft=JsonSerializer.Deserialize<Draft>(File.ReadAllText(draftPath));if(draft is not null&&draft.Profile.Id==profile.Id&&draft.Panel>=0&&draft.Panel<=8&&draft.Port>=0&&draft.Port<4){draft.Profile.Validate();Result=draft.Profile;panelIndex=draft.Panel;stage=draft.Stage>=2?2:draft.Stage==1?1:0;this.port=draft.Port;calibrationMessage="Restored your saved setup progress.";}}catch(Exception){calibrationMessage="Saved setup could not be restored. Starting again.";}
        Title=healthOnly?"DanceBridge · Mat health":"DanceBridge · Guided setup";Width=720;Height=790;WindowStartupLocation=WindowStartupLocation.CenterOwner;Focusable=true;
        SetResourceReference(BackgroundProperty,"Page");SetResourceReference(ForegroundProperty,"Text");
        var root=new StackPanel{Margin=new Thickness(28)};Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};root.Children.Add(title);root.Children.Add(progress);
        for(int i=0;i<4;i++)ports.Items.Add("Port "+(i+1));ports.SelectedIndex=this.port;ports.IsEnabled=stage==0;ports.SelectionChanged+=(_,_)=>{if(stage==0)this.port=ports.SelectedIndex;};root.Children.Add(ports);
        root.Children.Add(completion);root.Children.Add(prompt);root.Children.Add(mat);root.Children.Add(feedback);root.Children.Add(gesture);root.Children.Add(detail);root.Children.Add(driver);
        for(int i=0;i<3;i++)mat.ColumnDefinitions.Add(new ColumnDefinition());
        for(int i=0;i<4;i++)mat.RowDefinitions.Add(new RowDefinition());
        AddTile(PadButton.Z,0,0,"−");AddTile(PadButton.Start,0,2,"+");
        AddTile(PadButton.B,1,0,"B");AddTile(PadButton.Up,1,1,"↑");AddTile(PadButton.A,1,2,"A");
        AddTile(PadButton.Left,2,0,"←");AddTile(PadButton.Down,3,1,"↓");AddTile(PadButton.Right,2,2,"→");
        driver.Click+=(_,_)=>new DriverSetupWindow(main.SuspendAdapter){Owner=this}.ShowDialog();
        var actions=new WrapPanel{Margin=new Thickness(0,20,0,0)};actions.Children.Add(back);actions.Children.Add(skip);actions.Children.Add(next);root.Children.Add(actions);
        back.Click+=(_,_)=>{lock(sync){if(stage==1&&panelIndex>0){panelIndex--;waitingForKey=false;awaitRelease=true;calibrationMessage="Repeat this panel to replace its mapping.";}else if(stage>=2){health=new MatHealth();stage=2;}}Refresh();Focus();};
        next.Click+=(_,_)=>Advance();skip.Click+=(_,_)=>Skip();PreviewKeyDown+=CaptureKey;
        PreviewKeyUp+=(_,e)=>{var key=e.Key==Key.System?e.SystemKey:e.Key;if(assignmentKeysDown.Remove(key))e.Handled=true;};
        Loaded+=(_,_)=>{main.RawReportReceived+=Observe;timer.Start();Refresh();};
        Closed+=(_,_)=>{timer.Stop();main.RawReportReceived-=Observe;};timer.Tick+=(_,_)=>Refresh();
    }
    void Observe(PadState[] reports)
    {
        lock(sync)
        {
            var raw=reports[port];disconnected=!raw.Connected;if(raw.Buttons!=lastRaw)lastChange=DateTimeOffset.UtcNow;lastRaw=raw.Buttons;
            if(disconnected){awaitRelease=true;waitingForKey=false;health.ResetCapture();return;}
            if(stage==1)
            {
                if(awaitRelease){if(raw.Buttons==PadButton.None)awaitRelease=false;return;}
                if(waitingForKey)return;
                var inputs=Enum.GetValues<PadButton>().Where(b=>b!=PadButton.None&&raw.Buttons.HasFlag(b)).ToArray();
                if(inputs.Length==1){captured=inputs[0];waitingForKey=true;calibrationMessage="Detected signal "+captured+". Now press the keyboard key to assign.";Dispatcher.BeginInvoke(new Action(()=>{if(IsActive)System.Windows.Input.Keyboard.Focus(this);}));}
                else if(inputs.Length>1)calibrationMessage="Multiple signals detected. Release everything and press one panel.";
            }
            if(stage==2){health.Observe(Result.PhysicalButtons(port,raw.Buttons));while(!health.Finished&&health.Step<MatHealth.Steps.Length&&MatHealth.Steps[health.Step] is var target&&Enum.GetValues<PadButton>().Where(b=>b!=PadButton.None&&target.HasFlag(b)).Any(b=>Result.Sources[port][b]==PadButton.None))health.Skip();if(health.Finished)stage=3;}
        }
    }
    void CaptureKey(object sender,System.Windows.Input.KeyEventArgs e)
    {
        // Escape is a valid DDR menu binding. Never let WPF treat it as dialog cancellation.
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(assignmentKeysDown.Contains(key))
        {
            if(e.IsRepeat){e.Handled=true;return;}
            // A fresh key-down is a new press even if focus changed before the old key-up.
            assignmentKeysDown.Remove(key);
        }
        if(key==Key.Escape)e.Handled=true;
        lock(sync)
        {
            if(stage!=1||!waitingForKey)return;
            e.Handled=true;
            int vk=KeyInterop.VirtualKeyFromKey(key);if(vk<1||vk>254)return;
            if(e.IsRepeat)return;
            assignmentKeysDown.Add(key);
            Result.BindPanel(port,panels[panelIndex],captured);Result.Ports[port][panels[panelIndex]]=vk;
            panelIndex++;waitingForKey=false;awaitRelease=true;calibrationMessage="Mapping captured.";
            if(panelIndex==panels.Length){stage=2;calibrationMessage="";}
        }
        Refresh();
        // A mapping key must not activate the control that previously had keyboard focus.
        System.Windows.Input.Keyboard.Focus(this);
    }
    void Advance()
    {
        lock(sync)
        {
            if(stage==0){if(!main.SnapshotRaw()[port].Connected)return;stage=healthOnly?2:1;ports.IsEnabled=false;Result.EnabledPorts[port]=true;}
            else if(stage==2&&health.Finished)stage=3;
            else if(stage==3){if(!healthOnly){Result.Validate();}DialogResult=true;return;}
        }
        Refresh();Focus();
    }
    void Skip()
    {
        lock(sync)
        {
            if(stage==1&&panelIndex>=4){Result.BindPanel(port,panels[panelIndex],PadButton.None);panelIndex++;waitingForKey=false;awaitRelease=true;if(panelIndex==panels.Length)stage=2;}
            else if(stage==2)health.Skip();
        }
        Refresh();Focus();
    }
    void Refresh()
    {
        lock(sync)
        {
            if(!healthOnly)try{var draft=JsonSerializer.Serialize(new Draft{Profile=Result,Panel=panelIndex,Stage=stage,Port=port});if(draft!=lastDraft){Directory.CreateDirectory(Path.GetDirectoryName(draftPath)!);File.WriteAllText(draftPath+".tmp",draft);File.Move(draftPath+".tmp",draftPath,true);lastDraft=draft;}}catch(Exception ex){calibrationMessage="Could not save setup progress: "+ex.Message;}
            title.Text=healthOnly?"Mat health check":"Set up your dance mat";
            progress.Text=stage switch{0=>"1 · Adapter and port",1=>"2 · Physical panels and keyboard keys",2=>"3 · Input health check",_=>"4 · Ready"};
            driver.Visibility=stage==0?Visibility.Visible:Visibility.Collapsed;
            back.Visibility=stage==1&&panelIndex>0?Visibility.Visible:Visibility.Collapsed;
            back.Content=stage==1?"Previous panel":"Restart health test";
            skip.Content="My mat has no " +(stage==1&&panelIndex<panels.Length?WiiDdrLayout.Label(panels[panelIndex]):"panel");
            skip.Visibility=stage==1&&panelIndex>=4&&!waitingForKey?Visibility.Visible:Visibility.Collapsed;
            next.Visibility=stage is 0 or 3?Visibility.Visible:Visibility.Collapsed;
            RenderLive();
            if(stage==0){prompt.Text="Connect your adapter and mat";detail.Text=main.AdapterStatus+"\nChoose the port containing your mat. Driver setup is available here if needed.";next.Content=healthOnly?"Begin health test":"Begin calibration";next.IsEnabled=main.SnapshotRaw()[port].Connected;}
            if(stage==1){prompt.Text=$"Panel {panelIndex+1}/8 · Step on {WiiDdrLayout.Label(panels[panelIndex])}";detail.Text=disconnected?"Mat disconnected. Reconnect it to continue.":awaitRelease?"Release every panel before the next capture.":waitingForKey?calibrationMessage:"Press only the requested physical panel, then press its target keyboard key.\n"+calibrationMessage;}
            if(stage==2){prompt.Text=health.Prompt;detail.Text="Each check advances automatically after the requested input and release.\n"+(lastRaw!=PadButton.None&&DateTimeOffset.UtcNow-lastChange>TimeSpan.FromSeconds(5)?"Input has been held for over five seconds. If the mat is untouched, check for a stuck panel.":"White panels show held inputs.");}
            if(stage==3){int passed=health.Passed.Count(p=>p)+(health.RapidPassed?1:0)+(health.HoldPassed?1:0);prompt.Text=passed==12?"✓ Your mat passed every check":"✓ All available inputs checked";detail.Text=ResultSummary()+"\n"+(healthOnly?"Your profile has not been changed.":"Save these verified mappings to your profile. Output stays paused.");next.Content=healthOnly?"Done":"Save setup";}
        }
    }
    void AddTile(PadButton button,int row,int column,string label)
    {
        var tile=new Border{CornerRadius=new CornerRadius(10),Margin=new Thickness(4),BorderThickness=new Thickness(2),Child=new TextBlock{Text=label,FontSize=25,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        tile.SetResourceReference(Border.BackgroundProperty,"Page");tile.SetResourceReference(Border.BorderBrushProperty,"Text");Grid.SetRow(tile,row);Grid.SetColumn(tile,column);mat.Children.Add(tile);tiles[button]=tile;
    }
    void RenderLive()
    {
        var held=stage>=2?Result.PhysicalButtons(port,lastRaw):PadButton.None;
        var target=stage==1&&panelIndex<panels.Length?panels[panelIndex]:stage==2&&!health.Finished?health.Step<10?MatHealth.Steps[health.Step]:health.Step==10?PadButton.Left:PadButton.Up:PadButton.None;
        foreach(var (button,tile) in tiles){bool active=held.HasFlag(button)||(stage==1&&waitingForKey&&target==button);bool wanted=target.HasFlag(button);tile.Opacity=stage==0||stage==3||wanted||active?1:.38;tile.BorderThickness=new Thickness(wanted?4:1);if(active){tile.Background=System.Windows.Media.Brushes.White;((TextBlock)tile.Child).Foreground=System.Windows.Media.Brushes.Black;}else{tile.SetResourceReference(Border.BackgroundProperty,"Page");((TextBlock)tile.Child).SetResourceReference(TextBlock.ForegroundProperty,"Text");}}
        completion.Value=stage==0?0:stage==1?panelIndex:stage==2?8+health.Step:20;
        gesture.Visibility=stage==2&&health.Step>=10?Visibility.Visible:Visibility.Collapsed;
        gesture.Value=health.Step==10?health.TapCount/5.0:health.HoldProgress;
        feedback.Text=disconnected?"Connection lost — reconnect your mat to continue.":stage==1?waitingForKey?"✓ Panel detected · Press its keyboard key now":awaitRelease?"Release the mat to continue":"Step on the highlighted panel":stage==2?health.HoldProgress>=1?"✓ Hold detected · Release Up to complete":held!=PadButton.None?"Input detected: "+held+" · Release to confirm":health.WaitingForRelease?"Release all panels":"Ready for your next input":stage==3?"Checks complete · "+(health.Passed.Count(p=>p)+(health.RapidPassed?1:0)+(health.HoldPassed?1:0))+" verified":"Connect your mat, then start";
        if(stage==2)detail.Text="Checks advance automatically after a valid press and release. White panels show held inputs.";
    }
    string ResultSummary(){var unverified=MatHealth.Steps.Where((_,i)=>!health.Passed[i]).Select(b=>b.ToString()).ToList();if(!health.RapidPassed)unverified.Add("five Left taps in two seconds");if(!health.HoldPassed)unverified.Add("two-second Up hold");return unverified.Count==0?"All panels, opposite-arrow pairs, rapid taps and hold were verified.":"Not verified: "+string.Join("; ",unverified)+". Skipped checks do not prove a hardware fault.";}
}
