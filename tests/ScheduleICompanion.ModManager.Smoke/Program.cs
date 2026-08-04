using System;
using System.IO;
using System.Threading;
using ScheduleICompanion.App;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: smoke <catalog.json> <backpack.dll> <clonal-cultivation.dll>");
    return 2;
}

var root = Path.Combine(Path.GetTempPath(), "ScheduleICompanion-ModManager-Smoke-" + Guid.NewGuid().ToString("N"));
var game = Path.Combine(root, "Schedule I");
var companion = Path.Combine(game, "ScheduleICompanion");
var packages = Path.Combine(companion, "ModPackages");
Directory.CreateDirectory(packages);
File.WriteAllText(Path.Combine(game, "Schedule I.exe"), "fixture");
File.Copy(args[0], Path.Combine(packages, "catalog.json"));
File.Copy(args[1], Path.Combine(packages, "ScheduleICompanion.Backpack.dll"));
File.Copy(args[2], Path.Combine(packages, "ScheduleICompanion.ClonalCultivation.dll"));
var data = Path.Combine(game, "UserData", "ScheduleICompanion", "Backpacks", "keep.json");
Directory.CreateDirectory(Path.GetDirectoryName(data)!);
File.WriteAllText(data, "preserve me");

try
{
    var manager = new ModManagerService(companion);
    var rows = await manager.LoadAsync(null, CancellationToken.None);
    Check(rows.Count == 2 && rows.All(row => !row.Enabled), "two-mod catalogue loaded");
    var backpack = rows.Single(row => row.Id == "personal-backpack");
    var cultivation = rows.Single(row => row.Id == "clonal-cultivation");
    await manager.SetEnabledAsync(backpack.Definition, true, CancellationToken.None);
    Check(File.Exists(Path.Combine(game, "Mods", "ScheduleICompanion.Backpack.dll")), "mod enabled");
    rows = await manager.LoadAsync(null, CancellationToken.None);
    backpack = rows.Single(row => row.Id == "personal-backpack");
    cultivation = rows.Single(row => row.Id == "clonal-cultivation");
    Check(backpack.Enabled && backpack.Current && !cultivation.Enabled, "independent enabled state detected");
    await manager.SetEnabledAsync(cultivation.Definition, true, CancellationToken.None);
    Check(File.Exists(Path.Combine(game, "Mods", "ScheduleICompanion.ClonalCultivation.dll")), "cultivation enabled");
    await File.AppendAllTextAsync(Path.Combine(game, "Mods", "ScheduleICompanion.Backpack.dll"), "changed");
    rows = await manager.LoadAsync(null, CancellationToken.None);
    backpack = rows.Single(row => row.Id == "personal-backpack");
    Check(backpack.Enabled && !backpack.Current && backpack.Action == "Update", "update detected");
    await manager.SetEnabledAsync(backpack.Definition, true, CancellationToken.None);
    rows = await manager.LoadAsync(null, CancellationToken.None);
    backpack = rows.Single(row => row.Id == "personal-backpack");
    Check(backpack.Current, "mod updated");
    await manager.SetEnabledAsync(backpack.Definition, false, CancellationToken.None);
    Check(!File.Exists(Path.Combine(game, "Mods", "ScheduleICompanion.Backpack.dll")), "mod disabled");
    Check(File.Exists(Path.Combine(game, "Mods", "ScheduleICompanion.ClonalCultivation.dll")), "other mod left enabled");
    Check(File.ReadAllText(data) == "preserve me", "player data preserved");
}
finally
{
    Directory.Delete(root, true);
}

return 0;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL  " + name);
    Console.WriteLine("PASS  " + name);
}
