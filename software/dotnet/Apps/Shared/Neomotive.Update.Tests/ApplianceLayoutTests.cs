using Neomotive.Update;
using Xunit;

namespace Neomotive.Update.Tests;

/// <summary>
/// Under the Pi Appliance Kit the app runs from app.service with
/// ProtectSystem=strict and ReadWritePaths=/data. Everything outside /data —
/// /tmp and $HOME included — is mounted read-only in that unit's namespace, so
/// any updater path that escapes baseDir fails on the device with a bare
/// "access denied" and shows up as a download failure with no cause.
///
/// These tests pin every working path to baseDir so that can't regress.
/// </summary>
public class ApplianceLayoutTests : IDisposable
{
    private readonly string _base = Directory.CreateTempSubdirectory("neomotive-appliance-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    private UpdateService NewService() => new("scantool", "1.0.0", _base);

    [Fact]
    public void Every_working_directory_lives_under_baseDir()
    {
        var applicator = new UpdateApplicator(_base);

        foreach (var dir in new[]
                 {
                     applicator.CurrentDir, applicator.PreviousDir, applicator.StagingDir,
                     applicator.DownloadDir, applicator.ConfigDir, applicator.DataDir
                 })
        {
            Assert.StartsWith(_base, Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Downloads_never_land_in_the_system_temp_directory()
    {
        // The regression itself: Path.GetTempPath() resolves to a read-only /tmp
        // on the appliance, so a package downloaded there could never be written.
        using var svc = NewService();

        Assert.StartsWith(_base, Path.GetFullPath(svc.DownloadDir), StringComparison.OrdinalIgnoreCase);

        // Not "is outside temp" — this test's own baseDir is a temp subdirectory.
        // The guarantee is that the download dir is derived from baseDir rather
        // than from Path.GetTempPath(), which is what the appliance forbids.
        var systemTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        Assert.NotEqual(systemTemp,
            Path.GetFullPath(svc.DownloadDir).TrimEnd(Path.DirectorySeparatorChar),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Construction_creates_the_writable_directories_the_device_needs()
    {
        using var svc = NewService();
        var applicator = new UpdateApplicator(_base);

        Assert.True(Directory.Exists(applicator.DownloadDir));
        Assert.True(Directory.Exists(applicator.ConfigDir));
        Assert.True(Directory.Exists(applicator.DataDir));
    }

    [Fact]
    public void EnsureLayout_clears_staging_left_by_an_interrupted_update()
    {
        // Staging at startup means the last attempt died between extraction and
        // the slot swap. It is never the running app, and on the appliance it is
        // a full payload copy sitting on the same partition as both slots.
        var applicator = new UpdateApplicator(_base);
        Directory.CreateDirectory(Path.Combine(applicator.StagingDir, "app"));
        File.WriteAllText(Path.Combine(applicator.StagingDir, "app", "scantool"), "half-extracted");

        applicator.EnsureLayout();

        Assert.False(Directory.Exists(applicator.StagingDir));
    }

    [Fact]
    public void EnsureLayout_clears_a_stale_download_but_keeps_unrelated_files()
    {
        var applicator = new UpdateApplicator(_base);
        applicator.EnsureLayout();

        var stale = Path.Combine(applicator.DownloadDir, "neomotive-update-1.0.1.zip");
        var keep = Path.Combine(applicator.DownloadDir, "notes.txt");
        File.WriteAllText(stale, "abandoned");
        File.WriteAllText(keep, "not ours");

        applicator.EnsureLayout();

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void EnsureLayout_is_idempotent()
    {
        var applicator = new UpdateApplicator(_base);
        applicator.EnsureLayout();
        applicator.EnsureLayout();

        Assert.True(Directory.Exists(applicator.DownloadDir));
    }

    [Fact]
    public void EnsureLayout_leaves_the_running_slot_and_its_rollback_alone()
    {
        var applicator = new UpdateApplicator(_base);
        Directory.CreateDirectory(applicator.CurrentDir);
        Directory.CreateDirectory(applicator.PreviousDir);
        File.WriteAllText(Path.Combine(applicator.CurrentDir, "scantool"), "running");
        File.WriteAllText(Path.Combine(applicator.PreviousDir, "scantool"), "rollback");

        applicator.EnsureLayout();

        Assert.Equal("running", File.ReadAllText(Path.Combine(applicator.CurrentDir, "scantool")));
        Assert.Equal("rollback", File.ReadAllText(Path.Combine(applicator.PreviousDir, "scantool")));
    }

    [Fact]
    public void FreeSpaceBytes_reports_the_volume_holding_baseDir()
    {
        var applicator = new UpdateApplicator(_base);

        var free = applicator.FreeSpaceBytes();

        Assert.NotNull(free);
        Assert.True(free > 0);
    }

    [Fact]
    public void Data_directory_survives_a_slot_promotion()
    {
        // User settings (UDS config, input mappings) live beside the slots, not
        // inside them — an update that wiped them would be a data-loss bug.
        var applicator = new UpdateApplicator(_base);
        applicator.EnsureLayout();

        var settings = Path.Combine(applicator.DataDir, "neoteric.config.json");
        File.WriteAllText(settings, "{\"vin\":\"AWWWWWWWWWWW0YEAH\"}");

        Directory.CreateDirectory(Path.Combine(applicator.StagingDir, "app"));
        File.WriteAllText(Path.Combine(applicator.StagingDir, "app", "scantool"), "new binary");

        applicator.Apply(new UpdateManifest { Version = "1.1.0", Target = "scantool", Platform = "linux-arm64" });

        Assert.True(File.Exists(settings));
        Assert.Contains("AWWWWWWWWWWW0YEAH", File.ReadAllText(settings));
    }
}
