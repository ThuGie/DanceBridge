namespace DanceBridge;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    protected override void OnStartup(System.Windows.StartupEventArgs e) { instance = new Mutex(true, "Local\\DanceBridge", out bool first); if (!first) { System.Windows.MessageBox.Show("DanceBridge is already running. Open it from the system tray."); Shutdown(); return; } ThemeManager.Load(); base.OnStartup(e); }
    protected override void OnExit(System.Windows.ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
