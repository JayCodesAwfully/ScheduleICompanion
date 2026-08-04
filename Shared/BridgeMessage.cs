using System.Text.Json.Serialization;

namespace ScheduleICompanion.Shared;

public sealed record BridgeMessage
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("payload")] public object? Payload { get; init; }
}

public sealed record NotificationPayload(string Text, string Category, string? Source);
public sealed record PositionPayload(
    float X, float Y, float Z, float Heading, string Area,
    bool HasNativeMapPosition = false, float MapX = 0, float MapY = 0,
    float MapWidth = 0, float MapHeight = 0);
public sealed record OrderPayload(string Customer, IReadOnlyList<OrderLine> Lines, string? RawText);
public sealed record OrderLine(string Product, int Quantity);
public sealed record SessionPayload(bool InGame, string? SaveName, bool IsHost);
public sealed record DiagnosticPayload(string Name, string Value);
public sealed record GameTimePayload(int Time24, string Day, int ElapsedDays);
public sealed record MapPoiPayload(
    string Id, string Name, string Kind,
    float MapX, float MapY, float MapWidth, float MapHeight);
public sealed record MapPoiSnapshotPayload(IReadOnlyList<MapPoiPayload> Pois);
public sealed record QuestItemPayload(string Id, string Title, string Description, bool IsTracked, IReadOnlyList<string> Entries);
public sealed record QuestSnapshotPayload(IReadOnlyList<QuestItemPayload> Quests);
public sealed record MessagePreviewPayload(string Id, string Contact, string Text, string Sender, bool Unread);
public sealed record MessageSnapshotPayload(IReadOnlyList<MessagePreviewPayload> Messages);
public sealed record DevToolCommandPayload(string Action, bool Enabled = false, int IntervalSeconds = 30);

public sealed record PlayerMarkerPayload(
    string Id, string DisplayName, float X, float Y, float Z, float Heading,
    bool IsLocal, bool IsInVehicle, string Area,
    bool HasNativeMapPosition = false, float MapX = 0, float MapY = 0,
    float MapWidth = 0, float MapHeight = 0);
public sealed record PlayerMarkersSnapshotPayload(IReadOnlyList<PlayerMarkerPayload> Players);

public sealed record NpcMarkerPayload(
    string Id, string DisplayName, float X, float Y, float Z, float Heading,
    string Kind, string Area,
    bool HasNativeMapPosition = false, float MapX = 0, float MapY = 0,
    float MapWidth = 0, float MapHeight = 0);
public sealed record NpcMarkersSnapshotPayload(IReadOnlyList<NpcMarkerPayload> Npcs);
