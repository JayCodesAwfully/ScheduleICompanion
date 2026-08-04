using System.Text.Json;

namespace ScheduleICompanion.Backpack;

internal sealed class BackpackStore
{
    private readonly string _root;

    public BackpackStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public BackpackState Load(ulong owner, string career)
    {
        var path = GetPath(owner, career);
        if (!File.Exists(path))
        {
            var created = New(owner, career);
            return owner == 0 ? created : RecoverLegacyOwner(created);
        }
        try
        {
            var state = JsonSerializer.Deserialize<BackpackState>(File.ReadAllText(path)) ?? New(owner, career);
            state.OwnerSteamId = owner;
            state.CareerId = career;
            if (state.Slots.Length != 12)
            {
                var slots = state.Slots;
                Array.Resize(ref slots, 12);
                state.Slots = slots;
            }
            return owner == 0 ? state : RecoverLegacyOwner(state);
        }
        catch
        {
            var corrupt = path + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(path, corrupt, true);
            return New(owner, career);
        }
    }

    private BackpackState RecoverLegacyOwner(BackpackState destination)
    {
        var legacyPath = GetPath(0, destination.CareerId);
        if (!File.Exists(legacyPath)) return destination;

        try
        {
            var legacy = JsonSerializer.Deserialize<BackpackState>(File.ReadAllText(legacyPath));
            if (legacy?.Slots is null || !legacy.Slots.Any(value => !string.IsNullOrWhiteSpace(value)))
                return destination;

            if (destination.Slots.Length != 12)
            {
                var slots = destination.Slots;
                Array.Resize(ref slots, 12);
                destination.Slots = slots;
            }
            if (legacy.Slots.Length != 12)
            {
                var slots = legacy.Slots;
                Array.Resize(ref slots, 12);
                legacy.Slots = slots;
            }

            var changed = false;
            for (var source = 0; source < legacy.Slots.Length; source++)
            {
                if (string.IsNullOrWhiteSpace(legacy.Slots[source])) continue;
                var target = string.IsNullOrWhiteSpace(destination.Slots[source])
                    ? source
                    : Array.FindIndex(destination.Slots, string.IsNullOrWhiteSpace);
                if (target < 0) break;
                destination.Slots[target] = legacy.Slots[source];
                changed = true;
            }

            if (!changed) return destination;
            destination.Revision = Math.Max(destination.Revision, legacy.Revision) + 1;
            Save(destination);

            var claimedPath = legacyPath + $".claimed-by-{destination.OwnerSteamId}";
            if (File.Exists(claimedPath))
                claimedPath += $"-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}";
            File.Move(legacyPath, claimedPath);
            return destination;
        }
        catch
        {
            return destination;
        }
    }

    public void Save(BackpackState state)
    {
        Directory.CreateDirectory(_root);
        var path = GetPath(state.OwnerSteamId, state.CareerId);
        var next = path + ".new";
        var previous = path + ".previous";
        File.WriteAllText(next, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        using (var stream = new FileStream(next, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            stream.Flush(true);
        if (File.Exists(path)) File.Replace(next, path, previous, true);
        else File.Move(next, path);
    }

    private string GetPath(ulong owner, string career)
    {
        var safe = string.Concat(career.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(_root, $"{safe}-{owner}.json");
    }

    private static BackpackState New(ulong owner, string career) => new() { OwnerSteamId = owner, CareerId = career };
}
