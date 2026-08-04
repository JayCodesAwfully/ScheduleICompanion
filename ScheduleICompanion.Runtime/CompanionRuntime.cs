using MelonLoader;
using ScheduleICompanion.Mod;
using ScheduleICompanion.Shared;
using UnityEngine;

namespace ScheduleICompanion.Runtime;

public sealed class CompanionRuntime : ICompanionRuntime
{
    private readonly PipeServer _server;
    private readonly GameProbe _probe;
    private float _nextPositionSend;

    public CompanionRuntime(MelonLogger.Instance logger, PipeServer server)
    {
        _server = server;
        _probe = new GameProbe(logger, server);
    }

    public void Initialize()
    {
        _probe.Discover();
        _server.Publish(new BridgeMessage
        {
            Type = "diagnostic",
            Payload = new DiagnosticPayload("Runtime", "Reloadable game probe initialized")
        });
    }

    public void OnSceneLoaded(string sceneName) => _probe.OnSceneLoaded(sceneName);

    public void HandleCompanionMessage(BridgeMessage message)
    {
        if (!message.Type.Equals("devtool", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize<DevToolCommandPayload>(
                System.Text.Json.JsonSerializer.Serialize(message.Payload));
            if (payload is not null) _probe.HandleDevToolCommand(payload);
        }
        catch { }
    }

    public void Update(float now)
    {
        _probe.Tick(now);

        if (now >= _nextPositionSend)
        {
            _nextPositionSend = now + 0.20f;
            _probe.PublishPlayerPosition();
        }
    }

    public void Dispose()
    {
        _probe.Dispose();
    }
}
