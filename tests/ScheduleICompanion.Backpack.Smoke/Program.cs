using ScheduleICompanion.Backpack;

var root = Path.Combine(Path.GetTempPath(), "ScheduleICompanion-Backpack-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new BackpackStore(root);
    var state = store.Load(76561198000000001, "Career One");
    Check(state.Slots.Length == 12 && state.Revision == 0, "new personal backpack");
    state.Slots[0] = "{\"ID\":\"test\",\"Quantity\":2}";
    state.Revision = 1;
    store.Save(state);
    var loaded = store.Load(state.OwnerSteamId, state.CareerId);
    Check(loaded.Revision == 1 && loaded.Slots[0] == state.Slots[0], "atomic save and load");
    loaded.Revision = 2;
    store.Save(loaded);
    Check(Directory.GetFiles(root, "*.previous").Length == 1, "previous revision retained");
    var active = Directory.GetFiles(root, "*.json").Single();
    File.WriteAllText(active, "not-json");
    var recovered = store.Load(state.OwnerSteamId, state.CareerId);
    Check(recovered.Revision == 0 && Directory.GetFiles(root, "*.corrupt-*").Length == 1, "corrupt state quarantined");
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
