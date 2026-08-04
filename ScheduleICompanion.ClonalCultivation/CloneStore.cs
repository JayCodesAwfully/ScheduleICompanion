using System.Text.Json;

namespace ScheduleICompanion.ClonalCultivation;

internal sealed class CloneStore
{
    private readonly string _root;

    public CloneStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(root);
    }

    public CloneRegistry Load(ulong ownerSteamId, string careerId)
    {
        var path = GetPath(ownerSteamId, careerId);
        if (!File.Exists(path)) return CloneRegistry.Create(ownerSteamId, careerId);
        try
        {
            var registry = JsonSerializer.Deserialize<CloneRegistry>(File.ReadAllText(path)) ??
                           CloneRegistry.Create(ownerSteamId, careerId);
            registry.OwnerSteamId = ownerSteamId;
            registry.CareerId = careerId;
            registry.Strains ??= new Dictionary<string, CloneStrain>(StringComparer.OrdinalIgnoreCase);
            return registry;
        }
        catch
        {
            File.Copy(path, path + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}", true);
            return CloneRegistry.Create(ownerSteamId, careerId);
        }
    }

    public void Save(CloneRegistry registry)
    {
        Directory.CreateDirectory(_root);
        var path = GetPath(registry.OwnerSteamId, registry.CareerId);
        var next = path + ".new";
        var previous = path + ".previous";
        File.WriteAllText(next, JsonSerializer.Serialize(registry, new JsonSerializerOptions { WriteIndented = true }));
        using (var stream = new FileStream(next, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            stream.Flush(true);
        if (File.Exists(path)) File.Replace(next, path, previous, true);
        else File.Move(next, path);
    }

    private string GetPath(ulong ownerSteamId, string careerId)
    {
        var safeCareer = string.Concat(careerId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(_root, $"{safeCareer}-{ownerSteamId}.json");
    }
}
