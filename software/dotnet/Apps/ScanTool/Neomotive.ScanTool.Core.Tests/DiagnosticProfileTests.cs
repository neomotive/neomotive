using System.Text.Json;
using Meadow.Foundation.Telematics.J1979;
using Neomotive.ScanTool.Core.Capture;
using Neomotive.ScanTool.Core.Diagnostics;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class DiagnosticProfileLibraryTests
{
    [Fact]
    public void Profile_keys_are_unique()
    {
        var keys = DiagnosticProfileLibrary.BuiltIn.Select(p => p.Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_profile_is_named_categorised_and_explained()
    {
        Assert.All(DiagnosticProfileLibrary.BuiltIn, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Key));
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Category));

            // The description is what tells an operator which profile to reach for.
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.NotEmpty(p.Signals);
        });
    }

    [Fact]
    public void Every_referenced_signal_exists_in_the_pid_registry()
    {
        var known = PidRegistry.CommonPids.Select(d => d.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Built-in profiles must not reference signals the tool cannot actually capture; a
        // user-authored profile may, and is tolerated at runtime, but the shipped set should not.
        foreach (var profile in DiagnosticProfileLibrary.BuiltIn)
        {
            foreach (var signal in profile.Signals)
            {
                Assert.True(known.Contains(signal),
                    $"Profile '{profile.Key}' references unknown signal '{signal}'.");
            }
        }
    }

    [Fact]
    public void Threshold_triggers_reference_a_signal_the_profile_captures()
    {
        foreach (var profile in DiagnosticProfileLibrary.BuiltIn
            .Where(p => p.Trigger.Mode == ProfileTriggerMode.Threshold))
        {
            Assert.Contains(profile.Trigger.Signal, profile.Signals);
        }
    }

    [Fact]
    public void Stop_conditions_reference_a_signal_the_profile_captures()
    {
        foreach (var profile in DiagnosticProfileLibrary.BuiltIn.Where(p => p.Stop.Enabled))
        {
            Assert.Contains(profile.Stop.Signal, profile.Signals);
        }
    }

    [Fact]
    public void Windows_are_sane()
    {
        Assert.All(DiagnosticProfileLibrary.BuiltIn, p =>
        {
            Assert.True(p.PreTriggerSeconds >= 0);
            Assert.True(p.MaxDurationSeconds > 0);
        });
    }

    [Fact]
    public void The_library_spans_more_than_one_domain()
    {
        // The tool is general-purpose; a library collapsed onto a single category would be a
        // regression towards the one scenario it was first built for.
        var categories = DiagnosticProfileLibrary.BuiltIn
            .Select(p => p.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(categories.Length >= 5, $"Only {categories.Length} categories.");
        Assert.Contains("General", categories);
    }

    [Fact]
    public void A_freeform_manual_profile_is_available_as_a_fallback()
    {
        var manual = DiagnosticProfileLibrary.BuiltIn.Single(p => p.Key == "manual-freeform");

        Assert.Equal(ProfileTriggerMode.Manual, manual.Trigger.Mode);
    }
}

public class DiagnosticProfileFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "scantool-profile-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, DiagnosticProfileFile.DefaultFileName);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_missing_file_falls_back_to_the_built_in_library()
    {
        Assert.Equal(DiagnosticProfileLibrary.BuiltIn.Count, DiagnosticProfileFile.Load(Path_).Count);
    }

    [Fact]
    public void Defaults_are_written_once()
    {
        Assert.True(DiagnosticProfileFile.WriteDefaultsIfMissing(Path_));
        Assert.False(DiagnosticProfileFile.WriteDefaultsIfMissing(Path_));
    }

    [Fact]
    public void Profiles_round_trip_through_the_file()
    {
        var profile = new DiagnosticProfile
        {
            Key = "brake-test",
            Category = "Chassis",
            Name = "Brake pressure",
            Description = "Custom.",
            Signals = ["VehicleSpeed", "EngineRpm"],
            Trigger = new ProfileTrigger
            {
                Mode = ProfileTriggerMode.Threshold,
                Signal = "VehicleSpeed",
                Above = false,
                Value = 5,
                DwellMs = 250,
            },
            PreTriggerSeconds = 8,
            MaxDurationSeconds = 45,
            Stop = new ProfileStop { Enabled = true, Signal = "VehicleSpeed", Floor = 1, DurationMs = 2000 },
            CaptureName = "brake",
        };

        DiagnosticProfileFile.Save(Path_, [profile]);
        var loaded = Assert.Single(DiagnosticProfileFile.Load(Path_));

        // Compared field by field: record equality falls back to reference equality for the
        // Signals list, so `Assert.Equal(profile, loaded)` would never hold across a round trip.
        Assert.Equal(profile.Key, loaded.Key);
        Assert.Equal(profile.Category, loaded.Category);
        Assert.Equal(profile.Name, loaded.Name);
        Assert.Equal(profile.Description, loaded.Description);
        Assert.Equal(profile.Signals, loaded.Signals);
        Assert.Equal(profile.Trigger, loaded.Trigger);
        Assert.Equal(profile.PreTriggerSeconds, loaded.PreTriggerSeconds);
        Assert.Equal(profile.MaxDurationSeconds, loaded.MaxDurationSeconds);
        Assert.Equal(profile.Stop, loaded.Stop);
        Assert.Equal(profile.CaptureName, loaded.CaptureName);
    }

    [Fact]
    public void A_corrupt_file_falls_back_rather_than_leaving_no_profiles()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{ not json");

        // A bad edit must not strand the operator with an empty picker.
        Assert.NotEmpty(DiagnosticProfileFile.Load(Path_));
    }

    [Fact]
    public void Entries_without_a_key_are_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, JsonSerializer.Serialize(
            new[] { new DiagnosticProfile { Key = "", Name = "Nameless" }, new DiagnosticProfile { Key = "ok", Name = "Ok" } },
            CaptureFile.JsonOptions));

        Assert.Equal("ok", Assert.Single(DiagnosticProfileFile.Load(Path_)).Key);
    }

    [Fact]
    public void Merging_adds_new_built_ins_without_touching_user_profiles()
    {
        var mine = new DiagnosticProfile { Key = "mine", Name = "Mine", Signals = ["EngineRpm"] };
        DiagnosticProfileFile.Save(Path_, [mine]);

        var merged = DiagnosticProfileFile.MergeNewDefaults(Path_, DiagnosticProfileFile.Load(Path_));

        Assert.Contains(merged, p => p.Key == "mine");
        Assert.Equal(DiagnosticProfileLibrary.BuiltIn.Count + 1, merged.Count);

        // Persisted, so the next run sees the same set.
        Assert.Equal(merged.Count, DiagnosticProfileFile.Load(Path_).Count);
    }

    [Fact]
    public void Merging_preserves_an_edited_built_in()
    {
        var edited = DiagnosticProfileLibrary.BuiltIn
            .First(p => p.Key == "fuel-trim") with { Name = "My fuel trim", PreTriggerSeconds = 99 };

        DiagnosticProfileFile.Save(Path_, [edited]);

        var merged = DiagnosticProfileFile.MergeNewDefaults(Path_, DiagnosticProfileFile.Load(Path_));
        var kept = merged.Single(p => p.Key == "fuel-trim");

        Assert.Equal("My fuel trim", kept.Name);
        Assert.Equal(99, kept.PreTriggerSeconds);
    }

    [Fact]
    public void Merging_is_idempotent()
    {
        DiagnosticProfileFile.WriteDefaultsIfMissing(Path_);

        var first = DiagnosticProfileFile.MergeNewDefaults(Path_, DiagnosticProfileFile.Load(Path_));
        var second = DiagnosticProfileFile.MergeNewDefaults(Path_, first);

        Assert.Equal(first.Count, second.Count);
    }
}
