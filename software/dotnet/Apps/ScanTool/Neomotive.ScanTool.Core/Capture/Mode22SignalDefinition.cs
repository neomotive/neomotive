using System;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// A user-defined UDS / Mode $22 channel: where the value lives inside a Data Identifier's
/// response and how to scale it.
/// </summary>
/// <remarks>
/// Mode $22 identifiers are manufacturer-specific and are not published in any standard, which is
/// why these are configuration rather than code. The channels that matter most on a common-rail
/// diesel — <em>commanded</em> rail pressure and fuel control actuator duty — live here, and
/// commanded-versus-actual is the pair that separates a mechanical fuel-supply fault from
/// everything else.
/// </remarks>
public record Mode22SignalDefinition
{
    /// <summary>Stable identifier used in the capture CSV and in trigger configuration.</summary>
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    /// <summary>The Data Identifier to request, e.g. 0x1234.</summary>
    public ushort Did { get; init; }

    /// <summary>Offset of the value within the DID's data bytes (after the echoed DID).</summary>
    public int ByteOffset { get; init; }

    /// <summary>Width of the value in bytes, 1-4, big-endian.</summary>
    public int ByteLength { get; init; } = 2;

    /// <summary>Whether the raw value is two's-complement signed.</summary>
    public bool Signed { get; init; }

    public double Scale { get; init; } = 1;

    public double Offset { get; init; }

    public double Min { get; init; }

    public double Max { get; init; } = 100;

    /// <summary>Request address; 0x7E0 is the engine ECU on most vehicles.</summary>
    public ushort TxId { get; init; } = 0x7E0;

    /// <summary>Response address; 0x7E8 pairs with 0x7E0.</summary>
    public ushort RxId { get; init; } = 0x7E8;

    /// <summary>
    /// Decodes the data bytes of a DID response into an engineering value, or null if the response
    /// is too short to contain the configured field.
    /// </summary>
    public double? Decode(byte[]? data)
    {
        if (data is null || ByteLength is < 1 or > 4)
        {
            return null;
        }

        if (ByteOffset < 0 || ByteOffset + ByteLength > data.Length)
        {
            return null;
        }

        var raw = 0L;

        for (var i = 0; i < ByteLength; i++)
        {
            raw = (raw << 8) | data[ByteOffset + i];
        }

        if (Signed)
        {
            // Sign-extend from the field's width.
            var signBit = 1L << (ByteLength * 8 - 1);

            if ((raw & signBit) != 0)
            {
                raw -= 1L << (ByteLength * 8);
            }
        }

        return raw * Scale + Offset;
    }

    public string Describe() => $"DID 0x{Did:X4}[{ByteOffset}..{ByteOffset + ByteLength - 1}]";
}
