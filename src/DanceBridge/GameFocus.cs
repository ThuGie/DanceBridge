using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
namespace DanceBridge;

public static class GameFocus
{
    static uint cachedPid;
    static string? cachedPath;
    static bool cachedMatch;
    static long lastCheck;
    public static bool Matches(string? path)
    {
        if (path is null) return true;
        try
        {
            var window = GetForegroundWindow(); if (window == IntPtr.Zero) return false;
            GetWindowThreadProcessId(window, out var pid);
            var now=Environment.TickCount64;
            if(pid==cachedPid&&string.Equals(cachedPath,path,StringComparison.OrdinalIgnoreCase)&&now-lastCheck<1000)return cachedMatch;
            using var process = Process.GetProcessById((int)pid);
            var actual = process.MainModule?.FileName;
            cachedPid=pid;cachedPath=path;lastCheck=now;
            return cachedMatch=actual is not null && Path.GetFullPath(actual).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
}
