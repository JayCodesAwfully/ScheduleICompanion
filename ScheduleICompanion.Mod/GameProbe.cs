using MelonLoader;
using ScheduleICompanion.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Phone.Messages;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Calling;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI.Shop;
using GameConsole = Il2CppScheduleOne.Console;
using GameProperty = Il2CppScheduleOne.Property.Property;
using GameBusiness = Il2CppScheduleOne.Property.Business;
using GameCustomer = Il2CppScheduleOne.Economy.Customer;

namespace ScheduleICompanion.Mod;

internal sealed record OrderSnapshotPayload(IReadOnlyList<OrderPayload> Orders);
internal sealed record ActiveOrderDetailPayload(
    string Customer, string Location, string Window, float Payment, IReadOnlyList<OrderLine> Lines);
internal sealed record ProductStockPayload(string Product, int Quantity);
internal sealed record MixRecommendationPayload(string Product, string BaseProduct, string Ingredient, float Price);
internal sealed record DebugCatalogPayload(
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> LaunderingInterfaces,
    IReadOnlyList<string> TeleportDestinations,
    IReadOnlyList<string> SpawnItems,
    IReadOnlyList<string> SpawnVehicles,
    IReadOnlyList<string> People);
internal sealed record DebugInspectorPayload(string Title, IReadOnlyList<string> Lines);
internal sealed record OperationItemPayload(string Title, string Detail, string State);
internal sealed record OperationsSnapshotPayload(
    IReadOnlyList<ActiveOrderDetailPayload> Orders,
    IReadOnlyList<ProductStockPayload> Stock,
    float Cash, float OnlineBalance, float NetWorth,
    IReadOnlyList<OperationItemPayload> Production,
    IReadOnlyList<OperationItemPayload> Dealers,
    IReadOnlyList<OperationItemPayload> Deliveries,
    IReadOnlyList<OperationItemPayload> Employees,
    IReadOnlyList<OperationItemPayload> Laundering,
    string Risk, string Rank, int TotalXp,
    IReadOnlyList<MixRecommendationPayload> MixRecommendations);

/// <summary>Targeted player, NPC, and native-map state probe.</summary>
public sealed class GameProbe
{
    private readonly MelonLogger.Instance _logger;
    private readonly PipeServer _server;
    private string _sceneName = "";
    private Transform? _selectedPlayer;
    private string _selectedPlayerPath = "";
    private bool _reportedMissingPlayer;
    private float _nextTargetedPlayerDiscovery;
    private float _nextPotentialCustomerRefresh;
    private float _nextNpcHierarchyDiscovery;
    private int _lastRegisteredPlayerCount = -1;
    private readonly Dictionary<int, Transform> _trackedPlayers = new();
    private readonly Dictionary<int, string> _trackedPlayerNames = new();
    private readonly Dictionary<int, Transform> _trackedNpcs = new();
    private readonly Dictionary<int, string> _trackedNpcNames = new();
    private readonly Dictionary<int, string> _trackedNpcKinds = new();
    private readonly Dictionary<int, string> _trackedNpcMarkerIds = new();
    private readonly HashSet<int> _potentialCustomerIds = new();
    private readonly Queue<Transform> _npcScanQueue = new();
    private readonly Dictionary<int, Transform> _scannedNpcs = new();
    private readonly Dictionary<int, string> _scannedNpcNames = new();
    private readonly Dictionary<int, string> _scannedNpcKinds = new();
    private int _npcScanVisited;
    private MapPositionUtility? _mapPositionUtility;
    private RectTransform? _phoneMapContent;
    private Image? _phoneMapImage;
    private float _nativeMapWidth;
    private float _nativeMapHeight;
    private bool _reportedNativeMapReady;
    private TimeManager? _timeManager;
    private MoneyManager? _moneyManager;
    private DeliveryManager? _deliveryManager;
    private float _nextSupplementalPublish;
    private float _nextDetailPhase;
    private float _nextContractFallbackScan;
    private float _nextNetWorthRefresh;
    private float _nextDebugCatalogPublish;
    private int _debugCatalogWarmupPasses;
    private float _nextNpcMarkerPublish;
    private float _nextMixRecommendationRefresh;
    private float _nextProductionStatusRefresh;
    private float _nextEmployeeStatusRefresh;
    private int _detailPhase;
    private ActiveOrderDetailPayload[] _operationOrders = Array.Empty<ActiveOrderDetailPayload>();
    private ProductStockPayload[] _operationStock = Array.Empty<ProductStockPayload>();
    private float _operationCash;
    private float _operationOnlineBalance;
    private float _operationNetWorth;
    private OperationItemPayload[] _operationProduction = Array.Empty<OperationItemPayload>();
    private OperationItemPayload[] _operationDealers = Array.Empty<OperationItemPayload>();
    private OperationItemPayload[] _operationDeliveries = Array.Empty<OperationItemPayload>();
    private OperationItemPayload[] _operationEmployees = Array.Empty<OperationItemPayload>();
    private OperationItemPayload[] _operationLaundering = Array.Empty<OperationItemPayload>();
    private MixRecommendationPayload[] _operationMixRecommendations = Array.Empty<MixRecommendationPayload>();
    private string _operationRisk = "Waiting for local player";
    private string _operationRank = "Waiting for game";
    private int _operationTotalXp;
    private bool _freezeGameTime;
    private float _timeSpeedBeforeFreeze = 1f;
    private bool _autoClearTrash;
    private int _trashClearIntervalSeconds = 30;
    private float _nextTrashClear;
    private string _lastQuestFeedError = "";
    private string _lastObjectiveSummary = "";
    private string _lastPotentialCustomerSummary = "";
    private string _lastQuestSnapshotSummary = "";
    private string _lastOrderFeedError = "";
    private string _lastOrderSnapshotSummary = "";
    private string _lastDebugCatalogSummary = "";

    public GameProbe(MelonLogger.Instance logger, PipeServer server)
    {
        _logger = logger;
        _server = server;
    }

    public void Discover()
    {
        NotificationHookInstaller.ReportSafeMode(_logger, _server);
        Report("Orders", "Authoritative active-contract snapshots enabled.");
        Report("Player tracking", "Targeted Player_Local lookup and multiplayer tracking enabled.");
    }

    public void OnSceneLoaded(string sceneName)
    {
        _sceneName = sceneName;
        _selectedPlayer = null;
        _selectedPlayerPath = "";
        _reportedMissingPlayer = false;
        _trackedPlayers.Clear();
        _trackedPlayerNames.Clear();
        _trackedNpcs.Clear();
        _trackedNpcNames.Clear();
        _trackedNpcKinds.Clear();
        _trackedNpcMarkerIds.Clear();
        _potentialCustomerIds.Clear();
        _npcScanQueue.Clear();
        _scannedNpcs.Clear();
        _scannedNpcNames.Clear();
        _scannedNpcKinds.Clear();
        _npcScanVisited = 0;
        _mapPositionUtility = null;
        _phoneMapContent = null;
        _phoneMapImage = null;
        _nativeMapWidth = 0;
        _nativeMapHeight = 0;
        _reportedNativeMapReady = false;
        _timeManager = null;
        _moneyManager = null;
        _deliveryManager = null;
        var now = Time.unscaledTime;
        _nextSupplementalPublish = now;
        _nextDetailPhase = now + 0.5f;
        _nextPotentialCustomerRefresh = now + 2f;
        _nextNpcHierarchyDiscovery = now + 2f;
        _nextContractFallbackScan = now + 5f;
        _nextNetWorthRefresh = now + 3f;
        _nextDebugCatalogPublish = now + 2f;
        _debugCatalogWarmupPasses = 0;
        _nextNpcMarkerPublish = now;
        _nextMixRecommendationRefresh = now;
        _nextProductionStatusRefresh = now + 7f;
        _nextEmployeeStatusRefresh = now + 19f;
        _detailPhase = 0;
        _operationOrders = Array.Empty<ActiveOrderDetailPayload>();
        _operationStock = Array.Empty<ProductStockPayload>();
        _operationCash = 0;
        _operationOnlineBalance = 0;
        _operationNetWorth = 0;
        _operationProduction = Array.Empty<OperationItemPayload>();
        _operationDealers = Array.Empty<OperationItemPayload>();
        _operationDeliveries = Array.Empty<OperationItemPayload>();
        _operationEmployees = Array.Empty<OperationItemPayload>();
        _operationLaundering = Array.Empty<OperationItemPayload>();
        _operationMixRecommendations = Array.Empty<MixRecommendationPayload>();
        _operationRisk = "Waiting for local player";
        _lastDebugCatalogSummary = "";
        _nextTrashClear = 0;
        _nextTargetedPlayerDiscovery = now;
        Report("Scene", sceneName);
    }

    public void Tick(float now)
    {
        if (now >= _nextTargetedPlayerDiscovery)
        {
            _nextTargetedPlayerDiscovery = now + 5f;
            DiscoverTargetedPlayers();
            ResolveNativeMapServices();
        }

        if (now >= _nextPotentialCustomerRefresh)
        {
            _nextPotentialCustomerRefresh = now + 5f;
            TrackPotentialCustomers();
        }

        // The hierarchy walk is only a fallback for miscellaneous NPCs. Moving potential
        // customers come from the authoritative customer registry and need no scene scan.
        if (now >= _nextNpcHierarchyDiscovery)
        {
            _nextNpcHierarchyDiscovery = now + 30f;
            StartNpcHierarchyDiscovery();
        }
        ProcessNpcHierarchySlice(24, 8000);

        if (now >= _nextSupplementalPublish)
        {
            _nextSupplementalPublish = now + 1f;
            PublishGameTime();
        }

        if (now >= _nextDebugCatalogPublish)
        {
            PublishDebugCatalog();
            _debugCatalogWarmupPasses++;
            _nextDebugCatalogPublish = now + (_debugCatalogWarmupPasses < 4 ? 3f : 60f);
        }

        // Run one dashboard section at a time so scene searches and inventory reads cannot
        // all land in the same Unity frame.
        if (now >= _nextDetailPhase)
        {
            _nextDetailPhase = now + 1f;
            PublishNextDetailPhase(now);
        }

        if (_autoClearTrash && now >= _nextTrashClear)
        {
            _nextTrashClear = now + _trashClearIntervalSeconds;
            SubmitConsoleCommand("cleartrash", "Auto-clear trash");
        }
    }

