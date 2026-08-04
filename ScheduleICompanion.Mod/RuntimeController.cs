using System.Reflection;
using System.Runtime.Loader;
using MelonLoader;
using ScheduleICompanion.Shared;

namespace ScheduleICompanion.Mod;

internal sealed class RuntimeController : IDisposable
{
    private readonly MelonLogger.Instance _logger;
    private readonly PipeServer _server;
    private RuntimeLoadContext? _loadContext;
    private ICompanionRuntime? _runtime;
    private string _sceneName = "";

    public RuntimeController(MelonLogger.Instance logger, PipeServer server)
    {
        _logger = logger;
        _server = server;
    }

    public bool Reload()
    {
        var runtimePath = FindRuntimeAssembly();
        if (runtimePath is null)
        {
            Report("Runtime reload", "ScheduleICompanion.Runtime.dll was not found. Run BUILD-AND-INSTALL.bat once to install it.");
            return false;
        }

        RuntimeLoadContext? replacementContext = null;
        ICompanionRuntime? replacementRuntime = null;
        var hadRuntime = _runtime is not null;
        try
        {
            replacementContext = new RuntimeLoadContext();
            using var assemblyStream = new MemoryStream(File.ReadAllBytes(runtimePath));
            var pdbPath = Path.ChangeExtension(runtimePath, ".pdb");
            Assembly assembly;
            if (File.Exists(pdbPath))
            {
                using var pdbStream = new MemoryStream(File.ReadAllBytes(pdbPath));
                assembly = replacementContext.LoadFromStream(assemblyStream, pdbStream);
            }
            else
            {
                assembly = replacementContext.LoadFromStream(assemblyStream);
            }

            var type = assembly.GetType("ScheduleICompanion.Runtime.CompanionRuntime", throwOnError: true)!;
            replacementRuntime = (ICompanionRuntime)Activator.CreateInstance(type, _logger, _server)!;
            replacementRuntime.Initialize();
            if (!string.IsNullOrWhiteSpace(_sceneName))
                replacementRuntime.OnSceneLoaded(_sceneName);

            Unload();
            _loadContext = replacementContext;
            _runtime = replacementRuntime;

            Report("Runtime reload", $"Loaded {File.GetLastWriteTime(runtimePath):yyyy-MM-dd HH:mm:ss} without restarting the game.");
            return true;
        }
        catch (Exception ex)
        {
            try { replacementRuntime?.Dispose(); } catch { }
            replacementContext?.Unload();
            var prefix = hadRuntime ? "Failed; previous runtime remains active" : "Failed to start runtime";
            Report("Runtime reload", $"{prefix}: {ex.GetBaseException().Message}");
            return false;
        }
    }

    public void HandleCompanionMessage(BridgeMessage message)
    {
        if (message.Type.Equals("runtime_refresh", StringComparison.OrdinalIgnoreCase))
        {
            Reload();
            return;
        }

        _runtime?.HandleCompanionMessage(message);
    }

    public void OnSceneLoaded(string sceneName)
    {
        _sceneName = sceneName;
        _runtime?.OnSceneLoaded(sceneName);
    }

    public void Update(float now) => _runtime?.Update(now);

    private void Unload()
    {
        try { _runtime?.Dispose(); }
        catch (Exception ex) { _logger.Warning($"Runtime shutdown warning: {ex.Message}"); }
        _runtime = null;

        if (_loadContext is null)
            return;

        _loadContext.Unload();
        _loadContext = null;

        // Collect now so file/static references from the previous collectible context do not
        // linger across repeated development reloads.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string? FindRuntimeAssembly()
    {
        var gameDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(gameDirectory, "ScheduleICompanion", "Runtime", "ScheduleICompanion.Runtime.dll"),
            Path.Combine(gameDirectory, "Mods", "ScheduleICompanion.Runtime.dll"),
            Path.Combine(gameDirectory, "ScheduleICompanion.Runtime.dll")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private void Report(string name, string value)
    {
        try { _logger.Msg($"{name}: {value}"); } catch { }
        try
        {
            _server.Publish(new BridgeMessage
            {
                Type = "diagnostic",
                Payload = new DiagnosticPayload(name, value)
            });
        }
        catch { }
    }

    public void Dispose() => Unload();

    private sealed class RuntimeLoadContext : AssemblyLoadContext
    {
        public RuntimeLoadContext() : base("ScheduleICompanion.Runtime", isCollectible: true) { }

        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
