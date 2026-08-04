using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScheduleICompanion.Backpack;

internal sealed class BackpackState
{
    public int Schema { get; set; } = 1;
    public ulong OwnerSteamId { get; set; }
    public string CareerId { get; set; } = "unknown";
    public long Revision { get; set; }
    public string[] Slots { get; set; } = Enumerable.Repeat(string.Empty, 12).ToArray();
    public List<BackpackJournalEntry> Journal { get; set; } = new();

    public BackpackState Clone() => JsonSerializer.Deserialize<BackpackState>(JsonSerializer.Serialize(this))!;

    public string Hash()
    {
        var canonical = JsonSerializer.Serialize(new { Schema, OwnerSteamId, CareerId, Revision, Slots });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal sealed class BackpackJournalEntry
{
    public Guid TransactionId { get; set; }
    public string Operation { get; set; } = "";
    public string Phase { get; set; } = "pending";
    public long RevisionBefore { get; set; }
    public int InventorySlot { get; set; }
    public int BackpackSlot { get; set; }
    public string ItemJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
