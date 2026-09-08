using Meadow.Foundation.Telematics.Uds;
using Neomotive.Obd2;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neomotive.Uds;

/// <summary>
/// The UDS description database: DID names and formatting, fault-type and negative-response text.
/// </summary>
/// <remarks>
/// Three layers, lowest first: an embedded seed catalog, then every <c>uds-catalog*.json</c> in the
/// application data directory (filename order), then runtime edits. Nothing is trapped in the build —
/// a new DID is a file drop and a <see cref="Reload"/> away, and <see cref="Export"/> hands the whole
/// table back out as JSON.
/// </remarks>
public class UdsCatalog : IUdsDescriptionProvider
{
    private const string SeedResourceName = "Neomotive.Uds.Data.uds-catalog.default.json";
    private const string OverlayFileName = "uds-catalog.json";
    private const string OverlaySearchPattern = "uds-catalog*.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    /// <summary>
    /// The serializer settings catalog files use — camel-cased encoding names, indented output.
    /// Exposed so anything reading or writing a catalog file agrees with this class.
    /// </summary>
    public static JsonSerializerOptions FileJsonOptions => JsonOptions;

    private readonly Dictionary<ushort, UdsDidDefinition> _seedDids = [];
    private readonly Dictionary<byte, string> _seedFaultTypes = [];
    private readonly Dictionary<byte, string> _seedNrcs = [];

    private readonly Dictionary<ushort, UdsDidDefinition> _dids = [];
    private readonly Dictionary<byte, string> _faultTypes = [];
    private readonly Dictionary<byte, string> _nrcs = [];

    private readonly Lock _sync = new();

    /// <summary>A process-wide catalog, for hosts that do not want to thread one through.</summary>
    public static UdsCatalog Shared { get; } = new();

    /// <summary>Where overlay files are read from and written to. Null means overlays are skipped.</summary>
    public string? DataDirectory { get; private set; }

    /// <summary>Files that failed to parse on the last load, with the reason. Surface these; do not throw.</summary>
    public IReadOnlyList<string> LoadErrors { get; private set; } = [];

    /// <summary>Every DID the catalog currently resolves, lowest identifier first.</summary>
    public IReadOnlyList<UdsDidDefinition> Dids
    {
        get { lock (_sync) return [.. _dids.Values.OrderBy(d => d.Identifier)]; }
    }

    /// <summary>Creates a catalog holding just the seed data.</summary>
    public UdsCatalog()
    {
        LoadSeed();
        ResetToSeed();
    }

    /// <summary>
    /// Points the catalog at a directory of overlay files and loads them. Call this with the same
    /// data directory the application uses for its config.
    /// </summary>
    public void SetDataDir(string path)
    {
        DataDirectory = path;
        Reload();
    }

