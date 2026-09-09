using Neomotive.Update;
using Xunit;

namespace Neomotive.Update.Tests;

/// <summary>
/// Configuration and version reporting. A device that reports the wrong running
/// version, or silently points at no server, looks identical to one that is
/// simply up to date.
/// </summary>
public class UpdateServiceConfigurationTests
{
    private static UpdateService NewService(string version = "1.0.0")
        => new("scantool", version, Path.Combine(Path.GetTempPath(), "neomotive-tests"));

    [Fact]
    public void Unconfigured_service_reports_the_github_manifest()
    {
        using var svc = NewService();
        Assert.Equal(UpdateService.DefaultManifestUrl, svc.ManifestUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Configure_with_nothing_falls_back_to_the_github_manifest(string? url)
    {
        using var svc = NewService();
        svc.Configure(url);
        Assert.Equal(UpdateService.DefaultManifestUrl, svc.ManifestUrl);
    }

    [Fact]
    public void Configure_with_a_url_overrides_the_default()
    {
        using var svc = NewService();
        svc.Configure("http://192.168.1.50:8080/version-manifest.json");
        Assert.Equal("http://192.168.1.50:8080/version-manifest.json", svc.ManifestUrl);
    }

    [Fact]
    public void Configure_trims_surrounding_whitespace()
    {
        using var svc = NewService();
        svc.Configure("  http://host/version-manifest.json  ");
        Assert.Equal("http://host/version-manifest.json", svc.ManifestUrl);
    }

    [Fact]
    public void DefaultManifestUrl_points_at_the_rolling_release_asset()
    {
        // The release workflow republishes this exact asset on every tag. If the
        // constant and the workflow's MANIFEST_RELEASE_TAG ever disagree, devices
        // poll a URL nothing writes to and never see another update.
        Assert.Contains("/releases/download/updates-latest/version-manifest.json",
            UpdateService.DefaultManifestUrl);
        Assert.StartsWith("https://", UpdateService.DefaultManifestUrl);
    }

    // ── Version reporting ─────────────────────────────────────────────────────

    [Fact]
    public void CurrentVersion_is_normalized_for_display()
    {
        // What the assembly actually hands us in a CI build.
        using var svc = NewService("1.1.1+9f2c1ab");
        Assert.Equal("1.1.1", svc.CurrentVersion);
    }

    [Fact]
    public void CurrentVersion_passes_a_clean_version_through()
    {
        using var svc = NewService("1.1.1");
        Assert.Equal("1.1.1", svc.CurrentVersion);
    }
}
