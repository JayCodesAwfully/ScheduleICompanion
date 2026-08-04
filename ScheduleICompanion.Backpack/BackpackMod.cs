using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppFishySteamworks;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Il2CppTMPro;

namespace ScheduleICompanion.Backpack;

public sealed class BackpackMod : MelonMod
{
    private const string ProtocolVersion = "1";
    private BackpackStore? _store;
    private MelonPreferences_Entry<string>? _openKeyEntry;
    private KeyCode _openKey = KeyCode.B;
    private BackpackState? _state;
    private bool _menuOpen;
    private bool _waitingForHost;
    private bool _sessionVerified;
    private string _status = "Ready";
    private Vector2 _inventoryScroll;
    private Vector2 _backpackScroll;
    private CursorLockMode _previousCursorLock;
    private bool _previousCursorVisible;

    public override void OnInitializeMelon()
    {
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

        if (!Input.GetKeyDown(_openKey)) return;
        if (_menuOpen) CloseMenu();
        else if (CanOpen()) OpenMenu();
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
        var inventory = player.GetComponent<PlayerInventory>();
        if (inventory is null) { GUILayout.Label("Inventory is not ready."); GUILayout.EndScrollView(); GUILayout.EndVertical(); return; }
        var slots = inventory.GetAllInventorySlots();
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

    private void OpenMenu()
    {
        _previousCursorLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _menuOpen = true;
        LoadLocalState();
        _sessionVerified = !IsMultiplayerClient();

        if (IsMultiplayerClient())
        {
            _waitingForHost = true;
            _status = "Waiting for the host snapshot";
            if (!BackpackProtocol.Send(new BackpackMessage { Type = "hello", Protocol = ProtocolVersion, RequestId = Guid.NewGuid() }))
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
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
    }

    private bool CanOpen()
    {
        if (Player.Local is null) return false;
        if (Cursor.lockState != CursorLockMode.Locked) return false;
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected is not null &&
            (selected.GetComponent<InputField>() is not null || selected.GetComponent<TMP_InputField>() is not null))
            return false;
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

        if (IsHost() && message.Type is "hello" or "deposit" or "withdraw")
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
                _store?.Save(_state);
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

        var state = _store!.Load(sender, CareerId());
        RecoverInterrupted(state, player);
        if (request.Type == "hello")
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
        var playerInventory = player.GetComponent<PlayerInventory>() ?? throw new InvalidOperationException("Player inventory is not ready");
        var inventory = playerInventory.GetAllInventorySlots();
        if (request.InventorySlot < 0 || request.InventorySlot >= inventory.Count) throw new InvalidOperationException("Invalid inventory slot");
        var source = inventory[request.InventorySlot];
        var item = source.ItemInstance ?? throw new InvalidOperationException("That inventory slot is now empty");
        var json = SerializeItem(item);
        if (!Fingerprint(json).Equals(request.Fingerprint, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The source item changed before the host received it");
        var destination = Array.FindIndex(state.Slots, string.IsNullOrWhiteSpace);
        if (destination < 0) throw new InvalidOperationException("The backpack is full");

        var journal = BeginJournal(state, request, "deposit", destination, json);
        journal.Phase = "remove-authorized";
        _store!.Save(state);
        source.ClearStoredInstance(true);
        state.Slots[destination] = json;
        state.Revision++;
        journal.Phase = "committed";
        TrimJournal(state);
        _store.Save(state);
    }

    private void Withdraw(BackpackState state, Player player, BackpackMessage request)
    {
        if (request.BackpackSlot < 0 || request.BackpackSlot >= state.Slots.Length) throw new InvalidOperationException("Invalid backpack slot");
        var json = state.Slots[request.BackpackSlot];
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("That backpack slot is now empty");
        var item = ItemDeserializer.LoadItem(json) ?? throw new InvalidOperationException("The stored item could not be reconstructed and was left untouched");
        var playerInventory = player.GetComponent<PlayerInventory>() ?? throw new InvalidOperationException("Player inventory is not ready");
        var inventory = playerInventory.GetAllInventorySlots();
        var destination = -1;
        for (var i = 0; i < inventory.Count; i++)
            if (inventory[i].ItemInstance is null && !inventory[i].IsAddLocked) { destination = i; break; }
        if (destination < 0) throw new InvalidOperationException("Your inventory has no empty slot");

        var journal = BeginJournal(state, request, "withdraw", request.BackpackSlot, json);
        journal.InventorySlot = destination;
        state.Slots[request.BackpackSlot] = "";
        state.Revision++;
        journal.Phase = "backpack-removed";
        _store!.Save(state);
        inventory[destination].SetStoredItem(item, true);
        journal.Phase = "committed";
        TrimJournal(state);
        _store.Save(state);
    }

    private void RecoverInterrupted(BackpackState state, Player player)
    {
        var pending = state.Journal.LastOrDefault(entry => entry.Phase != "committed");
        if (pending is null) return;
        var playerInventory = player.GetComponent<PlayerInventory>() ?? throw new InvalidOperationException("Player inventory is not ready");
        var inventory = playerInventory.GetAllInventorySlots();

        if (pending.Operation == "deposit" && pending.Phase == "remove-authorized")
        {
            var sourceStillMatches = pending.InventorySlot >= 0 && pending.InventorySlot < inventory.Count &&
                inventory[pending.InventorySlot].ItemInstance is { } source && Fingerprint(SerializeItem(source)) == Fingerprint(pending.ItemJson);
            if (!sourceStillMatches && string.IsNullOrWhiteSpace(state.Slots[pending.BackpackSlot]))
            {
                state.Slots[pending.BackpackSlot] = pending.ItemJson;
                state.Revision++;
            }
            pending.Phase = "committed";
            _store!.Save(state);
        }
        else if (pending.Operation == "withdraw" && pending.Phase == "backpack-removed")
        {
            var targetMatches = pending.InventorySlot >= 0 && pending.InventorySlot < inventory.Count &&
                inventory[pending.InventorySlot].ItemInstance is { } target && Fingerprint(SerializeItem(target)) == Fingerprint(pending.ItemJson);
            if (!targetMatches && string.IsNullOrWhiteSpace(state.Slots[pending.BackpackSlot]))
            {
                state.Slots[pending.BackpackSlot] = pending.ItemJson;
                state.Revision++;
            }
            pending.Phase = "committed";
            _store!.Save(state);
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
        _store!.Save(state);
        return entry;
    }

    private void SendResult(ulong recipient, Guid requestId, bool success, string status, BackpackState? state, bool sessionVerified = true)
    {
        if (recipient == LocalSteamId())
        {
            if (state is not null) { _state = state.Clone(); _store?.Save(_state); }
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
        _state = _store!.Load(steam, CareerId());
        _status = "Local recovery copy loaded";
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
            return string.IsNullOrWhiteSpace(manager?.SaveName) ? "unsaved" : manager.SaveName;
        }
        catch { return "unsaved"; }
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
        style.normal.textColor = new Color(0.65f, 0.9f, 0.72f);
        return style;
    }

    private static void TrimJournal(BackpackState state)
    {
        if (state.Journal.Count > 30) state.Journal.RemoveRange(0, state.Journal.Count - 30);
    }

    public override void OnDeinitializeMelon()
    {
        BackpackProtocol.Received -= OnProtocolMessage;
        if (_menuOpen) CloseMenu();
    }
}
