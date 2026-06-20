using Avalonia.Threading;
using Meadow;
using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Neomotive.ModuleSimulator.UI;

public record DataFieldItem(string Field, string Value);
public record MonitorItem(string Key, string Name, bool Supported, bool Complete)
{
    public bool IsNotSupported => !Supported;
    public bool IsReady => Supported && Complete;
    public bool IsIncomplete => Supported && !Complete;
}
public record DtcItem(string Code, string Category, string Description, bool IsActive, bool IsQuick)
{
    public bool IsInactive => !IsActive;
    public bool IsCommand => !IsQuick;
    public bool IsCommandActive => !IsQuick && IsActive;
    public bool IsCommandInactive => !IsQuick && !IsActive;
}
public record DtcButtonItem(string Code, string Description, bool IsActive);
public record CanLogItem(string Time, string Id, bool IsOutgoing, string Data, string Description);

public class MainWindowViewModel : INotifyPropertyChanged
{
    private enum SettingsView { Data, Monitors, Dtcs, Can, Inputs, Config }
    private enum SelectedModule { Pcm, Tcu }

    private record DtcHolder(
        Dictionary<string, byte[]> StoredDtcs,
        Dictionary<string, byte[]> PendingDtcs,
        Dictionary<string, byte[]> PermanentDtcs,
        Action Sync);

    private SimulatorState _pcmState;
    private ISimulatorInputs? _inputs;
    private SimulatorTcuState _tcuState;
    private SimulatorPcm _pcm;
    private SimulatorTcu _tcu;
    private CanPacketLog _log;

    private SettingsView _view = SettingsView.Data;
    private SelectedModule _selectedModule = SelectedModule.Pcm;
    private DateTime _lastCanTimestamp = DateTime.MinValue;
    private int _lastCanCount = -1;
    private readonly SimulatorConfig _config;
    private readonly HashSet<string> _commandDtcCodes = new();

    public InputsViewModel InputsVm { get; private set; }

    public MainWindowViewModel(ISimulatorInputs? inputs, string feedbackText = "")
    {
        _inputs = inputs;
        FeedbackText = feedbackText;
        _config = ConfigManager.Load();
        if (_config.QuickDtcs.Count == 0)
        {
            SeedDefaultQuickDtcs();
        }

        InputsVm = new InputsViewModel(inputs, _config, () => ConfigManager.Save(_config), SetSimulatedValue, SetSimulatedBoolValue);

        // since Avalonia and Meadow are both starting at the same time, we must wait
        // for MeadowInitialize to complete before the output port is ready
        _ = Task.Run(WaitForHardware);
    }

    public void SetInputs(ISimulatorInputs inputs)
    {
        _inputs = inputs;
        InputsVm = new InputsViewModel(inputs, _config, () => ConfigManager.Save(_config), SetSimulatedValue, SetSimulatedBoolValue);
        OnPropertyChanged(nameof(InputsVm));
    }

    private LoggingCanBus _bus;

    private async Task WaitForHardware()
    {
        _log = new CanPacketLog(_config.CanLogMaxDepth);

        while (_bus == null) // TODO: wait for any other required hardware
        {
            var rawBus = Resolver.Services.Get<ICanBus>();
            if (rawBus != null)
            {
                if (rawBus is NullCanBus)
                {
                    FeedbackText = "No CAN bus detected — offline mode";
                }
                else
                {
                    FeedbackText = "Using CAN bus: " + rawBus.GetType().Name;
                }

                _bus = new LoggingCanBus(rawBus, _log);
                _bus.BusError += OnCanBusError;
                break;
            }
            await Task.Delay(100);
        }
        _pcmState = new SimulatorState(_inputs!) { Vin = _config.Vin };
        _tcuState = new SimulatorTcuState();
        _pcm = new SimulatorPcm(_bus, _pcmState);
        _tcu = new SimulatorTcu(_bus, _tcuState);

        _pcm.DtcsCleared += () => Dispatcher.UIThread.Post(Refresh);
        _tcu.DtcsCleared += () => Dispatcher.UIThread.Post(Refresh);

        Refresh();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
            {
                RefreshCanLog();
                InputsVm.RefreshValues();
                CheckCanBusWatchdog();
            };

        timer.Start();
    }

