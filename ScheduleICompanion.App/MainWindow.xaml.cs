using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScheduleICompanion.Shared;
using Forms = System.Windows.Forms;

namespace ScheduleICompanion.App;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private readonly ObservableCollection<ProductTotalRow> _totals = new();
    private readonly ObservableCollection<QuestRow> _quests = new();
    private readonly ObservableCollection<MessagePreviewRow> _messagePreviews = new();
    private readonly ObservableCollection<OperationItemRow> _operationOrders = new();
    private readonly ObservableCollection<OperationItemRow> _production = new();
    private readonly ObservableCollection<OperationItemRow> _dealers = new();
    private readonly ObservableCollection<OperationItemRow> _deliveries = new();
    private readonly ObservableCollection<OperationItemRow> _employees = new();
    private readonly ObservableCollection<OperationItemRow> _laundering = new();
    private readonly ObservableCollection<string> _debugShops = new();
    private readonly ObservableCollection<ManagedModRow> _managedMods = new();
    private readonly ObservableCollection<MixRecommendationPayload> _mixRecommendations = new();
    private readonly PipeClient _pipe = new();
    private readonly string _baseDirectory = AppContext.BaseDirectory;
    private readonly string _configDirectory;
    private readonly ModManagerService _modManager;
    private CompanionSettings _settings = new();
    private PositionPayload? _position;
    private IReadOnlyList<PlayerMarkerPayload> _playerMarkers = Array.Empty<PlayerMarkerPayload>();
    private IReadOnlyList<NpcMarkerPayload> _npcMarkers = Array.Empty<NpcMarkerPayload>();
    private IReadOnlyList<MapPoiPayload> _mapPois = Array.Empty<MapPoiPayload>();
    private GameTimePayload? _gameTime;
    private readonly List<System.Windows.FrameworkElement> _dynamicPlayerElements = new();
    private readonly List<SewerEntrance> _sewerEntrances = new();
    private readonly List<SewerPortal> _sewerPortals = new();
    private PositionPayload? _previousPosition;
    private bool _isUnderground;
    private double _mapZoom = 1.0;
    private bool _mapFitMode = true;
    private bool _isMapPanning;
    private bool _runtimeReloadConfirmed;
    private bool _showOperationsPane;
    private readonly DispatcherTimer _runtimeRefreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private System.Windows.Point _mapPanStart;
    private double _mapPanHorizontalStart;
    private double _mapPanVerticalStart;

    public MainWindow()
    {
        InitializeComponent();

        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScheduleICompanion",
            "Config");
        _modManager = new ModManagerService(_baseDirectory);
        OrderGrid.ItemsSource = _totals;
        QuestList.ItemsSource = _quests;
        MessageList.ItemsSource = _messagePreviews;
        OperationOrderList.ItemsSource = _operationOrders;
        ProductionList.ItemsSource = _production;
        DealerList.ItemsSource = _dealers;
        DealerDebugCombo.ItemsSource = _dealers;
        ShopDebugCombo.ItemsSource = _debugShops;
        DeliveryList.ItemsSource = _deliveries;
        EmployeeList.ItemsSource = _employees;
        LaunderingList.ItemsSource = _laundering;
        ManagedModsList.ItemsSource = _managedMods;
        MixRecommendationsGrid.ItemsSource = _mixRecommendations;
        _runtimeRefreshTimer.Tick += (_, _) => RequestRuntimeRefresh();

        LoadSettings();
        LoadSewerData();
        LoadMap("overworld");

        Loaded += (_, _) =>
        {
            if (_settings.StartOnSecondMonitor)
                MoveToSecondMonitor();
            _pipe.Start();
            _ = RefreshModsAsync();
            _ = CheckForUpdatesAsync();
        };

        SizeChanged += (_, _) => RenderMarker();
        _pipe.ConnectionChanged += connected =>
            DispatchToUi(() => SetConnected(connected));
        _pipe.MessageReceived += message =>
            DispatchToUi(() => HandleMessage(message));
        _pipe.Diagnostic += text =>
            DispatchToUi(() => AddDiagnostic(text));
    }

    private void DispatchToUi(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    private void HandleMessage(BridgeMessage message)
    {
        try
        {
            if (!_runtimeReloadConfirmed && message.Type is "game_time" or "position" or "player_markers" or
                "operations_snapshot" or "debug_catalog")
            {
                _runtimeReloadConfirmed = true;
                _runtimeRefreshTimer.Stop();
                StatusText.Text = "Connected; live runtime active";
                SendDevToolState();
            }
            var json = JsonSerializer.Serialize(message.Payload);
            switch (message.Type)
            {
                case "notification":
                {
                    var payload = JsonSerializer.Deserialize<NotificationPayload>(json);
                    if (payload is null) break;
                    AddDiagnostic($"{payload.Category}: {payload.Text}");
                    break;
                }
                case "order":
                {
                    var payload = JsonSerializer.Deserialize<OrderPayload>(json);
                    if (payload is null) break;
                    UpdateOutstandingOrders(new[] { payload });
                    break;
                }
                case "order_snapshot":
                {
                    var payload = JsonSerializer.Deserialize<OrderSnapshotPayload>(json);
                    if (payload is null) break;
                    UpdateOutstandingOrders(payload.Orders);
                    break;
                }
                case "operations_snapshot":
                {
                    var payload = JsonSerializer.Deserialize<OperationsSnapshotPayload>(json);
                    if (payload is null) break;
                    UpdateOperations(payload);
                    break;
                }
                case "debug_catalog":
                {
                    var payload = JsonSerializer.Deserialize<DebugCatalogPayload>(json);
                    if (payload is null) break;
                    var selected = ShopDebugCombo.Text;
                    _debugShops.Clear();
                    foreach (var item in payload.Interfaces) _debugShops.Add(item);
                    if (!string.IsNullOrWhiteSpace(selected) && _debugShops.Contains(selected))
                        ShopDebugCombo.SelectedItem = selected;
                    else if (_debugShops.Count > 0)
                        ShopDebugCombo.SelectedIndex = 0;
                    break;
                }
                case "position":
                {
                    _position = JsonSerializer.Deserialize<PositionPayload>(json);
                    if (_position is null) break;

                    PositionText.Text =
                        $"Position: {_position.X:0.0}, {_position.Y:0.0}, {_position.Z:0.0}  " +
                        $"Heading: {_position.Heading:0}°  Area: {_position.Area}" +
                        (_position.HasNativeMapPosition ? "  Map: native" : "  Map: fallback");

                    UpdateAutomaticLayer(_position, _previousPosition);

                    _previousPosition = _position;
                    RenderMarker();
                    break;
                }
                case "player_markers":
                {
                    var payload = JsonSerializer.Deserialize<PlayerMarkersSnapshotPayload>(json);
                    if (payload is null) break;
                    _playerMarkers = payload.Players;
                    PlayerCountText.Text = $"Players: {_playerMarkers.Count}";
                    RenderMarker();
                    break;
                }
                case "npc_markers":
                {
                    var payload = JsonSerializer.Deserialize<NpcMarkersSnapshotPayload>(json);
                    if (payload is null) break;
                    _npcMarkers = payload.Npcs;
                    NpcCountText.Text = $"NPCs: {_npcMarkers.Count}";
                    RenderMarker();
                    break;
                }
                case "game_time":
                {
                    _gameTime = JsonSerializer.Deserialize<GameTimePayload>(json);
                    UpdateClockDisplay();
                    break;
                }
                case "map_pois":
                {
                    var payload = JsonSerializer.Deserialize<MapPoiSnapshotPayload>(json);
                    if (payload is null) break;
                    _mapPois = payload.Pois;
                    RenderMarker();
                    break;
                }
                case "quests":
                {
                    var payload = JsonSerializer.Deserialize<QuestSnapshotPayload>(json);
                    if (payload is null) break;
                    _quests.Clear();
                    var hasTracked = payload.Quests.Any(x => x.IsTracked);
                    foreach (var quest in payload.Quests.Where(x => !hasTracked || x.IsTracked).Take(8))
                        _quests.Add(new QuestRow(quest.Title, quest.Description, quest.IsTracked,
                            string.Join(Environment.NewLine, quest.Entries)));
                    break;
                }
                case "messages":
                {
                    var payload = JsonSerializer.Deserialize<MessageSnapshotPayload>(json);
                    if (payload is null) break;
                    _messagePreviews.Clear();
                    foreach (var item in payload.Messages.Where(x => x.Unread).Take(10))
                        _messagePreviews.Add(new MessagePreviewRow(
                            item.Contact,
                            System.Text.RegularExpressions.Regex.Replace(item.Text, "<[^>]+>", ""),
                            item.Sender,
                            item.Unread));
                    break;
                }
                case "diagnostic":
                {
                    var payload = JsonSerializer.Deserialize<DiagnosticPayload>(json);
                    if (payload is not null)
                    {
                        AddDiagnostic($"{payload.Name}: {payload.Value}");
                        if (payload.Name.Equals("Runtime reload", StringComparison.OrdinalIgnoreCase))
                        {
                            _runtimeReloadConfirmed = true;
                            _runtimeRefreshTimer.Stop();
                            StatusText.Text = $"Runtime reload: {payload.Value}";
                            SendDevToolState();
                        }
                    }
                    break;
                }
                default:
                    AddDiagnostic($"{message.Type}: {json}");
                    break;
            }
        }
        catch (Exception ex)
        {
            App.WriteSessionLog($"Message '{message.Type}' failed: {ex}");
            AddDiagnostic($"Message handler error: {ex}");
        }
    }

    private void UpdateOutstandingOrders(IReadOnlyList<OrderPayload> orders)
    {
        var aggregate = orders
            .SelectMany(order => order.Lines)
            .Where(line => line.Quantity > 0 && !string.IsNullOrWhiteSpace(line.Product))
            .GroupBy(x => x.Product, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProductTotalRow(g.First().Product, g.Sum(x => x.Quantity), 0))
            .OrderByDescending(x => x.Needed)
            .ThenBy(x => x.Product)
            .ToArray();

        _totals.Clear();
        foreach (var item in aggregate)
            _totals.Add(item);
    }

    private void UpdateOperations(OperationsSnapshotPayload snapshot)
    {
        var stock = snapshot.Stock
            .GroupBy(item => item.Product, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity), StringComparer.OrdinalIgnoreCase);
        var totals = snapshot.Orders
            .SelectMany(order => order.Lines)
            .Where(line => line.Quantity > 0 && !string.IsNullOrWhiteSpace(line.Product))
            .GroupBy(line => line.Product, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var product = group.First().Product;
                return new ProductTotalRow(product, group.Sum(line => line.Quantity), stock.GetValueOrDefault(product));
            })
            .OrderByDescending(row => row.Short)
            .ThenBy(row => row.Product)
            .ToArray();

        _totals.Clear();
        foreach (var row in totals) _totals.Add(row);

        ReplaceRows(_operationOrders, snapshot.Orders.Select(order => new OperationItemRow(
            order.Customer,
            $"{string.Join(", ", order.Lines.Select(line => $"{line.Quantity}x {line.Product}"))} · {order.Location} · {order.Window} · £{order.Payment:0}",
            order.Lines.All(line => stock.GetValueOrDefault(line.Product) >= line.Quantity) ? "Ready" : "Short")));
        ReplaceRows(_production, snapshot.Production.Select(ToRow));
        ReplaceRows(_dealers, snapshot.Dealers.Select(ToRow));
        if (DealerDebugCombo.SelectedIndex < 0 && _dealers.Count > 0)
            DealerDebugCombo.SelectedIndex = 0;
        ReplaceRows(_deliveries, snapshot.Deliveries.Select(ToRow));
        ReplaceRows(_employees, snapshot.Employees.Select(ToRow));
        ReplaceRows(_laundering, snapshot.Laundering.Select(ToRow));

        CashBalanceText.Text = $"£{snapshot.Cash:N0}";
        OnlineBalanceText.Text = $"£{snapshot.OnlineBalance:N0}";
        NetWorthText.Text = $"£{snapshot.NetWorth:N0}";
        RiskText.Text = snapshot.Risk;
        _mixRecommendations.Clear();
        foreach (var recommendation in snapshot.MixRecommendations ?? Array.Empty<MixRecommendationPayload>())
            _mixRecommendations.Add(recommendation);
    }

    private static OperationItemRow ToRow(OperationItemPayload item) => new(item.Title, item.Detail, item.State);

    private static void ReplaceRows(ObservableCollection<OperationItemRow> target, IEnumerable<OperationItemRow> rows)
    {
        target.Clear();
        foreach (var row in rows) target.Add(row);
    }

    private void UpdateClockDisplay()
    {
        if (_gameTime is null) return;
        var hours = Math.Clamp(_gameTime.Time24 / 100, 0, 23);
        var minutes = Math.Clamp(_gameTime.Time24 % 100, 0, 59);
        ClockTimeText.Text = _settings.Use24HourClock
            ? $"{hours:00}:{minutes:00}"
            : $"{(hours % 12 == 0 ? 12 : hours % 12)}:{minutes:00} {(hours >= 12 ? "PM" : "AM")}";
        ClockDayText.Text = $"{_gameTime.Day.ToUpperInvariant()}  ·  DAY {_gameTime.ElapsedDays + 1}";
    }

    private void LoadSewerData()
    {
        _sewerEntrances.Clear();
        _sewerPortals.Clear();
        try
        {
            var entranceFile = Path.Combine(_baseDirectory, "Maps", "sewer-entrances.json");
            if (File.Exists(entranceFile))
                _sewerEntrances.AddRange(JsonSerializer.Deserialize<List<SewerEntrance>>(File.ReadAllText(entranceFile)) ?? new());

            var portalFile = Path.Combine(_baseDirectory, "Maps", "sewer-portals.json");
            if (File.Exists(portalFile))
                _sewerPortals.AddRange(JsonSerializer.Deserialize<List<SewerPortal>>(File.ReadAllText(portalFile)) ?? new());

            var overlayFile = Path.Combine(_baseDirectory, "Maps", "sewer-overlay.png");
            SewerOverlayImage.Source = LoadBitmap(overlayFile);
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Sewer map data: {ex.Message}");
        }
    }

    private static BitmapImage? LoadBitmap(string file)
    {
        if (!File.Exists(file)) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new System.Uri(file, System.UriKind.Absolute);
        bitmap.EndInit();
        return bitmap;
    }

    private void LoadMap(string map)
    {
        // Surface and underground use the same native phone-map projection.
        var file = Path.Combine(_baseDirectory, "Maps", "overworld.png");
        var bitmap = LoadBitmap(file);
        if (bitmap is null)
        {
            MapImage.Source = null;
            StatusText.Text = "Missing Maps\\overworld.png";
            return;
        }

        MapImage.Source = bitmap;
        MapSurface.Width = bitmap.PixelWidth;
        MapSurface.Height = bitmap.PixelHeight;
        MapImage.Width = bitmap.PixelWidth;
        MapImage.Height = bitmap.PixelHeight;
        UndergroundDimLayer.Width = bitmap.PixelWidth;
        UndergroundDimLayer.Height = bitmap.PixelHeight;
        SewerOverlayImage.Width = bitmap.PixelWidth;
        SewerOverlayImage.Height = bitmap.PixelHeight;
        MapOverlay.Width = bitmap.PixelWidth;
        MapOverlay.Height = bitmap.PixelHeight;
        _mapFitMode = true;
        Dispatcher.BeginInvoke(new Action(FitMapToViewport), System.Windows.Threading.DispatcherPriority.Loaded);
        ApplyUndergroundLayers();
        RenderMarker();
    }

    private void UpdateAutomaticLayer(PositionPayload current, PositionPayload? previous)
    {
        var explicitSewer = current.Area.Contains("sewer", StringComparison.OrdinalIgnoreCase) ||
                            current.Area.Contains("tunnel", StringComparison.OrdinalIgnoreCase);

        var nearest = FindNearestPortal(current.X, current.Z, out var nearSurface, out var nearUnderground);
        if (nearest is not null)
        {
            // Each portal has a midpoint between its measured surface and tunnel-side elevations.
            // This handles entrances whose surface opening is already below the global threshold.
            var localThreshold = nearest.LocalSwitchY;
            var verticalDirection = previous is null ? 0f : current.Y - previous.Y;

            if (!_isUnderground)
            {
                if (explicitSewer ||
                    (nearUnderground && current.Y <= localThreshold + 0.30f) ||
                    ((nearSurface || IsNearPortalCorridor(current, nearest)) && current.Y <= localThreshold && verticalDirection <= 0.15f))
                {
                    SetUndergroundMode(true, $"portal {nearest.Name}");
                    return;
                }
            }
            else
            {
                if (!explicitSewer && (
                    (nearSurface && current.Y >= localThreshold - 0.30f) ||
                    (IsNearPortalCorridor(current, nearest) && current.Y >= localThreshold && verticalDirection >= -0.15f)))
                {
                    SetUndergroundMode(false, $"portal {nearest.Name}");
                    return;
                }
            }
        }

        // Global hysteresis remains as a fallback away from surveyed portals.
        if (!_isUnderground && (explicitSewer || current.Y <= _settings.SewerEnterY))
            SetUndergroundMode(true, "global height/area");
        else if (_isUnderground && !explicitSewer && current.Y >= _settings.SewerExitY)
            SetUndergroundMode(false, "global height");
    }

    private SewerPortal? FindNearestPortal(float x, float z, out bool nearSurface, out bool nearUnderground)
    {
        nearSurface = false;
        nearUnderground = false;
        SewerPortal? nearest = null;
        var nearestDistance = float.MaxValue;
        var radius = Math.Max(2f, _settings.SewerPortalRadius);

        foreach (var portal in _sewerPortals)
        {
            var surfaceDistance = HorizontalDistance(x, z, portal.SurfaceX, portal.SurfaceZ);
            var undergroundDistance = HorizontalDistance(x, z, portal.UndergroundX, portal.UndergroundZ);
            var distance = Math.Min(surfaceDistance, undergroundDistance);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = portal;
                nearSurface = surfaceDistance <= radius;
                nearUnderground = undergroundDistance <= radius;
            }
        }

        if (nearestDistance > radius * 2.5f)
        {
            nearSurface = nearUnderground = false;
            return null;
        }
        return nearest;
    }

    private bool IsNearPortalCorridor(PositionPayload position, SewerPortal portal)
    {
        // Distance to the line segment joining the measured surface opening and tunnel-side landing.
        var ax = portal.SurfaceX;
        var az = portal.SurfaceZ;
        var bx = portal.UndergroundX;
        var bz = portal.UndergroundZ;
        var abx = bx - ax;
        var abz = bz - az;
        var lengthSquared = abx * abx + abz * abz;
        var t = lengthSquared <= 0.0001f ? 0f :
            Math.Clamp(((position.X - ax) * abx + (position.Z - az) * abz) / lengthSquared, 0f, 1f);
        var closestX = ax + abx * t;
        var closestZ = az + abz * t;
        return HorizontalDistance(position.X, position.Z, closestX, closestZ) <= Math.Max(3f, _settings.SewerPortalRadius);
    }

    private static float HorizontalDistance(float x1, float z1, float x2, float z2)
    {
        var dx = x1 - x2;
        var dz = z1 - z2;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private void SetUndergroundMode(bool underground, string reason)
    {
        if (_isUnderground == underground) return;
        _isUnderground = underground;
        ApplyUndergroundLayers();
        StatusText.Text = underground ? $"Underground view ({reason})" : $"Surface view ({reason})";
        RenderMarker();
    }

    private void ApplyUndergroundLayers()
    {
        var showTunnels = _settings.ShowSewerOverlay;
        UndergroundDimLayer.Visibility = _isUnderground ? Visibility.Visible : Visibility.Collapsed;
        SewerOverlayImage.Visibility = _isUnderground && showTunnels && SewerOverlayImage.Source is not null
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderMarker()
    {
        if (_position is null || MapImage.Source is not BitmapSource source ||
            source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            PlayerMarker.Visibility = Visibility.Collapsed;
            return;
        }

        var bounds = _settings.Overworld;
        var rangeX = bounds.MaxX - bounds.MinX;
        var rangeZ = bounds.MaxZ - bounds.MinZ;
        if (Math.Abs(rangeX) < 0.001 || Math.Abs(rangeZ) < 0.001)
            return;

        var native = TryConvertNativeMapPosition(
            _position.HasNativeMapPosition, _position.MapX, _position.MapY,
            _position.MapWidth, _position.MapHeight, source, out var x, out var y);
        var nx = native ? x / source.PixelWidth : (_position.X - bounds.MinX) / rangeX;
        var nz = native ? y / source.PixelHeight : (bounds.MaxZ - _position.Z) / rangeZ;
        if (!native)
        {
            x = nx * source.PixelWidth;
            y = nz * source.PixelHeight;
        }

        Canvas.SetLeft(PlayerMarker, x);
        Canvas.SetTop(PlayerMarker, y);
        PlayerMarker.RenderTransformOrigin = new System.Windows.Point(0, 0);
        var inverseZoom = 1d / Math.Max(0.1, _mapZoom);
        var playerTransform = new TransformGroup();
        playerTransform.Children.Add(new ScaleTransform(inverseZoom, inverseZoom));
        if (_settings.ShowFacingDirection)
            playerTransform.Children.Add(new RotateTransform(_position.Heading, 0, 0));
        PlayerMarker.RenderTransform = playerTransform;

        var playerIsVisible = nx is >= 0 and <= 1 && nz is >= 0 and <= 1;
        PlayerMarker.Visibility = playerIsVisible ? Visibility.Visible : Visibility.Collapsed;

        RenderOtherPlayers(source, bounds);
        RenderNpcMarkers(source, bounds);
        RenderSewerEntrances(source);
        RenderMapPois(source);
    }

    private void RenderMapPois(BitmapSource source)
    {
        foreach (var poi in _mapPois)
        {
            if (!ShouldShowPoi(poi.Kind) ||
                !TryConvertNativeMapPosition(true, poi.MapX, poi.MapY, poi.MapWidth, poi.MapHeight, source, out var x, out var y))
                continue;

            if (poi.Kind == "Objective")
            {
                var star = CreateMapBadge("★", Color.FromRgb(255, 190, 32), $"Objective: {poi.Name}", 25);
                Canvas.SetLeft(star, x - star.Width / 2);
                Canvas.SetTop(star, y - star.Height / 2);
                MapOverlay.Children.Add(star);
                _dynamicPlayerElements.Add(star);
                continue;
            }

            if (poi.Kind == "Potential customer")
            {
                var portrait = CreatePortraitMarker(poi.Id, poi.Name, 29);
                Panel.SetZIndex(portrait, 150);
                Canvas.SetLeft(portrait, x - portrait.Width / 2);
                Canvas.SetTop(portrait, y - portrait.Height / 2);
                MapOverlay.Children.Add(portrait);
                _dynamicPlayerElements.Add(portrait);
                continue;
            }

            var (glyph, color) = poi.Kind switch
            {
                "Property owned" => ("⌂", Color.FromRgb(35, 190, 92)),
                "Property unowned" => ("⌂", Color.FromRgb(218, 62, 62)),
                "Business owned" => ("£", Color.FromRgb(22, 165, 185)),
                "Business unowned" => ("£", Color.FromRgb(225, 118, 32)),
                "Contract" => ("!", Color.FromRgb(224, 155, 20)),
                "Vehicle" => ("V", Color.FromRgb(20, 155, 205)),
                "Dead drop" => ("↓", Color.FromRgb(194, 55, 143)),
                "Dealer" => ("$", Color.FromRgb(137, 70, 205)),
                _ => ("•", Color.FromRgb(100, 110, 105))
            };
            var marker = CreateMapBadge(glyph, color, $"{poi.Kind}: {poi.Name}");
            Panel.SetZIndex(marker, 100);
            Canvas.SetLeft(marker, x - marker.Width / 2);
            Canvas.SetTop(marker, y - marker.Height / 2);
            MapOverlay.Children.Add(marker);
            _dynamicPlayerElements.Add(marker);
        }
    }

    private bool ShouldShowPoi(string kind) => kind switch
    {
        "Property" => _settings.ShowPropertyPois,
        "Property owned" => _settings.ShowPropertyPois,
        "Property unowned" => _settings.ShowPropertyPois,
        "Business owned" => _settings.ShowBusinessPois,
        "Business unowned" => _settings.ShowBusinessPois,
        "Contract" => _settings.ShowContractPois,
        "Vehicle" => _settings.ShowOwnedVehiclePois,
        "Dead drop" => _settings.ShowDeadDropPois,
        "Dealer" => _settings.ShowDealerPois,
        "Objective" => _settings.ShowObjectivePois,
        "Potential customer" => _settings.ShowPotentialCustomerPois,
        _ => false
    };

    private FrameworkElement CreatePortraitMarker(string markerId, string displayName, double size)
    {
        try
        {
            const string prefix = "potential-customer-";
            if (!markerId.StartsWith(prefix, StringComparison.Ordinal))
                throw new FileNotFoundException();
            var portraitKey = markerId[prefix.Length..];
            var iconPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScheduleICompanion", "Portraits", $"{portraitKey}.png");
            if (!File.Exists(iconPath)) throw new FileNotFoundException();
            var bitmap = new BitmapImage();
            using (var stream = File.OpenRead(iconPath))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }

            var grid = new Grid
            {
                Width = size,
                Height = size,
                ToolTip = $"Potential customer: {displayName}",
                IsHitTestVisible = true,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1d / Math.Max(0.1, _mapZoom), 1d / Math.Max(0.1, _mapZoom)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 6,
                    ShadowDepth = 1,
                    Opacity = 1
                }
            };
            var portrait = new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
                Margin = new Thickness(2),
                Clip = new EllipseGeometry(new Rect(2, 2, size - 4, size - 4))
            };
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new SolidColorBrush(Color.FromRgb(228, 153, 45)) });
            grid.Children.Add(portrait);
            grid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Stroke = Brushes.White,
                StrokeThickness = 2
            });
            ToolTipService.SetInitialShowDelay(grid, 100);
            ToolTipService.SetShowDuration(grid, 10000);
            return grid;
        }
        catch
        {
            var fallback = CreateMapBadge("?", Color.FromRgb(228, 153, 45), $"Potential customer: {displayName}", size);
            fallback.IsHitTestVisible = true;
            ToolTipService.SetInitialShowDelay(fallback, 100);
            return fallback;
        }
    }

    private Border CreateMapBadge(string glyph, Color color, string tooltip, double size = 23)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(color),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            ToolTip = tooltip,
            IsHitTestVisible = false,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1d / Math.Max(0.1, _mapZoom), 1d / Math.Max(0.1, _mapZoom)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 5,
                ShadowDepth = 1,
                Opacity = 0.95
            },
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = Brushes.White,
                FontSize = size * 0.58,
                FontWeight = FontWeights.ExtraBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private System.Windows.Shapes.Polygon CreatePlayerPointer(Color color, float heading, string playerName)
    {
        var inverseZoom = 1d / Math.Max(0.1, _mapZoom);
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(inverseZoom, inverseZoom));
        if (_settings.ShowFacingDirection)
            transform.Children.Add(new RotateTransform(heading, 0, 0));

        var marker = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection
            {
                new(0, -10),
                new(7, 7),
                new(0, 4),
                new(-7, 7)
            },
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            ToolTip = playerName,
            IsHitTestVisible = true,
            RenderTransformOrigin = new System.Windows.Point(0, 0),
            RenderTransform = transform,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 7,
                ShadowDepth = 1,
                Opacity = 1
            }
        };
        ToolTipService.SetInitialShowDelay(marker, 100);
        ToolTipService.SetShowDuration(marker, 10000);
        return marker;
    }

    private void RenderOtherPlayers(BitmapSource source, MapBounds bounds)
    {
        foreach (var element in _dynamicPlayerElements)
            MapOverlay.Children.Remove(element);
        _dynamicPlayerElements.Clear();

        if (!_settings.ShowOtherPlayers)
            return;

        var rangeX = bounds.MaxX - bounds.MinX;
        var rangeZ = bounds.MaxZ - bounds.MinZ;
        foreach (var player in _playerMarkers.Where(x =>
                     !x.IsLocal && IsSameLayer(x.Y)))
        {
            var native = TryConvertNativeMapPosition(
                player.HasNativeMapPosition, player.MapX, player.MapY,
                player.MapWidth, player.MapHeight, source, out var x, out var y);
            var nx = native ? x / source.PixelWidth : (player.X - bounds.MinX) / rangeX;
            var nz = native ? y / source.PixelHeight : (bounds.MaxZ - player.Z) / rangeZ;
            if (!native)
            {
                x = nx * source.PixelWidth;
                y = nz * source.PixelHeight;
            }
            if (nx is < 0 or > 1 || nz is < 0 or > 1)
                continue;
            var marker = CreatePlayerPointer(Color.FromRgb(226, 69, 170), player.Heading, player.DisplayName);
            Panel.SetZIndex(marker, 900);
            Canvas.SetLeft(marker, x);
            Canvas.SetTop(marker, y);
            MapOverlay.Children.Add(marker);
            _dynamicPlayerElements.Add(marker);

            if (_settings.ShowPlayerNames)
            {
                var label = new TextBlock
                {
                    Text = player.DisplayName,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)),
                    Padding = new Thickness(5, 2, 5, 2),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new System.Windows.Point(0, 0),
                    RenderTransform = new ScaleTransform(1d / Math.Max(0.1, _mapZoom), 1d / Math.Max(0.1, _mapZoom))
                };
                Panel.SetZIndex(label, 901);
                Canvas.SetLeft(label, x + 15 / Math.Max(0.1, _mapZoom));
                Canvas.SetTop(label, y - 10 / Math.Max(0.1, _mapZoom));
                MapOverlay.Children.Add(label);
                _dynamicPlayerElements.Add(label);
            }
        }
    }

    private void RenderNpcMarkers(BitmapSource source, MapBounds bounds)
    {
        var rangeX = bounds.MaxX - bounds.MinX;
        var rangeZ = bounds.MaxZ - bounds.MinZ;
        if (Math.Abs(rangeX) < 0.001 || Math.Abs(rangeZ) < 0.001) return;

        foreach (var npc in _npcMarkers)
        {
            var potentialCustomer = npc.Kind == "Potential customer";
            if (potentialCustomer ? !_settings.ShowPotentialCustomerPois : !_settings.ShowNpcMarkers)
                continue;
            if (!IsSameLayer(npc.Y)) continue;
            var native = TryConvertNativeMapPosition(
                npc.HasNativeMapPosition, npc.MapX, npc.MapY,
                npc.MapWidth, npc.MapHeight, source, out var x, out var y);
            var nx = native ? x / source.PixelWidth : (npc.X - bounds.MinX) / rangeX;
            var nz = native ? y / source.PixelHeight : (bounds.MaxZ - npc.Z) / rangeZ;
            if (!native)
            {
                x = nx * source.PixelWidth;
                y = nz * source.PixelHeight;
            }
            if (nx is < 0 or > 1 || nz is < 0 or > 1) continue;

            if (potentialCustomer)
            {
                var portrait = CreatePortraitMarker(npc.Id, npc.DisplayName, 29);
                Panel.SetZIndex(portrait, 150);
                Canvas.SetLeft(portrait, x - portrait.Width / 2);
                Canvas.SetTop(portrait, y - portrait.Height / 2);
                MapOverlay.Children.Add(portrait);
                _dynamicPlayerElements.Add(portrait);
                continue;
            }
            var fill = npc.Kind switch
            {
                "Dealer" => Brushes.MediumPurple,
                "Customer" => Brushes.DeepSkyBlue,
                "Supplier" => Brushes.Gold,
                "Employee" => Brushes.Orange,
                _ => Brushes.OrangeRed
            };
            var glyph = npc.Kind switch
            {
                "Dealer" => "$",
                "Customer" => "C",
                "Supplier" => "S",
                "Employee" => "E",
                _ => "N"
            };
            var marker = CreateMapBadge(glyph, ((SolidColorBrush)fill).Color, $"{npc.Kind}: {npc.DisplayName}", 20);
            Canvas.SetLeft(marker, x - marker.Width / 2);
            Canvas.SetTop(marker, y - marker.Height / 2);
            MapOverlay.Children.Add(marker);
            _dynamicPlayerElements.Add(marker);
            if (_settings.ShowNpcNames)
            {
                var label = new TextBlock
                {
                    Text = npc.DisplayName,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)),
                    Padding = new Thickness(2),
                    FontSize = 10
                };
                Canvas.SetLeft(label, x + 7);
                Canvas.SetTop(label, y - 7);
                MapOverlay.Children.Add(label);
                _dynamicPlayerElements.Add(label);
            }
        }
    }

    private bool IsSameLayer(float y) => _isUnderground ? y <= _settings.SewerExitY : y > _settings.SewerEnterY;

    private void RenderSewerEntrances(BitmapSource source)
    {
        if (!_settings.ShowSewerEntrances)
            return;

        foreach (var entrance in _sewerEntrances)
        {
            if (!TryConvertNativeMapPosition(true, entrance.MapX, entrance.MapY,
                    entrance.MapWidth, entrance.MapHeight, source, out var x, out var y))
                continue;

            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = _isUnderground ? Brushes.Cyan : Brushes.Transparent,
                Stroke = Brushes.Cyan,
                StrokeThickness = 3,
                ToolTip = entrance.Name
            };
            Canvas.SetLeft(ring, x - 8);
            Canvas.SetTop(ring, y - 8);
            MapOverlay.Children.Add(ring);
            _dynamicPlayerElements.Add(ring);
        }

        foreach (var portal in _sewerPortals)
        {
            if (!TryConvertNativeMapPosition(true, portal.MapX, portal.MapY,
                    portal.MapWidth, portal.MapHeight, source, out var x, out var y))
                continue;

            var marker = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection(new[]
                {
                    new System.Windows.Point(0, -9),
                    new System.Windows.Point(9, 0),
                    new System.Windows.Point(0, 9),
                    new System.Windows.Point(-9, 0)
                }),
                Fill = _isUnderground ? Brushes.Cyan : Brushes.Black,
                Stroke = Brushes.Cyan,
                StrokeThickness = 2.5,
                ToolTip = $"{portal.Name}\nSurface: {portal.SurfaceX:0.0}, {portal.SurfaceY:0.0}, {portal.SurfaceZ:0.0}\nTunnel: {portal.UndergroundX:0.0}, {portal.UndergroundY:0.0}, {portal.UndergroundZ:0.0}"
            };
            Canvas.SetLeft(marker, x);
            Canvas.SetTop(marker, y);
            MapOverlay.Children.Add(marker);
            _dynamicPlayerElements.Add(marker);
        }
    }

    private static bool TryConvertNativeMapPosition(
        bool hasNative, float mapX, float mapY, float mapWidth, float mapHeight,
        BitmapSource source, out double x, out double y)
    {
        x = y = 0;
        if (!hasNative || mapWidth <= 0 || mapHeight <= 0)
            return false;

        // Unity RectTransform coordinates use the map centre as origin and positive Y upwards.
        // WPF image coordinates use the upper-left as origin and positive Y downwards.
        var normalizedX = 0.5 + mapX / mapWidth;
        var normalizedY = 0.5 - mapY / mapHeight;
        x = normalizedX * source.PixelWidth;
        y = normalizedY * source.PixelHeight;
        return normalizedX is >= -0.05 and <= 1.05 && normalizedY is >= -0.05 and <= 1.05;
    }

    private void SetMapZoom(double zoom, bool keepCentre = true)
    {
        if (MapImage.Source is not BitmapSource)
            return;

        zoom = Math.Clamp(zoom, 0.10, 5.0);
        var oldZoom = _mapZoom;
        var centreX = MapScrollViewer.HorizontalOffset + MapScrollViewer.ViewportWidth / 2;
        var centreY = MapScrollViewer.VerticalOffset + MapScrollViewer.ViewportHeight / 2;

        _mapZoom = zoom;
        MapScaleTransform.ScaleX = zoom;
        MapScaleTransform.ScaleY = zoom;
        MapZoomText.Text = $"{zoom * 100:0}%";
        _mapFitMode = false;
        RenderMarker();

        if (keepCentre && oldZoom > 0)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var ratio = zoom / oldZoom;
                MapScrollViewer.ScrollToHorizontalOffset(centreX * ratio - MapScrollViewer.ViewportWidth / 2);
                MapScrollViewer.ScrollToVerticalOffset(centreY * ratio - MapScrollViewer.ViewportHeight / 2);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void FitMapToViewport()
    {
        if (MapImage.Source is not BitmapSource source ||
            MapScrollViewer.ViewportWidth <= 0 || MapScrollViewer.ViewportHeight <= 0)
            return;

        var horizontal = Math.Max(1, MapScrollViewer.ViewportWidth - 4) / source.PixelWidth;
        var vertical = Math.Max(1, MapScrollViewer.ViewportHeight - 4) / source.PixelHeight;
        _mapZoom = Math.Clamp(Math.Min(horizontal, vertical), 0.10, 5.0);
        MapScaleTransform.ScaleX = _mapZoom;
        MapScaleTransform.ScaleY = _mapZoom;
        MapZoomText.Text = "Fit";
        _mapFitMode = true;
        MapScrollViewer.ScrollToHorizontalOffset(0);
        MapScrollViewer.ScrollToVerticalOffset(0);
        RenderMarker();
    }

    private void MapZoomIn_Click(object sender, RoutedEventArgs e) => SetMapZoom(_mapZoom * 1.20);
    private void MapZoomOut_Click(object sender, RoutedEventArgs e) => SetMapZoom(_mapZoom / 1.20);
    private void MapFit_Click(object sender, RoutedEventArgs e) => FitMapToViewport();
    private void MapActualSize_Click(object sender, RoutedEventArgs e) => SetMapZoom(1.0, false);

    private void MapScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_mapFitMode)
            Dispatcher.BeginInvoke(new Action(FitMapToViewport), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void MapScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        SetMapZoom(e.Delta > 0 ? _mapZoom * 1.12 : _mapZoom / 1.12);
        e.Handled = true;
    }

    private void MapScrollViewer_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isMapPanning = true;
        _mapPanStart = e.GetPosition(MapScrollViewer);
        _mapPanHorizontalStart = MapScrollViewer.HorizontalOffset;
        _mapPanVerticalStart = MapScrollViewer.VerticalOffset;
        MapScrollViewer.CaptureMouse();
        MapScrollViewer.Cursor = System.Windows.Input.Cursors.Hand;
        e.Handled = true;
    }

    private void MapScrollViewer_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isMapPanning || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(MapScrollViewer);
        MapScrollViewer.ScrollToHorizontalOffset(_mapPanHorizontalStart - (current.X - _mapPanStart.X));
        MapScrollViewer.ScrollToVerticalOffset(_mapPanVerticalStart - (current.Y - _mapPanStart.Y));
    }

    private void MapScrollViewer_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isMapPanning = false;
        MapScrollViewer.ReleaseMouseCapture();
        MapScrollViewer.Cursor = System.Windows.Input.Cursors.Arrow;
        e.Handled = true;
    }

    private void LoadSettings()
    {
        Directory.CreateDirectory(_configDirectory);
        var path = Path.Combine(_configDirectory, "settings.json");

        try
        {
            if (File.Exists(path))
                _settings = JsonSerializer.Deserialize<CompanionSettings>(File.ReadAllText(path)) ?? new();
        }
        catch
        {
            _settings = new();
        }

        Topmost = _settings.AlwaysOnTop;
        AlwaysOnTopCheck.IsChecked = _settings.AlwaysOnTop;
        StartOnSecondMonitorCheck.IsChecked = _settings.StartOnSecondMonitor;
        ShowOtherPlayersCheck.IsChecked = _settings.ShowOtherPlayers;
        ShowPlayerNamesCheck.IsChecked = _settings.ShowPlayerNames;
        ShowFacingDirectionCheck.IsChecked = _settings.ShowFacingDirection;
        ShowNpcMarkersCheck.IsChecked = _settings.ShowNpcMarkers;
        ShowNpcNamesCheck.IsChecked = _settings.ShowNpcNames;
        ShowSewerOverlayCheck.IsChecked = _settings.ShowSewerOverlay;
        ShowSewerEntrancesCheck.IsChecked = _settings.ShowSewerEntrances;
        ShowClockCheck.IsChecked = _settings.ShowClock;
        Use24HourClockCheck.IsChecked = _settings.Use24HourClock;
        ShowPropertyPoisCheck.IsChecked = _settings.ShowPropertyPois;
        ShowBusinessPoisCheck.IsChecked = _settings.ShowBusinessPois;
        ShowContractPoisCheck.IsChecked = _settings.ShowContractPois;
        ShowOwnedVehiclePoisCheck.IsChecked = _settings.ShowOwnedVehiclePois;
        ShowDeadDropPoisCheck.IsChecked = _settings.ShowDeadDropPois;
        ShowDealerPoisCheck.IsChecked = _settings.ShowDealerPois;
        ShowObjectivePoisCheck.IsChecked = _settings.ShowObjectivePois;
        ShowPotentialCustomerPoisCheck.IsChecked = _settings.ShowPotentialCustomerPois;
        ShowQuestPanelCheck.IsChecked = _settings.ShowQuestPanel;
        ShowMessagePanelCheck.IsChecked = _settings.ShowMessagePanel;
        ShowOrdersPanelCheck.IsChecked = _settings.ShowOrdersPanel;
        ShowPositionStatusCheck.IsChecked = _settings.ShowPositionStatus;
        ShowDiagnosticsTabCheck.IsChecked = _settings.ShowDiagnosticsTab;
        DevToolsEnabledCheck.IsChecked = _settings.DevToolsEnabled;
        FreezeTimeCheck.IsChecked = _settings.FreezeGameTime;
        AutoClearTrashCheck.IsChecked = _settings.AutoClearTrash;
        TrashIntervalText.Text = Math.Clamp(_settings.TrashClearIntervalSeconds, 5, 60).ToString();
        ShowFpsCheck.IsChecked = _settings.ShowFps;
        ModCatalogUrlText.Text = _settings.ModCatalogUrl;
        ApplyDashboardVisibility();
        UpdateClockDisplay();
    }

    private void SettingsGeneralOptions_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        _settings.StartOnSecondMonitor = StartOnSecondMonitorCheck.IsChecked == true;
        _settings.ShowClock = ShowClockCheck.IsChecked == true;
        _settings.Use24HourClock = Use24HourClockCheck.IsChecked == true;
        _settings.ShowQuestPanel = ShowQuestPanelCheck.IsChecked == true;
        _settings.ShowMessagePanel = ShowMessagePanelCheck.IsChecked == true;
        _settings.ShowOrdersPanel = ShowOrdersPanelCheck.IsChecked == true;
        Topmost = _settings.AlwaysOnTop;
        PersistSettings();
        StatusText.Text = "Settings updated";
        ApplyDashboardVisibility();
        UpdateClockDisplay();

        if (ReferenceEquals(sender, StartOnSecondMonitorCheck) && _settings.StartOnSecondMonitor)
            MoveToSecondMonitor();
    }

    private void DashboardOptions_Click(object sender, RoutedEventArgs e) =>
        DashboardOptionsPopup.IsOpen = true;

    private void DashboardInfoView_Click(object sender, RoutedEventArgs e) => SetDashboardInfoPane(false);

    private void OperationsInfoView_Click(object sender, RoutedEventArgs e) => SetDashboardInfoPane(true);

    private void SetDashboardInfoPane(bool showOperations)
    {
        _showOperationsPane = showOperations;
        ApplyDashboardVisibility();
        DashboardInfoViewButton.Background = (Brush)FindResource(showOperations ? "RaisedBrush" : "AccentDarkBrush");
        DashboardInfoViewButton.BorderBrush = (Brush)FindResource(showOperations ? "BorderBrush" : "AccentBrush");
        OperationsInfoViewButton.Background = (Brush)FindResource(showOperations ? "AccentDarkBrush" : "RaisedBrush");
        OperationsInfoViewButton.BorderBrush = (Brush)FindResource(showOperations ? "AccentBrush" : "BorderBrush");
    }

    private void Legend_Click(object sender, RoutedEventArgs e) =>
        LegendPopup.IsOpen = true;

    private void DashboardMapOptions_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.ShowPropertyPois = ShowPropertyPoisCheck.IsChecked == true;
        _settings.ShowBusinessPois = ShowBusinessPoisCheck.IsChecked == true;
        _settings.ShowContractPois = ShowContractPoisCheck.IsChecked == true;
        _settings.ShowOwnedVehiclePois = ShowOwnedVehiclePoisCheck.IsChecked == true;
        _settings.ShowDeadDropPois = ShowDeadDropPoisCheck.IsChecked == true;
        _settings.ShowDealerPois = ShowDealerPoisCheck.IsChecked == true;
        _settings.ShowObjectivePois = ShowObjectivePoisCheck.IsChecked == true;
        _settings.ShowPotentialCustomerPois = ShowPotentialCustomerPoisCheck.IsChecked == true;
        _settings.ShowOtherPlayers = ShowOtherPlayersCheck.IsChecked == true;
        _settings.ShowPlayerNames = ShowPlayerNamesCheck.IsChecked == true;
        _settings.ShowFacingDirection = ShowFacingDirectionCheck.IsChecked == true;
        _settings.ShowNpcMarkers = ShowNpcMarkersCheck.IsChecked == true;
        _settings.ShowNpcNames = ShowNpcNamesCheck.IsChecked == true;
        PersistSettings();
        StatusText.Text = "Dashboard map options updated";
        ApplyUndergroundLayers();
        RenderMarker();
    }

    private void SettingsDisplayOptions_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.ShowPositionStatus = ShowPositionStatusCheck.IsChecked == true;
        _settings.ShowDiagnosticsTab = ShowDiagnosticsTabCheck.IsChecked == true;
        _settings.ShowSewerOverlay = ShowSewerOverlayCheck.IsChecked == true;
        _settings.ShowSewerEntrances = ShowSewerEntrancesCheck.IsChecked == true;
        PersistSettings();
        ApplyDashboardVisibility();
        ApplyUndergroundLayers();
        RenderMarker();
    }

    private void PersistSettings()
    {
        Directory.CreateDirectory(_configDirectory);
        File.WriteAllText(
            Path.Combine(_configDirectory, "settings.json"),
            JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ApplyDashboardVisibility()
    {
        OperationsPane.Visibility = _showOperationsPane ? Visibility.Visible : Visibility.Collapsed;
        ClockPanel.Visibility = !_showOperationsPane && _settings.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        QuestPanel.Visibility = !_showOperationsPane && _settings.ShowQuestPanel ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = !_showOperationsPane && _settings.ShowMessagePanel ? Visibility.Visible : Visibility.Collapsed;
        OrdersPanel.Visibility = !_showOperationsPane && _settings.ShowOrdersPanel ? Visibility.Visible : Visibility.Collapsed;
        DevToolsPanel.Visibility = _settings.DevToolsEnabled ? Visibility.Visible : Visibility.Collapsed;
        PositionText.Visibility = _settings.ShowPositionStatus ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsTab.Visibility = _settings.ShowDiagnosticsTab ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.ShowDiagnosticsTab && DiagnosticsTab.IsSelected)
            Tabs.SelectedIndex = 0;
    }

    private void SendDevToolState()
    {
        _pipe.Send("devtool", new DevToolCommandPayload("freeze_time",
            _settings.DevToolsEnabled && _settings.FreezeGameTime));
        _pipe.Send("devtool", new DevToolCommandPayload("auto_clear_trash",
            _settings.DevToolsEnabled && _settings.AutoClearTrash, _settings.TrashClearIntervalSeconds));
        _pipe.Send("devtool", new DevToolCommandPayload("show_fps",
            _settings.DevToolsEnabled && _settings.ShowFps));
    }

    private void MoveToSecondMonitor()
    {
        var screens = Forms.Screen.AllScreens;
        if (screens.Length < 2)
            return;

        var screen = screens.FirstOrDefault(x => !x.Primary) ?? screens[1];
        WindowState = WindowState.Normal;
        Left = screen.WorkingArea.Left + 20;
        Top = screen.WorkingArea.Top + 20;
        Width = Math.Max(900, screen.WorkingArea.Width - 40);
        Height = Math.Max(600, screen.WorkingArea.Height - 40);
    }

    private void SetConnected(bool connected)
    {
        ConnectionDot.Fill = connected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.DarkRed;
        ConnectionText.Text = connected ? "Connected to game" : "Waiting for game";
        if (!connected)
        {
            _runtimeReloadConfirmed = false;
            _runtimeRefreshTimer.Stop();
        }
        else if (!_runtimeReloadConfirmed)
        {
            RequestRuntimeRefresh();
            _runtimeRefreshTimer.Start();
        }
    }

    private void RequestRuntimeRefresh()
    {
        if (_runtimeReloadConfirmed || !_pipe.Send("runtime_refresh")) return;
        StatusText.Text = "Connected; refreshing in-game runtime...";
    }

    private void RefreshLive_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        LoadSewerData();
        LoadMap("overworld");

        if (_pipe.Send("runtime_refresh"))
        {
            StatusText.Text = "Refreshing in-game runtime...";
            AddDiagnostic("Live refresh requested. Local maps and settings reloaded.");
        }
        else
        {
            StatusText.Text = "Local assets refreshed; waiting for the game connection.";
            AddDiagnostic("Local maps and settings reloaded. Runtime refresh was not sent because the game is disconnected.");
        }
    }

    private void AddDiagnostic(string text)
    {
        DiagnosticsText.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        DiagnosticsText.ScrollToEnd();
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e) => System.Windows.Clipboard.SetText(DiagnosticsText.Text);

    private void DevToolsEnabled_Click(object sender, RoutedEventArgs e)
    {
        _settings.DevToolsEnabled = DevToolsEnabledCheck.IsChecked == true;
        ApplyDashboardVisibility();
        if (!_settings.DevToolsEnabled)
        {
            FreezeTimeCheck.IsChecked = false;
            AutoClearTrashCheck.IsChecked = false;
            ShowFpsCheck.IsChecked = false;
            _settings.FreezeGameTime = false;
            _settings.AutoClearTrash = false;
            _settings.ShowFps = false;
        }
        PersistSettings();
        SendDevToolState();
    }

    private void FreezeTime_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.DevToolsEnabled) return;
        _settings.FreezeGameTime = FreezeTimeCheck.IsChecked == true;
        PersistSettings();
        _pipe.Send("devtool", new DevToolCommandPayload("freeze_time", _settings.FreezeGameTime));
    }

    private void ShowFps_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.DevToolsEnabled) return;
        _settings.ShowFps = ShowFpsCheck.IsChecked == true;
        PersistSettings();
        _pipe.Send("devtool", new DevToolCommandPayload("show_fps", _settings.ShowFps));
    }

    private void ClearTrash_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.DevToolsEnabled)
            _pipe.Send("devtool", new DevToolCommandPayload("clear_trash"));
    }

    private void ClearWeather_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.DevToolsEnabled)
            _pipe.Send("devtool", new DevToolCommandPayload("clear_weather"));
    }

    private void SetTime_Click(object sender, RoutedEventArgs e) =>
        SendDebugAction(DebugAction("set_time", SetTimeText.Text), "Set-time command sent");

    private void SetWeather_Click(object sender, RoutedEventArgs e) =>
        SendDebugAction(DebugAction("set_weather", WeatherCombo.Text), "Weather command sent");

    private void OpenDealer_Click(object sender, RoutedEventArgs e)
    {
        var dealer = (DealerDebugCombo.SelectedItem as OperationItemRow)?.Title ?? "";
        SendDebugAction(DebugAction("open_dealer", dealer), "Starting dealer dialogue in game");
    }

    private void OpenShop_Click(object sender, RoutedEventArgs e) =>
        SendDebugAction(DebugAction("open_interface", ShopDebugCombo.Text), "Opening selected interface in game");

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var quantity = int.TryParse(ItemQuantityText.Text, out var parsed) ? Math.Clamp(parsed, 1, 999) : 1;
        ItemQuantityText.Text = quantity.ToString();
        SendDebugAction(new DevToolCommandPayload($"add_item|{ItemIdText.Text}", IntervalSeconds: quantity),
            "Add-item command sent");
    }

    private void ClearInventory_Click(object sender, RoutedEventArgs e) =>
        SendDebugAction(new DevToolCommandPayload("clear_inventory"), "Clear-inventory command sent");

    private void ChangeCash_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDebugNumber(CashChangeText.Text, out var amount)) return;
        SendDebugAction(DebugAction("change_cash", FormatDebugNumber(amount)), "Cash command sent");
    }

    private void ChangeOnlineBalance_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDebugNumber(OnlineChangeText.Text, out var amount)) return;
        SendDebugAction(DebugAction("change_online_balance", FormatDebugNumber(amount)), "Online-balance command sent");
    }

    private void ToggleFreeCam_Click(object sender, RoutedEventArgs e) =>
        SendDebugAction(new DevToolCommandPayload("toggle_freecam"), "Free-camera command sent");

    private void SetMoveSpeed_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDebugNumber(MoveSpeedText.Text, out var speed)) return;
        SendDebugAction(DebugAction("set_move_speed", FormatDebugNumber(speed)), "Movement-speed command sent");
    }

    private void SpawnVehicle_Click(object sender, RoutedEventArgs e) =>
        SendDebugAction(DebugAction("spawn_vehicle", VehicleIdText.Text), "Spawn-vehicle command sent");

    private void RunConsoleCommand_Click(object sender, RoutedEventArgs e) =>
        SendDebugAction(DebugAction("console_command", RawConsoleCommandText.Text), "Console command sent");

    private static DevToolCommandPayload DebugAction(string action, string value) => new($"{action}|{value}");

    private static string FormatDebugNumber(float value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private void SendDebugAction(DevToolCommandPayload payload, string status)
    {
        if (!_settings.DevToolsEnabled)
        {
            StatusText.Text = "Enable DevTools before using debug actions.";
            return;
        }
        StatusText.Text = _pipe.Send("devtool", payload) ? status : "The game bridge is not connected.";
    }

    private bool TryReadDebugNumber(string text, out float value)
    {
        if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return true;
        StatusText.Text = "Enter a valid number using a decimal point.";
        return false;
    }

    private void AutoClearTrash_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.DevToolsEnabled) return;
        var interval = int.TryParse(TrashIntervalText.Text, out var parsed) ? Math.Clamp(parsed, 5, 60) : 30;
        TrashIntervalText.Text = interval.ToString();
        _settings.AutoClearTrash = AutoClearTrashCheck.IsChecked == true;
        _settings.TrashClearIntervalSeconds = interval;
        PersistSettings();
        _pipe.Send("devtool", new DevToolCommandPayload("auto_clear_trash", _settings.AutoClearTrash, interval));
    }

    private void TrashInterval_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var interval = int.TryParse(TrashIntervalText.Text, out var parsed) ? Math.Clamp(parsed, 5, 60) : 30;
        TrashIntervalText.Text = interval.ToString();
        _settings.TrashClearIntervalSeconds = interval;
        PersistSettings();

        if (_settings.DevToolsEnabled)
            _pipe.Send("devtool", new DevToolCommandPayload("auto_clear_trash", _settings.AutoClearTrash, interval));
    }

