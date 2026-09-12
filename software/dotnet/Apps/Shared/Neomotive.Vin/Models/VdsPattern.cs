namespace Neomotive.Vin.Models;

/// <summary>
/// Maps a WMI plus a VDS pattern to a model and trim, so a VIN can be resolved past Make without
/// a network call.
/// <para>
/// NHTSA's public endpoints hand out manufacturers and model *names*, but the VDS-to-model
/// patterns that turn one into the other live only behind their server-side decoder — there is no
/// bulk feed to embed. So the table is built two ways: shipped entries a human wrote, and entries
/// learned automatically the first time a VIN of that shape is decoded online. Either way the
/// second vehicle of a given type decodes offline, which on a tool that lives in a vehicle with no
/// network is the case that matters.
/// </para>
/// </summary>
public sealed class VdsPattern
{
    /// <summary>World Manufacturer Identifier — VIN characters 1-3.</summary>
    public string Wmi { get; set; } = "";

    /// <summary>
    /// Five characters matched against VIN positions 4-8, where <c>?</c> matches any character.
    /// A pattern with fewer wildcards beats one with more, so a learned exact VDS always wins over
    /// a hand-written broad rule.
    /// </summary>
    public string Vds { get; set; } = "";

    /// <summary>First model year this pattern applies to; 0 means no lower bound.</summary>
    public int YearStart { get; set; }

    /// <summary>Last model year this pattern applies to; 0 or 9999 means no upper bound.</summary>
    public int YearEnd { get; set; }

    public string? Model { get; set; }

    public string? Trim { get; set; }

    /// <summary>
    /// Where the entry came from — <c>shipped</c> for the embedded seed, <c>learned</c> for one
    /// recorded from an NHTSA decode. Kept so a bad learned entry can be told apart from a
    /// deliberate one when the file is inspected by hand.
    /// </summary>
    public string Source { get; set; } = "shipped";

    /// <summary>Specificity: the fewer wildcards, the more exactly this pattern was meant.</summary>
    public int WildcardCount
    {
        get
        {
            var count = 0;
            foreach (var c in Vds)
            {
                if (c == '?') count++;
            }
            return count;
        }
    }

    /// <summary>Whether this pattern covers the given VIN fragment and model year.</summary>
    public bool Matches(string wmi, string vds, int? modelYear)
    {
        if (!string.Equals(Wmi, wmi, StringComparison.OrdinalIgnoreCase)) return false;
        if (Vds.Length != vds.Length) return false;

        for (var i = 0; i < vds.Length; i++)
        {
            var p = Vds[i];
            if (p == '?') continue;
            if (char.ToUpperInvariant(p) != char.ToUpperInvariant(vds[i])) return false;
        }

        // A pattern with no year bounds applies to every year; one with bounds has to contain the
        // year. An undated VIN falls back to the pattern rather than being rejected by it.
        if (modelYear is not { } year) return true;
        if (YearStart > 0 && year < YearStart) return false;
        if (YearEnd is > 0 and < 9999 && year > YearEnd) return false;

        return true;
    }
}
