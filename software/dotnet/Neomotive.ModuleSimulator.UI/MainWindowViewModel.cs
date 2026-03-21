using Avalonia.Threading;
using Meadow.Foundation.ICs.CAN;
using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Neomotive.ModuleSimulator.UI;

public record DataFieldItem(string Field, string Value);
public record MonitorItem(string Key, string Name, bool Supported, bool Complete)
{
    public bool IsNotSupported => !Supported;
    public bool IsReady => Supported && Complete;
    public bool IsIncomplete => Supported && !Complete;
}
public record DtcItem(string Code, string Category, string Description);
public record DtcButtonItem(string Code, string Description, bool IsActive);
public record CanLogItem(string Time, string Id, bool IsOutgoing, string Data, string Description);

public class MainWindowViewModel : INotifyPropertyChanged
{
    private enum SettingsView { Data, Monitors, Dtcs, Inputs, Config }
    private enum SelectedModule { Pcm, Tcu }

    private record DtcHolder(
        Dictionary<string, byte[]> StoredDtcs,
        Dictionary<string, byte[]> PendingDtcs,
        Dictionary<string, byte[]> PermanentDtcs,
        Action Sync);

    private readonly SimulatorState _pcmState;
    private readonly SimulatorTcuState _tcuState;
    private readonly SimulatorPcm _pcm;
    private readonly SimulatorTcu _tcu;
    private readonly CanPacketLog _log;

    private SettingsView _view = SettingsView.Data;
    private SelectedModule _selectedModule = SelectedModule.Pcm;
    private DateTime _lastCanTimestamp = DateTime.MinValue;
    private readonly SimulatorConfig _config;

