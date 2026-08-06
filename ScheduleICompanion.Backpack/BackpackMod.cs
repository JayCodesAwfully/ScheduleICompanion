using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppFishySteamworks;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using Il2CppTMPro;

namespace ScheduleICompanion.Backpack;

public sealed class BackpackMod : MelonMod
{
    private static BackpackMod? ActiveInstance;
    private const string ProtocolVersion = "2";
    private BackpackStore? _store;
    private MelonPreferences_Entry<string>? _openKeyEntry;
    private KeyCode _openKey = KeyCode.B;
    private BackpackState? _state;
    private bool _menuOpen;
    private bool _waitingForHost;
    private float _nextHostSyncRetry;
    private bool _sessionVerified;
    private string _status = "Ready";
    private Vector2 _inventoryScroll;
    private Vector2 _backpackScroll;
    private CursorLockMode _previousCursorLock;
    private bool _previousCursorVisible;
    private PlayerCamera? _lockedCamera;
    private bool _previousCanLook = true;
    private bool _dragging;
    private bool _dragFromBackpack;
    private int _dragSlot = -1;
    private string _dragLabel = "";
    private ItemInstance? _dragItem;
    private int _nativeDragSlot = -1;
    private ItemInstance? _nativeDragItem;
    private StorageEntity? _nativeStorage;
    private GameObject? _nativeStorageObject;
    private readonly Dictionary<string, BackpackState> _stagedStates = new(StringComparer.OrdinalIgnoreCase);
    private SaveManager? _saveManager;
    private UnityAction? _saveCompleteAction;
    private bool _backpackSaveRequested;

    public override void OnInitializeMelon()
    {
        ActiveInstance = this;
        var category = MelonPreferences.CreateCategory("ScheduleICompanion.Backpack", "Personal Backpack");
        _openKeyEntry = category.CreateEntry("OpenKey", "B", "Open/close key");
        ParseOpenKey();
        var root = Path.Combine(AppContext.BaseDirectory, "UserData", "ScheduleICompanion", "Backpacks");
        _store = new BackpackStore(root);
        BackpackProtocol.Received += OnProtocolMessage;
        HarmonyInstance.PatchAll(typeof(BackpackProtocol).Assembly);
        LoggerInstance.Msg($"Personal Backpack initialized. Press {_openKey} to open it.");
    }

    public override void OnUpdate()
    {
        if (_openKeyEntry is not null && !_openKeyEntry.Value.Equals(_openKey.ToString(), StringComparison.OrdinalIgnoreCase))
            ParseOpenKey();

        MaintainNativeStorageMenu();
        EnsureSaveCheckpointHook();
        TryStartBackpackSave();
        if (_waitingForHost && IsMultiplayerClient() && Time.unscaledTime >= _nextHostSyncRetry)
        {
            _nextHostSyncRetry = Time.unscaledTime + 2f;
            SendHello();
        }

        if (Input.GetKeyDown(_openKey))
        {
            if (_nativeStorage is not null && StorageMenu.Instance is not null && StorageMenu.Instance.IsOpen &&
                StorageMenu.Instance.OpenedStorageEntity?.Pointer == _nativeStorage.Pointer)
                StorageMenu.Instance.Close();
            else if (CanOpen())
                OpenNativeStorage();
            return;
        }

        if (_menuOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _lockedCamera?.SetCanLook(false);
            if (Input.GetKeyDown(_openKey)) CloseMenu();
            return;
        }

    }

    public override void OnGUI()
    {
        if (!_menuOpen) return;
        var player = Player.Local;
        if (player is null)
        {
            CloseMenu();
            return;
        }

        if (DrawSatchelGui(player)) return;

        var width = Math.Min(900f, Screen.width - 40f);
        var height = Math.Min(620f, Screen.height - 40f);
        var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        var oldColor = GUI.color;
        GUI.color = new Color(0.08f, 0.12f, 0.09f, 0.98f);
        GUI.Box(area, GUIContent.none);
        GUI.color = oldColor;

        GUILayout.BeginArea(new Rect(area.x + 18, area.y + 14, area.width - 36, area.height - 28));
        GUILayout.BeginHorizontal();
        GUILayout.Label("PERSONAL BACKPACK", HeaderStyle());
        GUILayout.FlexibleSpace();
        GUILayout.Label($"Revision {_state?.Revision ?? 0}");
        if (GUILayout.Button("Close  [" + _openKey + "]", GUILayout.Width(120))) CloseMenu();
        GUILayout.EndHorizontal();
        GUILayout.Label(_waitingForHost ? "Synchronising with host…" : _status);
        GUILayout.Space(8);

        GUILayout.BeginHorizontal();
        DrawInventory(player);
        GUILayout.Space(16);
        DrawBackpack();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("Whole stacks only in this safety-first build. Transfers are host-authorised and journalled.");
        GUILayout.EndArea();
    }

