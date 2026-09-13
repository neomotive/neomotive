using System.Collections.Generic;
using System.Linq;
using Meadow.Foundation.Telematics.Uds;

namespace Neomotive.ScanTool.Core;

public enum FaultProtocol { Obd2, Uds, Both }

/// <summary>
/// A module as the DTCs page lists it. Built by merging the legislated OBD-II view of the
/// vehicle ($03/$07) with whatever UDS discovery has confirmed ($19), because a tech asks what
/// is wrong with the vehicle, not which protocol answered.
///
/// The protocol survives as an attribute on the address line rather than as a grouping: it is a
/// fact about the module, not a category anyone navigates by. It matters only when a module is
/// reachable one way and not the other.
/// </summary>
public record FaultModule(
    string Name,
    string AddressSummary,
    FaultProtocol Protocol,
    IReadOnlyList<FaultCode> Codes,
    VehicleModule? Obd2Module,
    UdsModuleInfo? UdsModule)
{
    public int CodeCount => Codes.Count;
    public bool HasFaults => CodeCount > 0;
    public bool HasNoFaults => !HasFaults;

    /// <summary>Only UDS modules carry identification DIDs and can answer a DID read.</summary>
    public bool SpeaksUds => UdsModule != null;

    public string ProtocolText => Protocol switch
    {
        FaultProtocol.Obd2 => "OBD-II",
        FaultProtocol.Uds  => UdsModule?.AddressingText is { } w ? $"UDS {w}" : "UDS",
        _                  => "OBD-II + UDS",
    };

    public string AddressLine => $"{AddressSummary} · {ProtocolText}";

    /// <summary>
    /// Merges the two module views into one list. A module that answers on both protocols
    /// becomes a single row: the ECM's powertrain codes come back on $03 and again on $19, and
    /// listing it twice invites a tech to read one fault as two.
    ///
    /// Matching is by request address — an OBD-II module's address against the UDS module's TxId.
    /// Codes from a merged pair are unioned by code string, preferring the UDS projection because
    /// it carries the status mask and the fault-type byte that $03 has no room for.
    /// </summary>
    public static IReadOnlyList<FaultModule> Merge(
        IReadOnlyList<ModuleDtcGroup> obd2Groups,
        IReadOnlyList<UdsModuleInfo> udsModules)
    {
        var merged = new List<FaultModule>();
        var claimed = new HashSet<UdsModuleInfo>();

        foreach (var group in obd2Groups)
        {
            var match = udsModules.FirstOrDefault(m => m.TxId == group.Module.Address);
            var obd2Codes = group.StoredDtcs.Concat(group.PendingDtcs).Select(FaultCode.FromObd2);

            if (match == null)
            {
                merged.Add(new FaultModule(
                    group.Module.Name,
                    group.Module.AddressHex,
                    FaultProtocol.Obd2,
                    obd2Codes.ToList(),
                    group.Module,
                    null));
                continue;
            }

            claimed.Add(match);

            var udsCodes = match.Dtcs.Select(FaultCode.FromUds).ToList();
            var seen = udsCodes.Select(c => c.Code).ToHashSet();
            var codes = udsCodes.Concat(obd2Codes.Where(c => !seen.Contains(c.Code))).ToList();

            merged.Add(new FaultModule(
                match.Name,
                match.AddressSummary,
                FaultProtocol.Both,
                codes,
                group.Module,
                match));
        }

        foreach (var module in udsModules.Where(m => !claimed.Contains(m)))
        {
            merged.Add(new FaultModule(
                module.Name,
                module.AddressSummary,
                FaultProtocol.Uds,
                module.Dtcs.Select(FaultCode.FromUds).ToList(),
                null,
                module));
        }

        // Faulted modules first: on a panel that shows six rows at a time, the module with codes
        // is the reason the tech opened the page.
        return merged
            .OrderByDescending(m => m.HasFaults)
            .ThenBy(m => m.Name)
            .ToList();
    }
}