    public MainWindowViewModel()
    {
        _config = ConfigManager.Load();
        ICanBus rawBus;
        try
        {
            var expander = new PCanUsb();
            rawBus = expander.CreateCanBus(CanBitrate.Can_500kbps);
            FeedbackText = "CAN bus connected (PCanUsb, 500 kbps)";
        }
        catch (Exception ex)
        {
            rawBus = new NullCanBus();
            FeedbackText = $"Offline mode — {ex.Message}";
        }

        _log = new CanPacketLog();
        var bus = new LoggingCanBus(rawBus, _log);
        _pcmState = new SimulatorState();
        _tcuState = new SimulatorTcuState();
        _pcm = new SimulatorPcm(bus, _pcmState);
        _tcu = new SimulatorTcu(bus, _tcuState);

        Refresh();

        // CAN log is the only thing that changes without user interaction
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => RefreshCanLog();
        timer.Start();
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
    public bool IsInputsView => _view == SettingsView.Inputs;
    public bool IsConfigView => _view == SettingsView.Config;
    public bool ShowMonitorsTab => _selectedModule == SelectedModule.Pcm;

    public string SettingsPanelTitle => (_selectedModule, _view) switch
    {
        (SelectedModule.Pcm, SettingsView.Data) => "PCM — Data",
        (SelectedModule.Pcm, SettingsView.Monitors) => "PCM — Readiness Monitors",
        (SelectedModule.Pcm, SettingsView.Dtcs) => "PCM — Fault Codes",
        (SelectedModule.Pcm, SettingsView.Inputs) => "Configure Inputs",
        (SelectedModule.Pcm, SettingsView.Config) => "System Config",
        (SelectedModule.Tcu, SettingsView.Dtcs) => "TCU — Fault Codes",
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

    // ── CAN log ───────────────────────────────────────────────────────────────

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

    public void SelectPcm() { _selectedModule = SelectedModule.Pcm; Refresh(); }
    public void SelectTcu() { _selectedModule = SelectedModule.Tcu; Refresh(); }
    public void ShowData() { _view = SettingsView.Data; Refresh(); }
    public void ShowMonitors() { _view = SettingsView.Monitors; Refresh(); }
    public void ShowDtcs() { _view = SettingsView.Dtcs; Refresh(); }
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

    // ── Config tab ────────────────────────────────────────────────────────────

    public ObservableCollection<QuickDtcConfig> ConfigDtcs => _config.QuickDtcs;
    public bool CanAddDtc                              => _config.QuickDtcs.Count < 10;
    public bool ConfigAtMax                            => _config.QuickDtcs.Count >= 10;
    public bool HasNoQuickDtcs                         => _config.QuickDtcs.Count == 0;

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

        NewDtcCode        = "";
        NewDtcDescription = "";
        ConfigFeedback    = $"Added {code}";

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
        if (_selectedModule == SelectedModule.Tcu)
        {
            _tcuState.StoredDtcs.Clear();
            _tcuState.PendingDtcs.Clear();
            _tcuState.PermanentDtcs.Clear();
            _tcu.SyncDtcsFromState();
        }
        else
        {
            _pcmState.StoredDtcs.Clear();
            _pcmState.PendingDtcs.Clear();
            _pcmState.PermanentDtcs.Clear();
            _pcm.SyncDtcsFromState();
        }
        Refresh();
    }

    public void ToggleMonitor(string key)
    {
        var r = _pcmState.Readiness;
        switch (key)
        {
            case "misfire":      if (r.MisfireSupported)                r.MisfireComplete                = !r.MisfireComplete;                break;
            case "fuel":         if (r.FuelSystemSupported)             r.FuelSystemComplete             = !r.FuelSystemComplete;             break;
            case "comprehensive":if (r.ComprehensiveComponentSupported) r.ComprehensiveComponentComplete = !r.ComprehensiveComponentComplete; break;
            case "catalyst":     if (r.CatalystSupported)               r.CatalystComplete               = !r.CatalystComplete;               break;
            case "hcat":         if (r.HeatedCatalystSupported)         r.HeatedCatalystComplete         = !r.HeatedCatalystComplete;         break;
            case "evap":         if (r.EvapSystemSupported)             r.EvapSystemComplete             = !r.EvapSystemComplete;             break;
            case "air":          if (r.SecondaryAirSupported)           r.SecondaryAirComplete           = !r.SecondaryAirComplete;           break;
            case "o2":           if (r.OxygenSensorSupported)           r.OxygenSensorComplete           = !r.OxygenSensorComplete;           break;
            case "o2heater":     if (r.OxygenSensorHeaterSupported)     r.OxygenSensorHeaterComplete     = !r.OxygenSensorHeaterComplete;     break;
            case "egr":          if (r.EgrSystemSupported)              r.EgrSystemComplete              = !r.EgrSystemComplete;              break;
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
        var packets = _log.GetRecent(6);
        var latest = packets.Count > 0 ? packets[^1].Timestamp : DateTime.MinValue;
        if (latest == _lastCanTimestamp) return;
        _lastCanTimestamp = latest;

        CanLogItems = BuildCanLog();
        var hasPackets = CanLogItems.Count > 0;
        if (HasCanPackets != hasPackets)
        {
            HasCanPackets = hasPackets;
            HasNoCanPackets = !hasPackets;
            OnPropertyChanged(nameof(HasCanPackets));
            OnPropertyChanged(nameof(HasNoCanPackets));
        }
    }

    // ── Data builders ─────────────────────────────────────────────────────────

    private IReadOnlyList<DataFieldItem> BuildDataFields()
    {
        if (_selectedModule == SelectedModule.Tcu)
        {
            return
            [
                new("ECU Name",  _tcu.EcuName ?? "—"),
                new("Cal ID",    _tcu.CalibrationId ?? "—"),
                new("CVN",       _tcu.CalibrationVerificationNumber is { } v ? $"0x{v:X8}" : "—"),
                new("Trans Temp",$"{_tcuState.TransTempCelsius:F1} °C"),
                new("Gear",      _tcuState.GearPosition),
            ];
        }

        return
        [
            new("ECU Name",    _pcm.EcuName ?? "—"),
            new("Cal ID",      _pcm.CalibrationId ?? "—"),
            new("CVN",         _pcm.CalibrationVerificationNumber is { } cv ? $"0x{cv:X8}" : "—"),
            new("VIN",         _pcmState.Vin),
            new("Coolant Temp",$"{_pcmState.CoolantTempCelsius:F1} °C"),
            new("Engine RPM",  $"{_pcmState.Rpm:F0} rpm"),
            new("Speed",       $"{_pcmState.SpeedKph:F1} km/h"),
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
        KnownDtc[] known;

        if (_selectedModule == SelectedModule.Tcu)
        {
            stored = _tcuState.StoredDtcs; pending = _tcuState.PendingDtcs;
            permanent = _tcuState.PermanentDtcs; known = SimulatorTcuState.KnownDtcs;
        }
        else
        {
            stored = _pcmState.StoredDtcs; pending = _pcmState.PendingDtcs;
            permanent = _pcmState.PermanentDtcs; known = SimulatorState.KnownDtcs;
        }

        var items = new List<DtcItem>();
        foreach (var (label, dict) in new (string, Dictionary<string, byte[]>)[]
            { ("Stored", stored), ("Pending", pending), ("Permanent", permanent) })
        {
            foreach (var (code, raw) in dict.OrderBy(kvp => kvp.Key))
            {
                var desc = known.FirstOrDefault(d => d.Code == code)?.Description
                    ?? new Dtc(raw).ToReadableErrorCode();
                items.Add(new DtcItem(code, label, desc));
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

    private IReadOnlyList<CanLogItem> BuildCanLog()
    {
        return _log.GetRecent(6).Select(pkt => new CanLogItem(
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
                    return HandleDtcSet(cat, codeArg, dtcHolder);
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
                return HandleDtcClearAll(dtcHolder);

            case "clear" when parts.Length >= 3 && parts[1].Equals("dtc", StringComparison.OrdinalIgnoreCase):
                _view = SettingsView.Dtcs;
                return HandleDtcClear(parts[2], dtcHolder);

            case "clear":
                return "Usage: clear dtc <code>  |  clear dtc all";

            case "help":
                return "select pcm|tcu  |  show data|monitors|dtcs  |  set vin|rpm|temp|speed|throttle <val>  |  set gear P|R|N|D|1-8  |  set transtemp <val>  |  set dtc [[cat]] <code>  |  clear dtc <code>|all";

            default:
                return $"Unknown command '{parts[0]}' — type 'help'";
        }
    }

    private static string HandleSet(string field, string value, SimulatorState state)
    {
        switch (field.ToLowerInvariant())
        {
            case "vin":
                if (value.Length < 1 || value.Length > 17) return "VIN must be 1-17 characters";
                state.Vin = value.ToUpperInvariant();
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
