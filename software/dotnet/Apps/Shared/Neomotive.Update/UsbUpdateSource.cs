namespace Neomotive.Update;

/// <summary>
/// Scans removable drives (Windows) or /media/ mount points (Linux) for a Neomotive update package.
/// Called on a timer; the caller decides the polling interval.
/// </summary>
public sealed class UsbUpdateSource : IUpdateSource
{
    private static readonly string[] SearchPatterns = ["neomotive-update*.zip"];
    private const string SubfolderName = "NEOMOTIVE";

    public Task<(UpdateManifest Manifest, string ZipPath)?> CheckAsync(
        string appId,
        string currentVersion,
        CancellationToken ct = default)
    {
        foreach (var zipPath in FindZips())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var manifest = UpdatePackage.ReadManifest(zipPath);
                if (!IsMatch(manifest, appId))
                    continue;
                if (!IsNewer(manifest.Version, currentVersion))
                    continue;

                return Task.FromResult<(UpdateManifest, string)?>(( manifest, zipPath ));
            }
            catch
            {
                // Malformed zip / bad manifest — skip this drive
            }
        }

        return Task.FromResult<(UpdateManifest, string)?>(null);
    }

    private static IEnumerable<string> FindZips()
    {
        IEnumerable<string> roots = OperatingSystem.IsWindows()
            ? GetWindowsRemovableRoots()
            : GetLinuxMediaRoots();

        foreach (var root in roots)
        {
            foreach (var pattern in SearchPatterns)
            {
                foreach (var zip in SafeEnumerateFiles(root, pattern))
                    yield return zip;

                // Also check a NEOMOTIVE/ subfolder
                var subfolder = Path.Combine(root, SubfolderName);
                if (Directory.Exists(subfolder))
                    foreach (var zip in SafeEnumerateFiles(subfolder, pattern))
                        yield return zip;
            }
        }
    }

    private static IEnumerable<string> GetWindowsRemovableRoots()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Removable && drive.IsReady)
                yield return drive.RootDirectory.FullName;
        }
    }

    private static IEnumerable<string> GetLinuxMediaRoots()
    {
        // /media/{user}/* or /media/*
        var mediaBase = "/media";
        if (!Directory.Exists(mediaBase))
            yield break;

        foreach (var dir in SafeEnumerateDirectories(mediaBase))
        {
            // Try one level deep (user subdirs like /media/pi/)
            var subs = SafeEnumerateDirectories(dir).ToList();
            if (subs.Count > 0)
                foreach (var sub in subs)
                    yield return sub;
            else
                yield return dir;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return []; }
    }

    private static bool IsMatch(UpdateManifest manifest, string appId)
    {
        return string.Equals(manifest.Target, appId, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(manifest.Platform, "any", StringComparison.OrdinalIgnoreCase)
                || string.Equals(manifest.Platform, CurrentPlatform(), StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsNewer(string candidate, string current)
    {
        return Version.TryParse(candidate, out var c)
            && Version.TryParse(current, out var cur)
            && c > cur;
    }

    private static string CurrentPlatform() =>
        OperatingSystem.IsWindows() ? "windows" : "linux-arm64";
}
