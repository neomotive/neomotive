using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Neomotive.Update;

public static class UpdatePackage
{
    public static UpdateManifest ReadManifest(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("update.json")
            ?? throw new InvalidDataException("update.json not found in package");

        using var stream = entry.Open();
        return JsonSerializer.Deserialize<UpdateManifest>(stream)
            ?? throw new InvalidDataException("update.json could not be deserialized");
    }

    /// <summary>
    /// Extracts the zip to stagingDir and verifies SHA256 hashes for every file listed in the manifest.
    /// Cleans staging on any failure so the caller's current slot is never touched.
    /// </summary>
    public static void ExtractAndVerify(string zipPath, UpdateManifest manifest, string stagingDir)
    {
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, stagingDir);
            VerifyHashes(manifest, stagingDir);
        }
        catch
        {
            TryClean(stagingDir);
            throw;
        }
    }

    private static void VerifyHashes(UpdateManifest manifest, string stagingDir)
    {
        foreach (var entry in manifest.Files)
        {
            // Directory entries end with '/' — skip, directories have no hash
            if (entry.Path.EndsWith('/'))
                continue;

            var fullPath = Path.Combine(stagingDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Expected file missing after extraction: {entry.Path}");

            var actual = ComputeSha256(fullPath);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"SHA256 mismatch for {entry.Path}: expected {entry.Sha256}, got {actual}");
        }
    }

    public static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryClean(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
