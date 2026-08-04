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
        if (!File.Exists(path)) return New(owner, career);
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
            return state;
        }
        catch
        {
            var corrupt = path + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(path, corrupt, true);
            return New(owner, career);
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