#if false // Removed v1.3 developer inspector/candidate UI.
    private void RefreshHierarchy_Click(object sender, RoutedEventArgs e)
    {
        if (!_pipe.Send("hierarchy_refresh")) StatusText.Text = "The game bridge is not connected.";
        else StatusText.Text = "Requesting the full live hierarchy...";
    }

    private void PickCrosshair_Click(object sender, RoutedEventArgs e)
    {
        if (!_pipe.Send("pick_crosshair")) StatusText.Text = "The game bridge is not connected.";
        else StatusText.Text = "Picking the object currently under the in-game crosshair...";
    }

    private void InspectSelected_Click(object sender, RoutedEventArgs e)
    {
        if (HierarchyGrid.SelectedItem is not HierarchyRow row) { StatusText.Text = "Select an object first."; return; }
        if (!_pipe.Send("inspect_object", new ObjectIdPayload(row.InstanceId))) StatusText.Text = "The game bridge is not connected.";
    }

    private void TestMovement_Click(object sender, RoutedEventArgs e)
    {
        if (HierarchyGrid.SelectedItem is not HierarchyRow row)
        {
            StatusText.Text = "Select an object first.";
            return;
        }

        MovementTestStatus.Text = $"Movement test: preparing to monitor {row.Path}";
        if (!_pipe.Send("test_object_movement", new ObjectMovementTestRequestPayload(row.InstanceId, 10f)))
        {
            MovementTestStatus.Text = "Movement test: game bridge is not connected.";
            StatusText.Text = "The game bridge is not connected.";
        }
    }

    private void UseAsPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (HierarchyGrid.SelectedItem is not HierarchyRow row) { StatusText.Text = "Select an object first."; return; }
        if (!_pipe.Send("use_as_player", new ObjectIdPayload(row.InstanceId))) StatusText.Text = "The game bridge is not connected.";
    }

    private void HierarchyGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (HierarchyGrid.SelectedItem is not HierarchyRow row) return;
        InspectorSelection.Text = row.Path;
        _pipe.Send("inspect_object", new ObjectIdPayload(row.InstanceId));
    }

    private void HierarchyFilter_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyHierarchyFilter();

    private void ApplyHierarchyFilter()
    {
        var filter = HierarchyFilter.Text.Trim();
        _filteredHierarchyRows.Clear();
        foreach (var row in _hierarchyRows.Where(x => string.IsNullOrEmpty(filter) ||
                     x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     x.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     x.Components.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     x.Layer.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     x.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            _filteredHierarchyRows.Add(row);
    }

    private void ExportHierarchy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_explorerDirectory);
            var file = Path.Combine(_explorerDirectory, $"hierarchy-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var export = new HierarchyExport(DateTimeOffset.Now, _hierarchyScene, _hierarchyRows.ToArray());
            File.WriteAllText(file, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = $"Full hierarchy exported to {file}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe", Arguments = $"/select,\"{file}\"", UseShellExecute = true
            });
        }
        catch (Exception ex) { AddDiagnostic($"Hierarchy export failed: {ex.Message}"); }
    }

    private void RefreshExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (!_pipe.Send("explorer_refresh"))
            StatusText.Text = "The game bridge is not connected.";
    }

    private void ApplyExplorerFilter()
    {
        var filter = ExplorerFilter.Text.Trim();
        var pinnedOnly = PinnedOnlyCheck.IsChecked == true;
        _filteredExplorerRows.Clear();
        foreach (var row in _explorerRows.Where(x =>
                     (!pinnedOnly || x.Pinned) &&
                     (string.IsNullOrEmpty(filter) ||
                      x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                      x.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                      x.Components.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                      x.Layer.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                      x.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase))))
        {
            _filteredExplorerRows.Add(row);
        }
    }

    private void ExplorerFilter_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyExplorerFilter();
    private void PinnedOnly_Changed(object sender, RoutedEventArgs e) => ApplyExplorerFilter();

    private void PinSelected_Click(object sender, RoutedEventArgs e) => SetSelectedPins(true);
    private void UnpinSelected_Click(object sender, RoutedEventArgs e) => SetSelectedPins(false);

    private void SetSelectedPins(bool pinned)
    {
        foreach (var row in ExplorerGrid.SelectedItems.OfType<ExplorerRow>())
        {
            row.Pinned = pinned;
            if (pinned) _pinnedExplorerPaths.Add(row.Path);
            else _pinnedExplorerPaths.Remove(row.Path);
        }
        SavePinnedExplorerPaths();
        ExplorerGrid.Items.Refresh();
        ApplyExplorerFilter();
    }

    private void ExplorerGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ExplorerGrid.SelectedItem is not ExplorerRow row)
        {
            ExplorerDetails.Text = string.Empty;
            return;
        }

        ExplorerDetails.Text =
            $"Name: {row.Name}{Environment.NewLine}" +
            $"Path: {row.Path}{Environment.NewLine}" +
            $"Active: {row.Active}{Environment.NewLine}" +
            $"Pinned: {row.Pinned}{Environment.NewLine}" +
            $"Player candidate: {row.Candidate}{Environment.NewLine}" +
            $"Movement score: {row.MovementScore}{Environment.NewLine}" +
            $"Movement distance: {row.MovementDistance:0.00} m{Environment.NewLine}" +
            $"Net displacement: {row.Displacement:0.00} m{Environment.NewLine}" +
            $"Rotation change: {row.RotationChange:0.0}°{Environment.NewLine}" +
            $"Position: {row.Position}{Environment.NewLine}" +
            $"Children: {row.ChildCount}{Environment.NewLine}" +
            $"Layer: {row.Layer}{Environment.NewLine}" +
            $"Tag: {row.Tag}{Environment.NewLine}{Environment.NewLine}" +
            $"Components:{Environment.NewLine}{row.Components.Replace(", ", Environment.NewLine)}";
    }

    private void ExportExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_explorerDirectory);
            var file = Path.Combine(_explorerDirectory, $"explorer-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var export = new ExplorerExport(DateTimeOffset.Now, ExplorerSummary.Text, _explorerRows.ToArray());
            File.WriteAllText(file, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = $"Explorer exported to {file}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{file}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Explorer export failed: {ex.Message}");
        }
    }

    private void LoadPinnedExplorerPaths()
    {
        try
        {
            Directory.CreateDirectory(_explorerDirectory);
            var file = Path.Combine(_explorerDirectory, "pinned.json");
            if (!File.Exists(file)) return;
            foreach (var value in JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? Array.Empty<string>())
                _pinnedExplorerPaths.Add(value);
        }
        catch { }
    }

    private void SavePinnedExplorerPaths()
    {
        try
        {
            Directory.CreateDirectory(_explorerDirectory);
            File.WriteAllText(
                Path.Combine(_explorerDirectory, "pinned.json"),
                JsonSerializer.Serialize(_pinnedExplorerPaths.OrderBy(x => x).ToArray(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
#endif

    private async Task RefreshModsAsync()
    {
        try
        {
            StatusText.Text = "Refreshing mod catalogue…";
            var rows = await _modManager.LoadAsync(_settings.ModCatalogUrl, CancellationToken.None);
            _managedMods.Clear();
            foreach (var row in rows) _managedMods.Add(row);
            StatusText.Text = rows.Count == 0 ? "No mod catalogue was found" : $"{rows.Count} verified mod(s) available";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Mod catalogue failed: " + ex.Message;
            AddDiagnostic(StatusText.Text);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateService.CheckAsync(CancellationToken.None);
            if (update is null) return;
            var answer = MessageBox.Show(this,
                $"Schedule I Companion {update.Version} is available.\n\n{update.Notes}\n\n" +
                "Close Schedule I, then choose Yes to download, install, and restart Companion.",
                "Companion update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;
            StatusText.Text = $"Downloading Companion {update.Version}…";
            var installer = await UpdateService.DownloadAndPrepareAsync(update, CancellationToken.None);
            UpdateService.StartInstaller(installer);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AddDiagnostic("Update check failed: " + ex.Message);
        }
    }

    private async void RefreshMods_Click(object sender, RoutedEventArgs e) => await RefreshModsAsync();

    private async void ApplyModCatalogUrl_Click(object sender, RoutedEventArgs e)
    {
        var value = ModCatalogUrlText.Text.Trim();
        if (value.Length > 0 && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusText.Text = "Catalogue URL must use HTTPS";
            return;
        }
        _settings.ModCatalogUrl = value;
        PersistSettings();
        await RefreshModsAsync();
    }

    private async void ToggleManagedMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string id } toggle) return;
        var row = _managedMods.FirstOrDefault(candidate => candidate.Id == id);
        if (row is null) return;
        try
        {
            var enable = toggle.IsChecked == true;
            StatusText.Text = $"{(enable ? "Enabling" : "Disabling")} {row.Name}…";
            await _modManager.SetEnabledAsync(row.Definition, enable, CancellationToken.None);
            await RefreshModsAsync();
            StatusText.Text = $"{row.Name} {(enable ? "enabled" : "disabled")}. Backpack data was preserved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            AddDiagnostic($"Mod change failed: {ex}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        App.WriteSessionLog("Main window closed");
        _pipe.Stop();
        base.OnClosed(e);
    }
}
