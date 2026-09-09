using Neomotive.Update;
using Xunit;

namespace Neomotive.Update.Tests;

/// <summary>
/// The version gate every update passes through. Both sources call
/// <see cref="UsbUpdateSource.IsNewer"/> and refuse anything it rejects, so a
/// false here is indistinguishable from "no update published" on the device.
/// </summary>
public class VersionComparisonTests
{
    // ── Normalize ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2")]
    [InlineData("  1.2.3  ", "1.2.3")]
    public void Normalize_leaves_a_plain_version_alone(string input, string expected)
        => Assert.Equal(expected, UsbUpdateSource.Normalize(input));

    [Theory]
    [InlineData("1.2.3+abc1234", "1.2.3")]
    [InlineData("1.2.3+abc1234.dirty", "1.2.3")]
    public void Normalize_strips_build_metadata(string input, string expected)
        => Assert.Equal(expected, UsbUpdateSource.Normalize(input));

    [Theory]
    [InlineData("1.2.3-beta", "1.2.3")]
    [InlineData("1.2.3-rc.1+abc1234", "1.2.3")]
    public void Normalize_strips_prerelease_tags(string input, string expected)
        => Assert.Equal(expected, UsbUpdateSource.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_maps_nothing_to_empty(string? input)
        => Assert.Equal(string.Empty, UsbUpdateSource.Normalize(input!));

    // ── IsNewer ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.1.0", "1.0.0")]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("2.0.0", "1.9.9")]
    [InlineData("1.3.1", "1.2")]      // the shape the simulator actually shipped
    public void IsNewer_accepts_a_higher_version(string candidate, string current)
        => Assert.True(UsbUpdateSource.IsNewer(candidate, current));

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.2", "1.2.0")]      // Version treats these as equal — not an update
    public void IsNewer_rejects_equal_or_lower(string candidate, string current)
        => Assert.False(UsbUpdateSource.IsNewer(candidate, current));

    /// <summary>
    /// Regression: the running version comes from AssemblyInformationalVersion, and
    /// .NET 8+ appends "+&lt;git sha&gt;" for any build inside a git repo — which is
    /// every CI build. Feeding that straight to Version.TryParse failed, so the
    /// device refused every update it was ever offered.
    /// </summary>
    [Theory]
    [InlineData("1.1.1", "1.1.0+9f2c1ab")]
    [InlineData("1.1.1+deadbee", "1.1.0+9f2c1ab")]
    [InlineData("1.1.1+deadbee", "1.1.0")]
    public void IsNewer_sees_through_git_sha_suffixes(string candidate, string current)
        => Assert.True(UsbUpdateSource.IsNewer(candidate, current));

    [Fact]
    public void IsNewer_still_rejects_an_older_version_carrying_a_sha()
        => Assert.False(UsbUpdateSource.IsNewer("1.0.0+deadbee", "1.1.0+9f2c1ab"));

    [Theory]
    [InlineData("not-a-version", "1.0.0")]
    [InlineData("1.0.0", "not-a-version")]
    [InlineData("", "1.0.0")]
    public void IsNewer_rejects_what_it_cannot_parse(string candidate, string current)
        => Assert.False(UsbUpdateSource.IsNewer(candidate, current));
}
