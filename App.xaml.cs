using System.IO;
using System.Windows;

namespace ColonyTracker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = false;
        };
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "colonytracker-crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* best effort */ }
    }
}
