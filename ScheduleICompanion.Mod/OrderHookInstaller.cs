using MelonLoader;
using ScheduleICompanion.Shared;

namespace ScheduleICompanion.Mod;

public static class OrderHookInstaller
{
    public static void ReportSafeMode(MelonLogger.Instance logger, PipeServer server)
    {
        const string message = "Direct order hook is disabled in safe mode until exact game classes are supplied.";
        logger.Warning(message);
        server.Publish(new BridgeMessage
        {
            Type = "diagnostic",
            Payload = new DiagnosticPayload("Orders", message)
        });
    }
}
