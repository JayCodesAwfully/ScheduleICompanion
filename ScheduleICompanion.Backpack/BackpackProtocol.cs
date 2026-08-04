using System.Text;
using System.Text.Json;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;

namespace ScheduleICompanion.Backpack;

internal sealed class BackpackMessage
{
    public string Type { get; set; } = "";
    public string Protocol { get; set; } = "1";
    public Guid RequestId { get; set; }
    public ulong Recipient { get; set; }
    public long ExpectedRevision { get; set; }
    public int InventorySlot { get; set; } = -1;
    public int BackpackSlot { get; set; } = -1;
    public string Fingerprint { get; set; } = "";
    public BackpackState? State { get; set; }
    public bool Success { get; set; }
    public bool SessionVerified { get; set; }
    public string Error { get; set; } = "";
}

internal static class BackpackProtocol
{
    private const string WirePrefix = "SICBP1:";
    private const int ChunkSize = 2400;
    private static readonly Dictionary<string, ChunkAccumulator> PendingChunks = new();
    public static event Action<ulong, bool, BackpackMessage>? Received;

    public static bool Send(BackpackMessage message)
    {
        var lobby = Lobby.Instance;
        if (lobby is null || !lobby.IsInLobby) return false;
        var json = JsonSerializer.Serialize(message);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var messageId = Guid.NewGuid().ToString("N");
        var count = Math.Max(1, (int)Math.Ceiling(payload.Length / (double)ChunkSize));
        for (var index = 0; index < count; index++)
        {
            var offset = index * ChunkSize;
            var length = Math.Min(ChunkSize, payload.Length - offset);
            lobby.SendLobbyMessage($"{WirePrefix}{messageId}:{index}:{count}:{payload.Substring(offset, length)}");
        }
        return true;
    }

    [HarmonyPatch(typeof(Lobby), "OnLobbyChatMessage")]
    private static class LobbyMessagePatch
    {
        private static bool Prefix(LobbyChatMsg_t result)
        {
            try
            {
                var lobbyId = new CSteamID(result.m_ulSteamIDLobby);
                var sender = new CSteamID(result.m_ulSteamIDUser);
                var buffer = new Il2CppStructArray<byte>(4096);
                var read = SteamMatchmaking.GetLobbyChatEntry(
                    lobbyId, unchecked((int)result.m_iChatID), out _, buffer, buffer.Length, out _);
                if (read <= 0) return true;
                var text = Encoding.UTF8.GetString(buffer.ToArray(), 0, read).TrimEnd('\0');
                if (!text.StartsWith(WirePrefix, StringComparison.Ordinal)) return true;
                var envelope = text[WirePrefix.Length..].Split(new[] { ':' }, 4);
                if (envelope.Length != 4 || !int.TryParse(envelope[1], out var index) ||
                    !int.TryParse(envelope[2], out var count) || count < 1 || count > 64 || index < 0 || index >= count)
                    return false;
                var key = $"{sender.m_SteamID}:{envelope[0]}";
                if (!PendingChunks.TryGetValue(key, out var accumulator) || accumulator.Parts.Length != count)
                    PendingChunks[key] = accumulator = new ChunkAccumulator(count);
                accumulator.Parts[index] = envelope[3];
                accumulator.UpdatedAt = DateTime.UtcNow;
                foreach (var expired in PendingChunks.Where(pair => DateTime.UtcNow - pair.Value.UpdatedAt > TimeSpan.FromMinutes(1)).Select(pair => pair.Key).ToArray())
                    PendingChunks.Remove(expired);
                if (accumulator.Parts.Any(part => part is null)) return false;
                PendingChunks.Remove(key);
                var payload = string.Concat(accumulator.Parts);
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var message = JsonSerializer.Deserialize<BackpackMessage>(json);
                if (message is not null)
                {
                    var lobbyOwner = SteamMatchmaking.GetLobbyOwner(lobbyId);
                    Received?.Invoke(sender.m_SteamID, sender == lobbyOwner, message);
                }
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    private sealed class ChunkAccumulator
    {
        public ChunkAccumulator(int count) => Parts = new string?[count];
        public string?[] Parts { get; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
