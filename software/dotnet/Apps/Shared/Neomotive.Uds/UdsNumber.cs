using System.Globalization;

namespace Neomotive.Uds;

/// <summary>
/// Number parsing for catalog files, where identifiers are written "0xF190" but a bare decimal
/// or bare hex ("F190") should work too — people hand-edit these files.
/// </summary>
public static class UdsNumber
{
    /// <summary>Parses a catalog number as a 16-bit value. Returns 0 when unparseable.</summary>
    public static ushort ParseUShort(string? text)
        => TryParse(text, out var value) && value <= ushort.MaxValue ? (ushort)value : (ushort)0;

    /// <summary>Parses a catalog number as a byte. Returns 0 when unparseable.</summary>
    public static byte ParseByte(string? text)
        => TryParse(text, out var value) && value <= byte.MaxValue ? (byte)value : (byte)0;

    /// <summary>Parses "0x1A", "1A" or "26" into a number.</summary>
    public static bool TryParse(string? text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim();

        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        if (uint.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;

        return uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Parses a hex byte string such as "01 02 0A" or "01020A".</summary>
    public static byte[] ParseHexBytes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var cleaned = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                          .Replace(",", " ")
                          .Replace("-", " ")
                          .Trim();

        if (cleaned.Contains(' '))
        {
            var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>(parts.Length);
            foreach (var part in parts)
            {
                if (byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    bytes.Add(b);
            }
            return [.. bytes];
        }

        if (cleaned.Length % 2 != 0) return [];

        var result = new byte[cleaned.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            if (!byte.TryParse(cleaned.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
                return [];
        }
        return result;
    }
}
