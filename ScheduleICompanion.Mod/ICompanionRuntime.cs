using ScheduleICompanion.Shared;

namespace ScheduleICompanion.Mod;

public interface ICompanionRuntime : IDisposable
{
    void Initialize();
    void HandleCompanionMessage(BridgeMessage message);
    void OnSceneLoaded(string sceneName);
    void Update(float now);
}