    private void OnCanBusError(object? sender, CanErrorInfo e)
    {
        Console.WriteLine($"CAN bus error: TxError={e.TransmitErrorCount} RxError={e.ReceiveErrorCount}");
        CanBusErrorCount++;
        CanLastTxErrors = e.TransmitErrorCount;
        CanLastRxErrors = e.ReceiveErrorCount;
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(CanBusErrorCount));
            OnPropertyChanged(nameof(CanLastTxErrors));
            OnPropertyChanged(nameof(CanLastRxErrors));
            OnPropertyChanged(nameof(HasCanHealthData));
            OnPropertyChanged(nameof(HasNoCanErrors));
            OnPropertyChanged(nameof(CanTxErrorSevere));
        });
    }

    // ── Module display ────────────────────────────────────────────────────────

    public bool IsPcmSelected => _selectedModule == SelectedModule.Pcm;
    public bool IsTcuSelected => _selectedModule == SelectedModule.Tcu;
    public string PcmDtcCounts => $"{_pcmState.StoredDtcs.Count}|{_pcmState.PendingDtcs.Count}|{_pcmState.PermanentDtcs.Count}";
    public string TcuDtcCounts => $"{_tcuState.StoredDtcs.Count}|{_tcuState.PendingDtcs.Count}|{_tcuState.PermanentDtcs.Count}";

    // ── View selection ────────────────────────────────────────────────────────

    public bool IsDataView => _view == SettingsView.Data;
    public bool IsMonitorsView => _view == SettingsView.Monitors;
    public bool IsDtcsView => _view == SettingsView.Dtcs;
    public bool IsCanView => _view == SettingsView.Can;
    public bool IsInputsView => _view == SettingsView.Inputs;
    public bool IsConfigView => _view == SettingsView.Config;
    public bool ShowMonitorsTab => _selectedModule == SelectedModule.Pcm;

    public string SettingsPanelTitle => (_selectedModule, _view) switch
    {
        (SelectedModule.Pcm, SettingsView.Data) => "PCM — Data",
        (SelectedModule.Pcm, SettingsView.Monitors) => "PCM — Readiness Monitors",
        (SelectedModule.Pcm, SettingsView.Dtcs) => "PCM — Fault Codes",
        (SelectedModule.Pcm, SettingsView.Can) => "PCM — CAN Log",
        (SelectedModule.Pcm, SettingsView.Inputs) => "Configure Inputs",
        (SelectedModule.Pcm, SettingsView.Config) => "System Config",
        (SelectedModule.Tcu, SettingsView.Dtcs) => "TCU — Fault Codes",
        (SelectedModule.Tcu, SettingsView.Can) => "TCU — CAN Log",
        _ => "TCU — Data",
    };

    // ── Data fields ───────────────────────────────────────────────────────────

    private IReadOnlyList<DataFieldItem> _dataFields = [];
    public IReadOnlyList<DataFieldItem> DataFields
    {
        get => _dataFields;
        private set { _dataFields = value; OnPropertyChanged(); }
    }

    // ── Monitors ──────────────────────────────────────────────────────────────

    public bool MilOn => _pcmState.StoredDtcs.Count > 0;
    public bool MilOff => _pcmState.StoredDtcs.Count == 0;

    private IReadOnlyList<MonitorItem> _monitors = [];
    public IReadOnlyList<MonitorItem> Monitors
    {
        get => _monitors;
        private set { _monitors = value; OnPropertyChanged(); }
    }

    // ── DTCs ──────────────────────────────────────────────────────────────────

    public bool HasNoDtcs { get; private set; } = true;
    public bool HasDtcs { get; private set; } = false;

    private IReadOnlyList<DtcItem> _dtcs = [];
    public IReadOnlyList<DtcItem> Dtcs
    {
        get => _dtcs;
        private set { _dtcs = value; OnPropertyChanged(); }
    }

    private IReadOnlyList<DtcButtonItem> _knownDtcButtons = [];
    public IReadOnlyList<DtcButtonItem> KnownDtcButtons
    {
        get => _knownDtcButtons;
        private set { _knownDtcButtons = value; OnPropertyChanged(); }
    }

    // ── CAN health ────────────────────────────────────────────────────────────

    private DateTime _lastCanActivity = DateTime.MinValue;
    private bool _canBusStuck = false;
    private bool _autoReconnect = true;
    private int _autoReconnectCount = 0;

    public bool AutoReconnect
    {
        get => _autoReconnect;
        set
        {
            _autoReconnect = value;
            OnPropertyChanged();
        }
    }

    public int AutoReconnectCount
    {
        get => _autoReconnectCount;
        private set
        {
            _autoReconnectCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAutoReconnects));
        }
    }

    public bool HasAutoReconnects => AutoReconnectCount > 0;


    public int CanBusErrorCount { get; private set; }
    public int CanLastTxErrors { get; private set; }
    public int CanLastRxErrors { get; private set; }
    public bool CanBusStuck => _canBusStuck;
    public bool HasCanHealthData => CanBusErrorCount > 0 || _canBusStuck;
    public bool HasNoCanErrors => !HasCanHealthData;
    public bool CanTxErrorSevere => CanLastTxErrors >= 128;

    public int CanBusWatchdogSeconds => 5;

    private void CheckCanBusWatchdog()
    {
        // If we've seen CAN activity before but nothing for 10+ seconds, the interrupt
        // handler is likely stuck (INT pin permanently asserted, no new falling edges).
        if (_lastCanActivity == DateTime.MinValue) return;
        var stuck = (DateTime.UtcNow - _lastCanActivity).TotalSeconds > CanBusWatchdogSeconds && !_canBusStuck;
        if (stuck)
        {
            if (AutoReconnect)
            {
                AutoReconnectCount++;
                ResetCanErrors();
            }
            else
            {
                _canBusStuck = true;
                OnPropertyChanged(nameof(CanBusStuck));
                OnPropertyChanged(nameof(HasCanHealthData));
                OnPropertyChanged(nameof(HasNoCanErrors));
            }
        }
    }

    public void ResetCanErrors()
    {
        CanBusErrorCount = 0;
        CanLastTxErrors = 0;
        CanLastRxErrors = 0;
        _canBusStuck = false;
        _lastCanActivity = DateTime.MinValue;

        // Force a hardware re-initialization if using the MCP2515 driver
        if (_bus is LoggingCanBus lcb)
        {
            // The BitRate setter in Mcp2515CanBus triggers a full hardware Initialize()
            lcb.BitRate = lcb.BitRate;
        }

        OnPropertyChanged(nameof(CanBusErrorCount));
        OnPropertyChanged(nameof(CanLastTxErrors));
        OnPropertyChanged(nameof(CanLastRxErrors));
        OnPropertyChanged(nameof(CanBusStuck));
        OnPropertyChanged(nameof(HasCanHealthData));
        OnPropertyChanged(nameof(HasNoCanErrors));
        OnPropertyChanged(nameof(CanTxErrorSevere));
    }

    // ── CAN log ───────────────────────────────────────────────────────────────

    public event Action? CanLogUpdated;

    public bool HasNoCanPackets { get; private set; } = true;
    public bool HasCanPackets { get; private set; } = false;

    private IReadOnlyList<CanLogItem> _canLogItems = [];
    public IReadOnlyList<CanLogItem> CanLogItems
    {
        get => _canLogItems;
        private set { _canLogItems = value; OnPropertyChanged(); }
    }

    // ── Command ───────────────────────────────────────────────────────────────

    private string _commandInput = "";
    public string CommandInput
    {
        get => _commandInput;
        set { _commandInput = value; OnPropertyChanged(); }
    }

    private string _feedbackText = "";
    public string FeedbackText
    {
        get => _feedbackText;
        set { _feedbackText = value; OnPropertyChanged(); }
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    public void SetSimulatedValue(string key, double value)
    {
        if (_pcmState is null) return;
        switch (key)
        {
            case "coolant_temp": _pcmState.CoolantTempCelsius = value; break;
            case "rpm": _pcmState.Rpm = (float)value; break;
            case "speed": _pcmState.SpeedKph = value; break;
            case "throttle": _pcmState.ThrottlePercent = (float)value; break;
            default: return;
        }
        Dispatcher.UIThread.Post(() => DataFields = BuildDataFields());
    }

    public void SetSimulatedBoolValue(string key, bool value)
    {
        // Boolean input channels — extend as new state properties are added to SimulatorState
        // Currently no boolean fields on SimulatorState; placeholder for future expansion.
    }

    public void SelectPcm() { _selectedModule = SelectedModule.Pcm; Refresh(); }
    public void SelectTcu() { _selectedModule = SelectedModule.Tcu; Refresh(); }
    public void ShowData() { _view = SettingsView.Data; Refresh(); }
    public void ShowMonitors() { _view = SettingsView.Monitors; Refresh(); }
    public void ShowDtcs() { _view = SettingsView.Dtcs; Refresh(); }
    public void ShowCan()  { _view = SettingsView.Can;  Refresh(); }
    public void ShowInputs() { _view = SettingsView.Inputs; Refresh(); }
    public void ShowConfig() { _view = SettingsView.Config; Refresh(); }

    public void ExecuteCommand()
    {
        var cmd = _commandInput.Trim();
        CommandInput = "";
        if (string.IsNullOrEmpty(cmd)) return;

        if (cmd.Equals("show data", StringComparison.OrdinalIgnoreCase)) { _view = SettingsView.Data; FeedbackText = ""; }
        else if (cmd.Equals("show monitors", StringComparison.OrdinalIgnoreCase)) { _view = SettingsView.Monitors; FeedbackText = ""; }
        else if (cmd.Equals("show dtcs", StringComparison.OrdinalIgnoreCase)) { _view = SettingsView.Dtcs; FeedbackText = ""; }
        else FeedbackText = ProcessCommand(cmd) ?? "";

        Refresh();
    }

    public void ClearCommand() => CommandInput = "";

    public void ClearCanLog()
    {
        _log.Clear();
        _lastCanTimestamp = DateTime.MinValue;
        _lastCanCount = 0;
        CanLogItems = [];
        HasCanPackets = false;
        HasNoCanPackets = true;
        OnPropertyChanged(nameof(HasCanPackets));
        OnPropertyChanged(nameof(HasNoCanPackets));
    }

    // ── Config tab ────────────────────────────────────────────────────────────

    public bool IsImperial => _config.Units == UnitsOfMeasure.Imperial;
    public bool IsMetric => _config.Units == UnitsOfMeasure.Metric;

    public void SetUnits(UnitsOfMeasure units)
    {
        _config.Units = units;
        ConfigManager.Save(_config);
        OnPropertyChanged(nameof(IsImperial));
        OnPropertyChanged(nameof(IsMetric));
        DataFields = BuildDataFields();
    }

    public string ConfigVin
    {
        get => _config.Vin;
        set
        {
            var v = value?.ToUpperInvariant().Trim() ?? "";
            if (v.Length == 0 || v.Length > 17) return;
            _config.Vin = v;
            // Only push to PCM when the VIN is fully valid — prevents broadcasting a malformed VIN
            if (_pcmState != null && IsValidVin(v))
                _pcmState.Vin = v;
            ConfigManager.Save(_config);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConfigVinNote));
            OnPropertyChanged(nameof(ConfigVinFeedback));
            DataFields = BuildDataFields();
        }
    }

    public int CanLogMaxDepth
    {
        get => _config.CanLogMaxDepth;
        set
        {
            var clamped = value < 10 ? 10 : value > 10000 ? 10000 : value;
            _config.CanLogMaxDepth = clamped;
            _log.MaxDepth = clamped;
            ConfigManager.Save(_config);
            OnPropertyChanged();
        }
    }

    public bool HasConfigVinNote => !IsValidVin(_config.Vin);
    public string ConfigVinFeedback => _config.Vin.Length != 17
        ? $"VIN must be exactly 17 characters ({_config.Vin.Length}/17)"
        : "Invalid character — I, O, Q are not allowed in VINs";

    public ObservableCollection<QuickDtcConfig> ConfigDtcs => _config.QuickDtcs;
    public bool CanAddDtc => _config.QuickDtcs.Count < 10;
    public bool ConfigAtMax => _config.QuickDtcs.Count >= 10;
    public bool HasNoQuickDtcs => _config.QuickDtcs.Count == 0;

    private string _newDtcCode = "";
    public string NewDtcCode
    {
        get => _newDtcCode;
        set { _newDtcCode = value; OnPropertyChanged(); }
    }

    private string _newDtcDescription = "";
    public string NewDtcDescription
    {
        get => _newDtcDescription;
        set { _newDtcDescription = value; OnPropertyChanged(); }
    }

    private string _configFeedback = "";
    public string ConfigFeedback
    {
        get => _configFeedback;
        set { _configFeedback = value; OnPropertyChanged(); }
    }

    public void AddQuickDtc()
    {
        if (!TryParseDtcCode(_newDtcCode, out var code, out _))
        {
            ConfigFeedback = $"Invalid code '{_newDtcCode}' — use format P0300";
            return;
        }
        if (_config.QuickDtcs.Any(d => d.Code == code))
        {
            ConfigFeedback = $"{code} is already in the list";
            return;
        }
        if (_config.QuickDtcs.Count >= 10)
        {
            ConfigFeedback = "Maximum of 10 quick DTCs reached";
            return;
        }

        var desc = string.IsNullOrWhiteSpace(_newDtcDescription) ? code : _newDtcDescription.Trim();
        _config.QuickDtcs.Add(new QuickDtcConfig { Code = code, Description = desc });
        ConfigManager.Save(_config);

        NewDtcCode = "";
        NewDtcDescription = "";
        ConfigFeedback = $"Added {code}";

        NotifyConfigChanged();
        KnownDtcButtons = BuildDtcButtons();
    }

    public void RemoveQuickDtc(string code)
    {
        var entry = _config.QuickDtcs.FirstOrDefault(d => d.Code == code);
        if (entry == null) return;

        _config.QuickDtcs.Remove(entry);
        ConfigManager.Save(_config);
        ConfigFeedback = $"Removed {code}";

        NotifyConfigChanged();
        KnownDtcButtons = BuildDtcButtons();
    }

    private void NotifyConfigChanged()
    {
        OnPropertyChanged(nameof(ConfigDtcs));
        OnPropertyChanged(nameof(CanAddDtc));
        OnPropertyChanged(nameof(ConfigAtMax));
        OnPropertyChanged(nameof(HasNoQuickDtcs));
    }

    public void ToggleKnownDtc(string code)
    {
        var stored = _selectedModule == SelectedModule.Tcu ? _tcuState.StoredDtcs : _pcmState.StoredDtcs;
        Action sync = _selectedModule == SelectedModule.Tcu ? _tcu.SyncDtcsFromState : _pcm.SyncDtcsFromState;

        if (stored.ContainsKey(code))
            stored.Remove(code);
        else if (TryParseDtcCode(code, out var normalized, out var raw))
            stored[normalized] = raw;

        sync();
        Refresh();
    }

    public void ClearDtc(string code)
    {
        if (_selectedModule == SelectedModule.Tcu)
        {
            _tcuState.StoredDtcs.Remove(code);
            _tcuState.PendingDtcs.Remove(code);
            _tcuState.PermanentDtcs.Remove(code);
            _tcu.SyncDtcsFromState();
        }
        else
        {
            _pcmState.StoredDtcs.Remove(code);
            _pcmState.PendingDtcs.Remove(code);
            _pcmState.PermanentDtcs.Remove(code);
            _pcm.SyncDtcsFromState();
        }
        Refresh();
    }

    public void ClearAllDtcs()
    {
        _commandDtcCodes.Clear();
        if (_selectedModule == SelectedModule.Tcu)
        {
            _tcuState.StoredDtcs.Clear();
            _tcuState.PendingDtcs.Clear();
            _tcuState.PermanentDtcs.Clear();
            _tcu.ClearAllDtcs(); // base method: wipes store + freeze frame atomically
        }
        else
        {
            _pcmState.StoredDtcs.Clear();
            _pcmState.PendingDtcs.Clear();
            _pcmState.PermanentDtcs.Clear();
            _pcm.ClearAllDtcs(); // base method: wipes store + freeze frame atomically
        }
        Refresh();
    }

    public void RemoveDtcFromList(string code)
    {
        _commandDtcCodes.Remove(code);
        ClearDtc(code);
    }

    public void DeactivateDtc(string code)
    {
        if (_selectedModule == SelectedModule.Tcu)
        {
            _tcuState.StoredDtcs.Remove(code);
            _tcuState.PendingDtcs.Remove(code);
            _tcuState.PermanentDtcs.Remove(code);
            _tcu.SyncDtcsFromState();
        }
        else
        {
            _pcmState.StoredDtcs.Remove(code);
            _pcmState.PendingDtcs.Remove(code);
            _pcmState.PermanentDtcs.Remove(code);
            _pcm.SyncDtcsFromState();
        }
        Refresh();
    }

    public void ActivateDtc(string code)
    {
        if (!TryParseDtcCode(code, out var normalized, out var raw)) return;
        var stored = _selectedModule == SelectedModule.Tcu ? _tcuState.StoredDtcs : _pcmState.StoredDtcs;
        stored[normalized] = raw;
        Action sync = _selectedModule == SelectedModule.Tcu ? _tcu.SyncDtcsFromState : _pcm.SyncDtcsFromState;
        sync();
        Refresh();
    }

    private void SeedDefaultQuickDtcs()
    {
        QuickDtcConfig[] defaults =
        [
            new() { Code = "P0300", Description = "Random Misfire" },
            new() { Code = "P0420", Description = "Catalyst Efficiency Low" },
            new() { Code = "P0171", Description = "System Too Lean (B1)" },
            new() { Code = "P0172", Description = "System Too Rich (B1)" },
            new() { Code = "P0442", Description = "EVAP Small Leak" },
            new() { Code = "P0455", Description = "EVAP Gross Leak" },
        ];
        foreach (var d in defaults)
            _config.QuickDtcs.Add(d);
        ConfigManager.Save(_config);
    }

    public void CycleDtcCategory(string code)
    {
        Dictionary<string, byte[]> stored, pending, permanent;
        Action sync;

        if (_selectedModule == SelectedModule.Tcu)
        {
            stored = _tcuState.StoredDtcs; pending = _tcuState.PendingDtcs;
            permanent = _tcuState.PermanentDtcs; sync = _tcu.SyncDtcsFromState;
        }
        else
        {
            stored = _pcmState.StoredDtcs; pending = _pcmState.PendingDtcs;
            permanent = _pcmState.PermanentDtcs; sync = _pcm.SyncDtcsFromState;
        }

        if (!TryParseDtcCode(code, out var normalized, out _)) return;

        if (stored.TryGetValue(normalized, out var raw))
        {
            stored.Remove(normalized);
            pending[normalized] = raw;
        }
        else if (pending.TryGetValue(normalized, out raw))
        {
            pending.Remove(normalized);
            permanent[normalized] = raw;
        }
        else if (permanent.TryGetValue(normalized, out raw))
        {
            permanent.Remove(normalized);
            stored[normalized] = raw;
        }

        sync();
        Refresh();
    }

    public void SaveToQuick(string code)
    {
        if (_config.QuickDtcs.Any(d => d.Code == code)) return;

        // FIFO: drop oldest entry when at capacity
        if (_config.QuickDtcs.Count >= 10)
            _config.QuickDtcs.RemoveAt(0);

        var staticKnown = _selectedModule == SelectedModule.Tcu
            ? (KnownDtc[])SimulatorTcuState.KnownDtcs
            : SimulatorState.KnownDtcs;
        var desc = staticKnown.FirstOrDefault(d => d.Code == code)?.Description ?? "";

        _config.QuickDtcs.Add(new QuickDtcConfig { Code = code, Description = desc });
        ConfigManager.Save(_config);
        _commandDtcCodes.Remove(code);
        NotifyConfigChanged();
        KnownDtcButtons = BuildDtcButtons();
        Refresh();
    }

    public void ToggleMonitor(string key)
    {
        var r = _pcmState.Readiness;
        switch (key)
        {
            case "misfire": if (r.MisfireSupported) r.MisfireComplete = !r.MisfireComplete; break;
            case "fuel": if (r.FuelSystemSupported) r.FuelSystemComplete = !r.FuelSystemComplete; break;
            case "comprehensive": if (r.ComprehensiveComponentSupported) r.ComprehensiveComponentComplete = !r.ComprehensiveComponentComplete; break;
            case "catalyst": if (r.CatalystSupported) r.CatalystComplete = !r.CatalystComplete; break;
            case "hcat": if (r.HeatedCatalystSupported) r.HeatedCatalystComplete = !r.HeatedCatalystComplete; break;
            case "evap": if (r.EvapSystemSupported) r.EvapSystemComplete = !r.EvapSystemComplete; break;
            case "air": if (r.SecondaryAirSupported) r.SecondaryAirComplete = !r.SecondaryAirComplete; break;
            case "o2": if (r.OxygenSensorSupported) r.OxygenSensorComplete = !r.OxygenSensorComplete; break;
            case "o2heater": if (r.OxygenSensorHeaterSupported) r.OxygenSensorHeaterComplete = !r.OxygenSensorHeaterComplete; break;
            case "egr": if (r.EgrSystemSupported) r.EgrSystemComplete = !r.EgrSystemComplete; break;
        }
        Refresh();
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsPcmSelected));
        OnPropertyChanged(nameof(IsTcuSelected));
        OnPropertyChanged(nameof(PcmDtcCounts));
        OnPropertyChanged(nameof(TcuDtcCounts));
        OnPropertyChanged(nameof(IsDataView));
        OnPropertyChanged(nameof(IsMonitorsView));
        OnPropertyChanged(nameof(IsDtcsView));
        OnPropertyChanged(nameof(IsCanView));
        OnPropertyChanged(nameof(IsInputsView));
        OnPropertyChanged(nameof(IsConfigView));
        OnPropertyChanged(nameof(ShowMonitorsTab));
        OnPropertyChanged(nameof(SettingsPanelTitle));
        OnPropertyChanged(nameof(MilOn));
        OnPropertyChanged(nameof(MilOff));

        DataFields = BuildDataFields();
        Monitors = BuildMonitors();

        var dtcList = BuildDtcs();
        Dtcs = dtcList;
        HasNoDtcs = dtcList.Count == 0;
        HasDtcs = dtcList.Count > 0;
        OnPropertyChanged(nameof(HasNoDtcs));
        OnPropertyChanged(nameof(HasDtcs));
        KnownDtcButtons = BuildDtcButtons();
    }

    private void RefreshCanLog()
    {
        var all = _log.GetAll();
        var latest = all.Count > 0 ? all[^1].Timestamp : DateTime.MinValue;
        if (latest == _lastCanTimestamp && all.Count == _lastCanCount) return;
        _lastCanTimestamp = latest;
        _lastCanCount = all.Count;
        _lastCanActivity = DateTime.UtcNow;
        if (_canBusStuck)
        {
            _canBusStuck = false;
            OnPropertyChanged(nameof(CanBusStuck));
            OnPropertyChanged(nameof(HasCanHealthData));
            OnPropertyChanged(nameof(HasNoCanErrors));
        }

        CanLogItems = BuildCanLog(all);
        var hasPackets = CanLogItems.Count > 0;
        if (HasCanPackets != hasPackets)
        {
            HasCanPackets = hasPackets;
            HasNoCanPackets = !hasPackets;
            OnPropertyChanged(nameof(HasCanPackets));
            OnPropertyChanged(nameof(HasNoCanPackets));
        }
        CanLogUpdated?.Invoke();
    }

    // ── Data builders ─────────────────────────────────────────────────────────

    private IReadOnlyList<DataFieldItem> BuildDataFields()
    {
        bool imperial = _config.Units == UnitsOfMeasure.Imperial;

        if (_selectedModule == SelectedModule.Tcu)
        {
            var transTemp = imperial
                ? $"{_tcuState.TransTempCelsius * 9.0 / 5.0 + 32:F1} °F"
                : $"{_tcuState.TransTempCelsius:F1} °C";
            return
            [
                new("ECU Name",  _tcu.EcuName ?? "—"),
                new("Cal ID",    _tcu.CalibrationId ?? "—"),
                new("CVN",       _tcu.CalibrationVerificationNumber is { } v ? $"0x{v:X8}" : "—"),
                new("Trans Temp", transTemp),
                new("Gear",      _tcuState.GearPosition),
            ];
        }

        var coolant = imperial
            ? $"{_pcmState.CoolantTempCelsius * 9.0 / 5.0 + 32:F1} °F"
            : $"{_pcmState.CoolantTempCelsius:F1} °C";
        var speed = imperial
            ? $"{_pcmState.SpeedKph * 0.621371:F1} mph"
            : $"{_pcmState.SpeedKph:F1} km/h";

        return
        [
            new("ECU Name",    _pcm.EcuName ?? "—"),
            new("Cal ID",      _pcm.CalibrationId ?? "—"),
            new("CVN",         _pcm.CalibrationVerificationNumber is { } cv ? $"0x{cv:X8}" : "—"),
            new("VIN",         _pcmState.Vin),
            new("Coolant Temp", coolant),
            new("Engine RPM",  $"{_pcmState.Rpm:F0} rpm"),
            new("Speed",       speed),
            new("Throttle",    $"{_pcmState.ThrottlePercent:F1} %"),
        ];
    }

    private IReadOnlyList<MonitorItem> BuildMonitors()
    {
        var r = _pcmState.Readiness;
        return
        [
            new("misfire",     "Misfire",          r.MisfireSupported,                r.MisfireComplete),
            new("fuel",        "Fuel System",      r.FuelSystemSupported,             r.FuelSystemComplete),
            new("comprehensive","Comprehensive",   r.ComprehensiveComponentSupported, r.ComprehensiveComponentComplete),
            new("catalyst",    "Catalyst",         r.CatalystSupported,               r.CatalystComplete),
            new("hcat",        "Heated Catalyst",  r.HeatedCatalystSupported,         r.HeatedCatalystComplete),
            new("evap",        "Evap System",      r.EvapSystemSupported,             r.EvapSystemComplete),
            new("air",         "Secondary Air",    r.SecondaryAirSupported,           r.SecondaryAirComplete),
            new("o2",          "O2 Sensor",        r.OxygenSensorSupported,           r.OxygenSensorComplete),
            new("o2heater",    "O2 Sensor Heater", r.OxygenSensorHeaterSupported,     r.OxygenSensorHeaterComplete),
            new("egr",         "EGR System",       r.EgrSystemSupported,              r.EgrSystemComplete),
        ];
    }

    private IReadOnlyList<DtcItem> BuildDtcs()
    {
        Dictionary<string, byte[]> stored, pending, permanent;
        KnownDtc[] staticKnown;

        if (_selectedModule == SelectedModule.Tcu)
        {
            stored = _tcuState.StoredDtcs; pending = _tcuState.PendingDtcs;
            permanent = _tcuState.PermanentDtcs; staticKnown = SimulatorTcuState.KnownDtcs;
        }
        else
        {
            stored = _pcmState.StoredDtcs; pending = _pcmState.PendingDtcs;
            permanent = _pcmState.PermanentDtcs; staticKnown = SimulatorState.KnownDtcs;
        }

        var items = new List<DtcItem>();
        var addedCodes = new HashSet<string>();

        // Command DTCs: always shown (active or inactive) until explicitly removed
        foreach (var code in _commandDtcCodes.OrderBy(c => c))
        {
            var isStored = stored.ContainsKey(code);
            var isPending = pending.ContainsKey(code);
            var isPermanent = permanent.ContainsKey(code);
            var isActive = isStored || isPending || isPermanent;
            var category = isStored ? "Stored" : isPending ? "Pending" : isPermanent ? "Permanent" : "";
            var desc = staticKnown.FirstOrDefault(d => d.Code == code)?.Description ?? code;
            items.Add(new DtcItem(code, category, desc, isActive, IsQuick: false));
            addedCodes.Add(code);
        }

        // Quick/other active DTCs: only shown when active
        foreach (var (label, dict) in new (string, Dictionary<string, byte[]>)[]
            { ("Stored", stored), ("Pending", pending), ("Permanent", permanent) })
        {
            foreach (var (code, raw) in dict.OrderBy(kvp => kvp.Key))
            {
                if (addedCodes.Contains(code)) continue;
                var desc = _config.QuickDtcs.FirstOrDefault(d => d.Code == code)?.Description
                    ?? staticKnown.FirstOrDefault(d => d.Code == code)?.Description
                    ?? new Dtc(raw).ToReadableErrorCode();
                items.Add(new DtcItem(code, label, desc, IsActive: true, IsQuick: true));
                addedCodes.Add(code);
            }
        }

        return items;
    }

    private IReadOnlyList<DtcButtonItem> BuildDtcButtons()
    {
        var stored = _selectedModule == SelectedModule.Tcu ? _tcuState.StoredDtcs : _pcmState.StoredDtcs;
        return _config.QuickDtcs
            .Select(d => new DtcButtonItem(d.Code, d.Description, stored.ContainsKey(d.Code)))
            .ToList();
    }

    private IReadOnlyList<CanLogItem> BuildCanLog(IReadOnlyList<CanPacketEntry>? packets = null)
    {
        return (packets ?? _log.GetAll()).Select(pkt => new CanLogItem(
            pkt.Timestamp.ToString("HH:mm:ss.fff"),
            pkt.Id.ToString("X3"),
            pkt.IsOutgoing,
            string.Join(" ", pkt.Data.Take(8).Select(b => b.ToString("X2"))),
            DescribePacket(pkt)
        )).ToList();
    }

    // ── Command processing ────────────────────────────────────────────────────

    private string? ProcessCommand(string cmd)
    {
        var parts = cmd.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var dtcHolder = _selectedModule == SelectedModule.Tcu
            ? new DtcHolder(_tcuState.StoredDtcs, _tcuState.PendingDtcs, _tcuState.PermanentDtcs, () => _tcu.SyncDtcsFromState())
            : new DtcHolder(_pcmState.StoredDtcs, _pcmState.PendingDtcs, _pcmState.PermanentDtcs, () => _pcm.SyncDtcsFromState());

        switch (parts[0].ToLowerInvariant())
        {
            case "select" when parts.Length >= 2:
                switch (parts[1].ToLowerInvariant())
                {
                    case "pcm": _selectedModule = SelectedModule.Pcm; return "Selected PCM";
                    case "tcu": _selectedModule = SelectedModule.Tcu; return "Selected TCU";
                    default: return $"Unknown module '{parts[1]}' — use: pcm  or  tcu";
                }

            case "set" when parts.Length >= 4 && parts[1].Equals("monitor", StringComparison.OrdinalIgnoreCase):
                _view = SettingsView.Monitors;
                return HandleSetMonitor(parts[2], parts[3], _pcmState);

            case "set" when parts.Length >= 3 && parts[1].Equals("dtc", StringComparison.OrdinalIgnoreCase):
                {
                    _view = SettingsView.Dtcs;
                    var (cat, codeArg) = IsCategory(parts[2]) && parts.Length >= 4
                        ? (parts[2].ToLowerInvariant(), parts[3])
                        : ("stored", parts[2]);
                    var result = HandleDtcSet(cat, codeArg, dtcHolder);
                    if (TryParseDtcCode(codeArg, out var normalized, out _))
                        _commandDtcCodes.Add(normalized);
                    return result;
                }

            case "set" when parts.Length >= 3 && parts[1].Equals("gear", StringComparison.OrdinalIgnoreCase):
                {
                    var validGears = new[] { "P", "R", "N", "D", "1", "2", "3", "4", "5", "6", "7", "8" };
                    var gear = parts[2].ToUpperInvariant();
                    if (!validGears.Contains(gear)) return $"Invalid gear '{parts[2]}' — use: P R N D 1-8";
                    _tcuState.GearPosition = gear;
                    _selectedModule = SelectedModule.Tcu;
                    _view = SettingsView.Data;
                    return $"Gear = {gear}";
                }

            case "set" when parts.Length >= 3 && parts[1].Equals("transtemp", StringComparison.OrdinalIgnoreCase):
                {
                    if (!double.TryParse(parts[2], out var transTemp) || transTemp < -40 || transTemp > 215)
                        return "Trans temp must be -40 to 215 °C";
                    _tcuState.TransTempCelsius = transTemp;
                    _selectedModule = SelectedModule.Tcu;
                    _view = SettingsView.Data;
                    return $"Trans Temp = {transTemp:F1} °C";
                }

            case "set" when parts.Length >= 3:
                return HandleSet(parts[1], parts[2], _pcmState);

            case "set":
                return "Usage: set <field> <value>  |  set dtc [[cat]] <code>  |  set monitor <name> ready|incomplete";

            case "clear" when parts.Length >= 3 && parts[1].Equals("dtc", StringComparison.OrdinalIgnoreCase) && parts[2].Equals("all", StringComparison.OrdinalIgnoreCase):
                _view = SettingsView.Dtcs;
                _commandDtcCodes.Clear();
                return HandleDtcClearAll(dtcHolder);

            case "clear" when parts.Length >= 3 && parts[1].Equals("dtc", StringComparison.OrdinalIgnoreCase):
                _view = SettingsView.Dtcs;
                if (TryParseDtcCode(parts[2], out var clearedCode, out _))
                    _commandDtcCodes.Remove(clearedCode);
                return HandleDtcClear(parts[2], dtcHolder);

            case "clear":
                return "Usage: clear dtc <code>  |  clear dtc all";

            case "exit":
                Environment.Exit(0);
                return null;

            case "help":
                return "select pcm|tcu  |  show data|monitors|dtcs  |  set vin|rpm|temp|speed|throttle <val>  |  set gear P|R|N|D|1-8  |  set transtemp <val>  |  set dtc [[cat]] <code>  |  clear dtc <code>|all  |  exit";

            default:
                return $"Unknown command '{parts[0]}' — type 'help'";
        }
    }

    private static string HandleSet(string field, string value, SimulatorState state)
    {
        switch (field.ToLowerInvariant())
        {
            case "vin":
                var vinUpper = value.ToUpperInvariant();
                if (!IsValidVin(vinUpper)) return vinUpper.Length != 17
                    ? $"VIN must be exactly 17 characters ({vinUpper.Length}/17)"
                    : "Invalid character — I, O, Q are not allowed in VINs";
                state.Vin = vinUpper;
                return $"VIN set to {state.Vin}";
            case "rpm":
                if (!float.TryParse(value, out var rpm) || rpm < 0 || rpm > 20000) return "RPM must be 0-20000";
                state.Rpm = rpm;
                return $"RPM = {rpm:F0}";
            case "temp":
                if (!double.TryParse(value, out var temp) || temp < -40 || temp > 215) return "Temp must be -40 to 215 °C";
                state.CoolantTempCelsius = temp;
                return $"Coolant = {temp:F1} °C";
            case "speed":
                if (!double.TryParse(value, out var speed) || speed < 0 || speed > 300) return "Speed must be 0-300 km/h";
                state.SpeedKph = speed;
                return $"Speed = {speed:F1} km/h";
            case "throttle":
                if (!float.TryParse(value, out var throttle) || throttle < 0 || throttle > 100) return "Throttle must be 0-100 %";
                state.ThrottlePercent = throttle;
                return $"Throttle = {throttle:F1} %";
            default:
                return $"Unknown field '{field}' — use: vin rpm temp speed throttle  (or: gear transtemp for TCU)";
        }
    }

    private static string HandleSetMonitor(string monitorName, string stateStr, SimulatorState state)
    {
        bool? complete = stateStr.ToLowerInvariant() switch
        {
            "ready" or "complete" or "on" => true,
            "incomplete" or "off" or "not" => false,
            _ => null
        };
        if (complete is null) return "State must be: ready  or  incomplete";

        var r = state.Readiness;
        switch (monitorName.ToLowerInvariant())
        {
            case "misfire": r.MisfireComplete = complete.Value; break;
            case "fuel": r.FuelSystemComplete = complete.Value; break;
            case "comprehensive": r.ComprehensiveComponentComplete = complete.Value; break;
            case "catalyst": r.CatalystComplete = complete.Value; break;
            case "heatedcatalyst" or "hcat": r.HeatedCatalystComplete = complete.Value; break;
            case "evap": r.EvapSystemComplete = complete.Value; break;
            case "secondaryair" or "air": r.SecondaryAirComplete = complete.Value; break;
            case "o2" or "o2sensor": r.OxygenSensorComplete = complete.Value; break;
            case "o2heater": r.OxygenSensorHeaterComplete = complete.Value; break;
            case "egr": r.EgrSystemComplete = complete.Value; break;
            case "all":
                r.MisfireComplete = r.FuelSystemComplete = r.ComprehensiveComponentComplete =
                r.CatalystComplete = r.HeatedCatalystComplete = r.EvapSystemComplete =
                r.SecondaryAirComplete = r.AcRefrigerantComplete =
                r.OxygenSensorComplete = r.OxygenSensorHeaterComplete = r.EgrSystemComplete = complete.Value;
                break;
            default:
                return $"Unknown monitor '{monitorName}'. Use: misfire fuel comprehensive catalyst evap o2 o2heater egr all";
        }
        return $"Monitor '{monitorName}' set to {(complete.Value ? "ready" : "incomplete")}";
    }

    private static bool IsCategory(string s) =>
        s.Equals("stored", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("permanent", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, byte[]> GetDtcDict(string category, DtcHolder holder) =>
        category.ToLowerInvariant() switch
        {
            "pending" => holder.PendingDtcs,
            "permanent" => holder.PermanentDtcs,
            _ => holder.StoredDtcs,
        };

    private static string HandleDtcSet(string category, string codeStr, DtcHolder holder)
    {
        if (!TryParseDtcCode(codeStr, out var code, out var raw))
            return $"Invalid DTC code '{codeStr}' — expected format P0300, C1234, etc.";
        GetDtcDict(category, holder)[code] = raw;
        holder.Sync();
        return $"Set {code} ({category})";
    }

    private static string HandleDtcClear(string codeStr, DtcHolder holder)
    {
        if (!TryParseDtcCode(codeStr, out var code, out _))
            return $"Invalid DTC code '{codeStr}'";
        var cleared = new List<string>();
        foreach (var (label, dict) in new[] {
            ("stored", holder.StoredDtcs), ("pending", holder.PendingDtcs), ("permanent", holder.PermanentDtcs) })
        {
            if (dict.Remove(code)) cleared.Add(label);
        }
        if (cleared.Count == 0) return $"{code} was not set";
        holder.Sync();
        return $"Cleared {code} ({string.Join(", ", cleared)})";
    }

    private static string HandleDtcClearAll(DtcHolder holder)
    {
        var count = holder.StoredDtcs.Count + holder.PendingDtcs.Count + holder.PermanentDtcs.Count;
        holder.StoredDtcs.Clear();
        holder.PendingDtcs.Clear();
        holder.PermanentDtcs.Clear();
        holder.Sync();
        return count > 0 ? $"Cleared {count} DTC(s)" : "No DTCs were set";
    }

    // ISO 3779: 17 alphanumeric chars; I, O, Q are prohibited (too similar to 1, 0, 0)
    private static bool IsValidVin(string vin)
    {
        if (vin.Length != 17) return false;
        foreach (var c in vin)
            if (!char.IsDigit(c) && !(char.IsAsciiLetter(c) && c != 'I' && c != 'O' && c != 'Q'))
                return false;
        return true;
    }

    private static bool TryParseDtcCode(string input, out string normalized, out byte[] raw)
    {
        normalized = input.ToUpperInvariant().Trim();
        raw = Array.Empty<byte>();
        if (normalized.Length != 5) return false;
        byte category = normalized[0] switch { 'P' => 0, 'C' => 1, 'B' => 2, 'U' => 3, _ => 255 };
        if (category == 255) return false;
        if (!char.IsDigit(normalized[1]) || !char.IsDigit(normalized[2]) ||
            !char.IsDigit(normalized[3]) || !char.IsDigit(normalized[4])) return false;
        byte subtype = (byte)(normalized[1] - '0');
        byte d1 = (byte)(normalized[2] - '0');
        byte d2 = (byte)(normalized[3] - '0');
        byte d3 = (byte)(normalized[4] - '0');
        raw = [(byte)((category << 6) | (subtype << 4) | d1), (byte)((d2 << 4) | d3)];
        return true;
    }

    private static string DescribePacket(CanPacketEntry pkt)
    {
        var d = pkt.Data;
        if (d.Length < 2) return "";

        if (!pkt.IsOutgoing)
        {
            byte len = d[0];
            byte svc = d.Length > 1 ? d[1] : (byte)0;

            if (len == 1)
                return svc switch
                {
                    0x03 => "Read Stored DTCs",
                    0x04 => "Clear DTCs",
                    0x07 => "Read Pending DTCs",
                    0x0A => "Read Permanent DTCs",
                    _ => $"Service {svc:X2}",
                };

            byte pid = d.Length > 2 ? d[2] : (byte)0;
            return svc switch
            {
                0x01 => pid switch
                {
                    0x00 => "Query Supported PIDs",
                    0x01 => "Query Monitor Status",
                    0x05 => "Query Coolant Temp",
                    0x0C => "Query Engine RPM",
                    0x0D => "Query Vehicle Speed",
                    0x11 => "Query Throttle",
                    0x20 => "Query Supported PIDs 21-40",
                    0x40 => "Query Supported PIDs 41-60",
                    0x5C => "Query Trans Fluid Temp",
                    _ => $"Query PID {pid:X2}",
                },
                0x09 => pid switch
                {
                    0x00 => "Query Veh Info PIDs",
                    0x02 => "Query VIN",
                    0x04 => "Query Cal ID",
                    0x06 => "Query CVN",
                    0x0A => "Query ECU Name",
                    _ => $"Query Veh Info {pid:X2}",
                },
                _ => $"Service {svc:X2} PID {pid:X2}",
            };
        }
        else
        {
            byte isoType = (byte)(d[0] & 0xF0);
            byte svcByte;
            int offset;
            if (isoType == 0x10) { svcByte = d.Length > 2 ? d[2] : (byte)0; offset = 3; }
            else if (isoType == 0x20) return "ISO-TP Continuation";
            else if (isoType == 0x30) return "ISO-TP Flow Control";
            else { svcByte = d.Length > 1 ? d[1] : (byte)0; offset = 2; }

            byte service = (byte)(svcByte - 0x40);
            return service switch
            {
                0x01 => d.Length > offset ? (d[offset]) switch
                {
                    0x00 => "Supported PIDs",
                    0x01 => "Monitor Status",
                    0x05 => "Coolant Temp",
                    0x0C => "Engine RPM",
                    0x0D => "Vehicle Speed",
                    0x11 => "Throttle Position",
                    0x20 => "Supported PIDs 21-40",
                    0x5C => "Trans Fluid Temp",
                    var p => $"PID {p:X2} Data",
                } : "Current Data",
                0x03 => $"Stored DTCs ({(d.Length > offset ? d[offset] : 0)})",
                0x04 => "DTCs Cleared",
                0x07 => $"Pending DTCs ({(d.Length > offset ? d[offset] : 0)})",
                0x09 => d.Length > offset ? d[offset] switch
                {
                    0x02 => "VIN",
                    0x04 => "Cal ID",
                    0x06 => "CVN",
                    0x0A => "ECU Name",
                    _ => "Vehicle Info",
                } : "Vehicle Info",
                0x0A => $"Permanent DTCs ({(d.Length > offset ? d[offset] : 0)})",
                _ => $"Response {svcByte:X2}",
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
