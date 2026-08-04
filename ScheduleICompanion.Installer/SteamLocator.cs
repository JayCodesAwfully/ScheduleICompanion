using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace ScheduleICompanion.Installer;

internal static class SteamLocator
{
    public static string? FindScheduleI()
    {
        foreach (var root in GetSteamRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var game = Path.Combine(root, "steamapps", "common", "Schedule I");
            if (IsGameDirectory(game)) return game;
        }
        return null;
    }

    public static bool IsGameDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "Schedule I.exe"));

    private static IEnumerable<string> GetSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) roots.Add(path);
        }
        var defaults = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };
        foreach (var path in defaults) Add(path);

        foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("InstallPath") is string path) Add(path);
        }
        using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            if (key?.GetValue("SteamPath") is string path) Add(path);

        foreach (var steam in roots.ToArray())
        {
            var libraryFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;
            var content = File.ReadAllText(libraryFile);
            foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase))
            {
                var path = match.Groups["path"].Value.Replace("\\\\", "\\");
                Add(path);
            }
        }
        return roots;
    }
}
