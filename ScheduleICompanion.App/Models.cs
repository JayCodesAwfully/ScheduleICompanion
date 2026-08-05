namespace ScheduleICompanion.App;

public sealed record NotificationRow(DateTimeOffset Timestamp, string Category, string Text)
{
    public string Time => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}

public sealed record ProductTotalRow(string Product, int Needed, int Owned = 0)
{
    public int Quantity => Needed;
    public int Short => Math.Max(0, Needed - Owned);
    public string Status => Short == 0 ? "READY" : $"NEED {Short}";
}
public sealed record OrderSnapshotPayload(IReadOnlyList<ScheduleICompanion.Shared.OrderPayload> Orders);
public sealed record ActiveOrderDetailPayload(string Customer, string Location, string Window, float Payment, IReadOnlyList<ScheduleICompanion.Shared.OrderLine> Lines);
public sealed record ProductStockPayload(string Product, int Quantity);
public sealed record MixRecommendationPayload(string Product, string BaseProduct, string Ingredient, float Price)
{
    public string Combination => $"{BaseProduct} + {Ingredient}";
    public string DisplayPrice => $"${Price:N0}";
}
public sealed record DebugCatalogPayload(
    IReadOnlyList<string>? Interfaces,
    IReadOnlyList<string>? LaunderingInterfaces,
    IReadOnlyList<string>? TeleportDestinations,
    IReadOnlyList<string>? SpawnItems,
    IReadOnlyList<string>? SpawnVehicles,
    IReadOnlyList<string>? People);
public sealed record DebugInspectorPayload(string Title, IReadOnlyList<string> Lines);
public sealed record OperationItemPayload(string Title, string Detail, string State);
public sealed record OperationsSnapshotPayload(
    IReadOnlyList<ActiveOrderDetailPayload> Orders,
    IReadOnlyList<ProductStockPayload> Stock,
    float Cash, float OnlineBalance, float NetWorth,
    IReadOnlyList<OperationItemPayload> Production,
    IReadOnlyList<OperationItemPayload> Dealers,
    IReadOnlyList<OperationItemPayload> Deliveries,
    IReadOnlyList<OperationItemPayload> Employees,
    IReadOnlyList<OperationItemPayload> Laundering,
    string Risk,
    IReadOnlyList<MixRecommendationPayload> MixRecommendations);
public sealed record OperationItemRow(string Title, string Detail, string State);

public sealed record OrderRow(DateTimeOffset Timestamp, string Customer, IReadOnlyList<ProductTotalRow> Lines)
{
    public string Time => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string Summary => string.Join(", ", Lines.Select(x => $"{x.Quantity} × {x.Product}"));
}

public sealed class CompanionSettings
{
    public bool AlwaysOnTop { get; set; }
    public bool StartOnSecondMonitor { get; set; } = true;
    public MapBounds Overworld { get; set; } = new(-500, 500, -500, 500);
    public MapBounds Sewer { get; set; } = new(-500, 500, -500, 500);
    public bool ShowOtherPlayers { get; set; } = true;
    public bool ShowPlayerNames { get; set; }
    public bool ShowFacingDirection { get; set; } = true;
    public bool ShowNpcMarkers { get; set; } = true;
    public bool ShowNpcNames { get; set; } = false;
    public bool ShowSewerOverlay { get; set; } = true;
    public bool ShowSewerEntrances { get; set; } = true;
    public float SewerEnterY { get; set; } = -4.10f;
    public float SewerExitY { get; set; } = -3.20f;
    public float SewerPortalRadius { get; set; } = 8.0f;
    public bool ShowClock { get; set; } = true;
    public bool Use24HourClock { get; set; }
    public bool ShowPropertyPois { get; set; } = true;
    public bool ShowBusinessPois { get; set; } = true;
    public bool ShowContractPois { get; set; } = true;
    public bool ShowOwnedVehiclePois { get; set; } = true;
    public bool ShowDeadDropPois { get; set; } = true;
    public bool ShowDealerPois { get; set; } = true;
    public bool ShowObjectivePois { get; set; } = true;
    public bool ShowPotentialCustomerPois { get; set; }
    public bool ShowQuestPanel { get; set; } = true;
    public bool ShowMessagePanel { get; set; } = true;
    public bool ShowOrdersPanel { get; set; } = true;
    public bool ShowPositionStatus { get; set; }
    public bool ShowDiagnosticsTab { get; set; }
    public bool DevToolsEnabled { get; set; }
    public bool FreezeGameTime { get; set; }
    public bool AutoClearTrash { get; set; }
    public int TrashClearIntervalSeconds { get; set; } = 30;
    public bool ShowFps { get; set; }
    public bool InstantGrowTesting { get; set; }
    public string ModCatalogUrl { get; set; } = "";
}

public sealed record MapBounds(float MinX, float MaxX, float MinZ, float MaxZ);

public sealed record SewerPortal(
    string Id, string Name,
    float SurfaceX, float SurfaceY, float SurfaceZ,
    float UndergroundX, float UndergroundY, float UndergroundZ,
    float MapX, float MapY, float MapWidth, float MapHeight)
{
    public float LocalSwitchY => (SurfaceY + UndergroundY) / 2f;
}

public sealed record SewerEntrance(
    string Id, string Name,
    float WorldX, float WorldY, float WorldZ,
    float MapX, float MapY, float MapWidth, float MapHeight);

public sealed record QuestRow(string Title, string Description, bool IsTracked, string Entries)
{
    public string Marker => IsTracked ? "●" : "○";
}

public sealed record MessagePreviewRow(string Contact, string Text, string Sender, bool Unread)
{
    public string Marker => Unread ? "●" : "";
}
