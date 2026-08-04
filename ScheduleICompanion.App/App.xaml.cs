using System.IO;
using System.Windows;

namespace ScheduleICompanion.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        WriteSessionLog($"Started {Environment.ProcessId} from {AppContext.BaseDirectory}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteSessionLog("Process exit");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            WriteSessionLog($"Unhandled AppDomain exception: {args.ExceptionObject}");
            WriteCrashLog(args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            MessageBox.Show(
                $"Schedule I Companion could not start.\n\n{args.Exception.Message}\n\nA crash log has been written to LocalAppData.",
                "Schedule I Companion",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };

        base.OnStartup(e);
    }

    internal static void WriteSessionLog(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScheduleICompanion");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "session.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void WriteCrashLog(Exception? exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScheduleICompanion");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTimeOffset.Now:O}]\n{exception}\n\n");
        }
        catch
        {
        }
    }
}
