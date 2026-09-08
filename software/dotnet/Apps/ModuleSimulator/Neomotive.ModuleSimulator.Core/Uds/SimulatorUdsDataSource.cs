using Meadow.Foundation.Telematics.Uds;
using Neomotive.Uds;
using System.Text;

namespace Neomotive.ModuleSimulator;

/// <summary>
/// Serves one simulated module's UDS data: static values from its config profile, plus — for the
/// PCM and TCU — a live mirror of the DTCs the simulator itself is holding, so a fault set from
/// the toolbox shows up over OBD-II and UDS at once.
/// </summary>
public class SimulatorUdsDataSource : IUdsDataSource
{
    private readonly UdsModuleProfile _profile;
    private readonly ISimulatorDtcStore? _mirror;
    private readonly Func<string>? _vin;
    private readonly Action? _onClear;
    private readonly UdsCatalog _catalog;

    /// <inheritdoc/>
    public UdsDtcStatusMask AvailabilityMask { get; set; } =
        UdsDtcStatusMask.TestFailed |
        UdsDtcStatusMask.PendingDtc |
        UdsDtcStatusMask.ConfirmedDtc |
        UdsDtcStatusMask.TestFailedSinceLastClear |
        UdsDtcStatusMask.WarningIndicatorRequested;

    /// <summary>
    /// Creates a data source for one module.
    /// </summary>
    /// <param name="profile">The module's configured identity, DIDs and static faults.</param>
    /// <param name="mirror">The simulator DTC store to reflect, when the profile asks for it.</param>
    /// <param name="vin">Supplies the live VIN for DID $F190.</param>
    /// <param name="onClear">Invoked after a UDS clear so the host can resync its OBD-II modules.</param>
    /// <param name="catalog">Decides a DID's encoding when the profile value carries no prefix.</param>
    public SimulatorUdsDataSource(
        UdsModuleProfile profile,
        ISimulatorDtcStore? mirror = null,
        Func<string>? vin = null,
        Action? onClear = null,
        UdsCatalog? catalog = null)
    {
        _profile = profile;
        _mirror = profile.MirrorSimulatorDtcs ? mirror : null;
        _vin = vin;
        _onClear = onClear;
        _catalog = catalog ?? UdsCatalog.Shared;
    }

    /// <inheritdoc/>
    public IReadOnlyList<UdsDtcRecord> GetDtcs()
    {
        // Keyed so a mirrored fault and a static one for the same code do not double up.
        var records = new Dictionary<(byte, byte, byte), UdsDtcRecord>();

        void Add(UdsDtcRecord? record)
        {
            if (record == null) return;
            var key = (record.High, record.Mid, record.FaultType);
            if (records.TryGetValue(key, out var existing))
            {
                // Same fault reported twice — union the status bits rather than pick a winner.
                records[key] = existing with { Status = existing.Status | record.Status };
            }
            else
            {
                records[key] = record;
            }
        }

        if (_mirror != null)
        {
            foreach (var raw in _mirror.StoredDtcs.Values)
                Add(FromRaw(raw, UdsDtcStatusMask.ConfirmedDtc | UdsDtcStatusMask.TestFailed));

            foreach (var raw in _mirror.PendingDtcs.Values)
                Add(FromRaw(raw, UdsDtcStatusMask.PendingDtc));

            foreach (var raw in _mirror.PermanentDtcs.Values)
                Add(FromRaw(raw, UdsDtcStatusMask.ConfirmedDtc | UdsDtcStatusMask.TestFailedSinceLastClear));
        }

        foreach (var dtc in _profile.Dtcs)
            Add(UdsDtcRecord.FromCode(dtc.Code, dtc.ParseStatus()));

        return [.. records.Values];
    }

    /// <inheritdoc/>
    public bool TryGetDid(ushort did, out byte[] data)
    {
        foreach (var (key, value) in _profile.Dids)
        {
            if (UdsNumber.ParseUShort(key) != did) continue;
            data = EncodeValue(did, value);
            return true;
        }

        // The VIN is live rather than configured — the simulator's VIN can change at runtime.
        if (did == 0xF190 && _vin != null)
        {
            data = Encoding.ASCII.GetBytes(_vin());
            return true;
        }

        // A module with no configured system name still answers with what it is called.
        if (did == 0xF197 && !string.IsNullOrWhiteSpace(_profile.Name))
        {
            data = Encoding.ASCII.GetBytes(_profile.Name);
            return true;
        }

        data = [];
        return false;
    }

    /// <inheritdoc/>
    public void ClearDtcs()
    {
        if (_mirror != null)
        {
            _mirror.StoredDtcs.Clear();
            _mirror.PendingDtcs.Clear();
            _mirror.PermanentDtcs.Clear();
        }

        _profile.Dtcs.Clear();
        _onClear?.Invoke();
    }

    /// <summary>
    /// Turns a configured DID value into bytes. An explicit <c>ascii:</c> or <c>hex:</c> prefix wins;
    /// otherwise the catalog's encoding for that DID decides, defaulting to ASCII.
    /// </summary>
    private byte[] EncodeValue(ushort did, string value)
    {
        if (value.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
            return UdsNumber.ParseHexBytes(value[4..]);

        if (value.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
            return Encoding.ASCII.GetBytes(value[6..]);

        return _catalog.Find(did)?.Encoding == UdsDidEncoding.Hex && LooksLikeHex(value)
            ? UdsNumber.ParseHexBytes(value)
            : Encoding.ASCII.GetBytes(value);
    }

    private static bool LooksLikeHex(string value)
    {
        var cleaned = value.Replace(" ", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        return cleaned.Length > 0 && cleaned.All(Uri.IsHexDigit);
    }

    private static UdsDtcRecord? FromRaw(byte[] raw, UdsDtcStatusMask status)
        => raw.Length >= 2 ? new UdsDtcRecord(raw[0], raw[1], 0x00, status) : null;
}
