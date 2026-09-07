using Meadow.Foundation.Telematics.J1979;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>Ties a capture channel to the Mode 01 PID that supplies it.</summary>
public record CapturePidBinding(CaptureSignal Signal, Pid Pid);

/// <summary>Builds capture channels from the curated PID registry.</summary>
public static class CaptureSignalSet
{
    /// <summary>
    /// Produces one binding per PID, in the order given. Indices are assigned densely so they can
    /// address the session's signal array directly.
    /// </summary>
    /// <exception cref="InvalidOperationException">A PID has no registry descriptor.</exception>
    public static IReadOnlyList<CapturePidBinding> FromPids(IEnumerable<Pid> pids)
    {
        var bindings = new List<CapturePidBinding>();
        var index = 0;

        foreach (var pid in pids)
        {
            var descriptor = PidRegistry.CommonPids.FirstOrDefault(d => d.Id == pid)
                ?? throw new InvalidOperationException(
                    $"PID {pid} has no descriptor in PidRegistry and cannot be captured.");

            bindings.Add(new CapturePidBinding(
                new CaptureSignal(
                    index++,
                    pid.ToString(),
                    descriptor.Name,
                    descriptor.Unit,
                    descriptor.Min,
                    descriptor.Max),
                pid));
        }

        return bindings;
    }

    public static IReadOnlyList<CaptureSignal> Signals(this IEnumerable<CapturePidBinding> bindings)
        => bindings.Select(b => b.Signal).ToArray();
}