    /// <summary>Re-reads the overlay files, discarding unsaved runtime edits.</summary>
    public void Reload()
    {
        var errors = new List<string>();

        lock (_sync)
        {
            ResetToSeed();

            if (!string.IsNullOrWhiteSpace(DataDirectory) && Directory.Exists(DataDirectory))
            {
                foreach (var file in Directory.GetFiles(DataDirectory, OverlaySearchPattern).OrderBy(f => f))
                {
                    try
                    {
                        var overlay = JsonSerializer.Deserialize<UdsCatalogFile>(File.ReadAllText(file), JsonOptions);
                        if (overlay != null) Apply(overlay);
                    }
                    catch (Exception ex)
                    {
                        // One bad file must not cost the user every other file.
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
        }

        LoadErrors = errors;
    }

    /// <summary>Adds or replaces a DID definition in memory. Persist it with <see cref="Save"/>.</summary>
    public void Upsert(UdsDidDefinition definition)
    {
        lock (_sync) _dids[definition.Identifier] = definition;
    }

    /// <summary>Removes a DID definition in memory. Persist it with <see cref="Save"/>.</summary>
    public bool Remove(ushort did)
    {
        lock (_sync) return _dids.Remove(did);
    }

    /// <summary>
    /// Reads a catalog file into this catalog. Accepts catalog JSON, or a two-column
    /// <c>did,name</c> CSV for quick additions.
    /// </summary>
    /// <param name="path">File to read.</param>
    /// <param name="merge">When false, the catalog is reset to the seed before applying the file.</param>
    /// <returns>How many DID definitions were applied.</returns>
    public int Import(string path, bool merge = true)
    {
        var text = File.ReadAllText(path);

        lock (_sync)
        {
            if (!merge) ResetToSeed();

            var file = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? ParseCsv(text)
                : JsonSerializer.Deserialize<UdsCatalogFile>(text, JsonOptions) ?? new UdsCatalogFile();

            Apply(file);
            return file.Dids.Count;
        }
    }

    /// <summary>Writes the catalog to a JSON file that <see cref="Import"/> can read back.</summary>
    /// <param name="path">File to write.</param>
    /// <param name="scope">The whole effective table, or only what differs from the seed.</param>
    public void Export(string path, UdsCatalogScope scope = UdsCatalogScope.All)
    {
        var file = Snapshot(scope);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
    }

    /// <summary>
    /// Persists runtime edits to <c>uds-catalog.json</c> in the data directory, writing only what
    /// differs from the seed so future seed updates still reach the user.
    /// </summary>
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(DataDirectory))
            throw new InvalidOperationException("No data directory set — call SetDataDir first.");

        Directory.CreateDirectory(DataDirectory);
        Export(Path.Combine(DataDirectory, OverlayFileName), UdsCatalogScope.Overrides);
    }

    /// <summary>Builds a catalog file from the current contents.</summary>
    public UdsCatalogFile Snapshot(UdsCatalogScope scope = UdsCatalogScope.All)
    {
        lock (_sync)
        {
            var file = new UdsCatalogFile();

            foreach (var (did, definition) in _dids.OrderBy(kvp => kvp.Key))
            {
                if (scope == UdsCatalogScope.Overrides && IsSeedEquivalent(did, definition)) continue;
                file.Dids.Add(definition);
            }

            if (scope == UdsCatalogScope.Overrides)
            {
                // A seed DID deleted at runtime is itself an override worth recording.
                foreach (var did in _seedDids.Keys.Where(k => !_dids.ContainsKey(k)).OrderBy(k => k))
                {
                    file.Dids.Add(new UdsDidDefinition { Did = $"0x{did:X4}", Remove = true });
                }
            }

            foreach (var (ftb, text) in _faultTypes.OrderBy(kvp => kvp.Key))
            {
                if (scope == UdsCatalogScope.Overrides &&
                    _seedFaultTypes.TryGetValue(ftb, out var seed) && seed == text) continue;
                file.FaultTypes[$"0x{ftb:X2}"] = text;
            }

            foreach (var (nrc, text) in _nrcs.OrderBy(kvp => kvp.Key))
            {
                if (scope == UdsCatalogScope.Overrides &&
                    _seedNrcs.TryGetValue(nrc, out var seed) && seed == text) continue;
                file.Nrcs[$"0x{nrc:X2}"] = text;
            }

            return file;
        }
    }

    /// <summary>Looks up a DID definition, if the catalog knows the identifier.</summary>
    public UdsDidDefinition? Find(ushort did)
    {
        lock (_sync) return _dids.TryGetValue(did, out var definition) ? definition : null;
    }

    /// <inheritdoc/>
    public string GetDidName(ushort did)
        => Find(did)?.Name is { Length: > 0 } name ? name : $"DID 0x{did:X4}";

    /// <inheritdoc/>
    public string FormatDidValue(ushort did, byte[] data)
    {
        if (data.Length == 0) return "";

        var definition = Find(did);

        switch (definition?.Encoding)
        {
            case UdsDidEncoding.Ascii:
                var text = Encoding.ASCII.GetString(data).Trim('\0', ' ');
                if (IsPrintableAscii(text)) return text;
                break;

            case UdsDidEncoding.Enum:
                if (definition.Values != null)
                {
                    foreach (var (key, label) in definition.Values)
                    {
                        if (UdsNumber.ParseByte(key) == data[0]) return label;
                    }
                }
                return $"0x{data[0]:X2}";

            case UdsDidEncoding.UInt:
                ulong raw = 0;
                foreach (var b in data) raw = (raw << 8) | b;
                var scaled = raw * definition.Scale + definition.Offset;
                return string.IsNullOrEmpty(definition.Units) ? $"{scaled:0.##}" : $"{scaled:0.##} {definition.Units}";
        }

        return string.Join(" ", data.Select(b => $"{b:X2}"));
    }

    /// <inheritdoc/>
    public string GetDtcDescription(string baseCode) => DtcDescriptions.Lookup(baseCode);

    /// <inheritdoc/>
    public string GetFaultTypeDescription(byte ftb)
    {
        lock (_sync)
        {
            return _faultTypes.TryGetValue(ftb, out var text) ? text : $"Failure Type 0x{ftb:X2}";
        }
    }

    /// <inheritdoc/>
    public string GetNrcDescription(byte nrc)
    {
        lock (_sync)
        {
            return _nrcs.TryGetValue(nrc, out var text) ? text : $"Unknown NRC (0x{nrc:X2})";
        }
    }

    private void LoadSeed()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SeedResourceName)
            ?? throw new InvalidOperationException($"Embedded catalog '{SeedResourceName}' is missing.");
        using var reader = new StreamReader(stream);

