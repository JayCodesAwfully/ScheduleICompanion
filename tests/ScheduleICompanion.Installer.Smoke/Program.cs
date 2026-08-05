using ScheduleICompanion.Installer;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: smoke-test <Payload directory>");
    return 2;
}

var root = Path.Combine(Path.GetTempPath(), $"ScheduleICompanion-Smoke-{Guid.NewGuid():N}");
var game = Path.Combine(root, "Schedule I");
Directory.CreateDirectory(Path.Combine(game, "MelonLoader"));
await File.WriteAllTextAsync(Path.Combine(game, "Schedule I.exe"), "smoke test placeholder");
await File.WriteAllTextAsync(Path.Combine(game, "version.dll"), "smoke test placeholder");
var messages = new List<string>();
var progress = new Progress<string>(message => messages.Add(message));

try
{
    var service = new InstallationService(Path.GetFullPath(args[0]), manageProcesses: false);
    await service.InstallAsync(game, installMelonLoader: false, createDesktopShortcut: false, progress, CancellationToken.None, installBackpack: true);
    var checks = new Dictionary<string, bool>
    {
        ["App installed"] = File.Exists(Path.Combine(game, "ScheduleICompanion", "ScheduleICompanion.App.exe")),
        ["Runtime installed"] = File.Exists(Path.Combine(game, "ScheduleICompanion", "Runtime", "ScheduleICompanion.Runtime.dll")),
        ["Bootstrap installed"] = File.Exists(Path.Combine(game, "Mods", "ScheduleICompanion.Mod.dll")),
        ["Backpack enabled"] = File.Exists(Path.Combine(game, "Mods", "ScheduleICompanion.Backpack.dll")),
        ["Manifest installed"] = File.Exists(Path.Combine(game, "ScheduleICompanion", "install-manifest.json"))
    };
    var cultivationPackage = Path.Combine(game, "ScheduleICompanion", "ModPackages", "ScheduleICompanion.ClonalCultivation.dll");
    var cultivationInstalled = Path.Combine(game, "Mods", "ScheduleICompanion.ClonalCultivation.dll");
    File.Copy(cultivationPackage, cultivationInstalled, true);
    await File.AppendAllTextAsync(cultivationInstalled, "stale enabled cultivation fixture");
    await service.InstallAsync(game, installMelonLoader: false, createDesktopShortcut: false, progress, CancellationToken.None);
    checks["Enabled Cultivation updated"] = File.ReadAllBytes(cultivationInstalled)
        .SequenceEqual(File.ReadAllBytes(cultivationPackage));
    checks["Repair backup created"] = Directory.Exists(Path.Combine(game, "ScheduleICompanion Backups")) &&
                                      Directory.GetDirectories(Path.Combine(game, "ScheduleICompanion Backups"), "Installer-*").Length > 0;
    service.UninstallCompanion(game, progress);
    checks["Companion removed"] = !Directory.Exists(Path.Combine(game, "ScheduleICompanion")) &&
                                  !File.Exists(Path.Combine(game, "Mods", "ScheduleICompanion.Mod.dll"));
    checks["MelonLoader preserved"] = Directory.Exists(Path.Combine(game, "MelonLoader")) &&
                                       File.Exists(Path.Combine(game, "version.dll"));

    foreach (var check in checks) Console.WriteLine($"{(check.Value ? "PASS" : "FAIL")}  {check.Key}");
    return checks.Values.All(value => value) ? 0 : 1;
}
finally
{
    try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
}
