using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Neomotive.Obd2;

/// <summary>
/// Looks up human-readable descriptions for SAE J2012 / ISO 15031-6 DTC codes.
/// Loaded from an embedded CSV resource; localized overrides are supported by placing
/// a dtc-descriptions.{locale}.csv alongside the default file.
/// </summary>
public static class DtcDescriptions
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> _table = new(Load);

    private static IReadOnlyDictionary<string, string> Load()
    {
        var asm = typeof(DtcDescriptions).Assembly;
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var localizedResource = $"Neomotive.Obd2.Resources.dtc-descriptions.{culture}.csv";
        var defaultResource   = "Neomotive.Obd2.Resources.dtc-descriptions.csv";

        var stream = asm.GetManifestResourceStream(localizedResource)
                  ?? asm.GetManifestResourceStream(defaultResource);

        if (stream == null) return new Dictionary<string, string>();

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;
            var sep = line.IndexOf(',');
            if (sep < 0) continue;
            var code = line[..sep].Trim();
            var desc = line[(sep + 1)..].Trim();
            if (!string.IsNullOrEmpty(code))
                dict[code] = desc;
        }
        return dict;
    }

    /// <summary>Returns the description for the given DTC code, or empty string if unknown.</summary>
    public static string Lookup(string code) =>
        _table.Value.TryGetValue(code, out var desc) ? desc : string.Empty;
}
