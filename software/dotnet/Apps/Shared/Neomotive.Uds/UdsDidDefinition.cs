using System.Text.Json.Serialization;

namespace Neomotive.Uds;

/// <summary>How a DID's raw bytes should be turned into text.</summary>
public enum UdsDidEncoding
{
    /// <summary>Space-separated hex bytes.</summary>
    Hex,
    /// <summary>ASCII text, trimmed of nulls and padding.</summary>
    Ascii,
    /// <summary>Big-endian unsigned integer, with optional scale, offset and units.</summary>
    UInt,
    /// <summary>A single byte looked up in <see cref="UdsDidDefinition.Values"/>.</summary>
    Enum
}

/// <summary>
/// One entry in the DID catalog: what an identifier is called and how to read its bytes.
/// This is the shape users edit in <c>uds-catalog*.json</c>.
/// </summary>
public class UdsDidDefinition
{
    /// <summary>The identifier, written as "0xF190" (a plain decimal number is also accepted).</summary>
    public string Did { get; set; } = "";

    /// <summary>Display name, e.g. "Vehicle Identification Number (VIN)".</summary>
    public string Name { get; set; } = "";

    /// <summary>How to format the value. Defaults to hex.</summary>
    public UdsDidEncoding Encoding { get; set; } = UdsDidEncoding.Hex;

    /// <summary>Expected byte count. Informational — nothing rejects an off-length value.</summary>
    public int? Length { get; set; }

    /// <summary>Unit suffix appended to a <see cref="UdsDidEncoding.UInt"/> value.</summary>
    public string? Units { get; set; }

    /// <summary>Multiplier applied to a <see cref="UdsDidEncoding.UInt"/> raw value.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Offset added after scaling a <see cref="UdsDidEncoding.UInt"/> value.</summary>
    public double Offset { get; set; }

    /// <summary>Value map for <see cref="UdsDidEncoding.Enum"/>, keyed like "0x01".</summary>
    public Dictionary<string, string>? Values { get; set; }

    /// <summary>Free-form grouping, e.g. "Identification".</summary>
    public string? Category { get; set; }

    /// <summary>Where the definition came from — a standard, a manufacturer, a capture session.</summary>
    public string? Source { get; set; }

    /// <summary>When true in an overlay file, deletes the inherited entry for this DID.</summary>
    public bool Remove { get; set; }

    /// <summary>The parsed identifier. Zero when <see cref="Did"/> cannot be read.</summary>
    [JsonIgnore]
    public ushort Identifier => UdsNumber.ParseUShort(Did);
}
