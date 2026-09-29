using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DownTrack;

public partial class App : Application
{
    private static readonly string CrashDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownTrack");

    private static readonly string CrashLogPath =
        Path.Combine(CrashDirectory, "crash.log");

    private static readonly object CrashLogLock = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            WriteCrashLog("Startup", ex);
            throw;
        }
    }

    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);

        // Keep the default WPF behavior: an unhandled UI exception terminates the process.
        e.Handled = false;
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("AppDomain.UnhandledException", exception);
        }
        else
        {
            WriteCrashLog("AppDomain.UnhandledException", new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception."));
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            lock (CrashLogLock)
            {
                Directory.CreateDirectory(CrashDirectory);

                var entry = new StringBuilder()
                    .AppendLine(new string('=', 72))
                    .AppendLine($"Timestamp: {DateTimeOffset.Now:O}")
                    .AppendLine($"Source: {source}")
                    .AppendLine($"OS: {Environment.OSVersion}")
                    .AppendLine($"Process: {Environment.ProcessPath}")
                    .AppendLine()
                    .AppendLine(exception.ToString())
                    .AppendLine();

                File.AppendAllText(CrashLogPath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Crash logging must never become another source of application failure.
        }
    }
}
