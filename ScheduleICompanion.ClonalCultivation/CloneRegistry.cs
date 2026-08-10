using System.Text.Json;

namespace ScheduleICompanion.ClonalCultivation;

internal sealed class CloneRegistry
{
    public int Schema { get; set; } = 1;
    public ulong OwnerSteamId { get; set; }
    public string CareerId { get; set; } = "unknown";
    public long Revision { get; set; }
    public Dictionary<string, CloneStrain> Strains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SavedClonePlant> Plants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static CloneRegistry Create(ulong ownerSteamId, string careerId) =>
        new() { OwnerSteamId = ownerSteamId, CareerId = careerId };

    public CloneRegistry Clone() => JsonSerializer.Deserialize<CloneRegistry>(JsonSerializer.Serialize(this))!;
}

internal sealed class SavedClonePlant
{
    public string ProductId { get; set; } = "";
    public int Quality { get; set; }
    public float PotX { get; set; }
    public float PotY { get; set; }
    public float PotZ { get; set; }
}

internal sealed class CloneStrain
{
    public string ProductId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SyntheticSeedId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
