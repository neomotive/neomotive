using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Neomotive.Update;
using Xunit;

namespace Neomotive.Update.Tests;

/// <summary>
/// The hash gate. This is what stands between a truncated or tampered download and
/// the running slot, so its failure modes matter more than its success path.
/// </summary>
public class UpdatePackageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("neomotive-pkg-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string BuildPackage(
        string name,
        IDictionary<string, string> files,
        string version = "1.1.0",
        string target = "scantool",
        string platform = "linux-arm64",
        Func<string, string>? corruptHash = null)
    {
        var zipPath = Path.Combine(_dir, name);
        var entries = new List<UpdateFileEntry>();

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (path, content) in files)
            {
                var entry = zip.CreateEntry(path);
                using (var s = entry.Open())
                    s.Write(Encoding.UTF8.GetBytes(content));

                var hash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content)))
                    .ToLowerInvariant();

                entries.Add(new UpdateFileEntry { Path = path, Sha256 = corruptHash?.Invoke(hash) ?? hash });
            }

            var manifest = new UpdateManifest
            {
                Version = version,
                Target = target,
                Platform = platform,
                Files = entries
            };

            var json = zip.CreateEntry("update.json");
            using var js = json.Open();
            js.Write(JsonSerializer.SerializeToUtf8Bytes(manifest));
        }

        return zipPath;
    }

    [Fact]
    public void ReadManifest_round_trips_the_fields_the_device_matches_on()
    {
        var zip = BuildPackage("pkg.zip", new Dictionary<string, string> { ["app/scantool"] = "binary" });

        var manifest = UpdatePackage.ReadManifest(zip);

        Assert.Equal("1.1.0", manifest.Version);
        Assert.Equal("scantool", manifest.Target);
        Assert.Equal("linux-arm64", manifest.Platform);
        Assert.Single(manifest.Files);
    }

    [Fact]
    public void ReadManifest_rejects_a_zip_with_no_manifest()
    {
        var zipPath = Path.Combine(_dir, "empty.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            zip.CreateEntry("app/scantool");

        Assert.Throws<InvalidDataException>(() => UpdatePackage.ReadManifest(zipPath));
    }

    [Fact]
    public void ExtractAndVerify_accepts_a_package_whose_hashes_match()
    {
        var zip = BuildPackage("good.zip", new Dictionary<string, string>
        {
            ["app/scantool"] = "binary contents",
            ["app/appsettings.json"] = "{}"
        });
        var staging = Path.Combine(_dir, "staging");

        UpdatePackage.ExtractAndVerify(zip, UpdatePackage.ReadManifest(zip), staging);

        Assert.True(File.Exists(Path.Combine(staging, "app", "scantool")));
        Assert.Equal("binary contents", File.ReadAllText(Path.Combine(staging, "app", "scantool")));
    }

    [Fact]
    public void ExtractAndVerify_throws_and_clears_staging_on_a_hash_mismatch()
    {
        // Every listed hash is wrong — the shape a tampered or truncated payload takes.
        var zip = BuildPackage("bad.zip",
            new Dictionary<string, string> { ["app/scantool"] = "binary contents" },
            corruptHash: _ => new string('0', 64));
        var staging = Path.Combine(_dir, "staging-bad");

        Assert.Throws<InvalidDataException>(() =>
            UpdatePackage.ExtractAndVerify(zip, UpdatePackage.ReadManifest(zip), staging));

        // Staging must not survive: the applicator promotes whatever is in it.
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void ExtractAndVerify_throws_when_a_listed_file_is_absent_from_the_zip()
    {
        var zip = BuildPackage("short.zip", new Dictionary<string, string> { ["app/scantool"] = "x" });

        var manifest = UpdatePackage.ReadManifest(zip);
        var withExtra = new UpdateManifest
        {
            Version = manifest.Version,
            Target = manifest.Target,
            Platform = manifest.Platform,
            Files = [.. manifest.Files, new UpdateFileEntry { Path = "app/missing.dll", Sha256 = new string('a', 64) }]
        };

        Assert.Throws<FileNotFoundException>(() =>
            UpdatePackage.ExtractAndVerify(zip, withExtra, Path.Combine(_dir, "staging-short")));
    }

    [Fact]
    public void ComputeSha256_is_lowercase_hex_and_content_addressed()
    {
        var a = Path.Combine(_dir, "a.txt");
        var b = Path.Combine(_dir, "b.txt");
        File.WriteAllText(a, "same");
        File.WriteAllText(b, "same");

        var hash = UpdatePackage.ComputeSha256(a);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.Equal(hash, UpdatePackage.ComputeSha256(b));

        File.WriteAllText(b, "different");
        Assert.NotEqual(hash, UpdatePackage.ComputeSha256(b));
    }
}
