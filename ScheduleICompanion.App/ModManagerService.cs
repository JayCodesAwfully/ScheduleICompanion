using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

namespace ScheduleICompanion.App;

public sealed class ModCatalog
{
    public int Schema { get; set; } = 1;
    public List<ManagedModDefinition> Mods { get; set; } = new();
}

public sealed class ManagedModDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string DllName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public bool Experimental { get; set; }
    public string MultiplayerPolicy { get; set; } = "optional";
}

public sealed class ManagedModRow
{
    public required ManagedModDefinition Definition { get; init; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Version => "v" + Definition.Version;
    public string Description => Definition.Description;
    public string Badge => Definition.Experimental ? "EXPERIMENTAL" : "STABLE";
    public bool Enabled { get; set; }
    public bool Current { get; set; }
    public string State => !Enabled ? "Disabled" : Current ? "Enabled" : "Enabled · update available";
    public string Action => !Enabled ? "Enable" : Current ? "Disable" : "Update";
}

public sealed class ModManagerService
{
    private readonly string _baseDirectory;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ModManagerService(string baseDirectory) => _baseDirectory = baseDirectory;

    public string? FindGameDirectory()
    {
        var current = new DirectoryInfo(_baseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Schedule I.exe"))) return current.FullName;
            current = current.Parent;
        }
        var common = @"C:\Program Files (x86)\Steam\steamapps\common\Schedule I";
        return File.Exists(Path.Combine(common, "Schedule I.exe")) ? common : null;
    }

    public async Task<IReadOnlyList<ManagedModRow>> LoadAsync(string? remoteCatalogUrl, CancellationToken cancellationToken)
    {
        ModCatalog? catalog = null;
        if (Uri.TryCreate(remoteCatalogUrl, UriKind.Absolute, out var remote) && remote.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                var json = await Http.GetStringAsync(remote, cancellationToken);
                catalog = JsonSerializer.Deserialize<ModCatalog>(json, JsonOptions);
            }
            catch { }
        }
        if (catalog is null)
        {
            var path = Path.Combine(_baseDirectory, "ModPackages", "catalog.json");
            if (!File.Exists(path)) return Array.Empty<ManagedModRow>();
            catalog = JsonSerializer.Deserialize<ModCatalog>(File.ReadAllText(path), JsonOptions);
        }

        var game = FindGameDirectory();
        return (catalog?.Mods ?? new()).Select(definition =>
        {
            var installed = game is null ? null : Path.Combine(game, "Mods", definition.DllName);
            var enabled = installed is not null && File.Exists(installed);
            var current = enabled && !string.IsNullOrWhiteSpace(definition.Sha256) &&
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installed!)))
                    .Equals(definition.Sha256, StringComparison.OrdinalIgnoreCase);
            return new ManagedModRow { Definition = definition, Enabled = enabled, Current = current };
        }).ToArray();
    }

    public async Task SetEnabledAsync(ManagedModDefinition definition, bool enabled, CancellationToken cancellationToken)
    {
        ValidateDefinition(definition);
        var game = FindGameDirectory() ?? throw new DirectoryNotFoundException("Schedule I could not be located.");
        if (Process.GetProcessesByName("Schedule I").Length > 0)
            throw new InvalidOperationException("Close Schedule I before enabling or disabling mods. Backpack data will remain untouched.");

        var mods = Path.Combine(game, "Mods");
        var target = Path.Combine(mods, definition.DllName);
        Directory.CreateDirectory(mods);
        if (!enabled)
        {
            if (File.Exists(target)) File.Delete(target);
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"sicmod-{Guid.NewGuid():N}.dll");
        try
        {
            if (definition.DownloadUrl.StartsWith("bundled:", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = definition.DownloadUrl["bundled:".Length..];
                var source = Path.Combine(_baseDirectory, "ModPackages", fileName);
                if (!File.Exists(source)) throw new FileNotFoundException("The bundled mod package is missing.", source);
                File.Copy(source, temp, true);
            }
            else
            {
                if (!Uri.TryCreate(definition.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("Mods must use a bundled package or an HTTPS download URL.");
                using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 50 * 1024 * 1024)
                    throw new InvalidDataException("The mod package exceeds the 50 MB safety limit.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(temp);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > 50 * 1024 * 1024) throw new InvalidDataException("The mod package exceeds the 50 MB safety limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            string actual;
            await using (var stream = File.OpenRead(temp))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (string.IsNullOrWhiteSpace(definition.Sha256) || !actual.Equals(definition.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded mod failed SHA-256 verification and was not installed.");
            File.Copy(temp, target, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static void ValidateDefinition(ManagedModDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.DllName))
            throw new InvalidDataException("The mod catalogue contains an incomplete entry.");
        if (!definition.DllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(definition.DllName).Equals(definition.DllName, StringComparison.Ordinal))
            throw new InvalidDataException("The mod catalogue contains an unsafe DLL path.");
        if (definition.DownloadUrl.StartsWith("bundled:", StringComparison.OrdinalIgnoreCase))
        {
            var bundled = definition.DownloadUrl["bundled:".Length..];
            if (!Path.GetFileName(bundled).Equals(bundled, StringComparison.Ordinal))
                throw new InvalidDataException("The mod catalogue contains an unsafe bundled path.");
        }
    }
}
