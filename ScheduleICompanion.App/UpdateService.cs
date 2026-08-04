using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleICompanion.App;

internal sealed record AvailableUpdate(Version Version, string Notes, Uri Archive, Uri Checksum);

internal static class UpdateService
{
    private const string LatestRelease = "https://api.github.com/repos/JayCodesAwfully/ScheduleICompanion/releases/latest";
    private static readonly HttpClient Http = CreateClient();

    public static async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(LatestRelease, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
        if (release is null || !Version.TryParse(release.TagName.TrimStart('v', 'V'), out var latest)) return null;
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        if (latest <= current) return null;
        var archive = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !asset.Name.Contains("Setup-Windows"));
        var checksum = release.Assets.FirstOrDefault(asset => asset.Name.Equals(archive?.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        return archive is null || checksum is null ? null : new AvailableUpdate(latest, release.Body, new Uri(archive.DownloadUrl), new Uri(checksum.DownloadUrl));
    }

    public static async Task<string> DownloadAndPrepareAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ScheduleICompanion-Update-{update.Version}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "update.zip");
        var checksumText = await Http.GetStringAsync(update.Checksum, cancellationToken);
        await using (var source = await Http.GetStreamAsync(update.Archive, cancellationToken))
        await using (var target = File.Create(archive))
            await source.CopyToAsync(target, cancellationToken);
        await using (var input = File.OpenRead(archive))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
            var expected = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update checksum did not match. Nothing was installed.");
        }
        var extracted = Path.Combine(root, "extracted");
        ZipFile.ExtractToDirectory(archive, extracted);
        var installer = Directory.GetFiles(extracted, "ScheduleICompanion-Setup.exe", SearchOption.AllDirectories).SingleOrDefault();
        if (installer is null || !Directory.Exists(Path.Combine(Path.GetDirectoryName(installer)!, "Payload")))
            throw new InvalidDataException("The update package layout was invalid.");
        return installer;
    }

    public static void StartInstaller(string installer)
    {
        var gameDirectory = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName
            ?? throw new DirectoryNotFoundException("The game directory could not be resolved.");
        Process.Start(new ProcessStartInfo(installer, $"--silent-update \"{gameDirectory}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(installer)!, UseShellExecute = true
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ScheduleICompanion", "1.7"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("body")] public string Body { get; set; } = "";
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    }
}