    public void HandleDevToolCommand(DevToolCommandPayload command)
    {
        try
        {
            var separator = command.Action.IndexOf('|');
            var action = (separator < 0 ? command.Action : command.Action[..separator]).ToLowerInvariant();
            var value = separator < 0 ? "" : command.Action[(separator + 1)..];
            switch (action)
            {
                case "freeze_time":
                    ResolveTimeManager();
                    if (command.Enabled)
                    {
                        if (!_freezeGameTime && _timeManager is not null)
                            _timeSpeedBeforeFreeze = _timeManager.TimeSpeedMultiplier;
                        _freezeGameTime = true;
                        _timeManager?.SetTimeSpeedMultiplier(0f);
                    }
                    else
                    {
                        var wasFrozen = _freezeGameTime;
                        _freezeGameTime = false;
                        if (wasFrozen) _timeManager?.SetTimeSpeedMultiplier(_timeSpeedBeforeFreeze);
                    }
                    Report("DevTools", command.Enabled ? "Game clock frozen." : "Game clock resumed.");
                    break;
                case "clear_trash":
                    SubmitConsoleCommand("cleartrash", "Clear trash");
                    break;
                case "auto_clear_trash":
                    _autoClearTrash = command.Enabled;
                    _trashClearIntervalSeconds = Math.Clamp(command.IntervalSeconds, 5, 60);
                    _nextTrashClear = Time.unscaledTime + _trashClearIntervalSeconds;
                    Report("DevTools", command.Enabled
                        ? $"Trash auto-clear every {_trashClearIntervalSeconds}s."
                        : "Trash auto-clear disabled.");
                    break;
                case "show_fps":
                    SubmitConsoleCommand(command.Enabled ? "showfps" : "hidefps", "FPS display");
                    break;
                case "instant_grow":
                    SetCultivationInstantGrow(command.Enabled);
                    break;
                case "refresh_debug_catalog":
                    PublishDebugCatalog();
                    break;
                case "clear_weather":
                    SubmitConsoleCommand("setweather clear", "Clear weather");
                    break;
                case "set_time":
                    SetGameTime(value);
                    break;
                case "teleport":
                    TeleportTo(value);
                    break;
                case "inspect_plant":
                    InspectTargetPlant();
                    break;
                case "advance_plant":
                    AdvanceTargetPlant();
                    break;
                case "mature_plant":
                    SetTargetPlantGrowth(1f, "Target plant matured.");
                    break;
                case "reset_plant":
                    SetTargetPlantGrowth(0f, "Target plant reset to its first stage.");
                    break;
                case "inspect_person":
                    InspectPerson(value);
                    break;
                case "set_weather":
                    SubmitConsoleCommand($"setweather {RequireWeatherToken(value)}", "Set weather");
                    break;
                case "open_dealer":
                    OpenDealerManagement(value);
                    break;
                case "open_shop":
                    OpenShopInterface(value);
                    break;
                case "open_interface":
                    OpenGameInterface(value);
                    break;
                case "open_laundering":
                    OpenLaunderingInterface(value);
                    break;
                case "add_item":
                    SubmitConsoleCommand(
                        $"additem {RequireCommandToken(value, "item ID")} {Math.Clamp(command.IntervalSeconds, 1, 999)}",
                        "Add inventory item");
                    break;
                case "clear_inventory":
                    SubmitConsoleCommand("clearinventory", "Clear player inventory");
                    break;
                case "change_cash":
                    SubmitConsoleCommand($"changecash {FormatCommandNumber(ParseCommandNumber(value))}", "Change cash");
                    break;
                case "change_online_balance":
                    SubmitConsoleCommand($"changeonlinebalance {FormatCommandNumber(ParseCommandNumber(value))}", "Change online balance");
                    break;
                case "toggle_freecam":
                    SubmitConsoleCommand("freecam", "Toggle free camera");
                    break;
                case "set_move_speed":
                    SubmitConsoleCommand($"setmovespeed {FormatCommandNumber(Math.Clamp(ParseCommandNumber(value), 0.1f, 10f))}", "Set movement speed");
                    break;
                case "spawn_vehicle":
                    SubmitConsoleCommand($"spawnvehicle {RequireCommandToken(value, "vehicle ID")}", "Spawn vehicle");
                    break;
                case "console_command":
                    SubmitConsoleCommand(RequireRawConsoleCommand(value), "Custom console command");
                    break;
            }
        }
        catch (Exception ex)
        {
            Report("DevTools", $"{command.Action} failed: {ex.Message}");
        }
    }

    private void PublishNextDetailPhase(float now)
    {
        switch (_detailPhase)
        {
            case 0: PublishMapPois(); break;
            case 1: PublishQuests(); break;
            case 2: RefreshOrders(now); break;
            case 3: PublishMessages(); break;
            case 4:
                _operationStock = GetAvailableProductStock()
                    .Select(item => new ProductStockPayload(item.Key, item.Value)).ToArray();
                PublishOperationsSnapshot();
                break;
            case 5: RefreshMoneyAndRisk(now); PublishOperationsSnapshot(); break;
            case 6:
                if (now >= _nextProductionStatusRefresh)
                {
                    _nextProductionStatusRefresh = now + 45f;
                    _operationProduction = BuildProductionStatus();
                    PublishOperationsSnapshot();
                }
                break;
            case 7: _operationDealers = BuildDealerStatus(); PublishOperationsSnapshot(); break;
            case 8: _operationDeliveries = BuildDeliveryStatus(); PublishOperationsSnapshot(); break;
            case 9:
                if (now >= _nextEmployeeStatusRefresh)
                {
                    _nextEmployeeStatusRefresh = now + 60f;
                    _operationEmployees = BuildEmployeeStatus();
                    PublishOperationsSnapshot();
                }
                break;
            case 10:
                var laundering = BuildLaunderingStatus();
                if (!_operationLaundering.SequenceEqual(laundering))
                {
                    _operationLaundering = laundering;
                    PublishOperationsSnapshot();
                }
                break;
        }

        _detailPhase = (_detailPhase + 1) % 11;
    }

    private void ResolveTimeManager()
    {
        if (_timeManager is null)
            _timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
    }

    private void SetGameTime(string value)
    {
        if (!int.TryParse(value, out var time) || time < 0 || time > 2359 || time % 100 > 59)
            throw new ArgumentException("Time must be a valid 24-hour HHMM value, such as 0630 or 2200.");
        ResolveTimeManager();
        if (_timeManager is null)
            throw new InvalidOperationException("The game time manager is not ready.");
        _timeManager.SetTimeAndSync(time);
        Report("DevTools", $"Game time set to {time / 100:00}:{time % 100:00}.");
    }

    private void TeleportTo(string selection)
    {
        if (_selectedPlayer is null) throw new InvalidOperationException("The local player is not ready.");
        var closingBracket = selection.IndexOf(']');
        if (!selection.StartsWith('[') || closingBracket < 2)
            throw new ArgumentException("Choose a teleport destination.");
        var kind = selection[1..closingBracket].Trim();
        var name = selection[(closingBracket + 1)..].Trim();
        if (name.Length == 0) throw new ArgumentException("Choose a teleport destination.");
        Transform? target = null;
        if ((kind.Equals("Safehouse", StringComparison.OrdinalIgnoreCase) || kind.Equals("Business", StringComparison.OrdinalIgnoreCase)) &&
            GameProperty.Properties is not null)
            foreach (var property in GameProperty.Properties)
                if (property is not null && (property.PropertyName ?? property.name).Equals(name, StringComparison.OrdinalIgnoreCase))
                { target = property.transform; break; }
        if (kind.Equals("Dealer", StringComparison.OrdinalIgnoreCase) && Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers is not null)
            foreach (var dealer in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
                if (dealer is not null && (dealer.FullName ?? dealer.name).Equals(name, StringComparison.OrdinalIgnoreCase))
                { target = dealer.transform; break; }
        if (target is null) throw new InvalidOperationException($"Destination '{name}' is not loaded.");
        _selectedPlayer.position = target.position + Vector3.up;
        Report("DevTools", $"Teleported to {name}.");
    }

    private static Pot? FindTargetPlantPot()
    {
        var camera = Il2CppScheduleOne.PlayerScripts.PlayerCamera.Instance?.Camera ?? Camera.main;
        if (camera is null) return null;
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        foreach (var hit in Physics.RaycastAll(ray, 7f).OrderBy(hit => hit.distance))
        {
            var collider = hit.collider;
            if (collider is null || collider.GetComponentInParent<Player>() is not null) continue;
            var pot = collider.GetComponentInParent<Pot>();
            if (pot?.Plant is not null) return pot;
            var root = collider.transform.root;
            if (root is null) continue;
            pot = root.GetComponentsInChildren<Pot>(true)
                .Where(candidate => candidate?.Plant is not null)
                .OrderBy(candidate => Vector3.SqrMagnitude(candidate.transform.position - hit.point))
                .FirstOrDefault();
            if (pot is not null) return pot;
        }
        return UnityEngine.Object.FindObjectsOfType<Pot>()
            .Where(pot => pot?.Plant is not null)
            .Select(pot => new { Pot = pot, Along = Vector3.Dot(pot.transform.position - ray.origin, ray.direction), Offset = Vector3.Cross(ray.direction, pot.transform.position - ray.origin).magnitude })
            .Where(entry => entry.Along > 0 && entry.Along < 7.5f && entry.Offset < 1.2f)
            .OrderBy(entry => entry.Offset)
            .Select(entry => entry.Pot)
            .FirstOrDefault();
    }

    private void InspectTargetPlant()
    {
        var pot = FindTargetPlantPot() ?? throw new InvalidOperationException("No growing plant found near the crosshair.");
        var plant = pot.Plant!;
        PublishInspector("Plant inspector", new[]
        {
            $"Plant: {plant.SeedDefinition?.Name ?? plant.SeedDefinition?.ID ?? "Unknown"}",
            $"Progress: {plant.NormalizedGrowthProgress * 100f:0.0}% ({(plant.IsFullyGrown ? "mature" : "growing")})",
            $"Quality level: {plant.QualityLevel:0.##}",
            $"Growth time: {plant.GrowthTime} minutes",
            $"Yield: {plant.BaseYieldQuantity} base Ã— {plant.YieldMultiplier:0.##}",
            $"Pot speed: Ã—{pot.GrowSpeedMultiplier:0.##}; temperature: Ã—{pot.GetTemperatureGrowthMultiplier():0.##}",
            $"Harvest points active: {plant.ActiveHarvestables?.Count ?? 0}"
        });
    }

    private void AdvanceTargetPlant()
    {
        var pot = FindTargetPlantPot() ?? throw new InvalidOperationException("No growing plant found near the crosshair.");
        var current = pot.GetGrowthProgressNormalized();
        var stages = Math.Max(1, pot.Plant?.GrowthStages?.Length ?? 4);
        var next = Math.Min(1f, (float)(Math.Floor(current * stages) + 1) / stages);
        pot.SetGrowthProgress_Server(next);
        Report("DevTools", $"Target plant advanced to {next * 100f:0}%.");
        InspectTargetPlant();
    }

    private void SetTargetPlantGrowth(float progress, string message)
    {
        var pot = FindTargetPlantPot() ?? throw new InvalidOperationException("No growing plant found near the crosshair.");
        pot.SetGrowthProgress_Server(progress);
        Report("DevTools", message);
        InspectTargetPlant();
    }

    private void InspectPerson(string selection)
    {
        var closingBracket = selection.IndexOf(']');
        if (!selection.StartsWith('[') || closingBracket < 2)
            throw new ArgumentException("Choose a customer or dealer.");
        var kind = selection[1..closingBracket].Trim();
        var name = selection[(closingBracket + 1)..].Trim();
        if (kind.Equals("Dealer", StringComparison.OrdinalIgnoreCase) && Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers is not null)
        {
            foreach (var dealer in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
            {
                if (dealer is null || !(dealer.FullName ?? dealer.name).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                PublishInspector("Dealer inspector", new[]
                {
                    $"Dealer: {dealer.FullName ?? dealer.name}",
                    $"Cash held: ${dealer.Cash:N0}",
                    $"Packaged product: {dealer.GetPackagedProductAmount()}",
                    $"Assigned customers: {dealer.AssignedCustomers?.Count ?? 0}/{Il2CppScheduleOne.Economy.Dealer.MAX_CUSTOMERS}",
                    $"Relationship: {dealer.RelationData?.NormalizedRelationDelta * 100f:0}%"
                });
                return;
            }
        }
        if (kind.Equals("Customer", StringComparison.OrdinalIgnoreCase) && GameCustomer.UnlockedCustomers is not null)
        {
            foreach (var customer in GameCustomer.UnlockedCustomers)
            {
                if (customer?.NPC is null || !(customer.NPC.FullName ?? customer.NPC.name).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                PublishInspector("Customer inspector", new[]
                {
                    $"Customer: {customer.NPC.FullName ?? customer.NPC.name}",
                    $"Relationship: {customer.NPC.RelationData?.NormalizedRelationDelta * 100f:0}%",
                    $"Addiction: {customer.CurrentAddiction * 100f:0}%",
                    $"Assigned dealer: {customer.AssignedDealer?.FullName ?? "None"}",
                    $"Unlocked: {customer.NPC.RelationData?.Unlocked == true}",
                    $"Maximum quantity per product: {GameCustomer.MaxOrderQuantityPerProduct}"
                });
                return;
            }
        }
        throw new InvalidOperationException($"'{name}' is not currently loaded.");
    }

    private void PublishInspector(string title, IReadOnlyList<string> lines) =>
        _server.Publish(new BridgeMessage { Type = "debug_inspector", Payload = new DebugInspectorPayload(title, lines) });

    private static string RequireWeatherToken(string value)
    {
        var weather = value.Trim().ToLowerInvariant();
        return weather is "clear" or "lightrain" or "heavyrain"
            ? weather
            : throw new ArgumentException("Weather must be Clear, Light Rain, or Heavy Rain.");
    }

    private static string RequireCommandToken(string value, string label)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 64 ||
            trimmed.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '_' and not '-'))
            throw new ArgumentException($"Enter a valid {label} using letters, numbers, '-' or '_'.");
        return trimmed;
    }

    private static string RequireRawConsoleCommand(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 160 || trimmed.Contains('\r') || trimmed.Contains('\n'))
            throw new ArgumentException("Enter one console command of no more than 160 characters.");
        return trimmed;
    }

