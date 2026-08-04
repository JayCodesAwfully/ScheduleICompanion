using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
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
    private readonly HashSet<string> _registeredSeeds = new(StringComparer.OrdinalIgnoreCase);
    private MelonPreferences_Entry<string>? _plantKeyEntry;
    private MelonPreferences_Entry<bool>? _instantGrowEntry;
    private KeyCode _plantKey = KeyCode.P;
    private bool _plantKeyHeld;
    private bool _registryStateLogged;
    private float _nextRegistryRefresh;
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
        HarmonyInstance.PatchAll(typeof(ClonalCultivationMod).Assembly);
        LoggerInstance.Msg($"Cultivation initialized. Hold a created weed bud, look at an empty pot, and press {_plantKey}.");
    }

    public override void OnUpdate()
    {
        if (_plantKeyEntry is not null && !_plantKeyEntry.Value.Equals(_plantKey.ToString(), StringComparison.OrdinalIgnoreCase))
            ParsePlantKey();
        if (Time.unscaledTime >= _nextRegistryRefresh)
        {
            _nextRegistryRefresh = Time.unscaledTime + 2f;
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
        if (_instantGrowEntry?.Value == true && Input.GetKeyDown(KeyCode.Insert)) TryInstantGrow();
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
            var seedId = SeedId(weed.ID);
            _productsBySeed[seedId] = weed;
            if (_registeredSeeds.Contains(seedId) || Registry.ItemExists(seedId))
            {
                _registeredSeeds.Add(seedId);
                continue;
            }

            var clone = UnityEngine.Object.Instantiate(template);
            clone.ID = seedId;
            clone.Name = weed.Name + " Bud Clone";
            clone.Description = "A plantable clone grown from " + weed.Name + ".";
            registry.AddToRegistry(clone);
            _registeredSeeds.Add(seedId);
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
            EnsureCloneSeed(definition, weed);
            if (!_productsBySeed.ContainsKey(seedId))
            {
                _status = "That weed variant could not be registered for planting";
                LoggerInstance.Msg(_status);
                return;
            }
        }
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        var hits = Physics.RaycastAll(ray, 5f).OrderBy(hit => hit.distance).ToArray();
        if (hits.Length == 0)
        {
            _status = "Look directly at an empty prepared pot";
            LoggerInstance.Msg(_status);
            return;
        }
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
        if (pot is null)
        {
            _status = $"No plantable pot found inside {firstWorldHit?.collider?.name ?? "the aimed object"}";
            LoggerInstance.Msg(_status);
            return;
        }
        if (!pot.CanAcceptSeed(out var reason))
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

        pot.PlantSeed_Server(seedId, (float)(int)weed.Quality);
        if (_qualityBySeed.TryGetValue(seedId, out var plantedTemplate))
            _cloneByPot[pot.Pointer.ToInt64()] = (definition, plantedTemplate);
        var quantityBefore = player._inventory[slotIndex].ItemInstance?.GetTotalAmount() ?? 0;
        player.RemoveEquippedItemFromInventory(definition.ID, 1);
        var quantityAfter = player._inventory[slotIndex].ItemInstance?.GetTotalAmount() ?? 0;
        _status = $"Planted {definition.Name} ({weed.Quality}); bud stack {quantityBefore} -> {quantityAfter}";
        LoggerInstance.Msg(_status);
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
            if (candidate is null || !candidate.CanAcceptSeed(out _)) continue;
            var distance = Vector3.SqrMagnitude(candidate.transform.position - hit.point);
            if (distance >= nearestDistance) continue;
            nearest = candidate;
            nearestDistance = distance;
        }
        return nearest;
    }

    private void TryInstantGrow()
    {
        var camera = PlayerCamera.Instance?.Camera ?? Camera.main;
        if (camera is null) return;
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        if (!Physics.Raycast(ray, out var hit, 5f))
        {
            _status = "Look directly at a plant and press Insert";
            return;
        }

        var plant = hit.collider?.GetComponentInParent<Plant>();
        if (plant is null && hit.collider?.transform.root is { } root)
        {
            var candidates = root.GetComponentsInChildren<Plant>(true);
            plant = candidates.OrderBy(candidate =>
                Vector3.SqrMagnitude(candidate.transform.position - hit.point)).FirstOrDefault();
        }
        if (plant?.Pot is null)
        {
            _status = "No plant found in that grow container";
            LoggerInstance.Msg(_status);
            return;
        }

        plant.Pot.SetGrowthProgress_Server(1f);
        _status = "Target plant set to full growth";
        LoggerInstance.Msg(_status);
    }

    private static string SeedId(string productId)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(productId.ToLowerInvariant())));
        return "sicclone_" + digest[..20].ToLowerInvariant();
    }

    private static string SeedId(string productId, int quality) => SeedId(productId + "|quality:" + quality);

    private void EnsureCloneSeed(WeedDefinition weed, ProductItemInstance plantedItem)
    {
        if (string.IsNullOrWhiteSpace(weed.ID)) return;
        var registry = UnityEngine.Object.FindObjectOfType<Registry>();
        var template = Registry.GetItem<SeedDefinition>("ogkushseed");
        if (registry is null || template is null) return;
        var seedId = SeedId(weed.ID, (int)plantedItem.Quality);
        _productsBySeed[seedId] = weed;
        var qualityTemplate = plantedItem.GetCopy(1).TryCast<ProductItemInstance>();
        if (qualityTemplate is not null)
        {
            qualityTemplate.Quality = plantedItem.Quality;
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
            var originalProduct = original?.TryCast<ProductItemInstance>();
            if (originalProduct is null) return original;
            originalProduct.Quality = plantedClone.Template.Quality;
            return original;
        }
        if (string.IsNullOrWhiteSpace(seedId) || !_productsBySeed.TryGetValue(seedId, out var product))
        {
            LoggerInstance.Msg($"Harvest hook ignored seed {seedId ?? "none"}; clone mapping unavailable.");
            return original;
        }
        if (_qualityBySeed.TryGetValue(seedId, out var qualityTemplate))
        {
            var copiedHarvest = qualityTemplate.GetCopy(quantity);
            LoggerInstance.Msg($"Harvested {quantity}x {product.Name} at preserved quality {qualityTemplate.Quality}.");
            return copiedHarvest;
        }
        LoggerInstance.Warning($"Harvest hook found {product.Name}, but its planted quality was unavailable.");
        return product.GetDefaultInstance(quantity);
    }

    [HarmonyPatch(typeof(WeedPlant), "GetHarvestedProduct")]
    private static class HarvestPatch
    {
        private static void Postfix(WeedPlant __instance, int quantity, ref ItemInstance __result) =>
            __result = Active?.ReplaceHarvest(__instance, quantity, __result) ?? __result;
    }

    internal CloneRegistry LoadRegistry(ulong ownerSteamId, string careerId) =>
        _store?.Load(ownerSteamId, careerId) ?? CloneRegistry.Create(ownerSteamId, careerId);

    internal void SaveRegistry(CloneRegistry registry) => _store?.Save(registry);
}
