using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neomotive.ScanTool.Core.Vehicles;

/// <summary>One row of the index, enough to list remembered vehicles without opening every record.</summary>
public sealed class VehicleIndexEntry
{
    public string Vin { get; set; } = "";
    public string ClassKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime LastSeenUtc { get; set; }
    public int VisitCount { get; set; }
}

/// <summary>
/// Remembers vehicles across visits: per-VIN records and per-class profiles, on disk under the data
/// directory.
/// <para>
/// Everything here is an optimisation. A missing directory, an unreadable file, a record written by
/// a newer version — each is treated as "nothing remembered" rather than an error, because a tool
/// that will not connect to a vehicle because it cannot read its own cache is worse than a tool
/// with no cache.
/// </para>
/// </summary>
public sealed class VehicleStore
{
    /// <summary>Vehicle records kept before the least recently seen are evicted.</summary>
    public const int MaxVehicleRecords = 500;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,

        // Must match the write options: addressing mode is written as a name so the file is
        // readable by a human, and without this the read side rejects it and every record comes
        // back as "not remembered".
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly string _root;

    /// <summary>Last read or write failure. Surfaced by the UI; never thrown.</summary>
    public string? LastError { get; private set; }

    public VehicleStore(string dataDirectory)
    {
        _root = Path.Combine(dataDirectory, "vehicles");
    }

    private string VinDirectory => Path.Combine(_root, "vin");
    private string ClassDirectory => Path.Combine(_root, "class");
    private string IndexPath => Path.Combine(_root, "index.json");

    // ── Reads ────────────────────────────────────────────────────────────────

    /// <summary>The record for this exact vehicle, or null if it has not been seen.</summary>
    public VehicleRecord? LoadVehicle(string? vin)
    {
        if (VehicleKey.VehicleKeyFor(vin) is not { } key) return null;
        return Read<VehicleRecord>(Path.Combine(VinDirectory, key + ".json"));
    }

    /// <summary>The profile for this vehicle's class, or null if no vehicle of the class is known.</summary>
    public ClassProfile? LoadClass(string? classKey)
    {
        if (string.IsNullOrWhiteSpace(classKey)) return null;
        return Read<ClassProfile>(Path.Combine(ClassDirectory, classKey + ".json"));
    }

    public IReadOnlyList<VehicleIndexEntry> LoadIndex()
        => Read<List<VehicleIndexEntry>>(IndexPath) ?? [];

