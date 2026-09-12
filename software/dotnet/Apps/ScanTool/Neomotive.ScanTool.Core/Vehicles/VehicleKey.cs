namespace Neomotive.ScanTool.Core.Vehicles;

/// <summary>
/// Derives the two keys a vehicle is remembered under, straight from the VIN — no decode, no
/// network, no catalog lookup.
/// </summary>
public static class VehicleKey
{
    /// <summary>
    /// The class key: WMI + VDS + year code, i.e. VIN characters 1-3, 4-8 and 10. A 2021 Ford
    /// Explorer ST comes out as <c>1FMSK8GCM</c>.
    /// <para>
    /// Deliberately not make/model/trim. Trim is only ever filled in by the NHTSA fallback, so on a
    /// tool sitting in a vehicle with no network it is always null, and keying on it would put
    /// every 2021 Explorer in one bucket regardless of series. The VDS is the manufacturer's own
    /// encoding of series, body and engine — it is in the VIN, it costs nothing to read, and it
    /// separates an ST from a base Explorer offline.
    /// </para>
    /// <para>
    /// Position 9 is the check digit and position 11 the plant, both deliberately excluded: the
    /// first varies per vehicle and the second says where it was built, not what it is.
    /// </para>
    /// </summary>
    public static string? ClassKeyFor(string? vin)
    {
        if (!IsUsable(vin)) return null;

        var v = vin!.ToUpperInvariant();
        return string.Concat(v.AsSpan(0, 3), v.AsSpan(3, 5), v.AsSpan(9, 1));
    }

    /// <summary>The VIN normalised for use as a record key, or null if it is not a usable VIN.</summary>
    public static string? VehicleKeyFor(string? vin)
        => IsUsable(vin) ? vin!.ToUpperInvariant() : null;

    /// <summary>
    /// Whether a VIN is worth remembering against. The VIN alphabet excludes I, O and Q, so
    /// anything containing them is a misread rather than a vehicle — and storing a misread under
    /// its own key would quietly accumulate junk records that never match anything again.
    /// </summary>
    public static bool IsUsable(string? vin)
    {
        if (string.IsNullOrWhiteSpace(vin) || vin.Length != 17) return false;

        foreach (var c in vin)
        {
            var u = char.ToUpperInvariant(c);
            if (u is 'I' or 'O' or 'Q') return false;
            if (!char.IsAsciiLetterOrDigit(u)) return false;
        }

        return true;
    }
}
