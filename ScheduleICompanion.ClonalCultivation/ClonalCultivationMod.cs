using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppFishySteamworks;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;

namespace ScheduleICompanion.ClonalCultivation;

public sealed class ClonalCultivationMod : MelonMod
{
    private static ClonalCultivationMod? Active;
    private CloneStore? _store;
    private readonly Dictionary<string, WeedDefinition> _productsBySeed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProductItemInstance> _qualityBySeed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, (WeedDefinition Product, ProductItemInstance Template)> _cloneByPot = new();
    private readonly Dictionary<long, Pot> _trackedPots = new();
    private readonly Dictionary<int, Pot> _potsByNetworkId = new();
    private readonly Dictionary<long, long> _clonePlantPointers = new();
    private readonly HashSet<long> _configuredClonePots = new();
    private readonly HashSet<string> _registeredSeeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _cataloguedProducts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _processedPlantRequests = new();
    private readonly HashSet<Guid> _completedPlantRequests = new();
    private readonly HashSet<Guid> _pendingPlantRequests = new();
    private readonly Dictionary<Guid, PendingPlantResult> _pendingPlantResults = new();
    private readonly List<PendingHostPlant> _pendingHostPlants = new();
    private PendingHarvest? _pendingHarvest;
    private MelonPreferences_Entry<string>? _plantKeyEntry;
    private MelonPreferences_Entry<bool>? _instantGrowEntry;
    private KeyCode _plantKey = KeyCode.P;
    private bool _plantKeyHeld;
    private string _notice = "";
    private float _noticeUntil;
    private bool _registryStateLogged;
    private float _nextRegistryRefresh;
    private float _nextTraitUpdate;
    private float _nextPotDiscovery;
    private float _nextCloneIdentitySync;
    private const float CloneGrowthTimeMultiplier = 1.3f;
    private string _status = "Hold a created weed bud, look at an empty pot, and press P";

    public override void OnInitializeMelon()
    {
        Active = this;
        var root = Path.Combine(AppContext.BaseDirectory, "UserData", "ScheduleICompanion", "ClonalCultivation");
        _store = new CloneStore(root);
        var preferences = MelonPreferences.CreateCategory("ScheduleICompanion_Cultivation", "Cultivation");
        _plantKeyEntry = preferences.CreateEntry("PlantKey", "P", "Plant held created bud key");
        if (_plantKeyEntry.Value.Equals("V", StringComparison.OrdinalIgnoreCase))
            _plantKeyEntry.Value = "P";
        _instantGrowEntry = preferences.CreateEntry("InstantGrowTesting", false, "Allow Insert to finish the targeted plant");
        ParsePlantKey();
        CultivationProtocol.Received += OnProtocolMessage;
        HarmonyInstance.PatchAll(typeof(ClonalCultivationMod).Assembly);
        // Stagger the two broad discovery passes so they never land on the same frame.
        // Planting itself still performs an immediate targeted registration/lookup.
        _nextRegistryRefresh = Time.unscaledTime + 8f;
        _nextPotDiscovery = Time.unscaledTime + 23f;
        LoggerInstance.Msg($"Cultivation initialized. Hold a created weed bud, look at an empty pot, and press {_plantKey}.");
    }

    public override void OnDeinitializeMelon() => CultivationProtocol.Received -= OnProtocolMessage;

    public override void OnUpdate()
    {
        UpdateClonePlantTraits();
        CompletePendingHostPlants();
        RetryPendingPlantResults();
        BroadcastCloneIdentities();
        if (_plantKeyEntry is not null && !_plantKeyEntry.Value.Equals(_plantKey.ToString(), StringComparison.OrdinalIgnoreCase))
            ParsePlantKey();
        if (Time.unscaledTime >= _nextRegistryRefresh)
        {
            _nextRegistryRefresh = Time.unscaledTime + 120f;
            EnsureCloneSeeds();
        }
        var plantKeyDown = Input.GetKeyDown(_plantKey) || (Input.GetKey(_plantKey) && !_plantKeyHeld);
        _plantKeyHeld = Input.GetKey(_plantKey);
        if (plantKeyDown)
        {
            _status = "Plant key detected";
            LoggerInstance.Msg($"Plant key {_plantKey} detected.");
            TryPlantEquippedBud();
        }
        if (Input.GetKeyDown(KeyCode.Insert))
        {
            if (_instantGrowEntry?.Value == true) TryInstantGrow();
            else ShowNotice("Instant Grow is disabled - enable it in Companion Settings > DevTools");
        }
    }

    public static bool SetInstantGrowTesting(bool enabled)
    {
        if (Active?._instantGrowEntry is null) return false;
        Active._instantGrowEntry.Value = enabled;
        MelonPreferences.Save();
        Active._status = $"Instant Grow testing {(enabled ? "enabled" : "disabled")}";
        Active.ShowNotice(Active._status + (enabled ? " - aim at a plant and press Insert" : ""));
        Active.LoggerInstance.Msg(Active._status + ".");
        return true;
    }