    private T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex)
        {
            // A record this version cannot parse is a record we do not have.
            LastError = $"{Path.GetFileName(path)} could not be read: {ex.Message}";
            return null;
        }
    }

    // ── Writes ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Persists a vehicle record and folds it into its class profile. Returns false if nothing
    /// could be written, so a caller can say so rather than believing the vehicle was remembered.
    /// </summary>
    public bool Save(VehicleRecord record)
    {
        if (VehicleKey.VehicleKeyFor(record.Vin) is not { } key) return false;

        lock (_gate)
        {
            if (!Write(Path.Combine(VinDirectory, key + ".json"), record)) return false;

            MergeIntoClass(record);
            UpdateIndex(record);
            Evict();
            return true;
        }
    }

    private bool Write<T>(string path, T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Temp-then-rename. The appliance is switched off by pulling the plug, and a record
            // half-written at that moment would be unparseable — losing not just this visit but
            // everything previously known about the vehicle.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, WriteOptions));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"{Path.GetFileName(path)} could not be written: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Folds a vehicle's modules into its class profile as a union. A module already in the profile
    /// gains a sighting; one the profile has never seen is added. Nothing is ever removed here —
    /// the class profile is what vehicles of this type have been observed to carry, and one
    /// vehicle's quiet bus is not evidence about the type.
    /// </summary>
    private void MergeIntoClass(VehicleRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ClassKey)) return;

        var profile = LoadClass(record.ClassKey) ?? new ClassProfile
        {
            ClassKey = record.ClassKey
        };

        var known = LoadIndex();
        profile.VinCount = Math.Max(
            profile.VinCount,
            known.Count(e => e.ClassKey == record.ClassKey && e.Vin != record.Vin) + 1);

        profile.LastSeenUtc = record.LastSeenUtc;

        // The best identity wins: a record decoded online names the model, one decoded offline
        // may only name the make, and the profile should carry the most complete answer seen.
        if (profile.Identity.Model is null && record.Identity.Model is not null)
            profile.Identity = record.Identity;
        else if (profile.Identity.Make is null)
            profile.Identity = record.Identity;

        foreach (var module in record.Modules)
        {
            var existing = profile.Modules.FirstOrDefault(m =>
                m.TxId == module.TxId && m.RxId == module.RxId && m.Addressing == module.Addressing);

            if (existing is null)
            {
                profile.Modules.Add(new RememberedModule
                {
                    TxId = module.TxId,
                    RxId = module.RxId,
                    Addressing = module.Addressing,
                    Name = module.Name,
                    EcuName = module.EcuName,
                    PartNumber = module.PartNumber,
                    SoftwareVersion = module.SoftwareVersion,
                    HardwareNumber = module.HardwareNumber,
                    FirstSeenUtc = module.FirstSeenUtc,
                    LastSeenUtc = module.LastSeenUtc,
                    SeenCount = module.SeenCount,
                    VinsSeenOn = 1
                });
            }
            else
            {
                existing.LastSeenUtc = module.LastSeenUtc;
                existing.SeenCount += module.SeenCount > 0 ? 1 : 0;
                existing.ConsecutiveMisses = 0;
                if (existing.VinsSeenOn < profile.VinCount) existing.VinsSeenOn++;
                existing.EcuName ??= module.EcuName;
                existing.PartNumber ??= module.PartNumber;
                existing.SoftwareVersion ??= module.SoftwareVersion;
                existing.HardwareNumber ??= module.HardwareNumber;
            }
        }

        if (record.SupportedPids.Count > 0)
        {
            if (profile.SupportedPids.Count == 0 ||
                profile.SupportedPids.SequenceEqual(record.SupportedPids))
            {
                profile.SupportedPids = [.. record.SupportedPids];
                profile.SupportedPidsAgreement++;
            }
            // Disagreement leaves the profile's set alone. Two vehicles of the same class can
            // genuinely differ — different engine, different market — and the per-VIN record is
            // always the authority for a vehicle actually present.
        }

        Write(Path.Combine(ClassDirectory, record.ClassKey + ".json"), profile);
    }

    private void UpdateIndex(VehicleRecord record)
    {
        var index = LoadIndex().ToList();
        index.RemoveAll(e => string.Equals(e.Vin, record.Vin, StringComparison.OrdinalIgnoreCase));

        index.Add(new VehicleIndexEntry
        {
            Vin = record.Vin,
            ClassKey = record.ClassKey,
            DisplayName = record.Identity.DisplayName,
            LastSeenUtc = record.LastSeenUtc,
            VisitCount = record.VisitCount
        });

        Write(IndexPath, index.OrderByDescending(e => e.LastSeenUtc).ToList());
    }

    /// <summary>
    /// Drops the least recently seen vehicle records past the cap. Class profiles are kept
    /// regardless: they are small, and they are the part that generalises to the next vehicle.
    /// </summary>
    private void Evict()
    {
        var index = LoadIndex().ToList();
        if (index.Count <= MaxVehicleRecords) return;

        var doomed = index
            .OrderByDescending(e => e.LastSeenUtc)
            .Skip(MaxVehicleRecords)
            .ToList();

        foreach (var entry in doomed)
        {
            TryDeleteVehicleFile(entry.Vin);
            index.Remove(entry);
        }

        Write(IndexPath, index);
    }

    // ── Forgetting ───────────────────────────────────────────────────────────

    /// <summary>Forgets one vehicle. Its class profile survives — other vehicles rely on it.</summary>
    public bool Forget(string? vin)
    {
        if (VehicleKey.VehicleKeyFor(vin) is not { } key) return false;

        lock (_gate)
        {
            var ok = TryDeleteVehicleFile(key);
            var index = LoadIndex().ToList();
            index.RemoveAll(e => string.Equals(e.Vin, key, StringComparison.OrdinalIgnoreCase));
            Write(IndexPath, index);
            return ok;
        }
    }

    /// <summary>Forgets everything — records, class profiles and the index.</summary>
    public bool ForgetAll()
    {
        lock (_gate)
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Remembered vehicles could not be cleared: {ex.Message}";
                return false;
            }
        }
    }

    private bool TryDeleteVehicleFile(string key)
    {
        try
        {
            var path = Path.Combine(VinDirectory, key + ".json");
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"{key} could not be forgotten: {ex.Message}";
            return false;
        }
    }

    /// <summary>How many vehicles are remembered. Shown in Settings next to the clear button.</summary>
    public int Count => LoadIndex().Count;
}
