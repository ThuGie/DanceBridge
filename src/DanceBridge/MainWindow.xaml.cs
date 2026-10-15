using DanceBridge.Core;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
namespace DanceBridge;

public partial class MainWindow : Window
{
    readonly object gate = new();
    readonly CancellationTokenSource stop = new();
    readonly KeyRouter router = new(Keyboard.Emit);
    Profile profile = new();
    ProfileLibrary library = new();
    ProfileStore store = null!;
    bool dirty, refreshingProfiles, gameFocused = true, requireRelease;
    internal event Action<PadState[]>? RawReportReceived;
    internal PadState[] SnapshotRaw() { lock (gate) return states.ToArray(); }
    internal string AdapterStatus { get { lock (gate) return status; } }
    internal void SuspendAdapter(bool value) { driverSetupActive = value; }
    PadState[] states = Enumerable.Range(0, 4).Select(_ => new PadState(false, PadButton.None)).ToArray();
    bool enabled, learnPanel;
    volatile bool driverSetupActive;
    string status = "Searching for adapter…";
    string? lastError;
    long reports;
    PadButton? learning;
    int learnPort;
    readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DanceBridge");
    readonly System.Windows.Forms.NotifyIcon tray = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly List<TextBlock> portLabels = [];
    readonly List<Button> portButtons = [];
    readonly Dictionary<PadButton, Border> padTiles = [];
    readonly Dictionary<PadButton, TextBlock> panelStateLabels = [];
    readonly Dictionary<PadButton, TextBlock> mappingLabels = [];
    public MainWindow()
    {
        InitializeComponent(); Directory.CreateDirectory(folder);
        store = new ProfileStore(folder);
        try { library = store.Load(); profile = ProfileStore.Clone(library.Active); } catch (Exception ex) { lastError = ex.Message; library = new ProfileLibrary { ActiveId = profile.Id, Profiles = [profile] }; Hint.Text = "Profile recovery needed: " + ex.Message; }
        ReloadProfilePicker();
        for (int p = 0; p < 4; p++)
        {
            int index = p;
            Port.Items.Add("Port " + (p + 1));
            var label = new TextBlock { Text = "Port " + (p + 1), LineHeight = 21 };
            var select = new Button { Content = label, Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, Padding = new Thickness(14, 12, 14, 12) };
            select.Click += (_, _) => Port.SelectedIndex = index;
            portLabels.Add(label); portButtons.Add(select); Ports.Children.Add(select);
        }
        foreach (var panel in WiiDdrLayout.Panels)
        {
            var input = panel.Source;
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock { Text = panel.Symbol, FontSize = panel.Row == 0 ? 18 : 30, FontWeight = FontWeights.SemiBold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            var panelState = new TextBlock { Text = panel.Row == 0 ? "" : panel.Label, FontSize = panel.Row == 0 ? 8 : 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, panel.Row == 0 ? 0 : 5, 0, 0) };
            panelStateLabels[input] = panelState;
            content.Children.Add(panelState);
            var tile = new Border { Child = content, CornerRadius = new CornerRadius(12), Margin = new Thickness(4), BorderThickness = new Thickness(1) };
            tile.Cursor = System.Windows.Input.Cursors.Hand;
            tile.ToolTip = "Assign a keyboard key to " + WiiDdrLayout.Label(input);
            tile.MouseLeftButtonUp += (_, _) => { Pause(); learning = input; learnPanel = false; learnPort = Port.SelectedIndex; Hint.Text = $"Press a keyboard key for {WiiDdrLayout.Label(input)}. Escape cancels."; Focus(); };
            Grid.SetRow(tile, panel.Row); Grid.SetColumn(tile, panel.Column);
            padTiles[input] = tile; PadGrid.Children.Add(tile);
        }
        var center = new TextBlock { Text = "DANCE\nBRIDGE", FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Rest area — no input" };
        center.SetResourceReference(TextBlock.ForegroundProperty, "Decorative");
        Grid.SetRow(center, 2); Grid.SetColumn(center, 1); PadGrid.Children.Add(center);
        Port.SelectedIndex = 0;
        try { var saved=JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(Path.Combine(folder,"window.json")));if(saved is not null){Port.SelectedIndex=Math.Clamp(saved.Port,0,3);Width=Math.Max(MinWidth,Math.Min(saved.Width,SystemParameters.WorkArea.Width));Height=Math.Max(MinHeight,Math.Min(saved.Height,SystemParameters.WorkArea.Height));if(saved.Maximized)WindowState=WindowState.Maximized;} }catch(IOException){}catch(JsonException){}
        ThemeSelect.SelectedIndex = (int)ThemeManager.Current;
        using(var iconStream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/dancebridge.ico")).Stream) tray.Icon = new System.Drawing.Icon(iconStream); tray.Visible = true; tray.Text = "DanceBridge";
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        var menu = new System.Windows.Forms.ContextMenuStrip(); menu.Items.Add("Show", null, (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); })); menu.Items.Add("Pause", null, (_, _) => Dispatcher.Invoke(Pause)); menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close)); tray.ContextMenuStrip = menu;
        timer.Tick += (_, _) => Refresh(); timer.Start(); PreviewKeyDown += CaptureKey;
        SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; UpdateTitleBar(); HwndSource.FromHwnd(handle).AddHook(Hook); if (!RegisterHotKey(handle, 1, 0x4003, 0x50)) Hint.Text = "Ctrl+Alt+P is unavailable. Pause using the window or tray."; };
        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
        _ = Task.Run(ReadLoop);
        if (!File.Exists(Path.Combine(folder,"setup-complete.json"))) Loaded += (_,_) => Dispatcher.BeginInvoke(new Action(()=>OpenWorkflow(false)));
    }
    void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { if (e.Mode == Microsoft.Win32.PowerModes.Suspend) Dispatcher.Invoke(Pause); }
    async Task ReadLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            if (driverSetupActive) { await Task.Delay(100); continue; }
            try
            {
                using var adapter = new Adapter(); adapter.Open(); lock (gate) status = "GameCube USB adapter connected · direct input";
                while (!stop.IsCancellationRequested && !driverSetupActive)
                {
                    var parsed = ReportParser.Parse(adapter.Read());
                    lock (gate)
                    {
                        states = parsed; reports++;
                        if (enabled)
                        {
                            bool focused = GameFocus.Matches(profile.GamePath);
                            if (!focused) requireRelease = true;
                            gameFocused = focused;
                            if (requireRelease && parsed.Where((s, p) => profile.EnabledPorts[p]).All(s => !s.Connected || s.Buttons == PadButton.None)) requireRelease = false;
                            router.Update(focused && !requireRelease ? profile.DesiredKeys(parsed) : []);
                        }
                    }
                    RawReportReceived?.Invoke(parsed);
                }
            }
            catch (Exception ex) { lock (gate) { enabled = false; try { router.Release(); } catch { } states = Enumerable.Range(0, 4).Select(_ => new PadState(false, PadButton.None)).ToArray(); status = ex.Message; lastError = ex.ToString(); } try { await Task.Delay(1000, stop.Token); } catch (OperationCanceledException) { break; } }
        }
    }
    void Refresh()
    {
        lock (gate)
        {
            Status.Text = status;
            Toggle.Content = enabled ? "Pause output" : "Start output";
            Detail.Text = enabled ? !gameFocused ? "Waiting for the selected game · keys released" : requireRelease ? "Release all panels before output resumes" : "Keyboard output active · " + (profile.GamePath is null ? "all applications" : Path.GetFileNameWithoutExtension(profile.GamePath)) : "Output paused · your mat can be tested safely";
            OutputBadge.Text = enabled ? "LIVE" : "PAUSED";
            OutputBadge.Foreground = Brush(enabled ? "LiveText" : "BadgeText");
            if (OutputBadge.Parent is Border badge) badge.Background = Brush(enabled ? "LiveBackground" : "BadgeBackground");
            for (int p = 0; p < 4; p++)
            {
                portLabels[p].Text = $"Port {p + 1} · {(profile.EnabledPorts[p] ? "On" : "Off")}\n{profile.PlayerNames[p]} · {(states[p].Connected ? "Connected" : "No mat")}";
                portButtons[p].Background = Brush(p == Port.SelectedIndex ? "Selection" : "Sidebar");
                portButtons[p].BorderBrush = Brush(p == Port.SelectedIndex ? "SelectionBorder" : "Border");
                portButtons[p].Foreground = Brush(ThemeManager.Current == AppTheme.Ddr && p == Port.SelectedIndex ? "DdrInk" : "Text");
            }
            if (Port.SelectedIndex >= 0)
            {
                PadTitle.Text = $"Port {Port.SelectedIndex + 1} · Live input";
                var raw = states[Port.SelectedIndex];
                var selected = new PadState(raw.Connected, profile.PhysicalButtons(Port.SelectedIndex, raw.Buttons));
                PadSubtitle.Text = selected.Connected ? "Press a panel to test. Click a tile to assign." : "No mat on this port. Connect one to test.";
                var held = Enum.GetValues<PadButton>().Where(b => b != PadButton.None && selected.Buttons.HasFlag(b)).ToArray();
                InputSummary.Text = !selected.Connected ? "No mat connected" : held.Length == 0 ? "No panels pressed" : "Held: " + string.Join(" + ", held.Select(WiiDdrLayout.Label));
                MatLegend.Text = ThemeManager.Current == AppTheme.Ddr ? "Colored = idle. White + PRESSED = held.\nThe center is a rest area, not a button." : "Held panels show PRESSED.\nThe center is a rest area, not a button.";
                foreach (var (button, tile) in padTiles)
                {
                    bool pressed = selected.Connected && selected.Buttons.HasFlag(button);
                    var accent = button switch { PadButton.Up => "DdrBlue", PadButton.Down => "DdrOrange", PadButton.Left => "DdrPink", PadButton.Right => "DdrMint", PadButton.A => "DdrViolet", PadButton.B => "DdrAmber", PadButton.Start => "DdrLime", _ => "DdrCoral" };
                    bool ddr = ThemeManager.Current == AppTheme.Ddr;
                    tile.Background = Brush(pressed ? "PressedFill" : ddr ? accent : "Tile");
                    tile.BorderBrush = Brush(pressed ? "PressedInk" : ddr ? accent : "Border");
                    tile.BorderThickness = new Thickness(pressed ? 4 : 1);
                    TextElementForeground(tile, pressed ? "PressedInk" : ddr ? "DdrInk" : "TileText");
                    panelStateLabels[button].Text = pressed ? "PRESSED" : WiiDdrLayout.Panels.Single(p => p.Source == button).Row == 0 ? "" : WiiDdrLayout.Panels.Single(p => p.Source == button).Label;
                }
                foreach (var (button, label) in mappingLabels) label.Foreground = Brush(selected.Connected && selected.Buttons.HasFlag(button) ? "Accent" : "Text");
            }
            if (learnPanel) { var pressed = states[learnPort].Buttons; var buttons = Enum.GetValues<PadButton>().Where(b => b != PadButton.None && pressed.HasFlag(b)).ToArray(); if (buttons.Length == 1) { learning = buttons[0]; learnPanel = false; Hint.Text = $"Detected {WiiDdrLayout.Label(learning.Value)}. Press its target keyboard key. Escape cancels."; } }
        }
    }
    static SolidColorBrush Brush(string key) => ThemeManager.Brush(key);
    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelect.SelectedIndex < 0) return;
        var selected = (AppTheme)ThemeSelect.SelectedIndex;
        if (selected == ThemeManager.Current) return;
        ThemeManager.Apply(selected); UpdateTitleBar();
        try { ThemeManager.Save(); } catch (Exception ex) { Hint.Text = "Theme changed, but could not be saved: " + ex.Message; }
    }
    void UpdateTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = ThemeManager.Current == AppTheme.Light ? 0 : 1;
        DwmSetWindowAttribute(handle, 20, ref dark, 4);
    }
    static void TextElementForeground(Border tile, string color)
    {
        if (tile.Child is StackPanel panel) foreach (TextBlock text in panel.Children) text.Foreground = Brush(color);
    }
    void Pause() { lock (gate) { enabled = false; try { router.Release(); } catch (Exception ex) { Hint.Text = ex.Message; } } }
    void Toggle_Click(object sender, RoutedEventArgs e) { lock (gate) { if (enabled) { Pause(); return; } if (!states.Where((s,p)=>profile.EnabledPorts[p]).Any(s => s.Connected)) { Hint.Text = "Enable a port with a connected mat before starting output."; return; } var conflicts=profile.DirectionConflicts(); if(conflicts.Count>0 && System.Windows.MessageBox.Show(this,"Shared direction keys:\n"+string.Join("\n",conflicts)+"\nContinue with shared keys?","Mapping conflict",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return; learning = null; learnPanel = false; requireRelease=true; enabled = true; } }
    void Port_Changed(object sender, SelectionChangedEventArgs e) { if (Port.SelectedIndex >= 0) { BuildMappings(); RefreshPlayerControls(); } }
    void BuildMappings()
    {
        Mappings.Children.Clear(); ExtraMappings.Children.Clear(); mappingLabels.Clear();
        PadButton[] order = [PadButton.Up, PadButton.Down, PadButton.Left, PadButton.Right, PadButton.B, PadButton.A, PadButton.Z, PadButton.Start, PadButton.X, PadButton.Y, PadButton.L, PadButton.R];
        foreach (var button in order)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            var label = new TextBlock { Text = WiiDdrLayout.Label(button), ToolTip = "GameCube signal: " + button, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Medium };
            mappingLabels[button] = label; row.Children.Add(label);
            var combo = new ComboBox { ToolTip = "Keyboard key for " + button }; combo.Items.Add(new KeyChoice(0, "Unassigned"));
            foreach (var key in Enum.GetValues<Key>().Where(k => k > Key.None && k < Key.DeadCharProcessed).Distinct()) { int vk = KeyInterop.VirtualKeyFromKey(key); if (vk > 0 && vk < 255) combo.Items.Add(new KeyChoice(vk, key.ToString())); }
            lock (gate) { int value = profile.Ports[Port.SelectedIndex].GetValueOrDefault(button); combo.SelectedItem = combo.Items.Cast<KeyChoice>().FirstOrDefault(k => k.Code == value) ?? combo.Items[0]; }
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is KeyChoice choice) { Pause(); lock (gate) { if (choice.Code == 0) profile.Ports[Port.SelectedIndex].Remove(button); else profile.Ports[Port.SelectedIndex][button] = choice.Code; } MarkDirty(); } };
            Grid.SetColumn(combo, 1); row.Children.Add(combo);
            var learn = new Button { Content = "Set", ToolTip = "Press a key to assign " + button, Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(8, 0, 0, 0) };
            learn.Click += (_, _) => { Pause(); learning = button; learnPanel = false; learnPort = Port.SelectedIndex; Hint.Text = $"Press a keyboard key for {WiiDdrLayout.Label(button)}. Escape cancels."; Focus(); };
            Grid.SetColumn(learn, 2); row.Children.Add(learn);
            if (button is PadButton.X or PadButton.Y or PadButton.L or PadButton.R) ExtraMappings.Children.Add(row); else Mappings.Children.Add(row);
        }
    }
    record KeyChoice(int Code, string Label) { public override string ToString() => Label; }
    void CaptureKey(object sender, System.Windows.Input.KeyEventArgs e) { if (learning is null) return; e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key; if (key == Key.Escape) { learning = null; Hint.Text = "Mapping cancelled."; return; } int vk = KeyInterop.VirtualKeyFromKey(key); if (vk < 1 || vk > 254) return; lock (gate) profile.Ports[learnPort][learning.Value] = vk; Hint.Text = $"{WiiDdrLayout.Label(learning.Value)} → {key}. This mapping is saved automatically."; learning = null; MarkDirty(); BuildMappings(); }
    void Setup_Click(object sender, RoutedEventArgs e) => OpenWorkflow(false);
    void Health_Click(object sender, RoutedEventArgs e) => OpenWorkflow(true);
    void OpenWorkflow(bool healthOnly) { Pause(); learning=null; var wizard=new SetupWizard(this,ProfileStore.Clone(profile),Math.Max(Port.SelectedIndex,0),healthOnly){Owner=this};if(wizard.ShowDialog()==true&&!healthOnly){lock(gate)profile=wizard.Result;MarkDirty();BuildMappings();RefreshPlayerControls();try{SaveWorking();File.Delete(Path.Combine(folder,"setup-"+profile.Id+".json"));File.WriteAllText(Path.Combine(folder,"setup-complete.json"),"true");Hint.Text="Setup saved. Output remains paused.";}catch(Exception ex){Hint.Text=ex.Message;}} }
    void Save_Click(object sender, RoutedEventArgs e) { try { SaveWorking(); Hint.Text="Profile saved."; }catch(Exception ex){Hint.Text=ex.Message;} }
    void SaveWorking(){lock(gate){profile.Validate();var candidate=new ProfileLibrary{ActiveId=profile.Id,Profiles=library.Profiles.Select(ProfileStore.Clone).ToList()};int index=candidate.Profiles.FindIndex(p=>p.Id==profile.Id);if(index<0)candidate.Profiles.Add(ProfileStore.Clone(profile));else candidate.Profiles[index]=ProfileStore.Clone(profile);store.Save(candidate);library=candidate;dirty=false;}ReloadProfilePicker();SaveProfileButton.Content="Save";}
    void MarkDirty(){dirty=true;SaveProfileButton.Content="Save •";try{SaveWorking();}catch(Exception ex){Hint.Text="Could not save settings: "+ex.Message;}}
    bool ResolveDirty(){if(!dirty)return true;var answer=System.Windows.MessageBox.Show(this,"Save changes to ‘"+profile.Name+"’ before continuing?","Unsaved changes",MessageBoxButton.YesNoCancel);if(answer==MessageBoxResult.Cancel)return false;if(answer==MessageBoxResult.Yes){try{SaveWorking();}catch(Exception ex){Hint.Text=ex.Message;return false;}}return true;}
    void ReloadProfilePicker(){refreshingProfiles=true;ProfileSelect.ItemsSource=library.Profiles.ToArray();ProfileSelect.SelectedItem=library.Profiles.FirstOrDefault(p=>p.Id==profile.Id);refreshingProfiles=false;}
    void Profile_Changed(object sender,SelectionChangedEventArgs e){if(refreshingProfiles||ProfileSelect.SelectedItem is not Profile selected||selected.Id==profile.Id)return;Pause();var id=selected.Id;if(!ResolveDirty()){ReloadProfilePicker();return;}try{lock(gate){var previous=library.ActiveId;library.ActiveId=id;try{store.Save(library);}catch{library.ActiveId=previous;throw;}profile=ProfileStore.Clone(library.Active);dirty=false;}SaveProfileButton.Content="Save";BuildMappings();RefreshPlayerControls();}catch(Exception ex){Hint.Text=ex.Message;}ReloadProfilePicker();}
    void ManageProfiles_Click(object sender,RoutedEventArgs e){Pause();if(!ResolveDirty())return;var manager=new ProfilesWindow(library,store){Owner=this};if(manager.ShowDialog()!=true)return;lock(gate){library=manager.Result;profile=ProfileStore.Clone(library.Active);dirty=false;}ReloadProfilePicker();BuildMappings();RefreshPlayerControls();SaveProfileButton.Content="Save";}
    void PlayerSettings_Click(object sender,RoutedEventArgs e){Pause();var settings=new PlayerSettingsWindow(ProfileStore.Clone(profile)){Owner=this};if(settings.ShowDialog()==true){lock(gate)profile=settings.Result;MarkDirty();RefreshPlayerControls();BuildMappings();}}
    void RefreshPlayerControls(){if(Port.SelectedIndex<0)return;Destination.Text=profile.GamePath is null?"Destination: all applications":"Destination: "+Path.GetFileNameWithoutExtension(profile.GamePath);}
    void Import_Click(object sender, RoutedEventArgs e) { if(!ResolveDirty())return;var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "DanceBridge profile|*.json" }; if (dialog.ShowDialog() != true) return; try { var loaded=ProfileStore.Parse(File.ReadAllText(dialog.FileName));loaded.Id=Guid.NewGuid();var basis=loaded.Name;int suffix=2;while(library.Profiles.Any(p=>p.Name.Equals(loaded.Name,StringComparison.OrdinalIgnoreCase)))loaded.Name=basis+" "+suffix++; Pause(); lock(gate)profile=loaded;SaveWorking();BuildMappings();RefreshPlayerControls();Hint.Text="Profile imported into your library.";}catch(Exception ex){Hint.Text="Import failed: "+ex.Message;} }
    void Export_Click(object sender, RoutedEventArgs e) { var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "DanceBridge profile|*.json", FileName = "DanceBridge-profile.json" }; if (dialog.ShowDialog() == true) try { lock (gate) File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true })); } catch (Exception ex) { Hint.Text = ex.Message; } }
    void Diagnostics_Click(object sender, RoutedEventArgs e) { var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Diagnostic report|*.txt", FileName = "DanceBridge-diagnostics.txt" }; if (dialog.ShowDialog() == true) try { lock (gate) File.WriteAllText(dialog.FileName, $"DanceBridge\nOS: {Environment.OSVersion}\nStatus: {status}\nReports: {reports}\nLast error: {lastError}\n"); Hint.Text = "Diagnostics exported."; } catch (Exception ex) { Hint.Text = ex.Message; } }
    void Driver_Click(object sender, RoutedEventArgs e)
    {
        Pause();
        new DriverSetupWindow(active => driverSetupActive = active) { Owner = this }.ShowDialog();
    }
    void Tray_Click(object sender, RoutedEventArgs e) => Hide();
    void Startup_Changed(object sender, RoutedEventArgs e) { }
    sealed class WindowPreferences { public int Port {get;set;} public double Width {get;set;} public double Height {get;set;} public bool Maximized {get;set;} }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e){Pause();if(!ResolveDirty()){e.Cancel=true;return;}try{var bounds=RestoreBounds;File.WriteAllText(Path.Combine(folder,"window.json"),JsonSerializer.Serialize(new WindowPreferences{Port=Port.SelectedIndex,Width=bounds.Width,Height=bounds.Height,Maximized=WindowState==WindowState.Maximized}));}catch(Exception ex){lastError=ex.Message;}base.OnClosing(e);}
    protected override void OnClosed(EventArgs e) { timer.Stop(); Pause(); stop.Cancel(); tray.Dispose(); Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged; UnregisterHotKey(new WindowInteropHelper(this).Handle, 1); base.OnClosed(e); }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) { if (msg == 0x312 && w == new IntPtr(1)) { Pause(); handled = true; } return IntPtr.Zero; }
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
