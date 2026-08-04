using System.Diagnostics;
using MelonLoader;

namespace ScheduleICompanion.Mod;

public static class CompanionLauncher
{
    public static void TryLaunch(MelonLogger.Instance logger)
    {
        try
        {
            if (Process.GetProcessesByName("ScheduleICompanion.App").Length > 0)
            {
                logger.Msg("Companion is already running.");
                return;
            }

            var gameDirectory = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(gameDirectory, "ScheduleICompanion", "ScheduleICompanion.App.exe"),
                Path.Combine(gameDirectory, "Mods", "ScheduleICompanion", "ScheduleICompanion.App.exe"),
                Path.Combine(gameDirectory, "ScheduleICompanion.App.exe")
            };

            var executable = candidates.FirstOrDefault(File.Exists);
            if (executable is null)
            {
                logger.Warning("Companion executable not found. Start it manually or place it in Schedule I\\ScheduleICompanion.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = true
            });

            logger.Msg($"Started companion: {executable}");
        }
        catch (Exception ex)
        {
            logger.Error($"Could not start companion: {ex}");
        }
    }
}
