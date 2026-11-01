using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Neomotive.Obd2;

/// <summary>
/// Looks up human-readable descriptions for SAE J2012 / ISO 15031-6 DTC codes and OEM-specific codes.
/// Loaded from an embedded CSV resource aggregated from SAE J2012DA, foerbsnavi/OBDex, obdb, and Wal33D/dtc-database.
/// Localized overrides and external overlays are supported by placing a dtc-descriptions.{locale}.csv
/// or dtc-descriptions*.csv file in the application data/config directory.
/// </summary>
public static class DtcDescriptions
{
    private static Lazy<DtcTable> _table = new(Load);

    /// <summary>
    /// Current vehicle manufacturer/make (e.g. "Ford", "Chevrolet", "Toyota") used to resolve
    /// manufacturer-specific codes (e.g. P1xxx, B1xxx, C1xxx, U1xxx/U2xxx).
    /// </summary>
    public static string? CurrentMake { get; set; }

    /// <summary>
    /// Optional directory to search for external DTC overlay CSV files (e.g. dtc-descriptions*.csv).
    /// </summary>
    public static string? OverlayDirectory { get; set; }

    /// <summary>Forces reloading of the DTC table.</summary>
    public static void Reload() => _table = new Lazy<DtcTable>(Load);

    /// <summary>Returns the description for the given DTC code, or empty string if unknown.</summary>
    public static string Lookup(string code) => Lookup(code, CurrentMake);

    /// <summary>
    /// Returns the description for the given DTC code, optionally scoped to a vehicle manufacturer,
    /// or empty string if unknown. Automatically handles FTB sub-type suffixes (e.g. "P0011-00" -> "P0011").
    /// </summary>
    public static string Lookup(string code, string? make)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;

        var cleanCode = code.Trim().ToUpperInvariant();
        var baseCode = cleanCode;
        var dashIndex = cleanCode.IndexOf('-');
        if (dashIndex > 0)
        {
            baseCode = cleanCode[..dashIndex].Trim();
        }

        var targetMake = !string.IsNullOrWhiteSpace(make) ? make : CurrentMake;
        return _table.Value.Lookup(baseCode, targetMake, cleanCode);
    }

    private static DtcTable Load()
    {
        var table = new DtcTable();
        var asm = typeof(DtcDescriptions).Assembly;
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var localizedResource = $"Neomotive.Obd2.Resources.dtc-descriptions.{culture}.csv";
        var defaultResource   = "Neomotive.Obd2.Resources.dtc-descriptions.csv";

        var stream = asm.GetManifestResourceStream(localizedResource)
                  ?? asm.GetManifestResourceStream(defaultResource);

        if (stream != null)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            LoadFromReader(reader, table);
        }

        // Overlay external files if present
        if (!string.IsNullOrWhiteSpace(OverlayDirectory) && Directory.Exists(OverlayDirectory))
        {
            try
            {
                var files = Directory.GetFiles(OverlayDirectory, "dtc-descriptions*.csv")
                    .Concat(Directory.GetFiles(OverlayDirectory, "dtc-database*.csv"));

                foreach (var file in files)
                {
                    try
                    {
                        using var reader = new StreamReader(file, Encoding.UTF8);
                        LoadFromReader(reader, table);
                    }
                    catch
                    {
                        // Ignore individual external file read failures
                    }
                }
            }
            catch
            {
                // Ignore directory enumeration failures
            }
        }

        return table;
    }

    private static void LoadFromReader(TextReader reader, DtcTable table)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (TryParseLine(line, out var code, out var desc, out var make))
            {
                table.Add(code, desc, make);
            }
        }
    }

    /// <summary>Parses standard CSV lines supporting quotes: CODE,Description[,Make]</summary>
    internal static bool TryParseLine(string line, out string code, out string desc, out string? make)
    {
        code = "";
        desc = "";
        make = null;

        if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
            return false;

        var fields = new List<string>(3);
        var inQuotes = false;
        var sb = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString().Trim());

        if (fields.Count < 2 || string.IsNullOrEmpty(fields[0]))
            return false;

        code = fields[0];
        desc = fields[1];
        if (fields.Count >= 3 && !string.IsNullOrWhiteSpace(fields[2]))
        {
            make = fields[2];
        }

        return true;
    }
}

/// <summary>
/// Storage structure separating generic SAE standard codes from manufacturer-specific codes.
/// </summary>
public sealed class DtcTable
{
    private readonly Dictionary<string, string> _generic = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _byMake = new(StringComparer.OrdinalIgnoreCase);

    public int GenericCount => _generic.Count;
    public int MakeCount => _byMake.Count;

    public void Add(string code, string description, string? make = null)
    {
        if (string.IsNullOrWhiteSpace(make) || string.Equals(make, "GENERIC", StringComparison.OrdinalIgnoreCase))
        {
            _generic[code] = description;
        }
        else
        {
            var normalizedMake = NormalizeMake(make);
            if (!_byMake.TryGetValue(normalizedMake, out var makeDict))
            {
                makeDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _byMake[normalizedMake] = makeDict;
            }
            makeDict[code] = description;
        }
    }

    public string Lookup(string baseCode, string? make, string originalCode)
    {
        if (!string.IsNullOrWhiteSpace(make))
        {
            var normalized = NormalizeMake(make);
            if (_byMake.TryGetValue(normalized, out var makeDict))
            {
                if (makeDict.TryGetValue(baseCode, out var makeDesc)) return makeDesc;
                if (makeDict.TryGetValue(originalCode, out makeDesc)) return makeDesc;
            }
        }

        if (_generic.TryGetValue(baseCode, out var genDesc)) return genDesc;
        if (_generic.TryGetValue(originalCode, out genDesc)) return genDesc;

        return string.Empty;
    }

    public static string NormalizeMake(string make)
    {
        var upper = make.Trim().ToUpperInvariant();
        if (upper.Contains("FORD") || upper.Contains("LINCOLN") || upper.Contains("MERCURY")) return "FORD";
        if (upper.Contains("CHEV") || upper.Contains("GMC") || upper.Contains("CADILLAC") ||
            upper.Contains("BUICK") || upper.Contains("PONTIAC") || upper.Contains("SATURN") ||
            upper.Contains("OLDSMOBILE") || upper == "GM") return "GM";
        if (upper.Contains("CHRYSLER") || upper.Contains("DODGE") || upper.Contains("JEEP") || upper.Contains("RAM")) return "CHRYSLER";
        if (upper.Contains("TOYOTA") || upper.Contains("LEXUS") || upper.Contains("SCION")) return "TOYOTA";
        if (upper.Contains("HONDA") || upper.Contains("ACURA")) return "HONDA";
        if (upper.Contains("NISSAN") || upper.Contains("INFINITI")) return "NISSAN";
        if (upper.Contains("VOLKSWAGEN") || upper.Contains("AUDI") || upper == "VW") return "VOLKSWAGEN";
        if (upper.Contains("BMW") || upper.Contains("MINI")) return "BMW";
        return upper;
    }
}
