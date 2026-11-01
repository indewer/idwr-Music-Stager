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

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"Application OnExit: code={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup entered");
        try
        {
            base.OnStartup(e);
            Log("base.OnStartup finished");
        }
        catch (Exception ex)
        {
            Log($"base.OnStartup EXCEPTION: {ex}");
        }
    }
}
