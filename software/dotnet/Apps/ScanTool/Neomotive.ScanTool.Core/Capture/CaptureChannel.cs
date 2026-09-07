using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meadow.Foundation.Telematics.J1979;

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

/// <summary>Reads a standard Mode $01 PID.</summary>
public sealed class PidChannelSource : ICaptureChannelSource
{
    private readonly IObd2Scanner _scanner;
    private readonly Pid _pid;

    public PidChannelSource(IObd2Scanner scanner, Pid pid)
    {
        _scanner = scanner;
        _pid = pid;
    }

    public async Task<double?> ReadAsync(CancellationToken ct)
        => (await _scanner.ReadPidAsync(_pid, ct))?.Value;

    public string Describe() => $"Mode $01 PID 0x{(byte)_pid:X2}";
}

/// <summary>Reads a user-defined UDS / Mode $22 Data Identifier.</summary>
public sealed class Mode22ChannelSource : ICaptureChannelSource
{
    private readonly IUdsScanner _uds;
    private readonly Mode22SignalDefinition _definition;

    public Mode22ChannelSource(IUdsScanner uds, Mode22SignalDefinition definition)
    {
        _uds = uds;
        _definition = definition;
    }

    public async Task<double?> ReadAsync(CancellationToken ct)
    {
        var value = await _uds.ReadDidAsync(_definition.TxId, _definition.RxId, _definition.Did, ct);
        return value is null ? null : _definition.Decode(value.RawBytes);
    }

    public string Describe() => $"Mode $22 {_definition.Describe()}";
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

    public CaptureChannelSetBuilder AddPid(IObd2Scanner scanner, Pid pid)
    {
        var descriptor = PidRegistry.CommonPids.FirstOrDefault(d => d.Id == pid)
            ?? throw new InvalidOperationException(
                $"PID {pid} has no descriptor in PidRegistry and cannot be captured.");

        _channels.Add(new CaptureChannel(
            new CaptureSignal(
                _channels.Count,
                pid.ToString(),
                descriptor.Name,
                descriptor.Unit,
                descriptor.Min,
                descriptor.Max),
            new PidChannelSource(scanner, pid)));

        return this;
    }

    public CaptureChannelSetBuilder AddMode22(IUdsScanner uds, Mode22SignalDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Key))
        {
            throw new InvalidOperationException("A Mode $22 signal definition needs a Key.");
        }

        _channels.Add(new CaptureChannel(
            new CaptureSignal(
                _channels.Count,
                definition.Key,
                string.IsNullOrWhiteSpace(definition.Name) ? definition.Key : definition.Name,
                definition.Unit,
                definition.Min,
                definition.Max),
            new Mode22ChannelSource(uds, definition)));

        return this;
    }

    public IReadOnlyList<CaptureChannel> Build() => _channels.ToArray();
}

public static class CaptureChannelSet
{
    /// <summary>Builds channels for a set of standard PIDs, in the order given.</summary>
    public static IReadOnlyList<CaptureChannel> FromPids(IObd2Scanner scanner, IEnumerable<Pid> pids)
    {
        var builder = new CaptureChannelSetBuilder();

        foreach (var pid in pids)
        {
            builder.AddPid(scanner, pid);
        }

        return builder.Build();
    }

    public static IReadOnlyList<CaptureSignal> Signals(this IEnumerable<CaptureChannel> channels)
        => channels.Select(c => c.Signal).ToArray();
}
