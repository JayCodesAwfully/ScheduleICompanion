using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScheduleICompanion.Installer;

public sealed class InstallationService
{
    public const string MelonLoaderVersion = "0.7.3";
    public const string MelonLoaderUrl = "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip";
    public const string MelonLoaderReleasesUrl = "https://github.com/LavaGang/MelonLoader/releases";
    private const string MelonLoaderSha256 = "5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416";
    private const string SupportedGameAssemblySha512 = "92D015CA25C8E1E4EA4A351A936565CC5E41549BDE045D59C72CD45A7D7DD6BF4D2B66E9F05A8346E1AD2C2CF71876B108B3917F861D1B608595F7C3D5092745";
    private readonly string _payloadRoot;
    private readonly bool _manageProcesses;

    public InstallationService(string? payloadRoot = null, bool manageProcesses = true)
    {
        _payloadRoot = payloadRoot ?? Path.Combine(AppContext.BaseDirectory, "Payload");
        _manageProcesses = manageProcesses;
    }

    public bool IsPayloadReady =>
        File.Exists(Path.Combine(_payloadRoot, "Companion", "ScheduleICompanion.App.exe")) &&
        File.Exists(Path.Combine(_payloadRoot, "Runtime", "ScheduleICompanion.Runtime.dll")) &&
        File.Exists(Path.Combine(_payloadRoot, "Mods", "ScheduleICompanion.Mod.dll"));

    public static bool IsMelonLoaderInstalled(string gameDirectory) =>
        File.Exists(Path.Combine(gameDirectory, "version.dll")) &&
        Directory.Exists(Path.Combine(gameDirectory, "MelonLoader"));

    public static bool IsCompanionInstalled(string gameDirectory) =>
        File.Exists(Path.Combine(gameDirectory, "Mods", "ScheduleICompanion.Mod.dll")) &&
        File.Exists(Path.Combine(gameDirectory, "ScheduleICompanion", "ScheduleICompanion.App.exe"));