    private static string FormatCommandNumber(float value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static float ParseCommandNumber(string value)
    {
        if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
            throw new ArgumentException("Enter a valid number using a decimal point.");
        return number;
    }

    private void OpenDealerManagement(string dealerName)
    {
        if (Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers is null)
            throw new InvalidOperationException("No recruited dealers are available.");
        Il2CppScheduleOne.Economy.Dealer? dealer = null;
        foreach (var item in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
        {
            if (item is null || !item.IsRecruited ||
                (!string.IsNullOrWhiteSpace(dealerName) &&
                 !string.Equals(item.FullName, dealerName, StringComparison.OrdinalIgnoreCase))) continue;
            dealer = item;
            break;
        }
        if (dealer is null)
            throw new InvalidOperationException($"Recruited dealer '{dealerName}' was not found.");

        var dialogue = dealer.GetComponent<DialogueController_Dealer>() ??
                       dealer.GetComponentInChildren<DialogueController_Dealer>();
        if (dialogue is null)
            throw new InvalidOperationException("The dealer's dialogue controller is not ready.");
        dialogue.StartGenericDialogue(false);
        Report("DevTools", $"Started dealer dialogue with {dealer.FullName ?? "dealer"}.");
    }

    private void OpenShopInterface(string shopName)
    {
        var filter = shopName.Trim();
        if (filter.Length == 0)
            throw new ArgumentException("Enter part of the shop object's name.");
        var shop = Resources.FindObjectsOfTypeAll<ShopInterface>()
            .FirstOrDefault(item => item is not null && item.gameObject.scene.IsValid() &&
                (item.name ?? item.gameObject.name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase));
        if (shop is null)
            throw new InvalidOperationException($"No loaded shop interface matched '{filter}'.");
        shop.Open();
        Report("DevTools", $"Opened shop interface {shop.name ?? shop.gameObject.name}.");
    }

    private void OpenGameInterface(string selection)
    {
        selection = selection.Trim();
        if (selection.StartsWith("[Shop]", StringComparison.OrdinalIgnoreCase))
        {
            OpenShopInterface(selection[6..].Trim());
            return;
        }

        if (selection.Equals("ATM", StringComparison.OrdinalIgnoreCase))
        {
            var atm = Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.Money.ATM>()
                .FirstOrDefault(item => item is not null && item.gameObject.scene.IsValid());
            if (atm is null)
                throw new InvalidOperationException("An ATM interface is not loaded.");
            OpenWorldInteraction(atm, "ATM", atm.gameObject.name ?? "ATM");
            return;
        }

        if (selection.Equals("Payphone", StringComparison.OrdinalIgnoreCase))
        {
            var payphone = Resources.FindObjectsOfTypeAll<PayPhone>().First(item => item is not null && item.gameObject.scene.IsValid());
            OpenWorldInteraction(payphone, "payphone", payphone.gameObject.name ?? "Payphone");
            return;
        }

        if (selection.Equals("Vending machine", StringComparison.OrdinalIgnoreCase))
        {
            var vending = Resources.FindObjectsOfTypeAll<VendingMachine>().First(item => item is not null && item.gameObject.scene.IsValid());
            OpenWorldInteraction(vending, "vending machine", vending.gameObject.name ?? "Vending machine");
            return;
        }

        if (selection.Equals("Jukebox", StringComparison.OrdinalIgnoreCase))
        {
            var jukebox = Resources.FindObjectsOfTypeAll<JukeboxInterface>().First(item => item is not null && item.gameObject.scene.IsValid());
            OpenWorldInteraction(jukebox, "jukebox", jukebox.gameObject.name ?? "Jukebox");
            return;
        }

        throw new InvalidOperationException($"Interface '{selection}' is not supported yet.");
    }

    private void OpenLaunderingInterface(string name)
    {
        name = name.Trim();
        var laundering = Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.UI.LaunderingInterface>()
            .FirstOrDefault(item => item is not null && item.gameObject.scene.IsValid() && IsOwnedLaunderingInterface(item) &&
                ((item.Business?.PropertyName ?? "").Equals(name, StringComparison.OrdinalIgnoreCase) ||
                 (item.gameObject.name ?? item.name ?? "").Equals(name, StringComparison.OrdinalIgnoreCase)));
        if (laundering is null)
            throw new InvalidOperationException($"Laundering interface '{name}' is not loaded.");
        laundering.Open();
        Report("DevTools", $"Opened laundering interface {name}.");
    }

    private static T FindLoadedInterface<T>(string name) where T : Component
    {
        var item = Resources.FindObjectsOfTypeAll<T>()
            .FirstOrDefault(candidate => candidate is not null && candidate.gameObject.scene.IsValid() &&
                (candidate.gameObject.name ?? candidate.name ?? "").Equals(name, StringComparison.OrdinalIgnoreCase));
        return item ?? throw new InvalidOperationException($"Interface '{name}' is not loaded.");
    }

    private void OpenWorldInteraction(Component component, string kind, string name)
    {
        var interactable = component.GetComponent<InteractableObject>() ??
                           component.GetComponentInParent<InteractableObject>() ??
                           component.GetComponentInChildren<InteractableObject>(true);
        if (interactable is null)
            throw new InvalidOperationException($"The {kind} '{name}' has no interaction controller.");
        interactable.onInteractStart?.Invoke();
        Report("DevTools", $"Opened {kind} interaction {name}.");
    }

    private void SetCultivationInstantGrow(bool enabled)
    {
        var cultivationType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("ScheduleICompanion.ClonalCultivation.ClonalCultivationMod", false))
            .FirstOrDefault(type => type is not null)
            ?? throw new InvalidOperationException("Cultivation is not loaded.");
        var method = cultivationType.GetMethod("SetInstantGrowTesting",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("The installed Cultivation version does not support live Instant Grow changes.");
        if (method.Invoke(null, new object[] { enabled }) is not true)
            throw new InvalidOperationException("Cultivation is still initializing.");
        Report("DevTools", enabled ? "Instant Grow enabled." : "Instant Grow disabled.");
    }

    private void PublishDebugCatalog()
    {
        try
        {
            var interfaces = Resources.FindObjectsOfTypeAll<ShopInterface>()
                .Where(item => item is not null && item.gameObject.scene.IsValid())
                .Select(item => $"[Shop] {item.gameObject.name ?? item.name ?? "Shop"}")
                .Concat(Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.Money.ATM>()
                    .Where(item => item is not null && item.gameObject.scene.IsValid())
                    .Select(_ => "ATM").Take(1))
                .Concat(Resources.FindObjectsOfTypeAll<PayPhone>()
                    .Where(item => item is not null && item.gameObject.scene.IsValid())
                    .Select(_ => "Payphone").Take(1))
                .Concat(Resources.FindObjectsOfTypeAll<VendingMachine>()
                    .Where(item => item is not null && item.gameObject.scene.IsValid())
                    .Select(_ => "Vending machine").Take(1))
                .Concat(Resources.FindObjectsOfTypeAll<JukeboxInterface>()
                    .Where(item => item is not null && item.gameObject.scene.IsValid())
                    .Select(_ => "Jukebox").Take(1))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var launderingInterfaces = Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.UI.LaunderingInterface>()
                .Where(item => item is not null && item.gameObject.scene.IsValid() && IsOwnedLaunderingInterface(item))
                .Select(item => item.Business?.PropertyName ?? item.gameObject.name ?? item.name ?? "Business")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var destinations = new List<string>();
            if (GameProperty.Properties is not null)
                foreach (var property in GameProperty.Properties)
                    if (property is not null)
                        destinations.Add($"[{(property is GameBusiness ? "Business" : "Safehouse")}] {property.PropertyName ?? property.name}");
            if (Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers is not null)
                foreach (var dealer in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
                    if (dealer is not null)
                        destinations.Add($"[Dealer] {dealer.FullName ?? dealer.name}");

            var spawnItems = new List<string>();
            var registry = UnityEngine.Object.FindObjectOfType<Il2CppScheduleOne.Registry>();
            if (registry is not null)
                foreach (var item in registry.GetAllItems())
                    if (item is not null && !string.IsNullOrWhiteSpace(item.ID))
                        spawnItems.Add($"{item.Name ?? item.ID} ({item.ID})");

            var spawnVehicles = new List<string>();
            var vehicleManager = UnityEngine.Object.FindObjectOfType<Il2CppScheduleOne.Vehicles.VehicleManager>();
            if (vehicleManager?.VehiclePrefabs is not null)
                foreach (var vehicle in vehicleManager.VehiclePrefabs)
                    if (vehicle is not null && !string.IsNullOrWhiteSpace(vehicle.VehicleCode))
                        spawnVehicles.Add($"{vehicle.VehicleName ?? vehicle.VehicleCode} ({vehicle.VehicleCode})");

            var people = new List<string>();
            if (GameCustomer.UnlockedCustomers is not null)
                foreach (var customer in GameCustomer.UnlockedCustomers)
                    if (customer?.NPC is not null)
                        people.Add($"[Customer] {customer.NPC.FullName ?? customer.NPC.name}");
            if (Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers is not null)
                foreach (var dealer in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
                    if (dealer is not null)
                        people.Add($"[Dealer] {dealer.FullName ?? dealer.name}");

            _server.Publish(new BridgeMessage
            {
                Type = "debug_catalog",
                Payload = new DebugCatalogPayload(
                    interfaces,
                    launderingInterfaces,
                    destinations.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
                    spawnItems.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
                    spawnVehicles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
                    people.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray())
            });
            var summary = $"{interfaces.Length} supported interface(s)";
            if (summary != _lastDebugCatalogSummary)
            {
                _lastDebugCatalogSummary = summary;
                Report("Debug catalog", summary);
            }
        }
        catch (Exception ex)
        {
            Report("Debug catalog", ex.GetBaseException().Message);
        }
    }

    private void PublishGameTime()
    {
        try
        {
            ResolveTimeManager();
            if (_timeManager is null) return;
            if (_freezeGameTime && _timeManager.TimeSpeedMultiplier != 0f)
                _timeManager.SetTimeSpeedMultiplier(0f);
            _server.Publish(new BridgeMessage
            {
                Type = "game_time",
                Payload = new GameTimePayload(_timeManager.CurrentTime, _timeManager.CurrentDay.ToString(), _timeManager.ElapsedDays)
            });
        }
        catch { _timeManager = null; }
    }

    private void PublishMapPois()
    {
        try
        {
            ResolveNativeMapServices();
            if (_phoneMapContent is null || _nativeMapWidth <= 0 || _nativeMapHeight <= 0) return;
            var contentWidth = Math.Abs(_phoneMapContent.rect.width);
            var contentHeight = Math.Abs(_phoneMapContent.rect.height);
            if (contentWidth <= 0 || contentHeight <= 0) return;

            var pois = new List<MapPoiPayload>();
            for (var i = 0; i < _phoneMapContent.childCount; i++)
            {
                var child = _phoneMapContent.GetChild(i);
                if (child is null) continue;
                var kind = GetPoiKind(child.name ?? "");
                if (kind is null) continue;
                var rect = child.GetComponent<RectTransform>();
                if (rect is null) continue;
                var label = child.GetComponentInChildren<Text>()?.text;
                if (string.IsNullOrWhiteSpace(label)) label = FriendlyPoiName(child.name ?? kind, kind);
                pois.Add(new MapPoiPayload(
                    child.GetInstanceID().ToString(), label!, kind,
                    rect.anchoredPosition.x * (_nativeMapWidth / contentWidth),
                    rect.anchoredPosition.y * (_nativeMapHeight / contentHeight),
                    _nativeMapWidth, _nativeMapHeight));
            }

            if (GameProperty.Properties is not null)
            {
                var businessIds = new HashSet<int>();
                var businessCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var businessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void AddBusiness(GameBusiness? business)
                {
                    if (business is null) return;
                    businessIds.Add(business.GetInstanceID());
                    if (!string.IsNullOrWhiteSpace(business.PropertyCode)) businessCodes.Add(business.PropertyCode);
                    if (!string.IsNullOrWhiteSpace(business.PropertyName)) businessNames.Add(business.PropertyName);
                }
                if (GameBusiness.Businesses is not null)
                {
                    foreach (var business in GameBusiness.Businesses)
                        AddBusiness(business);
                }
                if (GameBusiness.OwnedBusinesses is not null)
                {
                    foreach (var business in GameBusiness.OwnedBusinesses)
                        AddBusiness(business);
                }
                foreach (var property in GameProperty.Properties)
                {
                    var propertyPoi = property?.PoI;
                    if (property is null || propertyPoi is null) continue;
                    var p = propertyPoi.transform.position;
                    if (!TryGetNativeMapPosition(p, out var x, out var y, out var w, out var h)) continue;
                    var isBusiness = property is GameBusiness ||
                                     businessIds.Contains(property.GetInstanceID()) ||
                                     (!string.IsNullOrWhiteSpace(property.PropertyCode) && businessCodes.Contains(property.PropertyCode)) ||
                                     (!string.IsNullOrWhiteSpace(property.PropertyName) && businessNames.Contains(property.PropertyName));
                    // Only an acquired non-business property is a safehouse. Some world
                    // properties report IsOwned for internal game purposes without being
                    // present in the player's authoritative owned-property collection.
                    if (!isBusiness && !IsPlayerOwnedProperty(property)) continue;
                    var kind = isBusiness
                        ? (property.IsOwned ? "Business owned" : "Business unowned")
                        : "Property owned";
                    pois.Add(new MapPoiPayload(
                        $"property-{property.GetInstanceID()}",
                        property.PropertyName ?? property.name ?? "Property",
                        kind,
                        x, y, w, h));
                }
            }

            var sewerManager = SewerManager.Instance;
            if (sewerManager is not null && !sewerManager.IsRandomWorldKeyCollected)
            {
                var pickup = sewerManager.RandomWorldSewerKeyPickup;
                if (pickup is not null && pickup.gameObject.activeInHierarchy)
                {
                    var p = pickup.transform.position;
                    if (TryGetNativeMapPosition(p, out var x, out var y, out var w, out var h))
                        pois.Add(new MapPoiPayload("sewer-key", "Sewer key", "Sewer key", x, y, w, h));
                }
            }

            var objectiveNames = new List<string>();
            foreach (var quest in GetLiveQuests())
            {
                if (quest is null) continue;
                foreach (var entry in quest.Entries)
                {
                    if (entry is null || entry.PoI is null || entry.PoILocation is null ||
                        !entry.State.ToString().Equals("Active", StringComparison.OrdinalIgnoreCase)) continue;
                    var p = entry.PoILocation.position;
                    if (!TryGetNativeMapPosition(p, out var x, out var y, out var w, out var h)) continue;
                    pois.Add(new MapPoiPayload(
                        $"objective-{quest.GetInstanceID()}-{entry.GetInstanceID()}",
                        entry.Title ?? quest.Title ?? "Objective", "Objective", x, y, w, h));
                    objectiveNames.Add(entry.Title ?? quest.Title ?? "Objective");
                }
            }
            var objectiveSummary = objectiveNames.Count == 0
                ? "None visible"
                : string.Join(" | ", objectiveNames);
            if (objectiveSummary != _lastObjectiveSummary)
            {
                _lastObjectiveSummary = objectiveSummary;
                Report("Quest objectives", objectiveSummary);
            }

            if (Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers is not null)
            {
                foreach (var dealer in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
                {
                    if (dealer is null || !dealer.IsRecruited) continue;
                    var p = dealer.transform.position;
                    if (!TryGetNativeMapPosition(p, out var x, out var y, out var w, out var h)) continue;
                    var name = dealer.name ?? dealer.transform.name ?? "Dealer";
                    pois.Add(new MapPoiPayload($"dealer-{dealer.GetInstanceID()}", name, "Dealer", x, y, w, h));
                }
            }

            _server.Publish(new BridgeMessage { Type = "map_pois", Payload = new MapPoiSnapshotPayload(pois) });
        }
        catch (Exception ex) { Report("Map POIs", ex.Message); }
    }

    private static bool IsPlayerOwnedProperty(GameProperty property)
    {
        if (GameProperty.OwnedProperties is null) return false;
        foreach (var owned in GameProperty.OwnedProperties)
        {
            if (owned is not null && owned.GetInstanceID() == property.GetInstanceID())
                return true;
        }
        return false;
    }

    private static string? GetPoiKind(string name) =>
        name.StartsWith("PropertyPoI", StringComparison.OrdinalIgnoreCase) ? null :
        name.StartsWith("ContractPoI", StringComparison.OrdinalIgnoreCase) ? "Contract" :
        name.StartsWith("OwnedVehiclePoI", StringComparison.OrdinalIgnoreCase) ? "Vehicle" :
        name.StartsWith("DeaddropPoI", StringComparison.OrdinalIgnoreCase) ? "Dead drop" : null;

    private static string FriendlyPoiName(string name, string kind)
    {
        var trimmed = name.Replace("PoI", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(Clone)", "", StringComparison.OrdinalIgnoreCase).Trim(' ', '_', '-');
        return string.IsNullOrWhiteSpace(trimmed) ? kind : trimmed;
    }

    private static string GetNpcPortraitKey(Il2CppScheduleOne.NPCs.NPC npc)
    {
        var id = string.IsNullOrWhiteSpace(npc.ID) ? npc.GetInstanceID().ToString() : npc.ID;
        return string.Concat(id.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
    }

    private static bool CacheNpcPortrait(Il2CppScheduleOne.NPCs.NPC npc, string portraitKey)
    {
        Texture2D? cropped = null;
        RenderTexture? temporary = null;
        RenderTexture? previous = null;
        try
        {
            var sprite = npc.MugshotSprite;
            if (sprite is null || sprite.texture is null) return false;
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScheduleICompanion", "Portraits");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{portraitKey}.png");
            if (File.Exists(path)) return true;

            var rect = sprite.rect;
            const int portraitSize = 64;
            temporary = RenderTexture.GetTemporary(portraitSize, portraitSize, 0, RenderTextureFormat.ARGB32);
            var scale = new Vector2(rect.width / sprite.texture.width, rect.height / sprite.texture.height);
            var offset = new Vector2(rect.x / sprite.texture.width, rect.y / sprite.texture.height);
            Graphics.Blit(sprite.texture, temporary, scale, offset);
            previous = RenderTexture.active;
            RenderTexture.active = temporary;
            cropped = new Texture2D(portraitSize, portraitSize, TextureFormat.RGBA32, false);
            cropped.ReadPixels(new Rect(0, 0, portraitSize, portraitSize), 0, 0);
            cropped.Apply();
            var png = ImageConversion.EncodeToPNG(cropped);
            if (png is null || png.Length == 0) return false;
            File.WriteAllBytes(path, png.ToArray());
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (temporary is not null)
                RenderTexture.ReleaseTemporary(temporary);
            if (cropped is not null)
                UnityEngine.Object.Destroy(cropped);
        }
    }

    private void PublishQuests()
    {
        try
        {
            var quests = new List<QuestItemPayload>();
            foreach (var quest in GetLiveQuests())
            {
                if (quest is null) continue;
                var entries = new List<string>();
                foreach (var entry in quest.Entries)
                {
                    if (entry is null || !entry.State.ToString().Equals("Active", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrWhiteSpace(entry.Title)) entries.Add(entry.Title);
                }
                quests.Add(new QuestItemPayload(
                    $"{quest.GetType().Name}:{quest.GetInstanceID()}", quest.Title ?? "Quest", quest.Description ?? "",
                    quest.IsTracked, entries));
            }
            var summary = string.Join(" || ", quests.Select(q =>
                $"{q.Title} :: {q.Description} :: {string.Join(" | ", q.Entries)}"));
            if (summary != _lastQuestSnapshotSummary)
            {
                _lastQuestSnapshotSummary = summary;
                Report("Active quest text", string.IsNullOrWhiteSpace(summary) ? "None" : summary);
            }
            _server.Publish(new BridgeMessage { Type = "quests", Payload = new QuestSnapshotPayload(quests) });
        }
        catch (Exception ex)
        {
            var error = ex.GetBaseException().Message;
            if (error == _lastQuestFeedError) return;
            _lastQuestFeedError = error;
            Report("Quest feed", error);
        }
    }

    private static List<Quest> GetLiveQuests()
    {
        var result = new List<Quest>();
        var seen = new HashSet<int>();

        if (Quest.ActiveQuests is not null)
        {
            foreach (var quest in Quest.ActiveQuests)
            {
                if (quest is not null && seen.Add(quest.GetInstanceID()))
                    result.Add(quest);
            }
        }

        // Schedule I can leave ActiveQuests empty after loading a save even though the
        // registry contains quests whose authoritative state is Active.
        if (Quest.Quests is not null)
        {
            foreach (var quest in Quest.Quests)
            {
                if (quest is null || !quest.State.ToString().Equals("Active", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(quest.GetInstanceID()))
                    result.Add(quest);
            }
        }

        return result;
    }

    private void RefreshOrders(float now)
    {
        var allowSceneFallback = now >= _nextContractFallbackScan;
        if (allowSceneFallback)
            _nextContractFallbackScan = now + 30f;

        var contracts = GetLiveContracts(allowSceneFallback);
        PublishOrders(contracts);
        _operationOrders = contracts.Select(contract => new ActiveOrderDetailPayload(
            ResolveContractCustomer(contract),
            contract.DeliveryLocation?.LocationName ?? "Location not set",
            FormatDeliveryWindow(contract),
            contract.Payment,
            GetContractLines(contract))).ToArray();
        PublishOperationsSnapshot();
    }

    private void PublishOrders(IReadOnlyList<Contract> contracts)
    {
        try
        {
            var orders = new List<OrderPayload>();
            foreach (var contract in contracts)
            {
                if (contract.ProductList?.entries is null)
                    continue;

                var lines = new List<OrderLine>();
                foreach (var entry in contract.ProductList.entries)
                {
                    if (entry is null || entry.Quantity <= 0 || string.IsNullOrWhiteSpace(entry.ProductID))
                        continue;

                    lines.Add(new OrderLine(ResolveProductName(entry.ProductID), entry.Quantity));
                }

                if (lines.Count > 0)
                    orders.Add(new OrderPayload(contract.Title ?? "Active contract", lines, null));
            }

            var summary = string.Join(" || ", orders.Select(order =>
                $"{order.Customer}: {string.Join(", ", order.Lines.Select(line => $"{line.Quantity}x {line.Product}"))}"));
            if (summary != _lastOrderSnapshotSummary)
            {
                _lastOrderSnapshotSummary = summary;
                Report("Active orders", string.IsNullOrWhiteSpace(summary) ? "None" : summary);
            }

            _server.Publish(new BridgeMessage
            {
                Type = "order_snapshot",
                Payload = new OrderSnapshotPayload(orders)
            });
        }
        catch (Exception ex)
        {
            var error = ex.GetBaseException().Message;
            if (error == _lastOrderFeedError) return;
            _lastOrderFeedError = error;
            Report("Order feed", error);
        }
    }

    private static string ResolveProductName(string productId)
    {
        try
        {
            var definition = Il2CppScheduleOne.Registry.GetItem(productId);
            return definition is not null && !string.IsNullOrWhiteSpace(definition.Name)
                ? definition.Name
                : productId;
        }
        catch
        {
            return productId;
        }
    }

    private static List<Contract> GetLiveContracts(bool allowSceneFallback)
    {
        var result = new List<Contract>();
        var seen = new HashSet<int>();

        void Add(Contract? contract)
        {
            if (contract is null ||
                !contract.State.ToString().Equals("Active", StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(contract.GetInstanceID()))
                return;
            result.Add(contract);
        }

        if (Contract.Contracts is not null)
        {
            foreach (var contract in Contract.Contracts)
                Add(contract);
        }

        if (GameCustomer.UnlockedCustomers is not null)
        {
            foreach (var customer in GameCustomer.UnlockedCustomers)
            {
                if (customer is not null)
                    Add(customer.CurrentContract);
            }
        }

        // Registries are authoritative in normal play. Keep the scene-wide lookup as an
        // infrequent recovery path for saves whose registries have not populated correctly.
        if (allowSceneFallback && result.Count == 0)
        {
            foreach (var contract in UnityEngine.Object.FindObjectsOfType<Contract>())
                Add(contract);
        }

        return result;
    }

    private void RefreshMoneyAndRisk(float now)
    {
        try
        {
            if (_moneyManager is null)
                _moneyManager = UnityEngine.Object.FindObjectOfType<MoneyManager>();
            _operationCash = _moneyManager?.cashBalance ?? 0f;
            _operationOnlineBalance = _moneyManager?.onlineBalance ?? 0f;
            if (now >= _nextNetWorthRefresh)
            {
                _nextNetWorthRefresh = now + 15f;
                _operationNetWorth = _moneyManager?.GetNetWorth() ?? 0f;
            }

            var local = Player.Local;
            _operationRisk = local is null
                ? "Waiting for local player"
                : $"{local.CurrentRegion} · Police: {local.CrimeData?.CurrentPursuitLevel.ToString() ?? "None"}";
            var level = UnityEngine.Object.FindObjectOfType<Il2CppScheduleOne.Levelling.LevelManager>();
            if (level is not null)
            {
                _operationRank = level.GetFullRank().ToString();
                _operationTotalXp = level.TotalXP;
            }
        }
        catch (Exception ex)
        {
            _moneyManager = null;
            Report("Operations feed", ex.GetBaseException().Message);
        }
    }

    private void PublishOperationsSnapshot()
    {
        if (Time.unscaledTime >= _nextMixRecommendationRefresh)
        {
            _nextMixRecommendationRefresh = Time.unscaledTime + 15f;
            _operationMixRecommendations = BuildMixRecommendations();
        }
        _server.Publish(new BridgeMessage
        {
            Type = "operations_snapshot",
            Payload = new OperationsSnapshotPayload(
                _operationOrders, _operationStock,
                _operationCash, _operationOnlineBalance, _operationNetWorth,
                _operationProduction, _operationDealers, _operationDeliveries,
                _operationEmployees, _operationLaundering, _operationRisk,
                _operationRank, _operationTotalXp,
                _operationMixRecommendations)
        });
    }

    private static MixRecommendationPayload[] BuildMixRecommendations()
    {
        try
        {
            var manager = UnityEngine.Object.FindObjectOfType<ProductManager>();
            if (manager is null) return Array.Empty<MixRecommendationPayload>();
            var rows = new List<MixRecommendationPayload>();
            if (manager.mixRecipes is not null)
            {
                foreach (var recipe in manager.mixRecipes)
                {
                    if (recipe is null || !recipe.Unlocked ||
                        recipe.Product?.Item is not ProductDefinition output || recipe.Ingredients is null) continue;
                    var ingredients = new List<string>();
                    foreach (var entry in recipe.Ingredients)
                    {
                        var item = entry?.Item;
                        if (item is StorableItemDefinition { IsUnlocked: true } unlocked)
                            ingredients.Add(unlocked.Name);
                    }
                    if (ingredients.Count < 2) continue;
                    rows.Add(new MixRecommendationPayload(
                        output.Name, ingredients[0], string.Join(" + ", ingredients.Skip(1)), manager.GetPrice(output)));
                }
            }

            static string Key(string value) => new(value.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant).ToArray());
            var unlockedIngredients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (manager.ValidMixIngredients is not null)
                foreach (var ingredient in manager.ValidMixIngredients)
                    if (ingredient is StorableItemDefinition { IsUnlocked: true } unlocked)
                        unlockedIngredients.Add(Key(unlocked.Name));

            var unlockedProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (manager.AllProducts is not null)
                foreach (var product in manager.AllProducts)
                    if (product is StorableItemDefinition { IsUnlocked: true } unlocked)
                        unlockedProducts.Add(Key(unlocked.Name));

            var guideRecipes = new (string Product, float Price, string[] Ingredients)[]
            {
                ("Cocaine", 735f, new[] { "Motor Oil", "Cuke", "Paracetamol", "Gasoline", "Cuke", "Battery", "Horse Semen", "Mega Bean" }),
                ("Meth", 340f, new[] { "Banana", "Cuke", "Paracetamol", "Gasoline", "Cuke", "Battery", "Horse Semen", "Mega Bean" }),
                ("OG Kush", 172f, new[] { "Horse Semen", "Paracetamol", "Gasoline", "Cuke", "Mega Bean", "Paracetamol", "Mega Bean", "Battery" }),
                ("Granddaddy Purple", 167f, new[] { "Banana", "Cuke", "Paracetamol", "Gasoline", "Cuke", "Battery", "Horse Semen", "Mega Bean" }),
                ("Green Crack", 148f, new[] { "Gasoline", "Paracetamol", "Cuke", "Banana", "Gasoline", "Cuke", "Viagra", "Banana" }),
                ("Sour Diesel", 144f, new[] { "Iodine", "Paracetamol", "Chili", "Viagra", "Cuke", "Motor Oil" }),
                ("OG Kush (Beginner)", 127f, new[] { "Donut", "Mouth Wash", "Cuke", "Banana", "Viagra", "Flu Medicine" })
            };
            foreach (var guide in guideRecipes)
            {
                var baseName = guide.Product.Replace(" (Beginner)", "", StringComparison.OrdinalIgnoreCase);
                if (!unlockedProducts.Contains(Key(baseName)) ||
                    guide.Ingredients.Any(ingredient => !unlockedIngredients.Contains(Key(ingredient)))) continue;
                rows.Add(new MixRecommendationPayload(
                    guide.Product + " (Guide)", baseName, string.Join(" + ", guide.Ingredients), guide.Price));
            }

            return rows.OrderByDescending(row => row.Price).ThenBy(row => row.Product).Take(10).ToArray();
        }
        catch { return Array.Empty<MixRecommendationPayload>(); }
    }

    private static Dictionary<string, int> GetAvailableProductStock()
    {
        var stock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void AddItem(ItemInstance? item)
        {
            if (item is not ProductItemInstance product) return;
            var name = !string.IsNullOrWhiteSpace(product.Name)
                ? product.Name
                : product.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name)) return;
            var quantity = Math.Max(0, product.GetTotalAmount());
            stock[name] = stock.TryGetValue(name, out var current) ? current + quantity : quantity;
        }

        var playerInventory = Player.Local?._inventory;
        if (playerInventory is not null)
        {
            foreach (var slot in playerInventory)
                AddItem(slot?.ItemInstance);
        }

        if (WorldStorageEntity.All is not null)
        {
            foreach (var storage in WorldStorageEntity.All)
            {
                if (storage is null) continue;
                foreach (var item in storage.GetAllItems())
                    AddItem(item);
            }
        }

        return stock;
    }

    private static OrderLine[] GetContractLines(Contract contract)
    {
        var lines = new List<OrderLine>();
        if (contract.ProductList?.entries is null) return lines.ToArray();
        foreach (var entry in contract.ProductList.entries)
        {
            if (entry is not null && entry.Quantity > 0 && !string.IsNullOrWhiteSpace(entry.ProductID))
                lines.Add(new OrderLine(ResolveProductName(entry.ProductID), entry.Quantity));
        }
        return lines.ToArray();
    }

    private static string ResolveContractCustomer(Contract contract)
    {
        try
        {
            var customer = contract.Customer?.GetComponent<GameCustomer>();
            if (customer?.NPC is not null && !string.IsNullOrWhiteSpace(customer.NPC.FullName))
                return customer.NPC.FullName;
        }
        catch { }
        return contract.Title ?? "Customer";
    }

    private static string FormatDeliveryWindow(Contract contract)
    {
        if (contract.DeliveryWindow is null || !contract.DeliveryWindow.IsEnabled)
            return "Any time";
        return $"{FormatGameTime(contract.DeliveryWindow.WindowStartTime)}–{FormatGameTime(contract.DeliveryWindow.WindowEndTime)}";
    }

    private static string FormatGameTime(int value) => $"{Math.Clamp(value / 100, 0, 23):00}:{Math.Clamp(value % 100, 0, 59):00}";

    private static OperationItemPayload[] BuildProductionStatus()
    {
        var pots = UnityEngine.Object.FindObjectsOfType<Pot>().Where(pot => pot is not null && pot.Plant is not null).ToArray();
        var ready = pots.Count(pot => pot.Plant.IsFullyGrown);
        var slowed = pots.Count(pot => !pot.Plant.IsFullyGrown && pot.GetTemperatureGrowthMultiplier() < 0.95f);
        var growing = pots.Length - ready;
        var rows = new List<OperationItemPayload>
        {
            new("Plants", $"{ready} ready · {growing} growing", ready > 0 ? "Attention" : "Good")
        };
        if (slowed > 0)
            rows.Add(new OperationItemPayload("Temperature", $"{slowed} plant{(slowed == 1 ? "" : "s")} growing slowly", "Warning"));
        return rows.ToArray();
    }

    private static OperationItemPayload[] BuildDealerStatus()
    {
        if (Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers is null)
            return Array.Empty<OperationItemPayload>();
        var rows = new List<OperationItemPayload>();
        foreach (var dealer in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
        {
            if (dealer is null || !dealer.IsRecruited) continue;
            rows.Add(new OperationItemPayload(
                dealer.FullName ?? "Dealer",
                $"{dealer.GetPackagedProductAmount()} product · £{dealer.Cash:0} cash · {dealer.AssignedCustomers?.Count ?? 0} customers",
                dealer.IsConscious ? "Good" : "Warning"));
        }
        return rows.ToArray();
    }

    private OperationItemPayload[] BuildDeliveryStatus()
    {
        if (_deliveryManager is null)
            _deliveryManager = UnityEngine.Object.FindObjectOfType<DeliveryManager>();
        if (_deliveryManager?.Deliveries is null) return Array.Empty<OperationItemPayload>();
        var rows = new List<OperationItemPayload>();
        foreach (var delivery in _deliveryManager.Deliveries)
        {
            if (delivery is null || delivery.Status.ToString().Equals("Completed", StringComparison.OrdinalIgnoreCase)) continue;
            rows.Add(new OperationItemPayload(
                delivery.StoreName ?? "Delivery",
                $"{delivery.Status} · {Math.Max(0, delivery.TimeUntilArrival)} min · {delivery.Destination?.PropertyName ?? delivery.DestinationCode}",
                delivery.TimeUntilArrival <= 0 ? "Attention" : "Good"));
        }
        return rows.ToArray();
    }

    private static OperationItemPayload[] BuildEmployeeStatus()
    {
        return UnityEngine.Object.FindObjectsOfType<Employee>()
            .Where(employee => employee is not null && employee.initialized && !employee.Fired)
            .Select(employee =>
            {
                var working = employee.IsAnyWorkInProgress();
                var issues = employee.WorkIssues?.Count ?? 0;
                return new OperationItemPayload(
                    employee.FullName ?? employee.Type.ToString(),
                    $"{employee.Type} · {(working ? "working" : "idle")} · {(employee.PaidForToday ? "paid" : "unpaid")}",
                    issues > 0 || !employee.PaidForToday ? "Warning" : working ? "Good" : "Idle");
            }).ToArray();
    }

    private static bool IsOwnedLaunderingInterface(Il2CppScheduleOne.UI.LaunderingInterface laundering)
    {
        try
        {
            var business = laundering.Business;
            if (business is null || GameBusiness.OwnedBusinesses is null) return false;
            var businessId = business.GetInstanceID();
            foreach (var ownedBusiness in GameBusiness.OwnedBusinesses)
            {
                if (ownedBusiness is not null && ownedBusiness.GetInstanceID() == businessId)
                    return true;
            }
        }
        catch
        {
            // An interface can exist while its business is still loading. Keep it hidden
            // until the next catalogue refresh can verify ownership safely.
        }
        return false;
    }

    private static OperationItemPayload[] BuildLaunderingStatus()
    {
        if (GameBusiness.OwnedBusinesses is null) return Array.Empty<OperationItemPayload>();
        var rows = new List<OperationItemPayload>();
        foreach (var business in GameBusiness.OwnedBusinesses)
        {
            if (business?.LaunderingOperations is null) continue;
            foreach (var operation in business.LaunderingOperations)
            {
                if (operation is null) continue;
                var ready = operation.minutesSinceStarted >= operation.completionTime_Minutes;
                rows.Add(new OperationItemPayload(
                    business.PropertyName ?? "Laundering",
                    $"${operation.amount:N0}",
                    ready ? "Ready" : "In Progress"));
            }
        }
        return rows.ToArray();
    }

    private void PublishMessages()
    {
        try
        {
            var messages = new List<MessagePreviewPayload>();
            foreach (var conversation in MessagesApp.ActiveConversations)
            {
                if (conversation is null || conversation.messageHistory is null || conversation.messageHistory.Count == 0) continue;
                var last = conversation.messageHistory[conversation.messageHistory.Count - 1];
                if (last is null || string.IsNullOrWhiteSpace(last.text)) continue;
                messages.Add(new MessagePreviewPayload(
                    conversation.GetHashCode().ToString(), conversation.contactName ?? "Unknown",
                    last.text, last.sender.ToString(), !conversation.Read));
            }
            _server.Publish(new BridgeMessage
            {
                Type = "messages",
                Payload = new MessageSnapshotPayload(messages.Take(12).ToArray())
            });
        }
        catch { }
    }

    private void SubmitConsoleCommand(string command, string label)
    {
        GameConsole.SubmitCommand(command);
        Report("DevTools", $"{label}: {command}");
    }

    public void PublishPlayerPosition()
    {
        if (_selectedPlayer is null)
        {
            if (!_reportedMissingPlayer)
            {
                _reportedMissingPlayer = true;
                Report("Player position", "Player_Local has not appeared yet. Load into a save and wait a moment.");
            }
            return;
        }

        try
        {
            ResolveNativeMapServices();
            var markers = new List<PlayerMarkerPayload>();
            foreach (var pair in _trackedPlayers.ToArray())
            {
                var transform = pair.Value;
                if (transform is null) continue;
                var isLocal = transform == _selectedPlayer;
                var worldTransform = transform;
                var isInVehicle = false;

                // Match BetterMiniMap: use the current vehicle position when the player is driving.
                try
                {
                    var movement = transform.gameObject.GetComponent<PlayerMovement>();
                    if (movement is not null && movement.CurrentVehicle is not null)
                    {
                        worldTransform = movement.CurrentVehicle.transform;
                        isInVehicle = true;
                    }
                }
                catch { }

                var p = worldTransform.position;
                var path = BuildPath(transform);
                var native = TryGetNativeMapPosition(p, out var mapX, out var mapY, out var mapW, out var mapH);
                markers.Add(new PlayerMarkerPayload(
                    pair.Key.ToString(),
                    _trackedPlayerNames.GetValueOrDefault(pair.Key, isLocal ? "You" : transform.name ?? "Player"),
                    p.x, p.y, p.z, worldTransform.eulerAngles.y, isLocal, isInVehicle,
                    InferArea(_sceneName, path), native, mapX, mapY, mapW, mapH));
            }

            if (markers.Count == 0)
            {
                _server.Publish(new BridgeMessage
                {
                    Type = "player_markers",
                    Payload = new PlayerMarkersSnapshotPayload(Array.Empty<PlayerMarkerPayload>())
                });
                _server.Publish(new BridgeMessage
                {
                    Type = "npc_markers",
                    Payload = new NpcMarkersSnapshotPayload(Array.Empty<NpcMarkerPayload>())
                });
                return;
            }
            var local = markers.FirstOrDefault(x => x.IsLocal);
            if (local is not null)
            {
                _server.Publish(new BridgeMessage
                {
                    Type = "position",
                    Payload = new PositionPayload(local.X, local.Y, local.Z, local.Heading, local.Area,
                        local.HasNativeMapPosition, local.MapX, local.MapY, local.MapWidth, local.MapHeight)
                });
            }
            _server.Publish(new BridgeMessage { Type = "player_markers", Payload = new PlayerMarkersSnapshotPayload(markers) });

            var now = Time.unscaledTime;
            if (now < _nextNpcMarkerPublish) return;
            _nextNpcMarkerPublish = now + 1f;

            var npcMarkers = new List<NpcMarkerPayload>();
            foreach (var pair in _trackedNpcs.ToArray())
            {
                var transform = pair.Value;
                if (transform is null) continue;
                var p = transform.position;
                var path = BuildPath(transform);
                var native = TryGetNativeMapPosition(p, out var mapX, out var mapY, out var mapW, out var mapH);
                npcMarkers.Add(new NpcMarkerPayload(
                    _trackedNpcMarkerIds.GetValueOrDefault(pair.Key, pair.Key.ToString()),
                    _trackedNpcNames.GetValueOrDefault(pair.Key, transform.name ?? "NPC"),
                    p.x, p.y, p.z, transform.eulerAngles.y,
                    _trackedNpcKinds.GetValueOrDefault(pair.Key, "NPC"), InferArea(_sceneName, path),
                    native, mapX, mapY, mapW, mapH));
            }
            _server.Publish(new BridgeMessage { Type = "npc_markers", Payload = new NpcMarkersSnapshotPayload(npcMarkers) });
        }
        catch (Exception ex)
        {
            _selectedPlayer = null;
            Report("Player position", $"Targeted player became unavailable: {ex.Message}");
        }
    }

    private void ResolveNativeMapServices()
    {
        try
        {
            if (_mapPositionUtility is null)
                _mapPositionUtility = UnityEngine.Object.FindObjectOfType<MapPositionUtility>();

            if (_phoneMapContent is null)
            {
                var content = GameObject.Find("GameplayMenu/Phone/phone/AppsCanvas/MapApp/Container/Scroll View/Viewport/Content");
                if (content is not null)
                {
                    _phoneMapContent = content.GetComponent<RectTransform>();
                    _phoneMapImage = content.GetComponent<Image>();
                }
            }

            if (_phoneMapImage is not null && _phoneMapImage.sprite is not null)
            {
                _nativeMapWidth = _phoneMapImage.sprite.rect.width;
                _nativeMapHeight = _phoneMapImage.sprite.rect.height;
            }
            else if (_phoneMapContent is not null)
            {
                _nativeMapWidth = Math.Abs(_phoneMapContent.rect.width);
                _nativeMapHeight = Math.Abs(_phoneMapContent.rect.height);
            }

            if (!_reportedNativeMapReady && _mapPositionUtility is not null && _nativeMapWidth > 0 && _nativeMapHeight > 0)
            {
                _reportedNativeMapReady = true;
                Report("Native map", $"MapPositionUtility active; source map {_nativeMapWidth:0} x {_nativeMapHeight:0}.");
            }
        }
        catch (Exception ex)
        {
            if (!_reportedNativeMapReady)
                Report("Native map", $"Waiting for phone map services: {ex.Message}");
        }
    }

    private bool TryGetNativeMapPosition(Vector3 worldPosition, out float x, out float y, out float width, out float height)
    {
        x = y = 0;
        width = _nativeMapWidth;
        height = _nativeMapHeight;
        try
        {
            if (_mapPositionUtility is null || _phoneMapContent is null || width <= 0 || height <= 0)
                return false;

            var mapPosition = _mapPositionUtility.GetMapPosition(worldPosition);
            var contentWidth = Math.Abs(_phoneMapContent.rect.width);
            var contentHeight = Math.Abs(_phoneMapContent.rect.height);
            if (contentWidth <= 0 || contentHeight <= 0)
                return false;

            // BetterMiniMap scales the game's local map coordinates from the phone content rect
            // to the source sprite's pixel dimensions.
            x = mapPosition.x * (width / contentWidth);
            y = mapPosition.y * (height / contentHeight);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DiscoverTargetedPlayers()
    {
        try
        {
            var registeredCount = 0;
            if (Player.PlayerList is not null)
            {
                // PlayerList is authoritative. Rebuilding these maps removes disconnected
                // players instead of retaining their last transform indefinitely.
                _trackedPlayers.Clear();
                _trackedPlayerNames.Clear();
                _selectedPlayer = null;
                foreach (var player in Player.PlayerList)
                {
                    if (player is null || player.transform is null) continue;
                    registeredCount++;
                    var transform = player.transform;
                    var id = transform.gameObject.GetInstanceID();
                    var isLocal = player.IsLocalPlayer || player == Player.Local;
                    _trackedPlayers[id] = transform;
                    _trackedPlayerNames[id] = isLocal
                        ? "You"
                        : (!string.IsNullOrWhiteSpace(player.PlayerName) ? player.PlayerName : "Player");
                    if (isLocal)
                    {
                        _selectedPlayer = transform;
                        _selectedPlayerPath = BuildPath(transform);
                        _reportedMissingPlayer = false;
                    }
                }
            }
            if (registeredCount != _lastRegisteredPlayerCount)
            {
                _lastRegisteredPlayerCount = registeredCount;
                Report("Player registry", $"{registeredCount} connected player(s)." );
            }

            // The network registry is authoritative. Scanning the hierarchy as well can
            // discover the local player's nested Player_Local object as a third player.
            if (registeredCount > 0)
                return;

            GameObject? localObject = GameObject.Find("Player_Local");
            var lookupMethod = "GameObject.Find";

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            // GameObject.Find can miss an object during scene activation or when it is nested
            // beneath an inactive parent. Fall back to an exact, bounded hierarchy-name lookup.
            if (localObject is null)
            {
                lookupMethod = "hierarchy fallback";
                var inspected = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    localObject = FindExactNamedObject(root.transform, "Player_Local", ref inspected, 4000);
                    if (localObject is not null || inspected >= 4000)
                        break;
                }
            }

            if (localObject is not null)
            {
                var wasMissing = _selectedPlayer is null;
                _selectedPlayer = localObject.transform;
                _selectedPlayerPath = BuildPath(_selectedPlayer);
                _reportedMissingPlayer = false;
                var id = localObject.GetInstanceID();
                _trackedPlayers[id] = _selectedPlayer;
                _trackedPlayerNames[id] = "You";

                if (wasMissing)
                    Report("Player tracking", $"Player_Local resolved via {lookupMethod}: {_selectedPlayerPath}");
            }
            // Multiplayer entries come from PlayerList. A second full hierarchy walk here
            // used to examine thousands of objects every two seconds without adding reliable
            // network players, so the exact local-player fallback is deliberately bounded.
        }
        catch (Exception ex)
        {
            Report("Player discovery", ex.Message);
        }
    }


    private static GameObject? FindExactNamedObject(Transform transform, string expectedName, ref int inspected, int limit)
    {
        if (transform is null || inspected >= limit)
            return null;

        inspected++;
        try
        {
            var gameObject = transform.gameObject;
            if (gameObject is not null && string.Equals(gameObject.name, expectedName, StringComparison.Ordinal))
                return gameObject;

            for (var i = 0; i < transform.childCount && inspected < limit; i++)
            {
                var child = transform.GetChild(i);
                var match = FindExactNamedObject(child, expectedName, ref inspected, limit);
                if (match is not null)
                    return match;
            }
        }
        catch
        {
            // Ignore a destroyed IL2CPP wrapper and continue with the rest of the hierarchy.
        }

        return null;
    }

    private void DiscoverPlayerObjects(Transform transform, ref int visited, int limit)
    {
        if (visited++ >= limit) return;
        var go = transform.gameObject;
        var name = go.name ?? "";
        var likelyName = name.Equals("Player_Local", StringComparison.OrdinalIgnoreCase) ||
                         name.StartsWith("Player_", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("NetworkPlayer", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("PlayerAvatar", StringComparison.OrdinalIgnoreCase);
        var likelyComponent = false;
        if (!likelyName && name.Contains("player", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                likelyComponent = go.GetComponents<Component>().Where(x => x is not null).Take(30)
                    .Any(x => (x.GetType().Name ?? "").Contains("PlayerMovement", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
        }

        if (likelyName || likelyComponent)
        {
            var id = go.GetInstanceID();
            _trackedPlayers[id] = transform;
            if (!_trackedPlayerNames.ContainsKey(id))
                _trackedPlayerNames[id] = name.Equals("Player_Local", StringComparison.OrdinalIgnoreCase) ? "You" : name;
        }

        for (var i = 0; i < transform.childCount && visited < limit; i++)
            DiscoverPlayerObjects(transform.GetChild(i), ref visited, limit);
    }

    private void StartNpcHierarchyDiscovery()
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            _npcScanQueue.Clear();
            _scannedNpcs.Clear();
            _scannedNpcNames.Clear();
            _scannedNpcKinds.Clear();
            _npcScanVisited = 0;
            foreach (var root in scene.GetRootGameObjects())
                if (root is not null && root.transform is not null)
                    _npcScanQueue.Enqueue(root.transform);
        }
        catch (Exception ex) { Report("NPC discovery", ex.Message); }
    }

    private void ProcessNpcHierarchySlice(int budget, int limit)
    {
        if (_npcScanQueue.Count == 0) return;
        var processed = 0;
        while (processed++ < budget && _npcScanQueue.Count > 0 && _npcScanVisited < limit)
        {
            var transform = _npcScanQueue.Dequeue();
            if (transform is null) continue;
            _npcScanVisited++;
            try
            {
                CaptureNpcCandidate(transform);
                for (var i = 0; i < transform.childCount && _npcScanVisited + _npcScanQueue.Count < limit; i++)
                    _npcScanQueue.Enqueue(transform.GetChild(i));
            }
            catch { }
        }

        if (_npcScanQueue.Count > 0 && _npcScanVisited < limit) return;
        _npcScanQueue.Clear();

        // Swap only the generic NPC portion. Potential customers are maintained separately
        // from the game's registry, so their moving markers never disappear during a scan.
        foreach (var id in _trackedNpcs.Keys.Where(id => !_potentialCustomerIds.Contains(id)).ToArray())
        {
            _trackedNpcs.Remove(id);
            _trackedNpcNames.Remove(id);
            _trackedNpcKinds.Remove(id);
            _trackedNpcMarkerIds.Remove(id);
        }
        foreach (var pair in _scannedNpcs)
        {
            if (_potentialCustomerIds.Contains(pair.Key)) continue;
            _trackedNpcs[pair.Key] = pair.Value;
            _trackedNpcNames[pair.Key] = _scannedNpcNames[pair.Key];
            _trackedNpcKinds[pair.Key] = _scannedNpcKinds[pair.Key];
            _trackedNpcMarkerIds[pair.Key] = pair.Key.ToString();
        }
    }

    private void CaptureNpcCandidate(Transform transform)
    {
        var go = transform.gameObject;
        var name = go.name ?? "";
        var lower = name.ToLowerInvariant();
        var kind = lower.Contains("dealer") ? "Dealer" :
                   lower.Contains("customer") ? "Customer" :
                   lower.Contains("supplier") ? "Supplier" :
                   lower.Contains("employee") ? "Employee" :
                   lower.Contains("npc") ? "NPC" : "";
        var excluded = lower.Contains("manager") || lower.Contains("spawner") ||
                       lower.Contains("spawn") || lower.Contains("trigger") ||
                       lower.Contains("request") || lower.Contains("ui") ||
                       lower.Contains("icon") || lower.Contains("marker") ||
                       lower.Contains("template");
        if (string.IsNullOrEmpty(kind) || excluded || !go.activeInHierarchy) return;
        var id = go.GetInstanceID();
        _scannedNpcs[id] = transform;
        _scannedNpcNames[id] = name;
        _scannedNpcKinds[id] = kind;
    }

    private void TrackPotentialCustomers()
    {
        var visibleIds = new HashSet<int>();
        if (GameCustomer.LockedCustomers is null)
        {
            RemoveMissingPotentialCustomers(visibleIds);
            return;
        }
        var lockedCount = 0;
        var visibleCount = 0;
        var portraitCount = 0;
        foreach (var customer in GameCustomer.LockedCustomers)
        {
            lockedCount++;
            if (customer is null || customer.NPC is null || customer.potentialCustomerPoI is null)
                continue;
            var potentialPoi = customer.potentialCustomerPoI;
            var uiActive = potentialPoi.UI is not null && potentialPoi.UI.gameObject.activeSelf;
            var iconActive = potentialPoi.IconContainer is not null && potentialPoi.IconContainer.gameObject.activeSelf;
            if (!potentialPoi.enabled || (!uiActive && !iconActive)) continue;

            var npc = customer.NPC;
            if (npc.transform is null || !npc.gameObject.activeInHierarchy) continue;
            visibleCount++;
            var portraitKey = GetNpcPortraitKey(npc);
            if (CacheNpcPortrait(npc, portraitKey)) portraitCount++;
            var id = npc.gameObject.GetInstanceID();
            visibleIds.Add(id);
            _potentialCustomerIds.Add(id);
            _trackedNpcs[id] = npc.transform;
            _trackedNpcNames[id] = string.IsNullOrWhiteSpace(npc.FullName)
                ? npc.name ?? "Potential customer"
                : npc.FullName;
            _trackedNpcKinds[id] = "Potential customer";
            _trackedNpcMarkerIds[id] = $"potential-customer-{portraitKey}";
        }

        RemoveMissingPotentialCustomers(visibleIds);

        var summary = $"{visibleCount} moving marker(s) of {lockedCount} locked; {portraitCount} portrait(s) ready";
        if (summary == _lastPotentialCustomerSummary) return;
        _lastPotentialCustomerSummary = summary;
        Report("Potential customers", summary);
    }

    private void RemoveMissingPotentialCustomers(HashSet<int> visibleIds)
    {
        foreach (var id in _potentialCustomerIds.Where(id => !visibleIds.Contains(id)).ToArray())
        {
            _potentialCustomerIds.Remove(id);
            _trackedNpcs.Remove(id);
            _trackedNpcNames.Remove(id);
            _trackedNpcKinds.Remove(id);
            _trackedNpcMarkerIds.Remove(id);
        }
    }

#if false // Removed v1.3 developer inspector/candidate subsystem.
    public void PublishExplorerSnapshot()
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            var results = new List<ExplorerObjectPayload>();
            var visited = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                WalkInteresting(root.transform, root.name, results, ref visited, 5000, 500);
                if (visited >= 5000) break;
            }
            _server.Publish(new BridgeMessage
            {
                Type = "explorer_snapshot",
                Payload = new ExplorerSnapshotPayload(scene.name, visited, results.Take(500).ToArray())
            });
            Report("Explorer", $"Scanned {visited} objects; returned {results.Count} candidates in {scene.name}");
        }
        catch (Exception ex) { Report("Explorer error", ex.Message); }
    }

    public void PublishHierarchySnapshot()
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Report("Hierarchy", "No loaded scene is available.");
                return;
            }

            _objectCache.Clear();
            var nodes = new List<HierarchyNodePayload>();
            var visited = 0;
            const int limit = 8000;
            foreach (var root in scene.GetRootGameObjects())
            {
                AddHierarchy(root.transform, 0, 0, root.name, nodes, ref visited, limit);
                if (visited >= limit) break;
            }

            _server.Publish(new BridgeMessage
            {
                Type = "hierarchy_snapshot",
                Payload = new HierarchySnapshotPayload(scene.name, visited, visited >= limit, nodes)
            });
            Report("Hierarchy", $"Exported {nodes.Count} live objects from {scene.name}");
        }
        catch (Exception ex) { Report("Hierarchy error", ex.Message); }
    }

    public void InspectObject(int instanceId)
    {
        if (!TryGetObject(instanceId, out var gameObject))
        {
            Report("Inspector", "Object was not found. Refresh the hierarchy and try again.");
            return;
        }

        try
        {
            var t = gameObject.transform;
            var p = t.position;
            var r = t.eulerAngles;
            var components = new List<ComponentInspectionPayload>();
            foreach (var component in gameObject.GetComponents<Component>().Where(x => x is not null).Take(40))
                components.Add(InspectComponent(component));

            _server.Publish(new BridgeMessage
            {
                Type = "object_inspection",
                Payload = new ObjectInspectionPayload(
                    instanceId, gameObject.name ?? "(unnamed)", BuildPath(t), gameObject.activeInHierarchy,
                    SafeLayerName(gameObject.layer), SafeTag(gameObject), p.x, p.y, p.z,
                    r.x, r.y, r.z, components)
            });
        }
        catch (Exception ex) { Report("Inspection error", ex.Message); }
    }

    public void StartObjectMovementTest(int instanceId, float durationSeconds)
    {
        if (!TryGetObject(instanceId, out var gameObject))
        {
            PublishObjectMovementTest("error", instanceId, "", 0, Vector3.zero, Vector3.zero,
                0, 0, 0, false, "Object was not found. Refresh the hierarchy and try again.");
            return;
        }

        try
        {
            var transform = gameObject.transform;
            var duration = Mathf.Clamp(durationSeconds, 2f, 30f);
            _movementTestTransform = transform;
            _movementTestInstanceId = instanceId;
            _movementTestPath = BuildPath(transform);
            _movementTestStartedAt = Time.unscaledTime;
            _movementTestDuration = duration;
            _movementTestLastPublishAt = 0;
            _movementTestStartPosition = transform.position;
            _movementTestLastPosition = _movementTestStartPosition;
            _movementTestStartRotation = transform.eulerAngles;
            _movementTestDistanceTravelled = 0;
            _movementTestMaxDisplacement = 0;
            _movementTestMaxRotationChange = 0;

            PublishObjectMovementTest("started", instanceId, _movementTestPath, duration,
                _movementTestStartPosition, _movementTestStartPosition, 0, 0, 0, false,
                $"Monitoring {_movementTestPath} for {duration:0} seconds. Move around now.");
        }
        catch (Exception ex)
        {
            CancelObjectMovementTest($"Could not start movement test: {ex.Message}", publish: true);
        }
    }

    private void UpdateObjectMovementTest(float now)
    {
        if (_movementTestTransform is null)
            return;

        try
        {
            var current = _movementTestTransform.position;
            var rotation = _movementTestTransform.eulerAngles;
            var frameDistance = Vector3.Distance(_movementTestLastPosition, current);

            // Ignore microscopic floating-point jitter but retain normal walking movement.
            if (frameDistance > 0.0005f && frameDistance < 100f)
                _movementTestDistanceTravelled += frameDistance;

            _movementTestLastPosition = current;
            _movementTestMaxDisplacement = Mathf.Max(
                _movementTestMaxDisplacement,
                Vector3.Distance(_movementTestStartPosition, current));

            var rotationChange = Quaternion.Angle(
                Quaternion.Euler(_movementTestStartRotation),
                Quaternion.Euler(rotation));
            _movementTestMaxRotationChange = Mathf.Max(_movementTestMaxRotationChange, rotationChange);

            var elapsed = now - _movementTestStartedAt;
            var remaining = Mathf.Max(0, _movementTestDuration - elapsed);
            var positionChanged = _movementTestMaxDisplacement >= 0.02f ||
                                  _movementTestDistanceTravelled >= 0.05f;

            if (now >= _movementTestLastPublishAt)
            {
                _movementTestLastPublishAt = now + 0.25f;
                PublishObjectMovementTest("running", _movementTestInstanceId, _movementTestPath,
                    remaining, _movementTestStartPosition, current,
                    _movementTestMaxDisplacement, _movementTestDistanceTravelled,
                    _movementTestMaxRotationChange, positionChanged,
                    $"Testing {_movementTestPath}: {remaining:0.0}s remaining.");
            }

            if (elapsed < _movementTestDuration)
                return;

            var state = positionChanged ? "moving" : "static";
            var message = positionChanged
                ? $"Position changed for {_movementTestPath}. This remains a possible player object."
                : $"No meaningful position change was detected for {_movementTestPath}. It is unlikely to be the moving player root.";

            PublishObjectMovementTest(state, _movementTestInstanceId, _movementTestPath,
                0, _movementTestStartPosition, current,
                _movementTestMaxDisplacement, _movementTestDistanceTravelled,
                _movementTestMaxRotationChange, positionChanged, message);

            _movementTestTransform = null;
        }
        catch (Exception ex)
        {
            CancelObjectMovementTest($"Selected object became unavailable: {ex.Message}", publish: true);
        }
    }

    private void CancelObjectMovementTest(string message, bool publish)
    {
        if (publish)
        {
            PublishObjectMovementTest("cancelled", _movementTestInstanceId, _movementTestPath,
                0, _movementTestStartPosition, _movementTestLastPosition,
                _movementTestMaxDisplacement, _movementTestDistanceTravelled,
                _movementTestMaxRotationChange, false, message);
        }

        _movementTestTransform = null;
        _movementTestInstanceId = 0;
        _movementTestPath = "";
    }

    public void UseAsPlayer(int instanceId)
    {
        if (!TryGetObject(instanceId, out var gameObject))
        {
            PublishPlayerSelection(false, "Object was not found. Refresh the hierarchy.", instanceId, "");
            return;
        }

        _selectedPlayer = gameObject.transform;
        _selectedPlayerPath = BuildPath(_selectedPlayer);
        _reportedMissingPlayer = false;
        PublishPlayerSelection(true, $"Tracking {_selectedPlayerPath} as the player.", instanceId, _selectedPlayerPath);
        Report("Player selected", _selectedPlayerPath);
    }

    public void PickUnderCrosshair()
    {
        try
        {
            var camera = Camera.main;
            if (camera is null)
            {
                PublishPick(false, "Camera.main was not available.", 0, "", "", 0);
                return;
            }

            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (!Physics.Raycast(ray, out var hit, 250f))
            {
                PublishPick(false, "Nothing was hit under the crosshair.", 0, "", "", 0);
                return;
            }

            var gameObject = hit.collider.gameObject;
            CacheObject(gameObject);
            PublishPick(true, "Object selected under crosshair.", gameObject.GetInstanceID(),
                gameObject.name ?? "(unnamed)", BuildPath(gameObject.transform), hit.distance);
        }
        catch (Exception ex) { PublishPick(false, ex.Message, 0, "", "", 0); }
    }

    private ComponentInspectionPayload InspectComponent(Component component)
    {
        // Important: do not invoke arbitrary IL2CPP fields or property getters here.
        // Some generated accessors can raise a native access violation (0xc0000005),
        // which cannot be recovered reliably by a managed try/catch block.
        try
        {
            var type = component.GetType();
            var typeName = type.FullName ?? type.Name;
            return new ComponentInspectionPayload(
                typeName,
                Array.Empty<MemberValuePayload>(),
                "Member-value inspection is disabled in safe mode. Component type only.");
        }
        catch
        {
            return new ComponentInspectionPayload(
                "<IL2CPP component>",
                Array.Empty<MemberValuePayload>(),
                "Component metadata could not be read safely.");
        }
    }

    private static bool IsSafeValueType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)) return true;
        var name = type.FullName ?? type.Name;
        return name == "UnityEngine.Vector2" || name == "UnityEngine.Vector3" || name == "UnityEngine.Vector4" ||
               name == "UnityEngine.Quaternion" || name == "UnityEngine.Color";
    }

    private static string SafeTypeName(Type type)
    {
        try { return type.FullName ?? type.Name; } catch { return "unknown"; }
    }

    private static string SafeValue(Func<object?> getter)
    {
        try
        {
            var value = getter();
            if (value is null) return "null";
            var text = value.ToString() ?? "";
            return text.Length > 300 ? text[..300] + "…" : text;
        }
        catch (Exception ex) { return $"<unavailable: {ex.GetType().Name}>"; }
    }

    private void AddHierarchy(Transform transform, int parentId, int depth, string path,
        List<HierarchyNodePayload> nodes, ref int visited, int limit)
    {
        if (visited >= limit) return;
        visited++;
        try
        {
            var go = transform.gameObject;
            var id = go.GetInstanceID();
            _objectCache[id] = go;
            var p = transform.position;
            nodes.Add(new HierarchyNodePayload(id, parentId, depth, go.name ?? "(unnamed)", path,
                go.activeInHierarchy, SafeLayerName(go.layer), SafeTag(go), p.x, p.y, p.z,
                transform.childCount, SafeComponentNames(go)));

            for (var i = 0; i < transform.childCount && visited < limit; i++)
            {
                Transform child;
                try { child = transform.GetChild(i); } catch { continue; }
                AddHierarchy(child, id, depth + 1, $"{path}/{child.gameObject.name}[{i}]", nodes, ref visited, limit);
            }
        }
        catch { }
    }

    private void WalkInteresting(Transform transform, string path, List<ExplorerObjectPayload> results,
        ref int visited, int maxVisited, int maxResults)
    {
        if (visited >= maxVisited) return;
        visited++;
        try
        {
            var go = transform.gameObject;
            var components = SafeComponentNames(go);
            if (results.Count < maxResults && IsInteresting(go.name ?? "", path, SafeTag(go), components))
            {
                var p = transform.position;
                results.Add(new ExplorerObjectPayload(path, go.name ?? "(unnamed)", go.activeInHierarchy,
                    components, p.x, p.y, p.z, transform.childCount, SafeLayerName(go.layer), SafeTag(go)));
            }
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                WalkInteresting(child, $"{path}/{child.gameObject.name}[{i}]", results, ref visited, maxVisited, maxResults);
                if (visited >= maxVisited) break;
            }
        }
        catch { }
    }

    private bool TryGetObject(int instanceId, out GameObject gameObject)
    {
        if (_objectCache.TryGetValue(instanceId, out gameObject!) && gameObject is not null) return true;
        gameObject = null!;
        return false;
    }

    private void CacheObject(GameObject gameObject) => _objectCache[gameObject.GetInstanceID()] = gameObject;
#endif

    private static string BuildPath(Transform transform)
    {
        var parts = new List<string>();
        Transform? current = transform;
        var guard = 0;
        while (current is not null && guard++ < 100)
        {
            parts.Add(current.gameObject.name ?? "(unnamed)");
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

#if false
    private static string[] SafeComponentNames(GameObject gameObject)
    {
        try
        {
            return gameObject.GetComponents<Component>().Where(c => c is not null).Select(c =>
            {
                try { return c.GetType().FullName ?? c.GetType().Name; }
                catch { return "<IL2CPP component>"; }
            }).Distinct().Take(50).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static bool IsInteresting(string name, string path, string tag, string[] components)
    {
        if (string.Equals(tag, "Player", StringComparison.OrdinalIgnoreCase)) return true;
        return InterestingHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase) ||
            path.Contains(h, StringComparison.OrdinalIgnoreCase) || components.Any(c => c.Contains(h, StringComparison.OrdinalIgnoreCase)));
    }

    private static string SafeLayerName(int layer) { try { return LayerMask.LayerToName(layer) ?? layer.ToString(); } catch { return layer.ToString(); } }
    private static string SafeTag(GameObject go) { try { return go.tag ?? ""; } catch { return ""; } }
#endif
    private static string InferArea(string scene, string path) =>
        scene.Contains("sewer", StringComparison.OrdinalIgnoreCase) || path.Contains("sewer", StringComparison.OrdinalIgnoreCase) || path.Contains("tunnel", StringComparison.OrdinalIgnoreCase)
            ? "sewer" : "overworld";

#if false
    private void PublishObjectMovementTest(
        string state, int instanceId, string path, float secondsRemaining,
        Vector3 start, Vector3 current, float displacement, float distanceTravelled,
        float rotationChange, bool positionChanged, string message)
    {
        _server.Publish(new BridgeMessage
        {
            Type = "object_movement_test",
            Payload = new ObjectMovementTestStatusPayload(
                state, instanceId, path, secondsRemaining,
                start.x, start.y, start.z,
                current.x, current.y, current.z,
                displacement, distanceTravelled, rotationChange,
                positionChanged, message)
        });
    }

    private void PublishPick(bool hit, string message, int id, string name, string path, float distance) =>
        _server.Publish(new BridgeMessage { Type = "pick_result", Payload = new PickResultPayload(hit, message, id, name, path, distance) });

    private void PublishPlayerSelection(bool success, string message, int id, string path) =>
        _server.Publish(new BridgeMessage { Type = "player_selection", Payload = new PlayerSelectionPayload(success, message, id, path) });
#endif

    private void Report(string name, string value)
    {
        _logger.Msg($"{name}: {value}");
        _server.Publish(new BridgeMessage { Type = "diagnostic", Payload = new DiagnosticPayload(name, value) });
    }

    public void Dispose()
    {
        try
        {
            if (_freezeGameTime && _timeManager is not null)
                _timeManager.SetTimeSpeedMultiplier(_timeSpeedBeforeFreeze);
        }
        catch { }
    }
}
