using Neomotive.ScanTool.Core.Vehicles;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class VehicleKeyTests
{
    // A real 2021 Explorer ST and a base Explorer of the same year. The point of keying on the VDS
    // rather than make/model/trim is that these two must not land in the same bucket offline,
    // where Trim is always null.
    private const string ExplorerSt2021 = "1FM5K8GC1MGA00001";
    private const string ExplorerStOther = "1FM5K8GC3MGA99999";
    private const string ExplorerBase2021 = "1FMSK7DH5MGA00001";

    [Fact]
    public void ClassKey_IsWmiPlusVdsPlusYearCode()
    {
        // Characters 1-3, 4-8 and 10.
        Assert.Equal("1FM5K8GCM", VehicleKey.ClassKeyFor(ExplorerSt2021));
    }

    [Fact]
    public void TwoVehiclesOfTheSameTypeShareAClassKey()
    {
        Assert.Equal(
            VehicleKey.ClassKeyFor(ExplorerSt2021),
            VehicleKey.ClassKeyFor(ExplorerStOther));
    }

    [Fact]
    public void ADifferentSeriesDoesNotShareAClassKey()
    {
        Assert.NotEqual(
            VehicleKey.ClassKeyFor(ExplorerSt2021),
            VehicleKey.ClassKeyFor(ExplorerBase2021));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TOOSHORT")]
    [InlineData("1FM5K8GC5MGA0000")]      // 16 characters
    [InlineData("1FM5K8GC1MGA000012")]    // 18 characters
    [InlineData("1FM5K8GC5MGI00001")]     // contains I, not in the VIN alphabet
    [InlineData("1FM5K8GC5MGO00001")]     // contains O
    [InlineData("1FM5K8GC5MGQ00001")]     // contains Q
    public void UnusableVinsAreRejected(string? vin)
    {
        Assert.False(VehicleKey.IsUsable(vin));
        Assert.Null(VehicleKey.ClassKeyFor(vin));
        Assert.Null(VehicleKey.VehicleKeyFor(vin));
    }
}

public class RememberedModuleTests
{
    [Fact]
    public void ASingleMissDoesNotStopAModuleBeingPrepopulated()
    {
        // The operator's own point: a scan can miss a module because the bus was unhappy, not
        // because the module is gone.
        var module = new RememberedModule();
        module.MarkSeen(DateTime.UtcNow);
        module.MarkMissed();

        Assert.True(module.ShouldPrepopulate);
    }

    [Fact]
    public void ThreeMissesAgainstOneSightingDemotesIt()
    {
        var module = new RememberedModule();
        module.MarkSeen(DateTime.UtcNow);
        module.MarkMissed();
        module.MarkMissed();
        module.MarkMissed();

        Assert.False(module.ShouldPrepopulate);
    }

    [Fact]
    public void AWellEstablishedModuleSurvivesABadDay()
    {
        var module = new RememberedModule();
        for (var i = 0; i < 10; i++) module.MarkSeen(DateTime.UtcNow);
        module.MarkMissed();
        module.MarkMissed();
        module.MarkMissed();

        Assert.True(module.ShouldPrepopulate);
    }

    [Fact]
    public void ASightingClearsTheMissRun()
    {
        var module = new RememberedModule();
        module.MarkSeen(DateTime.UtcNow);
        module.MarkMissed();
        module.MarkMissed();
        module.MarkSeen(DateTime.UtcNow);

        Assert.Equal(0, module.ConsecutiveMisses);
        Assert.True(module.ShouldPrepopulate);
    }

    [Fact]
    public void ExtendedAddressesAreFormattedAtFullWidth()
    {
        // A 29-bit identifier rendered :X3 would come out as three nibbles of an unrelated
        // address, which reads as a valid 11-bit one.
        var module = new RememberedModule
        {
            TxId = 0x18DA10F1,
            RxId = 0x18DAF110,
            Addressing = ModuleAddressing.Ext29
        };

        Assert.Equal("TX: 0x18DA10F1 → RX: 0x18DAF110", module.AddressSummary);
    }
}

public class VehicleRecordTests
{
    [Fact]
    public void RepeatVisitsWithTheSameCalibrationRecordOneEntry()
    {
        var record = new VehicleRecord();

        for (var i = 0; i < 20; i++)
        {
            record.RecordCalibration("HU5A-14C204-BCB", "1A2B3C4D", "PCM", DateTime.UtcNow);
        }

        Assert.Single(record.Calibrations);
    }

    [Fact]
    public void AReflashAppendsAnEntryAndKeepsTheBaseline()
    {
        var record = new VehicleRecord();
        record.RecordCalibration("HU5A-14C204-BCB", "1A2B3C4D", "PCM", DateTime.UtcNow);
        record.RecordCalibration("MU5A-14C204-AAA", "9F8E7D6C", "PCM", DateTime.UtcNow);

        Assert.Equal(2, record.Calibrations.Count);
        Assert.Equal("MU5A-14C204-AAA", record.LatestCalibration!.CalibrationId);
        Assert.Equal("HU5A-14C204-BCB", record.PreviousCalibration!.CalibrationId);
    }

    [Fact]
    public void HistoryIsCappedAtTheLimit()
    {
        var record = new VehicleRecord();

        for (var i = 0; i < VehicleRecord.HistoryLimit + 5; i++)
        {
            record.RecordCalibration($"CAL-{i}", $"CVN-{i}", "PCM", DateTime.UtcNow);
            record.RecordDtcSnapshot(new DtcSnapshot { SeenUtc = DateTime.UtcNow });
        }

        Assert.Equal(VehicleRecord.HistoryLimit, record.Calibrations.Count);
        Assert.Equal(VehicleRecord.HistoryLimit, record.DtcSnapshots.Count);

        // The newest survive, not the oldest.
        Assert.Equal($"CAL-{VehicleRecord.HistoryLimit + 4}", record.LatestCalibration!.CalibrationId);
    }
}

public class VehicleStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "neomotive-vehiclestore-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* a leaked temp directory must not fail a test run */ }
    }

    private VehicleRecord NewRecord(string vin) => new()
    {
        Vin = vin,
        ClassKey = VehicleKey.ClassKeyFor(vin)!,
        FirstSeenUtc = DateTime.UtcNow,
        LastSeenUtc = DateTime.UtcNow,
        VisitCount = 1,
        Identity = new VehicleIdentity { Make = "Ford", Model = "Explorer", Year = 2021, Trim = "ST" }
    };

    [Fact]
    public void ARecordRoundTrips()
    {
        var store = new VehicleStore(_dir);
        var record = NewRecord("1FM5K8GC1MGA00001");
        record.Modules.Add(new RememberedModule { TxId = 0x760, RxId = 0x768, Name = "ABS", SeenCount = 1 });
        record.SupportedPids = [0x0C, 0x0D, 0x11];

        Assert.True(store.Save(record));

        var loaded = store.LoadVehicle("1FM5K8GC1MGA00001");

        Assert.NotNull(loaded);
        Assert.Equal("Explorer", loaded!.Identity.Model);
        Assert.Single(loaded.Modules);
        Assert.Equal(0x760u, loaded.Modules[0].TxId);
        Assert.Equal(3, loaded.SupportedPids.Count);
    }

    [Fact]
    public void AnUnknownVehicleReadsAsNothingRemembered()
    {
        var store = new VehicleStore(_dir);
        Assert.Null(store.LoadVehicle("1FM5K8GC1MGA00001"));
        Assert.Null(store.LoadClass("1FM5K8GCM"));
    }

    [Fact]
    public void ACorruptRecordIsTreatedAsAColdStart()
    {
        // A cache the tool cannot read must never stop it working — it just means nothing is
        // remembered.
        var store = new VehicleStore(_dir);
        store.Save(NewRecord("1FM5K8GC1MGA00001"));

        var path = Path.Combine(_dir, "vehicles", "vin", "1FM5K8GC1MGA00001.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Null(store.LoadVehicle("1FM5K8GC1MGA00001"));
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public void ASecondVehicleOfTheSameTypeBuildsTheClassProfile()
    {
        var store = new VehicleStore(_dir);

        var first = NewRecord("1FM5K8GC1MGA00001");
        first.Modules.Add(new RememberedModule { TxId = 0x760, RxId = 0x768, Name = "ABS", SeenCount = 1 });
        store.Save(first);

        var second = NewRecord("1FM5K8GC3MGA99999");
        second.Modules.Add(new RememberedModule { TxId = 0x720, RxId = 0x728, Name = "IPC", SeenCount = 1 });
        store.Save(second);

        var profile = store.LoadClass("1FM5K8GCM");

        Assert.NotNull(profile);

        // A union, never an intersection: the second vehicle's quiet ABS is not evidence that
        // vehicles of this type do not carry one.
        Assert.Equal(2, profile!.Modules.Count);
        Assert.Equal(2, profile.VinCount);
    }

    [Fact]
    public void ForgettingAVehicleKeepsItsClassProfile()
    {
        var store = new VehicleStore(_dir);
        store.Save(NewRecord("1FM5K8GC1MGA00001"));

        Assert.True(store.Forget("1FM5K8GC1MGA00001"));
        Assert.Null(store.LoadVehicle("1FM5K8GC1MGA00001"));
        Assert.NotNull(store.LoadClass("1FM5K8GCM"));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void ForgetAllClearsEverything()
    {
        var store = new VehicleStore(_dir);
        store.Save(NewRecord("1FM5K8GC1MGA00001"));

        Assert.True(store.ForgetAll());
        Assert.Equal(0, store.Count);
        Assert.Null(store.LoadClass("1FM5K8GCM"));
    }

    [Fact]
    public void TheIndexTracksWhatIsStored()
    {
        var store = new VehicleStore(_dir);
        store.Save(NewRecord("1FM5K8GC1MGA00001"));
        store.Save(NewRecord("1FM5K8GC3MGA99999"));

        Assert.Equal(2, store.Count);
        Assert.All(store.LoadIndex(), e => Assert.Equal("2021 Ford Explorer ST", e.DisplayName));
    }

    [Fact]
    public void ResavingTheSameVinDoesNotDuplicateTheIndexRow()
    {
        var store = new VehicleStore(_dir);
        var record = NewRecord("1FM5K8GC1MGA00001");

        store.Save(record);
        record.VisitCount = 2;
        store.Save(record);

        Assert.Equal(1, store.Count);
        Assert.Equal(2, store.LoadIndex()[0].VisitCount);
    }
}