        var seed = JsonSerializer.Deserialize<UdsCatalogFile>(reader.ReadToEnd(), JsonOptions)
            ?? new UdsCatalogFile();

        foreach (var definition in seed.Dids) _seedDids[definition.Identifier] = definition;
        foreach (var (key, text) in seed.FaultTypes) _seedFaultTypes[UdsNumber.ParseByte(key)] = text;
        foreach (var (key, text) in seed.Nrcs) _seedNrcs[UdsNumber.ParseByte(key)] = text;
    }

    private void ResetToSeed()
    {
        _dids.Clear();
        _faultTypes.Clear();
        _nrcs.Clear();

        foreach (var (did, definition) in _seedDids) _dids[did] = definition;
        foreach (var (ftb, text) in _seedFaultTypes) _faultTypes[ftb] = text;
        foreach (var (nrc, text) in _seedNrcs) _nrcs[nrc] = text;
    }

    private void Apply(UdsCatalogFile file)
    {
        foreach (var definition in file.Dids)
        {
            var did = definition.Identifier;
            if (definition.Remove) _dids.Remove(did);
            else _dids[did] = definition;
        }

        foreach (var (key, text) in file.FaultTypes) _faultTypes[UdsNumber.ParseByte(key)] = text;
        foreach (var (key, text) in file.Nrcs) _nrcs[UdsNumber.ParseByte(key)] = text;
    }

    private bool IsSeedEquivalent(ushort did, UdsDidDefinition definition)
    {
        if (!_seedDids.TryGetValue(did, out var seed)) return false;

        return seed.Name == definition.Name
            && seed.Encoding == definition.Encoding
            && seed.Length == definition.Length
            && seed.Units == definition.Units
            && seed.Scale.Equals(definition.Scale)
            && seed.Offset.Equals(definition.Offset)
            && seed.Category == definition.Category
            && SameValues(seed.Values, definition.Values);
    }

    private static bool SameValues(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (a == null || a.Count == 0) return b == null || b.Count == 0;
        if (b == null || a.Count != b.Count) return false;

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != value) return false;
        }
        return true;
    }

    private static UdsCatalogFile ParseCsv(string text)
    {
        var file = new UdsCatalogFile();

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var parts = trimmed.Split(',', 2);
            if (parts.Length != 2) continue;

            var did = parts[0].Trim();
            if (did.Equals("did", StringComparison.OrdinalIgnoreCase)) continue; // header row
            if (!UdsNumber.TryParse(did, out _)) continue;

            file.Dids.Add(new UdsDidDefinition
            {
                Did = did,
                Name = parts[1].Trim().Trim('"'),
                Source = "imported"
            });
        }

        return file;
    }

    private static bool IsPrintableAscii(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (c < 32 || c > 126) return false;
        }
        return true;
    }
}
