using System;
using System.Collections.Generic;

namespace Neomotive.ScanTool.Core.Signals;

/// <summary>Where a signal's bytes come from.</summary>
public enum SignalSource
{
    /// <summary>Standard OBD-II Mode $01 live data. <see cref="SignalDefinition.Address"/> is the PID.</summary>
    Mode01,

    /// <summary>UDS / Mode $22 ReadDataByIdentifier. <see cref="SignalDefinition.Address"/> is the DID.</summary>
    Mode22,
}

/// <summary>
/// One readable value, described entirely as data.
/// </summary>
/// <remarks>
/// Mode $01 and Mode $22 share this shape deliberately. The only difference between them is where
/// the bytes come from; the field extraction and scaling are identical, so there is no reason for
/// two formats.
/// <para>
/// Addressing is by <em>numeric</em> PID/DID rather than by an enum name, so the table can describe
/// signals the J1979 enum never named — manufacturer-specific channels included.
/// </para>
/// <para>
/// <see cref="ByteOffset"/> is relative to the start of the <em>data</em> bytes: for Mode $01 that
/// is after the service and PID echo, for Mode $22 after the echoed DID. This is what lets one PID
/// carry several signals — an oxygen sensor PID returning voltage in byte A and fuel trim in byte
/// B is simply two definitions sharing an address.
/// </para>
/// </remarks>
public record SignalDefinition
{
    /// <summary>Stable identifier, used in captures, triggers and profiles.</summary>
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Compact label for gauges and chart lanes. Falls back to <see cref="Name"/>.</summary>
    public string? ShortName { get; init; }

    public string Unit { get; init; } = string.Empty;

    /// <summary>
    /// Systems this signal belongs to. Deliberately a list: rail pressure is both Fuel and Engine,
    /// and forcing a single parent is what makes a strict tree awkward to navigate.
    /// </summary>
    public IReadOnlyList<string> Systems { get; init; } = Array.Empty<string>();

    /// <summary>Extra search terms — abbreviations and common names the label does not contain.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public SignalSource Source { get; init; } = SignalSource.Mode01;

    /// <summary>Mode $01 PID or Mode $22 DID.</summary>
    public int Address { get; init; }

    public int ByteOffset { get; init; }

    /// <summary>Width in bytes, 1-4, big-endian.</summary>
    public int ByteLength { get; init; } = 1;

    public bool Signed { get; init; }

    public double Scale { get; init; } = 1;

    public double Offset { get; init; }

    public double Min { get; init; }

    public double Max { get; init; } = 100;

    /// <summary>Decimal places for display.</summary>
    public int Decimals { get; init; } = 1;

    /// <summary>Request address for Mode $22; ignored for Mode $01.</summary>
    public ushort TxId { get; init; } = 0x7E0;

    /// <summary>Response address for Mode $22; ignored for Mode $01.</summary>
    public ushort RxId { get; init; } = 0x7E8;

    public string DisplayName => string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName!;

    /// <summary>Human-readable address, e.g. "PID 0x0C" or "DID 0x1234".</summary>
    public string AddressText => Source == SignalSource.Mode01
        ? $"PID 0x{Address:X2}"
        : $"DID 0x{Address:X4}";

    /// <summary>
    /// Just the number, e.g. "0x0C". Used as the leading column in the picker: technicians
    /// usually arrive knowing the PID they want, so it is the first thing to scan for.
    /// </summary>
    public string ShortAddressText => Source == SignalSource.Mode01
        ? $"0x{Address:X2}"
        : $"0x{Address:X4}";

    /// <summary>
    /// Extracts and scales this signal from a response's data bytes, or null when the response is
    /// too short to contain the field.
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
            var signBit = 1L << (ByteLength * 8 - 1);

            if ((raw & signBit) != 0)
            {
                raw -= 1L << (ByteLength * 8);
            }
        }

        return raw * Scale + Offset;
    }

    /// <summary>True when the search text matches the name, key, unit, address or a tag.</summary>
    public bool Matches(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var term = search.Trim();

        if (Contains(Name) || Contains(Key) || Contains(ShortName) || Contains(Unit))
        {
            return true;
        }

        // Let people search by the number they are looking at in a service manual, with or
        // without the 0x, and by the rendered form.
        if (Contains(AddressText)
            || Address.ToString("X2").Contains(term.TrimStart('0', 'x', 'X'), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var tag in Tags)
        {
            if (Contains(tag))
            {
                return true;
            }
        }

        foreach (var system in Systems)
        {
            if (Contains(system))
            {
                return true;
            }
        }

        return false;

        bool Contains(string? value)
            => value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Name;
}