    public async Task InstallAsync(
        string gameDirectory,
        bool installMelonLoader,
        bool createDesktopShortcut,
        IProgress<string> progress,
        CancellationToken cancellationToken,
        bool installBackpack = false)
    {
        ValidateGameClosed(gameDirectory);
        if (!IsPayloadReady) throw new InvalidOperationException("Installer payload is incomplete. Re-download the Companion release package.");
        if (_manageProcesses) StopCompanion(progress);
        BackupExisting(gameDirectory, progress);

        if (!IsMelonLoaderInstalled(gameDirectory))
        {
            if (File.Exists(Path.Combine(gameDirectory, "version.dll")) &&
                !Directory.Exists(Path.Combine(gameDirectory, "MelonLoader")))
                throw new InvalidOperationException(
                    "A version.dll proxy is already present but does not appear to belong to MelonLoader. " +
                    "Remove or identify that loader before continuing so it is not overwritten.");
            if (!installMelonLoader)
                throw new InvalidOperationException("MelonLoader is not installed. Enable its installation or use the official release page.");
            await InstallMelonLoaderAsync(gameDirectory, progress, cancellationToken);
        }
        else progress.Report("MelonLoader is already installed; leaving it intact.");

        InstallCompatibleInteropCache(gameDirectory, progress);

        cancellationToken.ThrowIfCancellationRequested();
        var companionDirectory = Path.Combine(gameDirectory, "ScheduleICompanion");
        var modsDirectory = Path.Combine(gameDirectory, "Mods");
        var installedBackpack = Path.Combine(modsDirectory, "ScheduleICompanion.Backpack.dll");
        var installedCultivation = Path.Combine(modsDirectory, "ScheduleICompanion.ClonalCultivation.dll");
        var backpackWasEnabled = File.Exists(installedBackpack);
        var cultivationWasEnabled = File.Exists(installedCultivation);
        Directory.CreateDirectory(companionDirectory);
        Directory.CreateDirectory(modsDirectory);

        progress.Report("Installing the self-contained Companion application...");
        CopyDirectory(Path.Combine(_payloadRoot, "Companion"), companionDirectory);
        CopyDirectory(Path.Combine(_payloadRoot, "Runtime"), Path.Combine(companionDirectory, "Runtime"));
        File.Copy(
            Path.Combine(_payloadRoot, "Mods", "ScheduleICompanion.Mod.dll"),
            Path.Combine(modsDirectory, "ScheduleICompanion.Mod.dll"), true);
        if (installBackpack || backpackWasEnabled)
        {
            var backpack = Path.Combine(_payloadRoot, "Companion", "ModPackages", "ScheduleICompanion.Backpack.dll");
            if (!File.Exists(backpack)) throw new FileNotFoundException("The Backpack mod is missing from setup.", backpack);
            progress.Report(backpackWasEnabled ? "Updating the enabled Backpack mod..." : "Enabling the Backpack mod...");
            File.Copy(backpack, installedBackpack, true);
        }
        if (cultivationWasEnabled)
        {
            var cultivation = Path.Combine(_payloadRoot, "Companion", "ModPackages", "ScheduleICompanion.ClonalCultivation.dll");
            if (!File.Exists(cultivation)) throw new FileNotFoundException("The Cultivation mod is missing from setup.", cultivation);
            progress.Report("Updating the enabled Cultivation mod...");
            File.Copy(cultivation, installedCultivation, true);
        }

        var manifest = new
        {
            product = "Schedule I Companion",
            version = "1.7.27",
            installedAt = DateTimeOffset.Now,
            melonLoader = IsMelonLoaderInstalled(gameDirectory) ? MelonLoaderVersion : null,
            gameDirectory
        };
        File.WriteAllText(
            Path.Combine(companionDirectory, "install-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        if (createDesktopShortcut)
        {
            progress.Report("Creating desktop shortcut...");
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Schedule I Companion.lnk"),
                Path.Combine(companionDirectory, "ScheduleICompanion.App.exe"), companionDirectory);
        }

        progress.Report("Installation completed successfully. Launch Schedule I normally through Steam.");
    }

    public void UninstallCompanion(string gameDirectory, IProgress<string> progress)
    {
        ValidateGameClosed(gameDirectory);
        if (_manageProcesses) StopCompanion(progress);
        BackupExisting(gameDirectory, progress);

        var mod = Path.Combine(gameDirectory, "Mods", "ScheduleICompanion.Mod.dll");
        var backpackMod = Path.Combine(gameDirectory, "Mods", "ScheduleICompanion.Backpack.dll");
        var cultivationMod = Path.Combine(gameDirectory, "Mods", "ScheduleICompanion.ClonalCultivation.dll");
        var app = Path.Combine(gameDirectory, "ScheduleICompanion");
        if (File.Exists(mod)) File.Delete(mod);
        if (File.Exists(backpackMod)) File.Delete(backpackMod);
        if (File.Exists(cultivationMod)) File.Delete(cultivationMod);
        if (Directory.Exists(app)) Directory.Delete(app, true);
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Schedule I Companion.lnk");
        if (File.Exists(shortcut)) File.Delete(shortcut);
        progress.Report("Companion removed. MelonLoader and LocalAppData settings were preserved.");
    }

    public static void LaunchCompanion(string gameDirectory)
    {
        var app = Path.Combine(gameDirectory, "ScheduleICompanion", "ScheduleICompanion.App.exe");
        if (!File.Exists(app)) throw new FileNotFoundException("The Companion is not installed.", app);
        Process.Start(new ProcessStartInfo(app) { WorkingDirectory = Path.GetDirectoryName(app)!, UseShellExecute = true });
    }

    private static void ValidateGameClosed(string gameDirectory)
    {
        if (!SteamLocator.IsGameDirectory(gameDirectory))
            throw new DirectoryNotFoundException("Select the folder containing Schedule I.exe.");
        if (Process.GetProcessesByName("Schedule I").Length > 0)
            throw new InvalidOperationException("Schedule I is running. Close the game before installing, repairing, or uninstalling.");
    }

    private static void StopCompanion(IProgress<string> progress)
    {
        foreach (var process in Process.GetProcessesByName("ScheduleICompanion.App"))
        {
            progress.Report("Closing the currently running Companion...");
            try { process.Kill(true); process.WaitForExit(5000); } catch { }
        }
    }

    private static void BackupExisting(string gameDirectory, IProgress<string> progress)
    {
        var app = Path.Combine(gameDirectory, "ScheduleICompanion");
        var mod = Path.Combine(gameDirectory, "Mods", "ScheduleICompanion.Mod.dll");
        var backpackMod = Path.Combine(gameDirectory, "Mods", "ScheduleICompanion.Backpack.dll");
        var cultivationMod = Path.Combine(gameDirectory, "Mods", "ScheduleICompanion.ClonalCultivation.dll");
        if (!Directory.Exists(app) && !File.Exists(mod) && !File.Exists(backpackMod) && !File.Exists(cultivationMod)) return;
        var backup = Path.Combine(gameDirectory, "ScheduleICompanion Backups", $"Installer-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
        Directory.CreateDirectory(backup);
        if (Directory.Exists(app)) CopyDirectory(app, Path.Combine(backup, "ScheduleICompanion"));
        if (File.Exists(mod))
        {
            Directory.CreateDirectory(Path.Combine(backup, "Mods"));
            File.Copy(mod, Path.Combine(backup, "Mods", "ScheduleICompanion.Mod.dll"), true);
        }
        if (File.Exists(backpackMod))
        {
            Directory.CreateDirectory(Path.Combine(backup, "Mods"));
            File.Copy(backpackMod, Path.Combine(backup, "Mods", "ScheduleICompanion.Backpack.dll"), true);
        }
        if (File.Exists(cultivationMod))
        {
            Directory.CreateDirectory(Path.Combine(backup, "Mods"));
            File.Copy(cultivationMod, Path.Combine(backup, "Mods", "ScheduleICompanion.ClonalCultivation.dll"), true);
        }
        progress.Report($"Backup created: {backup}");
    }

    private static async Task InstallMelonLoaderAsync(
        string gameDirectory, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ScheduleICompanion-Setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var archive = Path.Combine(tempRoot, "MelonLoader.x64.zip");
            progress.Report($"Downloading official MelonLoader v{MelonLoaderVersion} x64...");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ScheduleICompanion-Installer/1.7");
            await using (var input = await client.GetStreamAsync(MelonLoaderUrl, cancellationToken))
            await using (var output = File.Create(archive))
                await input.CopyToAsync(output, cancellationToken);

            progress.Report("Verifying MelonLoader archive integrity...");
            await using (var stream = File.OpenRead(archive))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(MelonLoaderSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("MelonLoader download failed integrity verification. No files were installed.");
            }

            var extracted = Path.Combine(tempRoot, "extracted");
            ZipFile.ExtractToDirectory(archive, extracted);
            if (!File.Exists(Path.Combine(extracted, "version.dll")) ||
                !File.Exists(Path.Combine(extracted, "dobby.dll")) ||
                !Directory.Exists(Path.Combine(extracted, "MelonLoader")))
                throw new InvalidDataException("The official MelonLoader archive had an unexpected layout.");
            progress.Report("Installing MelonLoader...");
            CopyDirectory(extracted, gameDirectory);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private void InstallCompatibleInteropCache(string gameDirectory, IProgress<string> progress)
    {
        var sourceAssemblies = Path.Combine(_payloadRoot, "InteropCache", "Il2CppAssemblies");
        var sourceConfig = Path.Combine(_payloadRoot, "InteropCache", "Config.cfg");
        if (!Directory.Exists(sourceAssemblies) || !File.Exists(sourceConfig)) return;

        var gameAssembly = Path.Combine(gameDirectory, "GameAssembly.dll");
        if (!File.Exists(gameAssembly)) return;
        using var stream = File.OpenRead(gameAssembly);
        var actualHash = Convert.ToHexString(SHA512.HashData(stream));
        if (!actualHash.Equals(SupportedGameAssemblySha512, StringComparison.OrdinalIgnoreCase))
        {
            progress.Report("The bundled IL2CPP cache does not match this game build; MelonLoader will generate its own.");
            return;
        }

        progress.Report("Installing the verified IL2CPP cache for this Schedule I build...");
        CopyDirectory(sourceAssemblies, Path.Combine(gameDirectory, "MelonLoader", "Il2CppAssemblies"));
        var configTarget = Path.Combine(gameDirectory, "MelonLoader", "Dependencies", "Il2CppAssemblyGenerator", "Config.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(configTarget)!);
        File.Copy(sourceConfig, configTarget, true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut service is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = "Schedule I Companion";
        shortcut.Save();
    }
}
