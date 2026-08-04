using MelonLoader;
using ScheduleICompanion.Shared;

namespace ScheduleICompanion.Mod;

public static class NotificationHookInstaller
{
    public static void ReportSafeMode(MelonLogger.Instance logger, PipeServer server)
    {
        const string message = "Notification hook is disabled in safe mode to prevent IL2CPP reflection crashes.";
        logger.Warning(message);
        server.Publish(new BridgeMessage
        {
            Type = "diagnostic",
            Payload = new DiagnosticPayload("Notifications", message)
        });
    }
}
