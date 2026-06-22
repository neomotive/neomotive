using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neomotive.Update;

/// <summary>
/// Checks a version manifest endpoint for available updates and downloads the package zip.
/// The version manifest JSON maps "{target}-{platform}" keys to { version, url, sha256 }.
/// </summary>
public sealed class NetworkUpdateSource(string versionManifestUrl) : IUpdateSource, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<(UpdateManifest Manifest, string ZipPath)?> CheckAsync(
        string appId,
        string currentVersion,
        CancellationToken ct = default)
    {
        var key = $"{appId}-{CurrentPlatform()}";

        VersionManifestEntry? entry;
        try
        {
            var json = await _http.GetStringAsync(versionManifestUrl, ct);
            var all = JsonSerializer.Deserialize<Dictionary<string, VersionManifestEntry>>(json);
            if (all == null || !all.TryGetValue(key, out entry))
                return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }

        if (!UsbUpdateSource.IsNewer(entry.Version, currentVersion))
            return null;

        // Download the zip to a temp file
        var tmpPath = Path.Combine(Path.GetTempPath(), $"neomotive-update-{entry.Version}.zip");
        try
        {
            using var response = await _http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(tmpPath);
            await response.Content.CopyToAsync(fs, ct);
        }
        catch
        {
            TryDelete(tmpPath);
            return null;
        }

        // Verify the zip itself (whole-file hash from version manifest)
        if (!string.IsNullOrEmpty(entry.Sha256))
        {
            var actual = UpdatePackage.ComputeSha256(tmpPath);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(tmpPath);
                return null;
            }
        }

        UpdateManifest manifest;
        try
        {
            manifest = UpdatePackage.ReadManifest(tmpPath);
        }
        catch
        {
            TryDelete(tmpPath);
            return null;
        }

        return (manifest, tmpPath);
    }

    private static string CurrentPlatform() =>
        OperatingSystem.IsWindows() ? "windows" : "linux-arm64";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class VersionManifestEntry
{
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("url")]     public string Url     { get; init; } = "";
    [JsonPropertyName("sha256")]  public string Sha256  { get; init; } = "";
}
