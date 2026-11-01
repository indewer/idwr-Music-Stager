using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MusicStagerWidget;

public partial class App : System.Windows.Application
{
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "MusicStagerWidget_lifecycle.log");

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PID:{Process.GetCurrentProcess().Id}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log($"AppDomain UnhandledException: {e.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, e) =>
        {
            Log($"DispatcherUnhandledException: {e.Exception}");
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string projectDir = @"x:\Obsidian Vaults\Antigravity 1-st\MusicStagerWidget";
            string icoPath = Path.Combine(projectDir, "app.ico");
            TrayIconHelper.EnsureIcoFileExists(icoPath);
        }
        catch { }
    }
}
