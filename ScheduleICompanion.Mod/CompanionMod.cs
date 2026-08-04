using MelonLoader;
using ScheduleICompanion.Shared;

namespace ScheduleICompanion.Mod;

public sealed class CompanionMod : MelonMod
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<BridgeMessage> _commands = new();
    private PipeServer? _server;
    private RuntimeController? _runtime;

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("Starting Schedule I Companion bridge.");

        _server = new PipeServer(LoggerInstance);
        _runtime = new RuntimeController(LoggerInstance, _server);
        _server.MessageReceived += QueueCompanionMessage;
        _server.Start();

        CompanionLauncher.TryLaunch(LoggerInstance);
        _runtime.Reload();

        _server.Publish(new BridgeMessage
        {
            Type = "diagnostic",
            Payload = new DiagnosticPayload("Bridge", "Initialized")
        });
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        _runtime?.OnSceneLoaded(sceneName);
    }

    public override void OnUpdate()
    {
        while (_commands.TryDequeue(out var message))
            _runtime?.HandleCompanionMessage(message);
        _runtime?.Update(UnityEngine.Time.unscaledTime);
    }

    private void QueueCompanionMessage(BridgeMessage message) => _commands.Enqueue(message);

    public override void OnDeinitializeMelon()
    {
        if (_server is not null)
            _server.MessageReceived -= QueueCompanionMessage;
        _runtime?.Dispose();
        _server?.Dispose();
    }
}
