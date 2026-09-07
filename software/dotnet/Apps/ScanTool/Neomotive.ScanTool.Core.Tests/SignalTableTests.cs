using Neomotive.ScanTool.Core.Signals;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class SignalLibraryTests
{
    [Fact]
    public void Keys_are_unique()
    {
        var keys = SignalLibrary.BuiltIn.Select(s => s.Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_table_is_substantially_larger_than_the_old_curated_list()
    {
        Assert.True(SignalLibrary.BuiltIn.Count > 90, $"Only {SignalLibrary.BuiltIn.Count} signals.");
    }

    [Fact]
    public void Every_signal_is_named_scaled_and_classified()
    {
        Assert.All(SignalLibrary.BuiltIn, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Key));
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.NotEmpty(s.Systems);
            Assert.InRange(s.ByteLength, 1, 4);
            Assert.True(s.Max > s.Min, $"{s.Key} has Max <= Min.");

            // A zero scale would flatten the signal to its offset for every reading.
            Assert.NotEqual(0, s.Scale);
        });
    }

    [Fact]
    public void Multi_value_pids_are_expressed_as_signals_sharing_an_address()
    {
        // The oxygen sensor PIDs pack voltage in byte A and fuel trim in byte B. This is the case
        // the byte-offset model exists for, and the reason a one-value-per-PID read was not enough.
        var shared = SignalLibrary.BuiltIn.Where(s => s.Address == 0x14).ToArray();

        Assert.Equal(2, shared.Length);
        Assert.Contains(shared, s => s.ByteOffset == 0 && s.Unit == "V");
        Assert.Contains(shared, s => s.ByteOffset == 1 && s.Unit == "%");
    }

    [Fact]
    public void Known_scalings_are_correct()
    {
        var table = new SignalTable(SignalLibrary.BuiltIn);

        // RPM: ((A*256)+B)/4 — 0x0BB8 raw is 750 rpm.
        Assert.Equal(750, table.Find("EngineRpm")!.Decode([0x0B, 0xB8]));

        // Coolant: A-40.
        Assert.Equal(90, table.Find("EngineCoolantTemperature")!.Decode([130]));

        // Fuel trim: A/1.28 - 100 — 128 is zero trim.
        Assert.Equal(0, table.Find("ShortTermFuelTrimBank1")!.Decode([128]));

        // Module voltage: ((A*256)+B)/1000.
        Assert.Equal(14.2, table.Find("ControlModuleVoltage")!.Decode([0x37, 0x78])!.Value, 3);

        // Rail pressure: ((A*256)+B)*10 kPa.
        Assert.Equal(30_000, table.Find("FuelRailGaugePressure")!.Decode([0x0B, 0xB8]));
    }

    [Fact]
    public void A_four_byte_signal_decodes()
    {
        // Odometer is the only 4-byte entry; it exercises the widest field the model allows.
        var odometer = new SignalTable(SignalLibrary.BuiltIn).Find("Odometer")!;

        Assert.Equal(4, odometer.ByteLength);
        Assert.Equal(1677721.6, odometer.Decode([0x01, 0x00, 0x00, 0x00])!.Value, 1);
    }

    [Fact]
    public void A_signed_signal_decodes_negatives()
    {
        var evap = new SignalTable(SignalLibrary.BuiltIn).Find("EvapVaporPressure")!;

        Assert.True(evap.Signed);
        Assert.Equal(-0.25, evap.Decode([0xFF, 0xFF])!.Value, 3);
    }

    [Fact]
    public void Systems_come_back_in_the_preferred_order()
    {
        var systems = SignalLibrary.Systems(SignalLibrary.BuiltIn);

        Assert.Equal(SignalLibrary.Engine, systems[0]);
        Assert.Contains(SignalLibrary.Emissions, systems);
    }
}

public class SignalSearchTests
{
    private static readonly SignalTable Table = new(SignalLibrary.BuiltIn);

    [Theory]
    [InlineData("rail", "FuelRailGaugePressure")]
    [InlineData("rpm", "EngineRpm")]
    [InlineData("coolant", "EngineCoolantTemperature")]
    [InlineData("maf", "MafAirFlowRate")]
    [InlineData("battery", "ControlModuleVoltage")]
    [InlineData("boost", "IntakeManifoldPressure")]
    public void Search_finds_signals_by_name_or_tag(string term, string expectedKey)
    {
        Assert.Contains(Table.Search(term), s => s.Key == expectedKey);
    }

    [Fact]
    public void Search_finds_a_signal_by_its_pid_number()
    {
        // People read a PID out of a service manual and want to type it straight in.
        Assert.Contains(Table.Search("0x0C"), s => s.Key == "EngineRpm");
        Assert.Contains(Table.Search("0C"), s => s.Key == "EngineRpm");
    }

    [Fact]
    public void An_empty_search_returns_everything()
    {
        Assert.Equal(Table.All.Count, Table.Search(null).Count);
        Assert.Equal(Table.All.Count, Table.Search("   ").Count);
    }

