using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Neomotive.Vin.Extensions;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Data;

/// <summary>
/// The VDS pattern table: shipped seed, plus an on-disk overlay the decoder writes to as it learns
/// patterns from online decodes. Resolving a model offline is what this exists for, so every path
/// through it tolerates a missing, unreadable or malformed file and simply resolves less.
/// </summary>
public sealed class VdsPatternProvider
{
    private const string ResourceName = "Neomotive.Vin.Resources.vds-patterns.json";
    private const string FileName = "vds-patterns.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly string? _storeDirectory;
    private List<VdsPattern>? _patterns;

    /// <summary>Last load or save failure, for a caller that wants to surface it. Never thrown.</summary>
    public string? LastError { get; private set; }

    public VdsPatternProvider(VinOptions? options = null)
    {
        // Learned patterns are written next to the other catalog overrides, which is a directory
        // chosen to survive an app update.
        _storeDirectory = options?.PatternStorePath ?? options?.ExternalCatalogPath;
    }

    private string? StorePath =>
        string.IsNullOrEmpty(_storeDirectory) ? null : Path.Combine(_storeDirectory, FileName);

    private List<VdsPattern> Patterns
    {
        get
        {
            lock (_gate)
            {
                return _patterns ??= Load();
            }
        }
    }

    private List<VdsPattern> Load()
    {
        var result = new List<VdsPattern>();

        // Embedded seed first, so a broken overlay still leaves the shipped rules working.
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                result.AddRange(JsonSerializer.Deserialize<List<VdsPattern>>(reader.ReadToEnd(), ReadOptions) ?? []);
            }
        }
        catch (Exception ex)
        {
            LastError = $"Embedded VDS patterns could not be read: {ex.Message}";
        }

        if (StorePath is { } path && File.Exists(path))
        {
            try
            {
                var overlay = JsonSerializer.Deserialize<List<VdsPattern>>(File.ReadAllText(path), ReadOptions) ?? [];
                result.AddRange(overlay);
            }
            catch (Exception ex)
            {
                LastError = $"{FileName} could not be read: {ex.Message}";
            }
        }

        return result;
    }

    /// <summary>
    /// Best match for a VIN's WMI, VDS and model year, or null. "Best" is the most specific: the
    /// fewest wildcards, then the narrowest year range, so an exact learned VIN shape wins over a
    /// broad hand-written rule covering the same vehicle.
    /// </summary>
    public VdsPattern? Match(string wmi, string vds, int? modelYear)
    {
        if (string.IsNullOrEmpty(wmi) || string.IsNullOrEmpty(vds)) return null;

        VdsPattern? best = null;

        foreach (var p in Patterns)
        {
            if (!p.Matches(wmi, vds, modelYear)) continue;
            if (best == null)
            {
                best = p;
                continue;
            }

            if (p.WildcardCount < best.WildcardCount) best = p;
            else if (p.WildcardCount == best.WildcardCount && IsNarrower(p, best)) best = p;
        }

        return best;
    }

    private static bool IsNarrower(VdsPattern candidate, VdsPattern incumbent)
    {
        static int Span(VdsPattern p)
        {
            var start = p.YearStart > 0 ? p.YearStart : 1980;
            var end = p.YearEnd is > 0 and < 9999 ? p.YearEnd : 9999;
            return end - start;
        }

        return Span(candidate) < Span(incumbent);
    }

    /// <summary>
    /// Records what an online decode discovered about this exact VIN shape, so the next vehicle of
    /// the same type resolves with no network. A no-op when there is nowhere to write, when the
    /// model is unknown, or when an equally specific entry already says the same thing.
    /// </summary>
    public void Learn(string wmi, string vds, int? modelYear, string? model, string? trim)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        if (string.IsNullOrEmpty(wmi) || string.IsNullOrEmpty(vds)) return;
        if (StorePath is not { } path) return;

        lock (_gate)
        {
            var patterns = _patterns ??= Load();

            // An exact-VDS entry for this year already present and agreeing: nothing to learn.
            var existing = patterns.FirstOrDefault(p =>
                string.Equals(p.Wmi, wmi, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Vds, vds, StringComparison.OrdinalIgnoreCase) &&
                p.YearStart == (modelYear ?? 0) &&
                p.YearEnd == (modelYear ?? 0));

            if (existing != null)
            {
                if (string.Equals(existing.Model, model, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Trim, trim, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // NHTSA changed its mind, or the first decode was partial. Newer wins.
                existing.Model = model;
                existing.Trim = trim;
            }
            else
            {
                patterns.Add(new VdsPattern
                {
                    Wmi = wmi.ToUpperInvariant(),
                    Vds = vds.ToUpperInvariant(),
                    YearStart = modelYear ?? 0,
                    YearEnd = modelYear ?? 0,
                    Model = model,
                    Trim = trim,
                    Source = "learned"
                });
            }

            Save(path, patterns);
        }
    }

    private void Save(string path, List<VdsPattern> patterns)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Only learned entries are written back; the shipped seed stays in the assembly, so
            // the overlay never ends up a stale copy of it.
            var learned = patterns.Where(p => p.Source == "learned").ToList();

            // Temp-then-rename: the appliance is switched off by pulling the plug, and a half-written
            // catalog that fails to parse would cost every learned pattern, not just the last one.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(learned, WriteOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            LastError = $"{FileName} could not be written: {ex.Message}";
        }
    }
}
