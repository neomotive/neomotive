using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neomotive.ScanTool.Core.Signals;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>Supplies one channel's value. Hides whether it comes from Mode $01 or Mode $22.</summary>
public interface ICaptureChannelSource
{
    Task<double?> ReadAsync(CancellationToken ct);

    /// <summary>Short description for the capture sidecar.</summary>
    string Describe();
}

/// <summary>A capture channel: what it is, and where its value comes from.</summary>
public record CaptureChannel(CaptureSignal Signal, ICaptureChannelSource Source);

/// <summary>Reads a standard Mode $01 PID and decodes one field from it.</summary>
public sealed class PidChannelSource : ICaptureChannelSource
{
    private readonly IObd2Scanner _scanner;
    private readonly SignalDefinition _definition;

    public PidChannelSource(IObd2Scanner scanner, SignalDefinition definition)
    {
        _scanner = scanner;
        _definition = definition;
    }

    public async Task<double?> ReadAsync(CancellationToken ct)
        => _definition.Decode(await _scanner.ReadPidDataAsync((byte)_definition.Address, ct));

    public string Describe() => _definition.AddressText;
}

/// <summary>Reads a UDS / Mode $22 Data Identifier and decodes one field from it.</summary>
public sealed class Mode22ChannelSource : ICaptureChannelSource
{
    private readonly IUdsScanner _uds;
    private readonly SignalDefinition _definition;

    public Mode22ChannelSource(IUdsScanner uds, SignalDefinition definition)
    {
        _uds = uds;
        _definition = definition;
    }

    public async Task<double?> ReadAsync(CancellationToken ct)
    {
        var value = await _uds.ReadDidAsync(
            _definition.TxId, _definition.RxId, (ushort)_definition.Address, ct);

        return value is null ? null : _definition.Decode(value.RawBytes);
    }

    public string Describe() => _definition.AddressText;
}

/// <summary>
/// Assembles a capture's channel list, assigning dense indices as channels are added.
/// </summary>
/// <remarks>
/// Indices must be dense and stable because samples carry an index rather than a name — that is
/// what keeps the rolling buffer allocation-free.
/// </remarks>
public sealed class CaptureChannelSetBuilder
{
    private readonly List<CaptureChannel> _channels = new();

    /// <summary>
    /// Adds a signal, routing it to the right transport for its source. Returns false when the
    /// signal needs a UDS client and none was supplied.
    /// </summary>
    public bool TryAdd(SignalDefinition definition, IObd2Scanner scanner, IUdsScanner? uds)
    {
        if (string.IsNullOrWhiteSpace(definition.Key))
        {
            throw new InvalidOperationException("A signal definition needs a Key.");
        }

        ICaptureChannelSource source;

        switch (definition.Source)
        {
            case SignalSource.Mode22 when uds is null:
                return false;

            case SignalSource.Mode22:
                source = new Mode22ChannelSource(uds, definition);
                break;

            default:
                source = new PidChannelSource(scanner, definition);
                break;
        }

        _channels.Add(new CaptureChannel(
            new CaptureSignal(
                _channels.Count,
                definition.Key,
                definition.Name,
                definition.Unit,
                definition.Min,
                definition.Max),
            source));

        return true;
    }

    public IReadOnlyList<CaptureChannel> Build() => _channels.ToArray();
}

public static class CaptureChannelSet
{
    /// <summary>Builds channels for a set of signals, in the order given.</summary>
    public static IReadOnlyList<CaptureChannel> From(
        IObd2Scanner scanner,
        IEnumerable<SignalDefinition> definitions,
        IUdsScanner? uds = null)
    {
        var builder = new CaptureChannelSetBuilder();

        foreach (var definition in definitions)
        {
            builder.TryAdd(definition, scanner, uds);
        }

        return builder.Build();
    }

    public static IReadOnlyList<CaptureSignal> Signals(this IEnumerable<CaptureChannel> channels)
        => channels.Select(c => c.Signal).ToArray();
}
