using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neomotive.Update;

/// <summary>
/// Checks a version manifest endpoint for available updates and downloads the package zip.
/// The version manifest JSON maps "{target}-{platform}" keys to { version, url, sha256 }.
/// </summary>
/// <param name="downloadDir">
/// Where the package lands. Must be caller-supplied rather than
/// <c>Path.GetTempPath()</c>: under the Pi Appliance Kit the app runs with
/// ProtectSystem=strict and ReadWritePaths=/data, so /tmp is mounted read-only
/// and every download failed with "Access to the path '/tmp/...' is denied" —
/// which the Updates screen showed as a download failure with no hint why.
/// </param>
public sealed class NetworkUpdateSource(string versionManifestUrl, string downloadDir)
    : IUpdateSource, IDisposable
{
    // A 30s ceiling on the whole request is fine for the small JSON manifest but
    // not for a ~100 MB self-contained payload over a Pi's Wi-Fi, where the
    // timeout fired mid-download and looked like a network fault.
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(20);

    public async Task<(UpdateManifest Manifest, string ZipPath)?> CheckAsync(
        string appId,
        string currentVersion,
        CancellationToken ct = default)
    {
        var key = $"{appId}-{CurrentPlatform()}";

        // A swallowed failure here would surface to the operator as "You are up to
        // date", which is the one thing it definitely does not mean. Let these
        // throw: UpdateService turns them into a Failed result with the reason.
        VersionManifestEntry? entry;
        using var manifestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        manifestCts.CancelAfter(ManifestTimeout);
        try
        {
            var json = await _http.GetStringAsync(versionManifestUrl, manifestCts.Token);
            var all = JsonSerializer.Deserialize<Dictionary<string, VersionManifestEntry>>(json);
            if (all == null || !all.TryGetValue(key, out entry))
                return null;   // Genuinely nothing published for this device.
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Could not reach the update server: {ex.Message}", ex);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Update server timed out.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Update server returned malformed JSON: {ex.Message}", ex);
        }

        if (!UsbUpdateSource.IsNewer(entry.Version, currentVersion))
            return null;

        // Download into the caller's writable scratch dir — see downloadDir above.
        var tmpPath = Path.Combine(downloadDir, $"neomotive-update-{entry.Version}.zip");
        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        downloadCts.CancelAfter(DownloadTimeout);
        try
        {
            Directory.CreateDirectory(downloadDir);

            using var response = await _http.GetAsync(
                entry.Url, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token);
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(tmpPath);
            await response.Content.CopyToAsync(fs, downloadCts.Token);
        }
        catch (UnauthorizedAccessException ex)
        {
            // The appliance signature: baseDir is not writable, so nothing about
            // this update could ever work. Say which path, not just "denied".
            throw new InvalidOperationException(
                $"Cannot write the download to {downloadDir}: {ex.Message}", ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryDelete(tmpPath);
            throw new InvalidOperationException($"Download of v{entry.Version} timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(tmpPath);
            throw new InvalidOperationException($"Download of v{entry.Version} failed: {ex.Message}", ex);
        }

        // Verify the zip itself (whole-file hash from version manifest)
        if (!string.IsNullOrEmpty(entry.Sha256))
        {
            var actual = UpdatePackage.ComputeSha256(tmpPath);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(tmpPath);
                throw new InvalidOperationException(
                    $"SHA256 mismatch on the v{entry.Version} download — the package was not applied.");
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