    public override void OnGUI()
    {
        if (Time.unscaledTime > _noticeUntil || string.IsNullOrWhiteSpace(_notice)) return;
        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 16,
            wordWrap = true
        };
        GUI.Box(new Rect(20, 80, Math.Min(520, Screen.width - 40), 44), _notice, style);
    }

    private void ShowNotice(string message)
    {
        _notice = message;
        _noticeUntil = Time.unscaledTime + 4f;
    }

    private void ParsePlantKey()
    {
        if (_plantKeyEntry is not null && Enum.TryParse(_plantKeyEntry.Value, true, out KeyCode parsed))
            _plantKey = parsed;
        else
            _plantKey = KeyCode.P;
    }

    private void EnsureCloneSeeds()
    {
        var manager = UnityEngine.Object.FindObjectOfType<ProductManager>();
        var registry = UnityEngine.Object.FindObjectOfType<Registry>();
        var template = Registry.GetItem<SeedDefinition>("ogkushseed");
        if (manager?.createdProducts is null || registry is null || template is null)
        {
            if (!_registryStateLogged)
            {
                LoggerInstance.Warning($"Cultivation registry not ready: manager={manager is not null}, products={manager?.createdProducts is not null}, registry={registry is not null}, template={template is not null}.");
                _registryStateLogged = true;
            }
            return;
        }
        if (!_registryStateLogged)
        {
            LoggerInstance.Msg($"Cultivation registry ready with {manager.createdProducts.Count} created products.");
            _registryStateLogged = true;
        }

        foreach (var product in manager.createdProducts)
        {
            if (product is not WeedDefinition weed || string.IsNullOrWhiteSpace(weed.ID)) continue;
            if (_cataloguedProducts.Contains(weed.ID)) continue;
            var seedId = SeedId(weed.ID);
            _productsBySeed[seedId] = weed;
            // Planting uses a quality-specific deterministic seed ID. Every peer must
            // register all of those IDs before an RPC can refer to one of them.
            foreach (var quality in Enum.GetValues<EQuality>())
                EnsureCloneSeed(weed, quality);
            if (_registeredSeeds.Contains(seedId) || Registry.ItemExists(seedId))
            {
                _registeredSeeds.Add(seedId);
                _cataloguedProducts.Add(weed.ID);
                continue;
            }

            var clone = UnityEngine.Object.Instantiate(template);
            clone.ID = seedId;
            clone.Name = weed.Name + " Bud Clone";
            clone.Description = "A plantable clone grown from " + weed.Name + ".";
            registry.AddToRegistry(clone);
            _registeredSeeds.Add(seedId);
            _cataloguedProducts.Add(weed.ID);
            LoggerInstance.Msg($"Registered clone seed {seedId} for {weed.Name}.");
        }
    }

    private void TryPlantEquippedBud()
    {
        EnsureCloneSeeds();
        var player = Player.Local;
        var camera = PlayerCamera.Instance?.Camera ?? Camera.main;
        if (player is null)
        {
            _status = "Local player is not ready";
            LoggerInstance.Warning(_status);
            return;
        }
        if (camera is null)
        {
            _status = "Player camera is not ready";
            LoggerInstance.Warning(_status);
            return;
        }
        var equipped = player.GetEquippedItem();
        var rawDefinition = equipped?.Definition;
        var definition = rawDefinition?.TryCast<WeedDefinition>();
        if (equipped is null || definition is null)
        {
            _status = $"Equip a weed bud first (held: {equipped?.GetType().Name ?? "none"}, definition: {rawDefinition?.GetType().Name ?? "none"}/{rawDefinition?.ID ?? "none"})";
            LoggerInstance.Msg(_status);
            return;
        }
        var weed = equipped.TryCast<ProductItemInstance>();
        if (weed is null)
        {
            _status = $"The held weed product could not expose its quality ({equipped.GetType().Name})";
            LoggerInstance.Msg(_status);
            return;
        }
        var seedId = SeedId(definition.ID, (int)weed.Quality);
        if (!_productsBySeed.ContainsKey(seedId))
        {
            EnsureCloneSeed(definition, weed.Quality);
            if (!_productsBySeed.ContainsKey(seedId))
            {
                _status = "That weed variant could not be registered for planting";
                LoggerInstance.Msg(_status);
                return;
            }
        }
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        var hits = Physics.RaycastAll(ray, 6.5f).OrderBy(hit => hit.distance).ToArray();
        Pot? pot = null;
        RaycastHit? firstWorldHit = null;
        foreach (var candidateHit in hits)
        {
            var collider = candidateHit.collider;
            if (collider is null || collider.GetComponentInParent<Player>() is not null) continue;
            firstWorldHit ??= candidateHit;
            pot = ResolveTargetPot(candidateHit);
            if (pot is not null) break;
        }
        pot ??= FindPotNearCrosshair(ray, 7f, 1.15f);
        if (pot is null)
        {
            _status = $"No plantable pot found inside {firstWorldHit?.collider?.name ?? "the aimed object"}";
            LoggerInstance.Msg(_status);
            return;
        }
        if (!IsMultiplayerClient() && !pot.CanAcceptSeed(out var reason))
        {
            _status = string.IsNullOrWhiteSpace(reason) ? "That pot cannot accept a plant" : reason;
            return;
        }

        var slotIndex = player.EquippedItemSlotIndex;
        if (slotIndex < 0 || slotIndex >= player._inventory.Length || player._inventory[slotIndex].ItemInstance is null)
        {
            _status = "The equipped inventory slot is not ready";
            return;
        }

        if (IsMultiplayerClient())
        {
            var request = new CultivationMessage
            {
                Type = "plant",
                RequestId = Guid.NewGuid(),
                ProductId = definition.ID,
                ProductDataJson = JsonUtility.ToJson(definition.GetSaveData()),
                Quality = (int)weed.Quality,
                InventorySlot = slotIndex,
                PotObjectId = pot.NetworkObject?.ObjectId ?? -1,
                SoilId = pot.CurrentSoil?.ID ?? "",
                SoilAmount = pot._currentSoilAmount,
                RemainingSoilUses = pot._remainingSoilUses,
                PotX = pot.transform.position.x,
                PotY = pot.transform.position.y,
                PotZ = pot.transform.position.z
            };
            if (!CultivationProtocol.Send(request))
            {
                _status = "Unable to contact the host for planting";
                LoggerInstance.Warning(_status);
                return;
            }
            _pendingPlantRequests.Add(request.RequestId);
            _status = "Waiting for host planting confirmation";
            LoggerInstance.Msg($"Sent host planting request {request.RequestId} for {definition.Name} ({weed.Quality}).");
            return;
        }

        PlantOnHost(LocalSteamId(), pot, player, definition, weed, slotIndex, slotIndex, Guid.NewGuid());
    }

    private void PlantOnHost(ulong sender, Pot pot, Player player, WeedDefinition definition,
        ProductItemInstance weed, int hostSlotIndex, int clientSlotIndex, Guid requestId)
    {
        var seedId = SeedId(definition.ID, (int)weed.Quality);
        EnsureCloneSeed(definition, weed.Quality);
        if (!_productsBySeed.ContainsKey(seedId) || !Registry.ItemExists(seedId))
        {
            SendPlantResult(sender, requestId, false, "The host could not register that strain and quality");
            return;
        }
        if (!pot.CanAcceptSeed(out var reason))
        {
            SendPlantResult(sender, requestId, false,
                string.IsNullOrWhiteSpace(reason) ? "That pot cannot accept a plant" : reason);
            return;
        }

        if (sender == LocalSteamId())
        {
            var held = hostSlotIndex >= 0 && hostSlotIndex < player._inventory.Length
                ? player._inventory[hostSlotIndex].ItemInstance?.TryCast<ProductItemInstance>()
                : null;
            var heldDefinition = held?.Definition?.TryCast<WeedDefinition>();
            if (held is null || heldDefinition is null ||
                !heldDefinition.ID.Equals(definition.ID, StringComparison.OrdinalIgnoreCase) || held.Quality != weed.Quality)
            {
                SendPlantResult(sender, requestId, false, "The host could not verify the held bud");
                return;
            }
        }

        if (IsMultiplayerSession())
        {
            CultivationProtocol.Send(new CultivationMessage
            {
                Type = "clone-prepare",
                ProductId = definition.ID,
                ProductDataJson = JsonUtility.ToJson(definition.GetSaveData()),
                Quality = (int)weed.Quality,
                PotObjectId = pot.NetworkObject?.ObjectId ?? -1,
                SoilId = pot.CurrentSoil?.ID ?? "",
                SoilAmount = pot._currentSoilAmount,
                RemainingSoilUses = pot._remainingSoilUses,
                PotX = pot.transform.position.x,
                PotY = pot.transform.position.y,
                PotZ = pot.transform.position.z
            });
            if (_pendingHostPlants.Any(entry => entry.Pot.Pointer == pot.Pointer))
            {
                SendPlantResult(sender, requestId, false, "That pot already has a planting request in progress");
                return;
            }
            _pendingHostPlants.Add(new PendingHostPlant(sender, pot, player, definition, weed,
                hostSlotIndex, clientSlotIndex, requestId, Time.unscaledTime + 0.75f));
            return;
        }

        CompletePlantOnHost(sender, pot, player, definition, weed, hostSlotIndex, clientSlotIndex, requestId);
    }

    private void CompletePendingHostPlants()
    {
        if (!IsHost() || _pendingHostPlants.Count == 0) return;
        foreach (var pending in _pendingHostPlants.Where(entry => Time.unscaledTime >= entry.ExecuteAt).ToArray())
        {
            _pendingHostPlants.Remove(pending);
            var reason = "";
            if (pending.Pot is null || !pending.Pot.CanAcceptSeed(out reason))
            {
                SendPlantResult(pending.Sender, pending.RequestId, false,
                    string.IsNullOrWhiteSpace(reason) ? "That pot can no longer accept a plant" : reason);
                continue;
            }
            CompletePlantOnHost(pending.Sender, pending.Pot, pending.Player, pending.Definition,
                pending.Weed, pending.HostSlotIndex, pending.ClientSlotIndex, pending.RequestId);
        }
    }

    private void CompletePlantOnHost(ulong sender, Pot pot, Player player, WeedDefinition definition,
        ProductItemInstance weed, int hostSlotIndex, int clientSlotIndex, Guid requestId)
    {
        var seedId = SeedId(definition.ID, (int)weed.Quality);
        pot.PlantSeed_Server(seedId, 0f);
        if (_qualityBySeed.TryGetValue(seedId, out var plantedTemplate))
        {
            var potKey = pot.Pointer.ToInt64();
            _cloneByPot[potKey] = (definition, plantedTemplate);
            _trackedPots[potKey] = pot;
            if (pot.Plant is not null) _clonePlantPointers[potKey] = pot.Plant.Pointer.ToInt64();
            SendCloneIdentity(pot, definition, plantedTemplate);
        }
        var quantityBefore = player._inventory[hostSlotIndex].ItemInstance?.GetTotalAmount() ?? 0;
        if (sender == LocalSteamId())
            ConsumeOneFromNetworkedSlot(player, hostSlotIndex);
        var quantityAfter = player._inventory[hostSlotIndex].ItemInstance?.GetTotalAmount() ?? 0;
        _status = $"Planted {definition.Name} ({weed.Quality}); bud stack {quantityBefore} -> {quantityAfter}";
        LoggerInstance.Msg(_status);
        SendPlantResult(sender, requestId, true, _status, clientSlotIndex, definition.ID, (int)weed.Quality,
            sender != LocalSteamId());
    }

    private sealed record PendingHostPlant(ulong Sender, Pot Pot, Player Player, WeedDefinition Definition,
        ProductItemInstance Weed, int HostSlotIndex, int ClientSlotIndex, Guid RequestId, float ExecuteAt);

    private void OnProtocolMessage(ulong sender, bool senderIsHost, CultivationMessage message)
    {
        if (message.Protocol != "3") return;
        if (message.Type == "plant" && IsHost())
        {
            ProcessHostPlantRequest(sender, message);
            return;
        }
        if (message.Type == "ack" && IsHost())
        {
            if (_pendingPlantResults.TryGetValue(message.RequestId, out var pending) && pending.Recipient == sender)
                _pendingPlantResults.Remove(message.RequestId);
            return;
        }
        if (message.Type == "clone-sync" && senderIsHost)
        {
            ApplyCloneIdentity(message);
            return;
        }
        if (message.Type == "clone-prepare" && senderIsHost && !IsHost())
        {
            PrepareRemoteClone(message);
            return;
        }
        if (message.Type == "soil-sync" && senderIsHost && !IsHost())
        {
            ApplyRemoteSoilState(message);
            return;
        }
        if (message.Type == "result" && senderIsHost &&
            (message.Recipient == 0 || message.Recipient == LocalSteamId()))
        {
            if (!_pendingPlantRequests.Contains(message.RequestId) && !_completedPlantRequests.Contains(message.RequestId))
            {
                LoggerInstance.Warning($"Ignored unsolicited planting result {message.RequestId}.");
                return;
            }
            CultivationProtocol.Send(new CultivationMessage
            {
                Type = "ack",
                RequestId = message.RequestId,
                Recipient = sender
            });
            if (!_completedPlantRequests.Add(message.RequestId)) return;
            _pendingPlantRequests.Remove(message.RequestId);
            if (message.Success && message.ConsumeOne) ConsumeAuthorizedBud(message);
            _status = message.Success ? "Host planted the bud" : "Planting rejected: " + message.Error;
            LoggerInstance.Msg(_status);
        }
    }

    private void ProcessHostPlantRequest(ulong sender, CultivationMessage request)
    {
        if (!_processedPlantRequests.Add(request.RequestId)) return;
        var player = ResolvePlayer(sender);
        if (player is null)
        {
            SendPlantResult(sender, request.RequestId, false, "The host could not resolve your player");
            return;
        }
        ProductItemInstance? equipped = null;
        WeedDefinition? definition = null;
        var verifiedSlot = -1;
        var slotOrder = Enumerable.Range(0, player._inventory.Length)
            .OrderBy(index => index == request.InventorySlot ? 0 : 1);
        foreach (var index in slotOrder)
        {
            var candidate = player._inventory[index].ItemInstance?.TryCast<ProductItemInstance>();
            var candidateDefinition = candidate?.Definition?.TryCast<WeedDefinition>();
            if (candidate is null || candidateDefinition is null ||
                !candidateDefinition.ID.Equals(request.ProductId, StringComparison.OrdinalIgnoreCase) ||
                (int)candidate.Quality != request.Quality) continue;
            equipped = candidate;
            definition = candidateDefinition;
            verifiedSlot = index;
            break;
        }
        if (equipped is null || definition is null || verifiedSlot < 0)
        {
            var visibleProducts = player._inventory
                .Select((slot, index) => (index, item: slot.ItemInstance?.TryCast<ProductItemInstance>()))
                .Where(entry => entry.item is not null)
                .Select(entry => $"{entry.index}:{entry.item!.Definition?.ID ?? "unknown"}/{entry.item.Quality}");
            LoggerInstance.Warning($"Remote bud verification failed for {request.ProductId}/{request.Quality} " +
                                   $"at reported slot {request.InventorySlot}; host inventory: {string.Join(", ", visibleProducts)}");
            // Newly-created products do not immediately replace the old item definition in
            // the host's mirror of a co-op inventory. Verify the strain against the host's
            // product catalogue; the requesting client consumes its actual bud after ack.
            definition = ResolveOrImportRemoteProduct(request);
            if (definition is null || !Enum.IsDefined(typeof(EQuality), request.Quality))
            {
                SendPlantResult(sender, request.RequestId, false, "The host has not registered that strain yet");
                return;
            }
            equipped = definition.GetDefaultInstance(1).TryCast<ProductItemInstance>();
            if (equipped is null)
            {
                SendPlantResult(sender, request.RequestId, false, "The host could not prepare that strain");
                return;
            }
            equipped.Quality = (EQuality)request.Quality;
            verifiedSlot = request.InventorySlot;
            LoggerInstance.Msg($"Using host catalogue verification for newly replicated co-op strain {definition.ID}/{equipped.Quality}.");
        }

        var requestedPosition = new Vector3(request.PotX, request.PotY, request.PotZ);
        var pots = UnityEngine.Object.FindObjectsOfType<Pot>();
        var pot = request.PotObjectId >= 0
            ? pots.FirstOrDefault(candidate => candidate?.NetworkObject?.ObjectId == request.PotObjectId)
            : null;
        pot ??= pots.Where(candidate => candidate is not null && candidate.CanAcceptSeed(out _))
            .OrderBy(candidate => Vector3.SqrMagnitude(candidate.transform.position - requestedPosition))
            .FirstOrDefault();
        if (pot is null || Vector3.SqrMagnitude(pot.transform.position - requestedPosition) > 2.25f)
        {
            SendPlantResult(sender, request.RequestId, false, "The host could not match the targeted pot");
            return;
        }
        if (Vector3.SqrMagnitude(player.transform.position - pot.transform.position) > 36f)
        {
            SendPlantResult(sender, request.RequestId, false, "You are too far from the targeted pot");
            return;
        }
        PlantOnHost(sender, pot, player, definition, equipped, verifiedSlot, request.InventorySlot, request.RequestId);
    }

    private WeedDefinition? ResolveOrImportRemoteProduct(CultivationMessage request)
    {
        var definition = Registry.GetItem<WeedDefinition>(request.ProductId);
        var manager = UnityEngine.Object.FindObjectOfType<ProductManager>();
        definition ??= FindCreatedWeed(manager, request.ProductId);
        if (definition is not null || manager is null || string.IsNullOrWhiteSpace(request.ProductDataJson))
            return definition;
        try
        {
            var data = JsonUtility.FromJson<WeedProductData>(request.ProductDataJson);
            if (data is null || !data.ID.Equals(request.ProductId, StringComparison.OrdinalIgnoreCase)) return null;
            var properties = new Il2CppSystem.Collections.Generic.List<string>();
            if (data.Properties is not null)
                foreach (var property in data.Properties)
                    if (!string.IsNullOrWhiteSpace(property)) properties.Add(property);
            manager.CreateWeed_Server(data.Name, data.ID, data.DrugType, properties, data.AppearanceSettings);
            definition = Registry.GetItem<WeedDefinition>(request.ProductId) ?? FindCreatedWeed(manager, request.ProductId);
            if (definition is not null)
                LoggerInstance.Msg($"Imported co-op-created strain {definition.Name} into the host product catalogue.");
            return definition;
        }
        catch (Exception ex)
        {
            LoggerInstance.Warning($"Could not import co-op-created strain {request.ProductId}: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static WeedDefinition? FindCreatedWeed(ProductManager? manager, string productId)
    {
        if (manager?.createdProducts is null) return null;
        foreach (var candidate in manager.createdProducts)
            if (candidate is WeedDefinition weed && weed.ID.Equals(productId, StringComparison.OrdinalIgnoreCase))
                return weed;
        return null;
    }

    private void ConsumeAuthorizedBud(CultivationMessage message)
    {
        var player = Player.Local;
        if (player is null) return;
        var slotOrder = Enumerable.Range(0, player._inventory.Length)
            .OrderBy(index => index == message.InventorySlot ? 0 : 1);
        foreach (var index in slotOrder)
        {
            var item = player._inventory[index].ItemInstance?.TryCast<ProductItemInstance>();
            var definition = item?.Definition?.TryCast<WeedDefinition>();
            if (item is null || definition is null ||
                !definition.ID.Equals(message.ProductId, StringComparison.OrdinalIgnoreCase) ||
                (int)item.Quality != message.Quality) continue;
            ConsumeOneFromNetworkedSlot(player, index);
            LoggerInstance.Msg($"Consumed host-authorized bud from local slot {index}.");
            return;
        }
        LoggerInstance.Warning("Host planted the clone, but the authorized bud was no longer in the local inventory.");
    }

    private static void ConsumeOneFromNetworkedSlot(Player player, int index)
    {
        var slot = player._inventory[index];
        var item = slot.ItemInstance;
        if (item is null) return;

        // Mutate the slot through its native quantity RPC. Replacing the stack with
        // SetInventoryItem leaves the equipped/networked slot holding its original
        // ItemInstance, so planting appears to consume nothing until inventory is
        // rebuilt. The slot APIs update that instance and replicate the change.
        if (item.GetTotalAmount() <= 1)
            slot.ClearStoredInstance(true);
        else
            slot.ChangeQuantity(-1, true);
    }

    private void SendPlantResult(ulong recipient, Guid requestId, bool success, string detail,
        int inventorySlot = -1, string productId = "", int quality = 0, bool consumeOne = false)
    {
        LoggerInstance.Msg($"Host planting {requestId} for Steam {recipient}: {(success ? "accepted" : "rejected - " + detail)}");
        if (recipient == LocalSteamId()) return;
        var result = new CultivationMessage
        {
            Type = "result",
            RequestId = requestId,
            Recipient = recipient,
            Success = success,
            ConsumeOne = consumeOne,
            InventorySlot = inventorySlot,
            ProductId = productId,
            Quality = quality,
            Error = success ? "" : detail
        };
        CultivationProtocol.Send(result);
        _pendingPlantResults[requestId] = new PendingPlantResult(recipient, result, Time.unscaledTime + 1f, 0);
    }

    private void RetryPendingPlantResults()
    {
        if (!IsHost() || _pendingPlantResults.Count == 0) return;
        foreach (var entry in _pendingPlantResults.ToArray())
        {
            var pending = entry.Value;
            if (Time.unscaledTime < pending.NextRetry) continue;
            if (pending.Attempts >= 8)
            {
                LoggerInstance.Warning($"No acknowledgement for planting result {entry.Key}; stopped retrying.");
                _pendingPlantResults.Remove(entry.Key);
                continue;
            }
            CultivationProtocol.Send(pending.Message);
            _pendingPlantResults[entry.Key] = pending with
            {
                NextRetry = Time.unscaledTime + 1f,
                Attempts = pending.Attempts + 1
            };
        }
    }

    private sealed record PendingPlantResult(
        ulong Recipient, CultivationMessage Message, float NextRetry, int Attempts);

    private void PrepareRemoteClone(CultivationMessage message)
    {
        ApplyRemoteSoilState(message);
        if (string.IsNullOrWhiteSpace(message.ProductId) || !Enum.IsDefined(typeof(EQuality), message.Quality)) return;
        var product = ResolveOrImportRemoteProduct(message);
        if (product is null)
        {
            LoggerInstance.Warning($"Could not prepare remote clone seed for {message.ProductId}/{message.Quality}.");
            return;
        }
        EnsureCloneSeed(product, (EQuality)message.Quality);
        LoggerInstance.Msg($"Prepared remote clone seed {SeedId(product.ID, message.Quality)} before planting.");
    }

    private static void SendSoilState(Pot pot)
    {
        if (pot?.CurrentSoil is null || pot._currentSoilAmount <= 0f) return;
        CultivationProtocol.Send(new CultivationMessage
        {
            Type = "soil-sync",
            PotObjectId = pot.NetworkObject?.ObjectId ?? -1,
            SoilId = pot.CurrentSoil.ID,
            SoilAmount = pot._currentSoilAmount,
            RemainingSoilUses = pot._remainingSoilUses,
            PotX = pot.transform.position.x,
            PotY = pot.transform.position.y,
            PotZ = pot.transform.position.z
        });
    }

    private void ApplyRemoteSoilState(CultivationMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.SoilId) || message.SoilAmount <= 0f) return;
        var soil = Registry.GetItem<SoilDefinition>(message.SoilId);
        if (soil is null) return;
        var position = new Vector3(message.PotX, message.PotY, message.PotZ);
        Pot? pot = null;
        if (message.PotObjectId >= 0)
            _potsByNetworkId.TryGetValue(message.PotObjectId, out pot);
        if (pot is null)
        {
            var pots = UnityEngine.Object.FindObjectsOfType<Pot>();
            foreach (var candidate in pots)
                if (candidate?.NetworkObject is not null)
                    _potsByNetworkId[candidate.NetworkObject.ObjectId] = candidate;
            pot = message.PotObjectId >= 0
                ? pots.FirstOrDefault(candidate => candidate?.NetworkObject?.ObjectId == message.PotObjectId)
                : null;
            pot ??= pots.OrderBy(candidate => Vector3.SqrMagnitude(candidate.transform.position - position)).FirstOrDefault();
        }
        if (pot is null || Vector3.SqrMagnitude(pot.transform.position - position) > 2.25f) return;
        pot.SetSoil(soil);
        pot.SetSoilAmount(message.SoilAmount);
        pot.SetRemainingSoilUses(Math.Max(0, message.RemainingSoilUses));
        pot.RefreshSoilVisuals();
    }

    private static bool IsMultiplayerSession()
    {
        try { return Lobby.Instance is not null && Lobby.Instance.IsInLobby; }
        catch { return false; }
    }

    private void BroadcastCloneIdentities()
    {
        if (!IsHost() || Time.unscaledTime < _nextCloneIdentitySync) return;
        _nextCloneIdentitySync = Time.unscaledTime + 8f;
        foreach (var entry in _cloneByPot)
        {
            if (!_trackedPots.TryGetValue(entry.Key, out var pot) || pot?.Plant is null) continue;
            SendCloneIdentity(pot, entry.Value.Product, entry.Value.Template);
        }
    }

    private static void SendCloneIdentity(Pot pot, WeedDefinition product, ProductItemInstance template)
    {
        CultivationProtocol.Send(new CultivationMessage
        {
            Type = "clone-sync",
            ProductId = product.ID,
            Quality = (int)template.Quality,
            PotObjectId = pot.NetworkObject?.ObjectId ?? -1,
            PotX = pot.transform.position.x,
            PotY = pot.transform.position.y,
            PotZ = pot.transform.position.z
        });
    }

    private void ApplyCloneIdentity(CultivationMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.ProductId)) return;
        var seedId = SeedId(message.ProductId, message.Quality);
        _productsBySeed.TryGetValue(seedId, out var product);
        var manager = UnityEngine.Object.FindObjectOfType<ProductManager>();
        if (product is null && manager?.createdProducts is not null)
        {
            foreach (var candidate in manager.createdProducts)
            {
                if (candidate is not WeedDefinition weed ||
                    !weed.ID.Equals(message.ProductId, StringComparison.OrdinalIgnoreCase)) continue;
                product = weed;
                break;
            }
        }
        if (product is null)
        {
            LoggerInstance.Warning($"Clone sync is waiting for custom product {message.ProductId} to register.");
            return;
        }
        var quality = (EQuality)message.Quality;
        EnsureCloneSeed(product, quality);
        if (!_qualityBySeed.TryGetValue(seedId, out var template)) return;
        var position = new Vector3(message.PotX, message.PotY, message.PotZ);
        var pot = UnityEngine.Object.FindObjectsOfType<Pot>()
            .Where(candidate => candidate?.Plant is not null)
            .OrderBy(candidate => Vector3.SqrMagnitude(candidate.transform.position - position))
            .FirstOrDefault();
        if (pot is null || Vector3.SqrMagnitude(pot.transform.position - position) > 0.25f) return;
        var potKey = pot.Pointer.ToInt64();
        _productsBySeed[seedId] = product;
        _qualityBySeed[seedId] = template;
        _cloneByPot[potKey] = (product, template);
        _trackedPots[potKey] = pot;
        _clonePlantPointers[potKey] = pot.Plant!.Pointer.ToInt64();
    }

    private static Pot? ResolveTargetPot(RaycastHit hit)
    {
        var collider = hit.collider;
        if (collider is null) return null;
        var direct = collider.GetComponentInParent<Pot>();
        if (direct is not null) return direct;

        var root = collider.transform.root;
        if (root is null) return null;
        Pot? nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var candidate in root.GetComponentsInChildren<Pot>(true))
        {
            if (candidate is null) continue;
            var distance = Vector3.SqrMagnitude(candidate.transform.position - hit.point);
            if (distance >= nearestDistance) continue;
            nearest = candidate;
            nearestDistance = distance;
        }
        return nearest;
    }

    private static Pot? FindPotNearCrosshair(Ray ray, float maxDistance, float corridorRadius)
    {
        return UnityEngine.Object.FindObjectsOfType<Pot>()
            .Where(candidate => candidate is not null)
            .Select(candidate => new
            {
                Pot = candidate,
                AlongRay = Vector3.Dot(candidate.transform.position - ray.origin, ray.direction),
                Distance = Vector3.Cross(ray.direction, candidate.transform.position - ray.origin).magnitude
            })
            .Where(candidate => candidate.AlongRay > 0f && candidate.AlongRay <= maxDistance &&
                                candidate.Distance <= corridorRadius)
            .OrderBy(candidate => candidate.Distance + candidate.AlongRay * 0.015f)
            .Select(candidate => candidate.Pot)
            .FirstOrDefault();
    }

    private void TryInstantGrow()
    {
        var camera = PlayerCamera.Instance?.Camera ?? Camera.main;
        if (camera is null) return;
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        var hits = Physics.RaycastAll(ray, 6f).OrderBy(hit => hit.distance).ToArray();
        var pot = hits.Select(ResolveGrowingPot).FirstOrDefault(candidate => candidate?.Plant is not null);
        if (pot is null)
        {
            pot = UnityEngine.Object.FindObjectsOfType<Pot>()
                .Where(candidate => candidate?.Plant is not null)
                .Select(candidate => new
                {
                    Pot = candidate,
                    AlongRay = Vector3.Dot(candidate.transform.position - ray.origin, ray.direction),
                    Distance = Vector3.Cross(ray.direction,
                        candidate.transform.position - ray.origin).magnitude
                })
                .Where(candidate => candidate.AlongRay > 0f && candidate.AlongRay < 6.5f && candidate.Distance < 1.1f)
                .OrderBy(candidate => candidate.Distance + candidate.AlongRay * 0.02f)
                .Select(candidate => candidate.Pot)
                .FirstOrDefault();
        }
        if (pot?.Plant is null)
        {
            _status = "No growing plant found near the crosshair";
            LoggerInstance.Msg(_status);
            ShowNotice(_status);
            return;
        }

        pot.SetGrowthProgress_Server(1f);
        _status = "Target plant set to full growth";
        LoggerInstance.Msg(_status);
        ShowNotice(_status);
    }

    private static Pot? ResolveGrowingPot(RaycastHit hit)
    {
        var collider = hit.collider;
        if (collider is null || collider.GetComponentInParent<Player>() is not null) return null;
        var direct = collider.GetComponentInParent<Pot>();
        if (direct?.Plant is not null) return direct;
        var root = collider.transform.root;
        if (root is null) return null;
        return root.GetComponentsInChildren<Pot>(true)
            .Where(candidate => candidate?.Plant is not null)
            .OrderBy(candidate => Vector3.SqrMagnitude(candidate.transform.position - hit.point))
            .FirstOrDefault();
    }

    private void UpdateClonePlantTraits()
    {
        if (Time.unscaledTime < _nextTraitUpdate) return;
        _nextTraitUpdate = Time.unscaledTime + 0.5f;
        if (Time.unscaledTime >= _nextPotDiscovery)
        {
            _nextPotDiscovery = Time.unscaledTime + 90f;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<Pot>())
            {
                if (candidate is null) continue;
                if (candidate.NetworkObject is not null)
                    _potsByNetworkId[candidate.NetworkObject.ObjectId] = candidate;
                if (IsHost()) SendSoilState(candidate);
                var plant = candidate.Plant;
                var potKey = candidate.Pointer.ToInt64();
                if (plant is null)
                {
                    if (_trackedPots.ContainsKey(potKey) || _cloneByPot.ContainsKey(potKey)) ForgetPot(potKey);
                    continue;
                }
                var plantPointer = plant.Pointer.ToInt64();
                if (_cloneByPot.ContainsKey(potKey))
                {
                    if (_clonePlantPointers.TryGetValue(potKey, out var boundPointer) && boundPointer != plantPointer)
                    {
                        ForgetPot(potKey);
                        continue;
                    }
                    _clonePlantPointers[potKey] = plantPointer;
                    _trackedPots[potKey] = candidate;
                    continue;
                }
                var seedId = plant.SeedDefinition?.ID;
                if (string.IsNullOrWhiteSpace(seedId) ||
                    !_productsBySeed.TryGetValue(seedId, out var product) ||
                    !_qualityBySeed.TryGetValue(seedId, out var template)) continue;
                _trackedPots[potKey] = candidate;
                _cloneByPot[potKey] = (product, template);
                _clonePlantPointers[potKey] = plantPointer;
            }
        }
        foreach (var entry in _trackedPots.ToArray())
        {
            var pot = entry.Value;
            if (pot is null)
            {
                ForgetPot(entry.Key);
                continue;
            }
            var plant = pot.Plant;
            if (plant is null) continue;
            var plantPointer = plant.Pointer.ToInt64();
            if (_clonePlantPointers.TryGetValue(entry.Key, out var boundPointer) && boundPointer != plantPointer)
            {
                ForgetPot(entry.Key);
                continue;
            }
            if (!_configuredClonePots.Contains(entry.Key) && plant.GrowthTime > 0)
            {
                var originalGrowthTime = plant.GrowthTime;
                plant.GrowthTime = Mathf.CeilToInt(originalGrowthTime * CloneGrowthTimeMultiplier);
                _configuredClonePots.Add(entry.Key);
                LoggerInstance.Msg($"Clone plant configured: growth time {originalGrowthTime} -> {plant.GrowthTime}; native yield preserved.");
            }
        }

    }

    private void ForgetPot(long potKey)
    {
        _trackedPots.Remove(potKey);
        _cloneByPot.Remove(potKey);
        _clonePlantPointers.Remove(potKey);
        _configuredClonePots.Remove(potKey);
    }

    private static string SeedId(string productId)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(productId.ToLowerInvariant())));
        return "sicclone_" + digest[..20].ToLowerInvariant();
    }

    private static string SeedId(string productId, int quality) => SeedId(productId + "|quality:" + quality);

    private void EnsureCloneSeed(WeedDefinition weed, EQuality quality)
    {
        if (string.IsNullOrWhiteSpace(weed.ID)) return;
        var registry = UnityEngine.Object.FindObjectOfType<Registry>();
        var template = Registry.GetItem<SeedDefinition>("ogkushseed");
        if (registry is null || template is null) return;
        var seedId = SeedId(weed.ID, (int)quality);
        _productsBySeed[seedId] = weed;
        var qualityTemplate = weed.GetDefaultInstance(1).TryCast<ProductItemInstance>();
        if (qualityTemplate is not null)
        {
            qualityTemplate.Quality = quality;
            _qualityBySeed[seedId] = qualityTemplate;
        }
        if (_registeredSeeds.Contains(seedId) || Registry.ItemExists(seedId)) return;
        var clone = UnityEngine.Object.Instantiate(template);
        clone.ID = seedId;
        clone.Name = weed.Name + " Bud Clone";
        clone.Description = "A plantable clone grown from " + weed.Name + ".";
        registry.AddToRegistry(clone);
        _registeredSeeds.Add(seedId);
        LoggerInstance.Msg($"Registered clone seed {seedId} for {weed.Name} on demand.");
    }

    private ItemInstance? ReplaceHarvest(WeedPlant plant, int quantity, ItemInstance? original)
    {
        var seedId = plant.SeedDefinition?.ID;
        var potKey = plant.Pot?.Pointer.ToInt64() ?? 0;
        if (potKey != 0 && _cloneByPot.TryGetValue(potKey, out var plantedClone))
        {
            var copiedHarvest = CreatePreservedHarvest(plantedClone.Product, plantedClone.Template, quantity);
            _pendingHarvest = new PendingHarvest(plantedClone.Product, plantedClone.Template, quantity,
                Time.unscaledTime + 2f);
            LoggerInstance.Msg($"Harvested {quantity}x {plantedClone.Product.Name} at preserved quality {plantedClone.Template.Quality}.");
            return copiedHarvest;
        }
        if (string.IsNullOrWhiteSpace(seedId) || !_productsBySeed.TryGetValue(seedId, out var product))
        {
            return original;
        }
        if (_qualityBySeed.TryGetValue(seedId, out var qualityTemplate))
        {
            var copiedHarvest = CreatePreservedHarvest(product, qualityTemplate, quantity);
            _pendingHarvest = new PendingHarvest(product, qualityTemplate, quantity, Time.unscaledTime + 2f);
            LoggerInstance.Msg($"Harvested {quantity}x {product.Name} at preserved quality {qualityTemplate.Quality}.");
            return copiedHarvest;
        }
        LoggerInstance.Warning($"Harvest hook found {product.Name}, but its planted quality was unavailable.");
        return product.GetDefaultInstance(quantity);
    }

    private static ProductItemInstance CreatePreservedHarvest(
        WeedDefinition product, ProductItemInstance plantedTemplate, int quantity)
    {
        // The cloned seed uses an OG Kush plant prefab, whose native harvest is
        // therefore always basic OG Kush. Build the result from the planted custom
        // definition so its effects/perks/value remain attached, and carry across
        // the exact planted quality and packaging.
        return new ProductItemInstance(product, quantity, plantedTemplate.Quality,
            plantedTemplate.AppliedPackaging);
    }

    private ItemInstance PreservePendingHarvestAtInventory(ItemInstance original)
    {
        var pending = _pendingHarvest;
        if (pending is null || Time.unscaledTime > pending.ExpiresAt) return original;
        if (original.Definition?.ID?.Equals("ogkush", StringComparison.OrdinalIgnoreCase) != true) return original;
        var quantity = original.GetTotalAmount();
        if (quantity != pending.Quantity) return original;
        _pendingHarvest = null;
        LoggerInstance.Msg($"Replaced native OG Kush inventory award with {pending.Product.Name} ({pending.Template.Quality}).");
        return CreatePreservedHarvest(pending.Product, pending.Template, quantity);
    }

    private sealed record PendingHarvest(
        WeedDefinition Product, ProductItemInstance Template, int Quantity, float ExpiresAt);

    private static Player? ResolvePlayer(ulong steamId)
    {
        if (steamId == LocalSteamId()) return Player.Local;
        var transport = UnityEngine.Object.FindObjectOfType<FishySteamworks>();
        if (transport is null) return null;
        foreach (var player in Player.PlayerList)
        {
            if (player?.Connection is null) continue;
            var address = transport.GetConnectionAddress(player.Connection.ClientId);
            if (address?.Contains(steamId.ToString(), StringComparison.Ordinal) == true) return player;
        }
        return null;
    }

    private static ulong LocalSteamId()
    {
        try { return SteamUser.GetSteamID().m_SteamID; }
        catch { return 0; }
    }

    private static bool IsHost()
    {
        try { return Lobby.Instance is null || !Lobby.Instance.IsInLobby || Lobby.Instance.IsHost; }
        catch { return true; }
    }

    private static bool IsMultiplayerClient()
    {
        try { return Lobby.Instance is not null && Lobby.Instance.IsInLobby && !Lobby.Instance.IsHost; }
        catch { return false; }
    }

    [HarmonyPatch(typeof(WeedPlant), "GetHarvestedProduct")]
    private static class HarvestPatch
    {
        [HarmonyPriority(HarmonyLib.Priority.Last)]
        private static void Postfix(WeedPlant __instance, int quantity, ref ItemInstance __result) =>
            __result = Active?.ReplaceHarvest(__instance, quantity, __result) ?? __result;
    }

    [HarmonyPatch(typeof(Plant), "GetHarvestedProduct")]
    private static class BaseHarvestPatch
    {
        [HarmonyPriority(HarmonyLib.Priority.Last)]
        private static void Postfix(Plant __instance, int quantity, ref ItemInstance __result)
        {
            var weedPlant = __instance.TryCast<WeedPlant>();
            if (weedPlant is not null)
                __result = Active?.ReplaceHarvest(weedPlant, quantity, __result) ?? __result;
        }
    }

    [HarmonyPatch(typeof(PlayerInventory), "AddItemToInventory")]
    private static class HarvestInventoryPatch
    {
        [HarmonyPriority(HarmonyLib.Priority.First)]
        private static void Prefix(ref ItemInstance __0)
        {
            if (Active is not null)
                __0 = Active.PreservePendingHarvestAtInventory(__0);
        }
    }


    internal CloneRegistry LoadRegistry(ulong ownerSteamId, string careerId) =>
        _store?.Load(ownerSteamId, careerId) ?? CloneRegistry.Create(ownerSteamId, careerId);

    internal void SaveRegistry(CloneRegistry registry) => _store?.Save(registry);
}