    private void DrawInventory(Player player)
    {
        GUILayout.BeginVertical(GUILayout.Width(410));
        GUILayout.Label("PLAYER INVENTORY", SectionStyle());
        _inventoryScroll = GUILayout.BeginScrollView(_inventoryScroll, GUILayout.Height(470));
        var slots = GetPlayerInventorySlots(player);
        if (slots is null) { GUILayout.Label("Inventory is not ready."); GUILayout.EndScrollView(); GUILayout.EndVertical(); return; }
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var item = slot?.ItemInstance;
            GUILayout.BeginHorizontal("box");
            GUILayout.Label($"{i + 1:00}   {Describe(item)}", GUILayout.Width(285));
            GUI.enabled = _sessionVerified && !_waitingForHost && item is not null && _state is not null;
            if (GUILayout.Button("Deposit", GUILayout.Width(92))) RequestTransfer("deposit", i, -1, item!);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawBackpack()
    {
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("BACKPACK", SectionStyle());
        _backpackScroll = GUILayout.BeginScrollView(_backpackScroll, GUILayout.Height(470));
        for (var i = 0; i < 12; i++)
        {
            var json = _state?.Slots.ElementAtOrDefault(i) ?? "";
            GUILayout.BeginHorizontal("box");
            GUILayout.Label($"{i + 1:00}   {DescribeJson(json)}", GUILayout.Width(285));
            GUI.enabled = _sessionVerified && !_waitingForHost && !string.IsNullOrWhiteSpace(json);
            if (GUILayout.Button("Withdraw", GUILayout.Width(92))) RequestTransfer("withdraw", -1, i, null);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private bool DrawSatchelGui(Player player)
    {
        CaptureNativeDrag(player);
        var width = Math.Min(550f, Screen.width - 28f);
        var height = Math.Min(570f, Screen.height - 28f);
        var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        DrawRect(area, new Color(0.12f, 0.075f, 0.035f, 0.96f));
        DrawRect(new Rect(area.x + 8, area.y + 8, area.width - 16, area.height - 16), new Color(0.25f, 0.13f, 0.055f, 0.98f));
        DrawStitches(area);

        GUI.Label(new Rect(area.x + 24, area.y + 18, 280, 32), "SATCHEL STORAGE", SectionStyle());
        GUI.Label(new Rect(area.x + 24, area.y + 46, 320, 22),
            _waitingForHost ? "Synchronising with host..." : _status, StatusStyle());
        if (GUI.Button(new Rect(area.xMax - 105, area.y + 18, 80, 32), $"Close [{_openKey}]")) CloseMenu();

        var storage = new Rect(area.x + 24, area.y + 78, area.width - 48, area.height - 125);
        DrawSatchelGrid(storage);

        GUI.Label(new Rect(area.x + 24, area.yMax - 36, area.width - 48, 22),
            "Drag from your hotbar into a slot; drag back onto an empty hotbar slot to withdraw.", HintStyle());

        if (_dragging)
        {
            var mouse = Event.current.mousePosition;
            var ghost = new Rect(mouse.x + 14, mouse.y + 12, 210, 44);
            DrawRect(ghost, new Color(0.12f, 0.075f, 0.035f, 0.94f));
            GUI.Label(new Rect(ghost.x + 9, ghost.y + 6, ghost.width - 18, ghost.height - 12), _dragLabel, SlotTextStyle());
            if (Event.current.type == EventType.MouseUp)
            {
                TryWithdrawToHoveredGameSlot();
                CancelDrag();
                Event.current.Use();
            }
        }

        return true;
    }

    private void DrawPocketGrid(Player player, Rect area)
    {
        GUI.Label(new Rect(area.x, area.y, area.width, 30), "YOUR POCKETS", SectionStyle());
        GUI.Label(new Rect(area.x, area.y + 28, area.width, 22), "Drag a stack into an empty satchel slot", HintStyle());
        var slots = GetPlayerInventorySlots(player);
        if (slots is null)
        {
            GUI.Label(new Rect(area.x, area.y + 65, area.width, 30), "Inventory is not ready.");
            return;
        }

        const int columns = 3;
        const float gap = 10f;
        var slotWidth = (area.width - gap * (columns - 1)) / columns;
        const float slotHeight = 88f;
        for (var i = 0; i < slots.Count; i++)
        {
            var row = i / columns;
            var col = i % columns;
            var rect = new Rect(area.x + col * (slotWidth + gap), area.y + 62 + row * (slotHeight + gap), slotWidth, slotHeight);
            var item = slots[i]?.ItemInstance;
            DrawItemSlot(rect, false, i, Describe(item), item is not null, item);
        }
    }

    private void DrawSatchelGrid(Rect area)
    {
        const int columns = 3;
        const float gap = 12f;
        var slotWidth = (area.width - gap * (columns - 1)) / columns;
        var slotHeight = (area.height - gap * 3) / 4f;
        for (var i = 0; i < 12; i++)
        {
            var row = i / columns;
            var col = i % columns;
            var json = _state?.Slots.ElementAtOrDefault(i) ?? "";
            var rect = new Rect(area.x + col * (slotWidth + gap), area.y + row * (slotHeight + gap), slotWidth, slotHeight);
            DrawItemSlot(rect, true, i, DescribeJson(json), !string.IsNullOrWhiteSpace(json), null);
        }
    }

    private void DrawItemSlot(Rect rect, bool backpack, int index, string label, bool occupied, ItemInstance? item)
    {
        var interactive = _sessionVerified && !_waitingForHost && _state is not null;
        var hovered = rect.Contains(Event.current.mousePosition);
        var nativeDropTarget = backpack && _nativeDragSlot >= 0 && !occupied;
        var isDropTarget = !occupied && ((_dragging && _dragFromBackpack != backpack) || nativeDropTarget);
        var color = isDropTarget && hovered
            ? new Color(0.43f, 0.56f, 0.25f, 1f)
            : occupied ? new Color(0.16f, 0.095f, 0.045f, 1f) : new Color(0.29f, 0.18f, 0.09f, 1f);
        DrawRect(rect, new Color(0.09f, 0.045f, 0.018f, 1f));
        DrawRect(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), color);
        GUI.Label(new Rect(rect.x + 8, rect.y + 5, rect.width - 16, 18), $"{index + 1:00}", SlotNumberStyle());
        GUI.Label(new Rect(rect.x + 8, rect.y + 27, rect.width - 16, rect.height - 32), label, SlotTextStyle());

        if (!interactive) return;
        if (backpack && Event.current.type == EventType.MouseUp && Event.current.button == 0 && hovered &&
            !occupied && _nativeDragSlot >= 0 && _nativeDragItem is not null)
        {
            RequestTransfer("deposit", _nativeDragSlot, index, _nativeDragItem);
            _nativeDragSlot = -1;
            _nativeDragItem = null;
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hovered && occupied)
        {
            _dragging = true;
            _dragFromBackpack = backpack;
            _dragSlot = index;
            _dragLabel = label;
            _dragItem = item;
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseUp && Event.current.button == 0 && hovered && _dragging && _dragFromBackpack != backpack)
        {
            if (occupied)
            {
                _status = "Choose an empty destination slot";
            }
            else if (_dragFromBackpack)
            {
                RequestTransfer("withdraw", index, _dragSlot, null);
            }
            else
            {
                RequestTransfer("deposit", _dragSlot, index, _dragItem);
            }
            CancelDrag();
            Event.current.Use();
        }
    }

    private void CancelDrag()
    {
        _dragging = false;
        _dragFromBackpack = false;
        _dragSlot = -1;
        _dragLabel = "";
        _dragItem = null;
    }

    private void CaptureNativeDrag(Player player)
    {
        try
        {
            var manager = ItemUIManager.Instance;
            if (manager is null || !manager.IsCurrentlyDragging || manager.draggedSlot is null) return;
            var slots = GetPlayerInventorySlots(player);
            if (slots is null) return;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].Pointer != manager.draggedSlot.Pointer) continue;
                _nativeDragSlot = i;
                _nativeDragItem = slots[i].ItemInstance;
                return;
            }
        }
        catch { }
    }

    private void TryWithdrawToHoveredGameSlot()
    {
        if (!_dragFromBackpack || _dragSlot < 0) return;
        try
        {
            var target = ItemUIManager.Instance?.HoveredSlot?.assignedSlot;
            var slots = Player.Local is null ? null : GetPlayerInventorySlots(Player.Local);
            if (target is null || slots is null) return;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].Pointer != target.Pointer) continue;
                if (slots[i].ItemInstance is null && !slots[i].IsAddLocked)
                    RequestTransfer("withdraw", i, _dragSlot, null);
                else
                    _status = "Choose an empty hotbar slot";
                return;
            }
        }
        catch { }
    }

    private static Il2CppReferenceArray<ItemSlot>? GetPlayerInventorySlots(Player player)
    {
        try { return player._inventory; }
        catch { return null; }
    }

    private static void DrawRect(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.Box(rect, GUIContent.none);
        GUI.color = previous;
    }

    private static void DrawStitches(Rect area)
    {
        var stitch = new Color(0.72f, 0.53f, 0.27f, 0.9f);
        for (var x = area.x + 18; x < area.xMax - 18; x += 16)
        {
            DrawRect(new Rect(x, area.y + 14, 8, 2), stitch);
            DrawRect(new Rect(x, area.yMax - 16, 8, 2), stitch);
        }
        for (var y = area.y + 22; y < area.yMax - 22; y += 16)
        {
            DrawRect(new Rect(area.x + 14, y, 2, 8), stitch);
            DrawRect(new Rect(area.xMax - 16, y, 2, 8), stitch);
        }
    }

    private void OpenNativeStorage()
    {
        LoadLocalState();
        _sessionVerified = !IsMultiplayerClient();
        if (IsMultiplayerClient())
        {
            _waitingForHost = true;
            _nextHostSyncRetry = Time.unscaledTime + 2f;
            _status = "Waiting for the host snapshot";
            SendHello();
        }

        EnsureNativeStorage();
        PopulateNativeStorage();
        if (_nativeStorage is null || StorageMenu.Instance is null)
        {
            _status = "Native storage menu is not ready";
            return;
        }

        StorageMenu.Instance.Open(_nativeStorage, (System.Action)NativeStorageClosed);
        StorageMenu.Instance.SlotGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        StorageMenu.Instance.SlotGridLayout.constraintCount = 4;
        for (var i = 0; i < StorageMenu.Instance.SlotsUIs.Count; i++)
            StorageMenu.Instance.SlotsUIs[i].gameObject.SetActive(i < 12);
    }

    private void EnsureNativeStorage()
    {
        if (_nativeStorage is not null) return;
        _nativeStorageObject = new GameObject("ScheduleICompanion_PersonalBackpack");
        _nativeStorageObject.SetActive(false);
        _nativeStorage = _nativeStorageObject.AddComponent<StorageEntity>();
        _nativeStorage.SlotCount = 12;
        _nativeStorage.DisplayRowCount = 3;
        _nativeStorage.StorageEntityName = "Backpack";
        _nativeStorage.StorageEntitySubtitle = "12 protected slots";
        _nativeStorage.EmptyOnSleep = false;
        _nativeStorage.SlotsAreFilterable = false;
        _nativeStorageObject.SetActive(true);

        var slots = new Il2CppSystem.Collections.Generic.List<ItemSlot>();
        for (var i = 0; i < 12; i++)
        {
            var slot = new ItemSlot(true);
            slot.SetSlotOwner(new IItemSlotOwner(_nativeStorage.Pointer));
            slot.SetIsAddLocked(false);
            slot.SetIsRemovalLocked(false);
            slots.Add(slot);
        }
        _nativeStorage.ItemSlots = slots;
        UnityEngine.Object.DontDestroyOnLoad(_nativeStorageObject);
    }

    private void PopulateNativeStorage()
    {
        if (_nativeStorage is null || _state is null) return;
        var slots = _nativeStorage.ItemSlots;
        for (var i = 0; i < slots.Count && i < 12; i++)
        {
            slots[i].ClearStoredInstance(false);
            var json = _state.Slots.ElementAtOrDefault(i);
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                var item = ItemDeserializer.LoadItem(json);
                if (item is not null) slots[i].SetStoredItem(item, false);
            }
            catch { }
        }
    }

    private void NativeStorageClosed()
    {
        if (_nativeStorage is null || _state is null) return;
        var slots = _nativeStorage.ItemSlots;
        for (var i = 0; i < 12; i++)
            _state.Slots[i] = i < slots.Count && slots[i].ItemInstance is { } item ? SerializeItem(item) : "";
        _state.Revision++;
        StageState(_state);
        SyncStateWithHost();
        _waitingForHost = false;
        _status = "Closing Backpack and saving game...";
        RestoreSharedStorageMenu();
        _backpackSaveRequested = true;
        TryStartBackpackSave();
    }

    private static void RestoreSharedStorageMenu()
    {
        var menu = StorageMenu.Instance;
        if (menu is null) return;
        menu.SlotGridLayout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        menu.SlotGridLayout.constraintCount = 1;
    }

    private void MaintainNativeStorageMenu()
    {
        var menu = StorageMenu.Instance;
        if (menu is null || !menu.IsOpen) return;

        if (_nativeStorage is not null && menu.OpenedStorageEntity?.Pointer == _nativeStorage.Pointer)
        {
            menu.SlotGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            menu.SlotGridLayout.constraintCount = 4;
            for (var i = 0; i < menu.SlotsUIs.Count; i++)
            {
                var visible = i < 12;
                if (menu.SlotsUIs[i].gameObject.activeSelf != visible)
                    menu.SlotsUIs[i].gameObject.SetActive(visible);
                if (visible) menu.SlotsUIs[i].UpdateUI();
            }
            PersistNativeStorageIfChanged();
            return;
        }

        var storage = menu.OpenedStorageEntity;
        if (storage is null) return;
        menu.SlotGridLayout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        menu.SlotGridLayout.constraintCount = Math.Max(1, storage.DisplayRowCount);
        var slotCount = storage.ItemSlots?.Count ?? 0;
        for (var i = 0; i < menu.SlotsUIs.Count; i++)
        {
            var visible = i < slotCount;
            if (menu.SlotsUIs[i].gameObject.activeSelf != visible)
                menu.SlotsUIs[i].gameObject.SetActive(visible);
        }
    }

    private void PersistNativeStorageIfChanged()
    {
        if (_nativeStorage is null || _state is null) return;
        var slots = _nativeStorage.ItemSlots;
        var changed = false;
        for (var i = 0; i < 12; i++)
        {
            var json = i < slots.Count && slots[i].ItemInstance is { } item ? SerializeItem(item) : "";
            if (string.Equals(_state.Slots[i], json, StringComparison.Ordinal)) continue;
            _state.Slots[i] = json;
            changed = true;
        }
        if (!changed) return;
        _state.Revision++;
        StageState(_state);
        SyncStateWithHost();
    }

    private void HandleNativeDrop(ItemUIManager manager)
    {
        if (_nativeStorage is null || manager.draggedSlot?.assignedSlot is null || manager.HoveredSlot?.assignedSlot is null)
            return;

        var source = manager.draggedSlot.assignedSlot;
        var target = manager.HoveredSlot.assignedSlot;
        var backpackSlots = _nativeStorage.ItemSlots;
        var backpackSource = FindSlot(backpackSlots, source);
        var backpackTarget = FindSlot(backpackSlots, target);
        if (backpackSource < 0 && backpackTarget < 0) return;

        var playerSlots = Player.Local is null ? null : GetPlayerInventorySlots(Player.Local);
        if (playerSlots is null) return;
        var inventorySource = FindSlot(playerSlots, source);
        var inventoryTarget = FindSlot(playerSlots, target);

        if (inventorySource >= 0 && backpackTarget >= 0)
        {
            if (target.ItemInstance is not null)
                _status = "Choose an empty Backpack slot";
            else
            {
                LoggerInstance.Msg($"Backpack drop: inventory slot {inventorySource + 1} to Backpack slot {backpackTarget + 1}.");
                RequestTransfer("deposit", inventorySource, backpackTarget, source.ItemInstance);
            }
        }
        else if (backpackSource >= 0 && inventoryTarget >= 0)
        {
            if (target.ItemInstance is not null || target.IsAddLocked)
                _status = "Choose an empty hotbar slot";
            else
            {
                LoggerInstance.Msg($"Backpack drop: Backpack slot {backpackSource + 1} to inventory slot {inventoryTarget + 1}.");
                RequestTransfer("withdraw", inventoryTarget, backpackSource, null);
            }
        }
        else if (backpackSource >= 0 && backpackTarget >= 0 && backpackSource != backpackTarget)
        {
            if (target.ItemInstance is not null)
                _status = "Choose an empty Backpack slot";
            else
                RequestTransfer("move", backpackTarget, backpackSource, source.ItemInstance);
        }

        PopulateNativeStorage();
    }

    private static int FindSlot(Il2CppSystem.Collections.Generic.List<ItemSlot> slots, ItemSlot target)
    {
        for (var i = 0; i < slots.Count; i++)
            if (slots[i].Pointer == target.Pointer) return i;
        return -1;
    }

    private static int FindSlot(Il2CppReferenceArray<ItemSlot> slots, ItemSlot target)
    {
        for (var i = 0; i < slots.Length; i++)
            if (slots[i].Pointer == target.Pointer) return i;
        return -1;
    }

    private bool OwnsNativeSlot(ItemSlot slot)
    {
        if (_nativeStorage is null) return false;
        return FindSlot(_nativeStorage.ItemSlots, slot) >= 0;
    }

    [HarmonyPatch(typeof(ItemSlot), "SetStoredItem")]
    private static class NativeBackpackSetItemPatch
    {
        private static bool Prefix(ItemSlot __instance, ItemInstance instance)
        {
            if (ActiveInstance?.OwnsNativeSlot(__instance) != true) return true;
            __instance._ItemInstance_k__BackingField = instance;
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemSlot), "ClearStoredInstance")]
    private static class NativeBackpackClearItemPatch
    {
        private static bool Prefix(ItemSlot __instance)
        {
            if (ActiveInstance?.OwnsNativeSlot(__instance) != true) return true;
            __instance._ItemInstance_k__BackingField = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(StorageEntity), "SetItemSlotQuantity")]
    private static class NativeBackpackQuantityPatch
    {
        private static bool Prefix(StorageEntity __instance, int itemSlotIndex, int quantity)
        {
            var active = ActiveInstance;
            if (active?._nativeStorage is null || __instance.Pointer != active._nativeStorage.Pointer) return true;
            var slots = __instance.ItemSlots;
            if (itemSlotIndex < 0 || itemSlotIndex >= slots.Count || slots[itemSlotIndex].ItemInstance is not { } item)
                return false;
            item.SetQuantity(Math.Max(0, quantity));
            if (quantity <= 0) slots[itemSlotIndex]._ItemInstance_k__BackingField = null;
            active.PersistNativeStorageIfChanged();
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemUIManager), "EndDrag")]
    private static class NativeBackpackRefreshPatch
    {
        private static void Postfix()
        {
            var instance = ActiveInstance;
            var menu = StorageMenu.Instance;
            if (instance?._nativeStorage is null || menu is null || !menu.IsOpen ||
                menu.OpenedStorageEntity?.Pointer != instance._nativeStorage.Pointer) return;
            for (var i = 0; i < menu.SlotsUIs.Count; i++)
                if (menu.SlotsUIs[i].gameObject.activeSelf) menu.SlotsUIs[i].UpdateUI();
        }
    }

    private void OpenMenu()
    {
        _previousCursorLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _lockedCamera = PlayerCamera.Instance;
        if (_lockedCamera is not null)
        {
            _previousCanLook = _lockedCamera.CanLook;
            _lockedCamera.SetCanLook(false);
            _lockedCamera.FreeMouse(true);
            _lockedCamera.AddActiveUIElement("ScheduleICompanion.Backpack");
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _menuOpen = true;
        LoadLocalState();
        _sessionVerified = !IsMultiplayerClient();

        if (IsMultiplayerClient())
        {
            _waitingForHost = true;
            _nextHostSyncRetry = Time.unscaledTime + 2f;
            _status = "Waiting for the host snapshot";
            if (!SendHello())
            {
                _waitingForHost = false;
                _status = "Not verified: unable to contact the host";
            }
        }
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _waitingForHost = false;
        _sessionVerified = false;
        CancelDrag();
        if (_lockedCamera is not null)
        {
            _lockedCamera.RemoveActiveUIElement("ScheduleICompanion.Backpack");
            _lockedCamera.SetCanLook(_previousCanLook);
            _lockedCamera.FreeMouse(false);
            _lockedCamera = null;
        }
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
    }

    private bool CanOpen()
    {
        try
        {
            if (Player.Local is null) return false;
        }
        catch
        {
            return false;
        }
        try
        {
            var storageMenu = StorageMenu.Instance;
            if (storageMenu is not null && storageMenu.IsOpen) return false;
        }
        catch
        {
            // Scene and runtime transitions can leave an IL2CPP singleton wrapper pointing
            // at a destroyed menu. It must not prevent the personal backpack from opening.
        }
        // Native shelves can leave the cursor unlocked for a frame (or indefinitely on a
        // client) after closing. Camera look is the reliable signal that gameplay resumed.
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            try
            {
                var camera = PlayerCamera.Instance;
                if (camera is null || !camera.CanLook) return false;
            }
            catch
            {
                // A stale camera singleton is equivalent to no UI blocking gameplay.
            }
        }
        try
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected is not null &&
                (selected.GetComponent<InputField>() is not null || selected.GetComponent<TMP_InputField>() is not null))
                return false;
        }
        catch
        {
            // Selected UI objects may be destroyed during a runtime refresh.
        }
        return true;
    }

    private void RequestTransfer(string type, int inventorySlot, int backpackSlot, ItemInstance? item)
    {
        if (_state is null || !_sessionVerified)
        {
            CompleteRequest(false, "This session has not been verified by the host");
            return;
        }
        var message = new BackpackMessage
        {
            Type = type,
            RequestId = Guid.NewGuid(),
            ExpectedRevision = _state.Revision,
            InventorySlot = inventorySlot,
            BackpackSlot = backpackSlot,
            Fingerprint = item is null ? "" : Fingerprint(SerializeItem(item))
        };

        _waitingForHost = true;
        _nextHostSyncRetry = Time.unscaledTime + 2f;
        _status = "Waiting for host confirmation…";
        if (IsMultiplayerClient())
        {
            if (!BackpackProtocol.Send(message)) CompleteRequest(false, "Unable to contact the host");
        }
        else
        {
            ProcessHostRequest(LocalSteamId(), message);
        }
    }

    private void OnProtocolMessage(ulong sender, bool senderIsHost, BackpackMessage message)
    {
        var local = LocalSteamId();
        if (message.Recipient != 0 && message.Recipient != local) return;

        if (IsHost() && message.Type is "hello" or "sync" or "deposit" or "withdraw" or "move")
        {
            ProcessHostRequest(sender, message);
            return;
        }

        if (message.Type == "snapshot" && senderIsHost)
        {
            _sessionVerified = message.SessionVerified && message.Protocol == ProtocolVersion;
            if (message.State is not null)
            {
                _state = message.State;
                StageState(_state);
                PopulateNativeStorage();
            }
            _waitingForHost = false;
            _status = !_sessionVerified
                ? "Not verified: " + (string.IsNullOrWhiteSpace(message.Error) ? "incompatible host" : message.Error)
                : message.Success ? "Verified by host" : message.Error;
        }
    }

    private void ProcessHostRequest(ulong sender, BackpackMessage request)
    {
        if (request.Protocol != ProtocolVersion)
        {
            SendResult(sender, request.RequestId, false, "Incompatible backpack protocol", null, false);
            return;
        }
        var player = ResolvePlayer(sender);
        if (player is null)
        {
            SendResult(sender, request.RequestId, false, "The host could not resolve your player inventory", null, false);
            return;
        }

        var state = LoadWorkingState(sender, CareerId());
        if (request.Type is "hello" or "sync")
            state = ReconcileClientRecovery(sender, state, request.State);
        RecoverInterrupted(state, player);
        if (request.Type is "hello" or "sync")
        {
            SendResult(sender, request.RequestId, true, "Synchronised", state);
            return;
        }
        if (request.ExpectedRevision != state.Revision)
        {
            SendResult(sender, request.RequestId, false, "Backpack changed; refreshed to the host copy", state);
            return;
        }

        try
        {
            if (request.Type == "deposit") Deposit(state, player, request);
            else if (request.Type == "withdraw") Withdraw(state, player, request);
            else if (request.Type == "move") MoveWithinBackpack(state, request);
            else throw new InvalidOperationException("Unsupported Backpack operation");
            SendResult(sender, request.RequestId, true, "Transfer committed", state);
        }
        catch (Exception ex)
        {
            LoggerInstance.Warning($"Backpack transaction {request.RequestId} rejected: {ex.Message}");
            try { RecoverInterrupted(state, player); } catch (Exception recovery) { LoggerInstance.Error($"Backpack recovery warning: {recovery}"); }
            SendResult(sender, request.RequestId, false, ex.Message, state);
        }
    }

    private void Deposit(BackpackState state, Player player, BackpackMessage request)
    {
        var inventory = GetPlayerInventorySlots(player) ?? throw new InvalidOperationException("Player inventory is not ready");
        if (request.InventorySlot < 0 || request.InventorySlot >= inventory.Length) throw new InvalidOperationException("Invalid inventory slot");
        var source = inventory[request.InventorySlot];
        var item = source.ItemInstance ?? throw new InvalidOperationException("That inventory slot is now empty");
        var json = SerializeItem(item);
        if (!Fingerprint(json).Equals(request.Fingerprint, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The source item changed before the host received it");
        var destination = request.BackpackSlot >= 0 && request.BackpackSlot < state.Slots.Length &&
            string.IsNullOrWhiteSpace(state.Slots[request.BackpackSlot])
                ? request.BackpackSlot
                : Array.FindIndex(state.Slots, string.IsNullOrWhiteSpace);
        if (destination < 0) throw new InvalidOperationException("The backpack is full");

        var journal = BeginJournal(state, request, "deposit", destination, json);
        journal.Phase = "remove-authorized";
        StageState(state);
        source.ClearStoredInstance(true);
        state.Slots[destination] = json;
        state.Revision++;
        journal.Phase = "committed";
        TrimJournal(state);
        StageState(state);
    }

    private void Withdraw(BackpackState state, Player player, BackpackMessage request)
    {
        if (request.BackpackSlot < 0 || request.BackpackSlot >= state.Slots.Length) throw new InvalidOperationException("Invalid backpack slot");
        var json = state.Slots[request.BackpackSlot];
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("That backpack slot is now empty");
        var item = ItemDeserializer.LoadItem(json) ?? throw new InvalidOperationException("The stored item could not be reconstructed and was left untouched");
        var inventory = GetPlayerInventorySlots(player) ?? throw new InvalidOperationException("Player inventory is not ready");
        var destination = request.InventorySlot >= 0 && request.InventorySlot < inventory.Length &&
            inventory[request.InventorySlot].ItemInstance is null && !inventory[request.InventorySlot].IsAddLocked
                ? request.InventorySlot
                : -1;
        if (destination < 0)
            for (var i = 0; i < inventory.Length; i++)
                if (inventory[i].ItemInstance is null && !inventory[i].IsAddLocked) { destination = i; break; }
        if (destination < 0) throw new InvalidOperationException("Your inventory has no empty slot");

        var journal = BeginJournal(state, request, "withdraw", request.BackpackSlot, json);
        journal.InventorySlot = destination;
        state.Slots[request.BackpackSlot] = "";
        state.Revision++;
        journal.Phase = "backpack-removed";
        StageState(state);
        inventory[destination].SetStoredItem(item, true);
        journal.Phase = "committed";
        TrimJournal(state);
        StageState(state);
    }

    private void MoveWithinBackpack(BackpackState state, BackpackMessage request)
    {
        var source = request.BackpackSlot;
        var destination = request.InventorySlot;
        if (source < 0 || source >= state.Slots.Length || destination < 0 || destination >= state.Slots.Length)
            throw new InvalidOperationException("Invalid Backpack slot");
        var json = state.Slots[source];
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("That Backpack slot is now empty");
        if (!string.IsNullOrWhiteSpace(state.Slots[destination]))
            throw new InvalidOperationException("The destination Backpack slot is not empty");
        if (!Fingerprint(json).Equals(request.Fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Backpack item changed before the host received the move");
        state.Slots[source] = "";
        state.Slots[destination] = json;
        state.Revision++;
        StageState(state);
    }

    private void RecoverInterrupted(BackpackState state, Player player)
    {
        var pending = state.Journal.LastOrDefault(entry => entry.Phase != "committed");
        if (pending is null) return;
        var inventory = GetPlayerInventorySlots(player) ?? throw new InvalidOperationException("Player inventory is not ready");

        if (pending.Operation == "deposit" && pending.Phase == "remove-authorized")
        {
            var sourceStillMatches = pending.InventorySlot >= 0 && pending.InventorySlot < inventory.Length &&
                inventory[pending.InventorySlot].ItemInstance is { } source && Fingerprint(SerializeItem(source)) == Fingerprint(pending.ItemJson);
            if (!sourceStillMatches && string.IsNullOrWhiteSpace(state.Slots[pending.BackpackSlot]))
            {
                state.Slots[pending.BackpackSlot] = pending.ItemJson;
                state.Revision++;
            }
            pending.Phase = "committed";
            StageState(state);
        }
        else if (pending.Operation == "withdraw" && pending.Phase == "backpack-removed")
        {
            var targetMatches = pending.InventorySlot >= 0 && pending.InventorySlot < inventory.Length &&
                inventory[pending.InventorySlot].ItemInstance is { } target && Fingerprint(SerializeItem(target)) == Fingerprint(pending.ItemJson);
            if (!targetMatches && string.IsNullOrWhiteSpace(state.Slots[pending.BackpackSlot]))
            {
                state.Slots[pending.BackpackSlot] = pending.ItemJson;
                state.Revision++;
            }
            pending.Phase = "committed";
            StageState(state);
        }
    }

    private BackpackJournalEntry BeginJournal(BackpackState state, BackpackMessage request, string operation, int backpackSlot, string json)
    {
        var entry = new BackpackJournalEntry
        {
            TransactionId = request.RequestId,
            Operation = operation,
            RevisionBefore = state.Revision,
            InventorySlot = request.InventorySlot,
            BackpackSlot = backpackSlot,
            ItemJson = json
        };
        state.Journal.Add(entry);
        StageState(state);
        return entry;
    }

    private void SendResult(ulong recipient, Guid requestId, bool success, string status, BackpackState? state, bool sessionVerified = true)
    {
        if (recipient == LocalSteamId())
        {
            if (state is not null) { _state = state.Clone(); StageState(_state); }
            CompleteRequest(success, status);
            return;
        }
        BackpackProtocol.Send(new BackpackMessage
        {
            Type = "snapshot", Protocol = ProtocolVersion, Recipient = recipient, RequestId = requestId,
            Success = success, SessionVerified = sessionVerified, Error = status, State = state?.Clone()
        });
    }

    private void CompleteRequest(bool success, string status)
    {
        _waitingForHost = false;
        _status = success ? status : "Not completed: " + status;
    }

    private void LoadLocalState()
    {
        var steam = LocalSteamId();
        _state = LoadWorkingState(steam, CareerId());
        _status = "Local recovery copy loaded";
    }

    private bool SendHello()
    {
        if (_state is null) return false;
        return BackpackProtocol.Send(new BackpackMessage
        {
            Type = "hello",
            Protocol = ProtocolVersion,
            RequestId = Guid.NewGuid(),
            ExpectedRevision = _state.Revision,
            State = _state.Clone()
        });
    }

    private void SyncStateWithHost()
    {
        if (_state is null || !_sessionVerified || !IsMultiplayerClient()) return;
        BackpackProtocol.Send(new BackpackMessage
        {
            Type = "sync",
            Protocol = ProtocolVersion,
            RequestId = Guid.NewGuid(),
            ExpectedRevision = _state.Revision,
            State = _state.Clone()
        });
    }

    private BackpackState ReconcileClientRecovery(ulong sender, BackpackState host, BackpackState? client)
    {
        if (client is null || client.Schema != host.Schema || client.Slots is null || client.Slots.Length != 12)
            return host;
        if (client.OwnerSteamId != sender || !string.Equals(client.CareerId, host.CareerId, StringComparison.OrdinalIgnoreCase))
            return host;
        if (client.Revision <= host.Revision) return host;

        var recovered = client.Clone();
        recovered.OwnerSteamId = sender;
        recovered.CareerId = host.CareerId;
        StageState(recovered);
        LoggerInstance.Msg($"Recovered newer Backpack revision {recovered.Revision} from Steam user {sender}; host was revision {host.Revision}.");
        return recovered;
    }

    private static string StateKey(ulong owner, string career) => $"{owner}:{career}";

    private BackpackState LoadWorkingState(ulong owner, string career)
    {
        var key = StateKey(owner, career);
        return _stagedStates.TryGetValue(key, out var staged)
            ? staged.Clone()
            : _store!.Load(owner, career, IsMultiplayerClient() ? null : LegacyCareerId());
    }

    private void StageState(BackpackState state)
    {
        var snapshot = state.Clone();
        _stagedStates[StateKey(state.OwnerSteamId, state.CareerId)] = snapshot;
        // The game save callback is still used as the cross-save checkpoint, but the
        // recovery copy must survive crashes and shutdown paths that never raise it.
        _store?.Save(snapshot);
    }

    private void EnsureSaveCheckpointHook()
    {
        if (_saveManager is not null) return;
        var manager = UnityEngine.Object.FindObjectOfType<SaveManager>();
        if (manager is null) return;
        _saveManager = manager;
        _saveCompleteAction = (System.Action)CommitStagedStates;
        manager.onSaveComplete.AddListener(_saveCompleteAction);
    }

    private void CommitStagedStates()
    {
        if (_store is null || _stagedStates.Count == 0) return;
        foreach (var state in _stagedStates.Values)
            _store.Save(state);
        _stagedStates.Clear();
        _status = "Backpack and game saved";
        LoggerInstance.Msg("Backpack checkpoint committed with the Schedule I save.");
    }

    private void TryStartBackpackSave()
    {
        if (!_backpackSaveRequested) return;
        EnsureSaveCheckpointHook();
        if (_saveManager is null || _saveManager.IsSaving) return;
        _backpackSaveRequested = false;
        _status = "Saving game and Backpack...";
        _saveManager.Save();
        LoggerInstance.Msg("Backpack closed; requested a Schedule I save.");
    }

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

    private static string CareerId()
    {
        try
        {
            var manager = UnityEngine.Object.FindObjectOfType<SaveManager>();
            if (manager is null) return "unsaved";
            var saveName = string.IsNullOrWhiteSpace(manager.SaveName) ? "unnamed" : manager.SaveName.Trim();
            var savePath = !string.IsNullOrWhiteSpace(manager.PlayersSavePath)
                ? manager.PlayersSavePath
                : manager.IndividualSavesContainerPath;
            if (string.IsNullOrWhiteSpace(savePath)) return $"{saveName}-unresolved";
            var canonical = Path.GetFullPath(savePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
            var authority = CareerAuthoritySteamId();
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{authority}|{canonical}")))[..20];
            return $"{saveName}-{digest}";
        }
        catch { return "unsaved"; }
    }

    private static string LegacyCareerId()
    {
        try
        {
            var manager = UnityEngine.Object.FindObjectOfType<SaveManager>();
            return string.IsNullOrWhiteSpace(manager?.SaveName) ? "unsaved" : manager.SaveName;
        }
        catch { return "unsaved"; }
    }

    private static ulong CareerAuthoritySteamId()
    {
        try
        {
            var lobby = Lobby.Instance;
            if (lobby is not null && lobby.IsInLobby && lobby.LobbyID != 0)
                return SteamMatchmaking.GetLobbyOwner(new CSteamID(lobby.LobbyID)).m_SteamID;
        }
        catch { }
        return LocalSteamId();
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

    private static string SerializeItem(ItemInstance item) => JsonUtility.ToJson(item.GetItemData());
    private static string Fingerprint(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static string Describe(ItemInstance? item)
    {
        if (item is null) return "Empty";
        var name = item.Definition is null ? "Unknown item" : item.Definition.Name;
        return $"{name}  ×{item.GetItemData().Quantity}";
    }

    private static string DescribeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "Empty";
        try { return Describe(ItemDeserializer.LoadItem(json)); }
        catch { return "Recovery item (unsupported by current game version)"; }
    }

    private void ParseOpenKey()
    {
        if (_openKeyEntry is not null && Enum.TryParse<KeyCode>(_openKeyEntry.Value, true, out var parsed)) _openKey = parsed;
        else
        {
            _openKey = KeyCode.B;
            if (_openKeyEntry is not null) _openKeyEntry.Value = "B";
        }
    }

    private static GUIStyle HeaderStyle()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        style.normal.textColor = new Color(0.38f, 0.84f, 0.55f);
        return style;
    }

    private static GUIStyle SectionStyle()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        style.normal.textColor = new Color(0.94f, 0.78f, 0.45f);
        return style;
    }

    private static GUIStyle StatusStyle()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        style.normal.textColor = new Color(0.91f, 0.80f, 0.60f);
        return style;
    }

    private static GUIStyle HintStyle()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        style.normal.textColor = new Color(0.78f, 0.68f, 0.52f);
        return style;
    }

    private static GUIStyle SlotNumberStyle()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
        style.normal.textColor = new Color(0.72f, 0.54f, 0.30f);
        return style;
    }

    private static GUIStyle SlotTextStyle()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        style.normal.textColor = new Color(0.96f, 0.88f, 0.70f);
        return style;
    }

    private static void TrimJournal(BackpackState state)
    {
        if (state.Journal.Count > 30) state.Journal.RemoveRange(0, state.Journal.Count - 30);
    }

    public override void OnDeinitializeMelon()
    {
        ActiveInstance = null;
        if (_saveManager is not null && _saveCompleteAction is not null)
            _saveManager.onSaveComplete.RemoveListener(_saveCompleteAction);
        _saveManager = null;
        _saveCompleteAction = null;
        RestoreSharedStorageMenu();
        BackpackProtocol.Received -= OnProtocolMessage;
        if (_menuOpen) CloseMenu();
    }
}
