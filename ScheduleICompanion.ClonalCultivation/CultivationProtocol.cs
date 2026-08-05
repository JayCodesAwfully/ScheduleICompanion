using System.Text;
using System.Text.Json;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;

namespace ScheduleICompanion.ClonalCultivation;

internal sealed class CultivationMessage
{
    public string Protocol { get; set; } = "1";
    public string Type { get; set; } = "plant";
    public Guid RequestId { get; set; }
    public ulong Recipient { get; set; }
    public string ProductId { get; set; } = "";
    public int Quality { get; set; }
    public int InventorySlot { get; set; } = -1;
    public float PotX { get; set; }
    public float PotY { get; set; }
    public float PotZ { get; set; }
    public bool Success { get; set; }
    public string Error { get; set; } = "";
}

internal static class CultivationProtocol
{
    private const string WirePrefix = "SICCU1:";
    public static event Action<ulong, bool, CultivationMessage>? Received;

    public static bool Send(CultivationMessage message)
    {
        var lobby = Lobby.Instance;
        if (lobby is null || !lobby.IsInLobby) return false;
        var json = JsonSerializer.Serialize(message);
        var wire = WirePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        lobby.SendLobbyMessage(wire);
        return true;
    }

    [HarmonyPatch(typeof(SteamLobbyService), "OnLobbyChatMessage")]
    private static class LobbyMessagePatch
    {
        private static bool Prefix(LobbyChatMsg_t result)
        {
            try
            {
                var lobbyId = new CSteamID(result.m_ulSteamIDLobby);
                var sender = new CSteamID(result.m_ulSteamIDUser);
                var buffer = new Il2CppStructArray<byte>(8192);
                var read = SteamMatchmaking.GetLobbyChatEntry(
                    lobbyId, unchecked((int)result.m_iChatID), out _, buffer, buffer.Length, out _);
                if (read <= 0) return true;
                var text = Encoding.UTF8.GetString(buffer.ToArray(), 0, read).TrimEnd('\0');
                if (!text.StartsWith(WirePrefix, StringComparison.Ordinal)) return true;
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(text[WirePrefix.Length..]));
                var message = JsonSerializer.Deserialize<CultivationMessage>(json);
                if (message is not null)
                {
                    var owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
                    Received?.Invoke(sender.m_SteamID, sender == owner, message);
                }
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