    [Fact]
    public void System_filters_narrow_the_results()
    {
        var fuel = Table.Search(null, [SignalLibrary.Fuel]);

        Assert.NotEmpty(fuel);
        Assert.All(fuel, s => Assert.Contains(SignalLibrary.Fuel, s.Systems));
    }

    [Fact]
    public void A_signal_can_belong_to_several_systems()
    {
        // The argument against a strict tree: rail pressure is genuinely both Fuel and Engine.
        var rail = Table.Find("FuelRailGaugePressure")!;

        Assert.Contains(SignalLibrary.Fuel, rail.Systems);
        Assert.Contains(SignalLibrary.Engine, rail.Systems);
        Assert.Contains(rail, Table.Search(null, [SignalLibrary.Engine]));
        Assert.Contains(rail, Table.Search(null, [SignalLibrary.Fuel]));
    }

    [Fact]
    public void Multiple_system_filters_union_rather_than_intersect()
    {
        var either = Table.Search(null, [SignalLibrary.Fuel, SignalLibrary.Electrical]);

        Assert.Contains(either, s => s.Key == "FuelPressure");
        Assert.Contains(either, s => s.Key == "ControlModuleVoltage");
    }

    [Fact]
    public void Search_and_system_filter_combine()
    {
        var results = Table.Search("pressure", [SignalLibrary.Fuel]);

        Assert.NotEmpty(results);
        Assert.All(results, s => Assert.Contains(SignalLibrary.Fuel, s.Systems));
        Assert.DoesNotContain(results, s => s.Key == "BarometricPressure");
    }
}

public class SignalTableFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "scantool-signal-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void The_built_in_table_is_written_on_first_load()
    {
        var table = SignalTable.Load(_dir);

        Assert.True(File.Exists(Path.Combine(_dir, SignalTable.PidTableFileName)));
        Assert.Equal(SignalLibrary.BuiltIn.Count, table.All.Count);
    }

    [Fact]
    public void Edits_to_the_file_are_honoured()
    {
        SignalTable.Load(_dir);

        var path = Path.Combine(_dir, SignalTable.PidTableFileName);
        SignalTable.Save(path, [new SignalDefinition { Key = "OnlyOne", Name = "Only One", Address = 1 }]);

        var reloaded = SignalTable.Load(_dir);

        Assert.Single(reloaded.All);
        Assert.Equal("OnlyOne", reloaded.All[0].Key);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_the_built_in_table()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SignalTable.PidTableFileName), "{ not json");

        // A bad edit must not leave the tool with nothing to read.
        Assert.Equal(SignalLibrary.BuiltIn.Count, SignalTable.Load(_dir).All.Count);
    }

    [Fact]
    public void Mode22_definitions_are_merged_in_as_first_class_signals()
    {
        SignalTable.Load(_dir);

        SignalTable.Save(Path.Combine(_dir, SignalTable.Mode22FileName),
        [
            new SignalDefinition
            {
                Key = "CommandedRail",
                Name = "Commanded Rail Pressure",
                Unit = "kPa",
                Systems = [SignalLibrary.Fuel],
                Source = SignalSource.Mode22,
                Address = 0x1234,
                ByteLength = 2,
                Scale = 10,
            },
        ]);

        var table = SignalTable.Load(_dir);
        var commanded = table.Find("CommandedRail");

        Assert.NotNull(commanded);
        Assert.Equal(SignalSource.Mode22, commanded!.Source);
        Assert.Equal("DID 0x1234", commanded.AddressText);

        // Searchable and filterable exactly like a standard PID.
        Assert.Contains(table.Search("commanded rail"), s => s.Key == "CommandedRail");
        Assert.Contains(table.Search(null, [SignalLibrary.Fuel]), s => s.Key == "CommandedRail");
    }

    [Fact]
    public void A_mode22_entry_can_override_a_standard_key()
    {
        SignalTable.Load(_dir);

        SignalTable.Save(Path.Combine(_dir, SignalTable.Mode22FileName),
        [
            new SignalDefinition
            {
                Key = "FuelRailGaugePressure",
                Name = "Rail Pressure (manufacturer)",
                Source = SignalSource.Mode22,
                Address = 0x4321,
            },
        ]);

        var rail = SignalTable.Load(_dir).Find("FuelRailGaugePressure")!;

        Assert.Equal(SignalSource.Mode22, rail.Source);
    }

    [Fact]
    public void Entries_without_a_key_are_ignored()
    {
        Directory.CreateDirectory(_dir);
        SignalTable.Save(Path.Combine(_dir, SignalTable.PidTableFileName),
        [
            new SignalDefinition { Key = "", Name = "Nameless" },
            new SignalDefinition { Key = "Good", Name = "Good" },
        ]);

        Assert.Single(SignalTable.Load(_dir).All);
    }

    [Fact]
    public void No_config_directory_still_yields_the_built_in_table()
    {
        Assert.Equal(SignalLibrary.BuiltIn.Count, SignalTable.Load(null).All.Count);
    }
}
