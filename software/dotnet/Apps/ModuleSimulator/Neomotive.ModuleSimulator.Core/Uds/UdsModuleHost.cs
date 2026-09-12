using Meadow.Foundation.Telematics.Uds;
using Meadow.Hardware;
using Neomotive.Uds;

namespace Neomotive.ModuleSimulator;

/// <summary>
/// Runs the simulator's UDS side: one <see cref="UdsServer"/> per configured module, all sharing
/// the CAN bus the J1979 modules already use.
/// </summary>
/// <remarks>
/// Modules beyond the PCM and TCU are UDS-only — they answer $19/$22 but stay silent on OBD-II,
/// which is how most body and chassis controllers actually behave.
/// </remarks>
public class UdsModuleHost : IDisposable
{
    private readonly List<UdsServer> _servers = [];

    /// <summary>The running servers, in configuration order.</summary>
    public IReadOnlyList<UdsServer> Servers => _servers;

    /// <summary>The data sources backing the servers, keyed by response address.</summary>
    public IReadOnlyDictionary<ushort, SimulatorUdsDataSource> Sources => _sources;

    private readonly Dictionary<ushort, SimulatorUdsDataSource> _sources = [];

    /// <summary>
    /// Starts a server for every enabled module in the config.
    /// </summary>
    /// <param name="bus">The bus to serve on — pass the same instance the J1979 modules use.</param>
    /// <param name="config">Which modules to run and what they report.</param>
    /// <param name="pcmState">Backs the PCM's mirrored DTCs and its live VIN.</param>
    /// <param name="tcuState">Backs the TCU's mirrored DTCs.</param>
    /// <param name="onDtcsCleared">Invoked after a UDS clear, so the host can resync its OBD-II modules.</param>
    /// <param name="catalog">DID catalog used to resolve encodings. Defaults to the shared one.</param>
    public void Start(
        ICanBus bus,
        UdsConfig config,
        SimulatorState? pcmState = null,
        SimulatorTcuState? tcuState = null,
        Action? onDtcsCleared = null,
        UdsCatalog? catalog = null)
    {
        Stop();

        if (!config.Enabled) return;

        foreach (var profile in config.Modules.Where(m => m.Enabled))
        {
            // Only the PCM and TCU have simulator state behind them; everything else is
            // profile-driven, which is what makes extra modules a config edit.
            ISimulatorDtcStore? mirror = profile.ResponseId switch
            {
                0x7E8 => pcmState,
                0x7E9 => tcuState,
                _ => null
            };

            Func<string>? vin = profile.ResponseId == 0x7E8 && pcmState != null
                ? () => pcmState.Vin
                : null;

            var source = new SimulatorUdsDataSource(profile, mirror, vin, onDtcsCleared, catalog);
            _sources[profile.ResponseId] = source;

            // Addressing width is a config field, so a 29-bit body controller is a JSON edit —
            // which is what makes the extended discovery tier testable without a vehicle.
            var address = profile.Extended
                ? UdsAddress.NormalFixed(profile.EcuAddress)
                : UdsAddress.Standard((uint)(profile.ResponseId - 8), profile.ResponseId);

            _servers.Add(new UdsServer([bus], address, source));
        }
    }

    /// <summary>Stops and releases every server.</summary>
    public void Stop()
    {
        foreach (var server in _servers) server.Dispose();
        _servers.Clear();
        _sources.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
