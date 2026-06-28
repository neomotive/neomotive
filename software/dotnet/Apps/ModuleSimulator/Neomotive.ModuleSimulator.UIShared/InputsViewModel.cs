using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Neomotive.ModuleSimulator.UI;

// ── Row model ─────────────────────────────────────────────────────────────────

public class SimulatedInputRow : INotifyPropertyChanged
{
    public string Label { get; }
    public InputType Type { get; }

    private string _currentValue = "—";
    public string CurrentValue
    {
        get => _currentValue;
        set { _currentValue = value; OnPropertyChanged(); }
    }

    private string _assignedName = "(unassigned)";
    public string AssignedName
    {
        get => _assignedName;
        set { _assignedName = value; OnPropertyChanged(); }
    }

    public PotChannel? AssignedPotChannel { get; set; }
    public BoolChannel? AssignedBoolChannel { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public SimulatedInputRow(string label, InputType type)
    {
        Label = label;
        Type = type;
    }
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public class InputsViewModel : INotifyPropertyChanged
{
    private readonly ISimulatorInputs? _inputs;

    private string? _hardwareError;
    public string? HardwareError
    {
        get => _hardwareError;
        private set { _hardwareError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasHardwareError)); }
    }
    public bool HasHardwareError => _hardwareError != null;

    public void SetHardwareError(string message) => HardwareError = message;
    private readonly Action<string, double>? _applyPotValue;
    private readonly Action<string, bool>?   _applyBoolValue;
    private readonly SimulatorConfig?        _config;
    private readonly Action?                 _saveConfig;

    public ObservableCollection<SimulatedInputRow> Rows { get; } = [];

    // ── Edit panel state ──────────────────────────────────────────────────────

    private SimulatedInputRow? _editingRow;
    public SimulatedInputRow? EditingRow
    {
        get => _editingRow;
        private set
        {
            _editingRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(EditIsPot));
            OnPropertyChanged(nameof(EditIsBool));
        }
    }

    public bool IsEditing  => _editingRow != null;
    public bool EditIsPot  => _editingRow?.Type == InputType.Pot;
    public bool EditIsBool => _editingRow?.Type is InputType.Switch or InputType.Button;

    // ── Pot edit — step 1: PID selection ─────────────────────────────────────

    private SimulatorPidDef? _editPid;

    /// <summary>Bound two-way to the PID ComboBox.  Setter drives SelectPid logic.</summary>
    public SimulatorPidDef? SelectedPid
    {
        get => _editPid;
        set
        {
            if (_editPid == value) return;
            _editPid = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPidSelected));
            OnPropertyChanged(nameof(FilteredQuickPicks));
            // Auto-select the first matching quick pick
            SelectedQuickPick = FilteredQuickPicks.FirstOrDefault();
        }
    }

    public bool HasPidSelected => _editPid != null;

    // ── Pot edit — step 2: scale/offset/unit ─────────────────────────────────

    private string _editUnit = "";
    public string EditUnit
    {
        get => _editUnit;
        set { _editUnit = value; OnPropertyChanged(); UpdateRangePreview(); }
    }

    private double _editScale = 20.0;
    public double EditScale
    {
        get => _editScale;
        set { _editScale = value; OnPropertyChanged(); UpdateRangePreview(); }
    }

    private double _editOffset = 0.0;
    public double EditOffset
    {
        get => _editOffset;
        set { _editOffset = value; OnPropertyChanged(); UpdateRangePreview(); }
    }

    private string _editRangePreview = "";
    public string EditRangePreview
    {
        get => _editRangePreview;
        private set { _editRangePreview = value; OnPropertyChanged(); }
    }

    // Quick picks for the selected PID — derived entirely from the two catalog maps
    public IReadOnlyList<PotQuickPick> FilteredQuickPicks =>
        _editPid == null
            ? []
            : InputChannelCatalog.QuickPicksFor(_editPid);

    private PotQuickPick? _selectedQuickPick;

    /// <summary>Bound two-way to the unit ComboBox.  Setter applies scale/offset/unit.</summary>
    public PotQuickPick? SelectedQuickPick
    {
        get => _selectedQuickPick;
        set
        {
            if (_selectedQuickPick == value) return;
            _selectedQuickPick = value;
            OnPropertyChanged();
            if (value != null)
            {
                EditUnit   = value.Unit;
                EditScale  = value.Scale;
                EditOffset = value.Offset;
            }
        }
    }

    // ── Bool edit ─────────────────────────────────────────────────────────────

    private BoolChannel? _editBoolChannel;
    public BoolChannel? EditBoolChannel
    {
        get => _editBoolChannel;
        set { _editBoolChannel = value; OnPropertyChanged(); }
    }

    // ── Catalog references ────────────────────────────────────────────────────

    public IReadOnlyList<SimulatorPidDef> SupportedPotPids   => InputChannelCatalog.SupportedPotPids;
    public IReadOnlyList<BoolChannel>     AvailableBoolChannels => InputChannelCatalog.BoolChannels;

    // ── Commands ──────────────────────────────────────────────────────────────

    public ICommand EditCommand       { get; }
    public ICommand SaveEditCommand   { get; }
    public ICommand CancelEditCommand { get; }

    // Scale coarse (±10) and fine (±1)
    public ICommand ScaleUpCoarseCommand    { get; }
    public ICommand ScaleDownCoarseCommand  { get; }
    public ICommand ScaleUpFineCommand      { get; }
    public ICommand ScaleDownFineCommand    { get; }

    // Offset coarse (±10) and fine (±1)
    public ICommand OffsetUpCoarseCommand   { get; }
    public ICommand OffsetDownCoarseCommand { get; }
    public ICommand OffsetUpFineCommand     { get; }
    public ICommand OffsetDownFineCommand   { get; }

    // Bool channel cycle
    public ICommand NextBoolChannelCommand { get; }
    public ICommand PrevBoolChannelCommand { get; }

    public InputsViewModel(ISimulatorInputs? inputs,
        SimulatorConfig?        config         = null,
        Action?                 saveConfig     = null,
        Action<string, double>? applyPotValue  = null,
        Action<string, bool>?   applyBoolValue = null)
    {
        _inputs         = inputs;
        _config         = config;
        _saveConfig     = saveConfig;
        _applyPotValue  = applyPotValue;
        _applyBoolValue = applyBoolValue;

        Rows.Add(new SimulatedInputRow("POT1",    InputType.Pot));
        Rows.Add(new SimulatedInputRow("POT2",    InputType.Pot));
        Rows.Add(new SimulatedInputRow("POT3",    InputType.Pot));
        Rows.Add(new SimulatedInputRow("POT4",    InputType.Pot));
        Rows.Add(new SimulatedInputRow("SWITCH1", InputType.Switch));
        Rows.Add(new SimulatedInputRow("SWITCH2", InputType.Switch));
        Rows.Add(new SimulatedInputRow("BUTTON1", InputType.Button));
        Rows.Add(new SimulatedInputRow("BUTTON2", InputType.Button));

        // Always-enabled — visibility is controlled by IsEditing / HasPidSelected
        EditCommand       = new RelayCommand<SimulatedInputRow>(BeginEdit);
        SaveEditCommand   = new RelayCommand(CommitEdit);
        CancelEditCommand = new RelayCommand(CancelEdit);

        ScaleUpCoarseCommand    = new RelayCommand(() => EditScale  += 10);
        ScaleDownCoarseCommand  = new RelayCommand(() => EditScale   = Math.Max(0.01, EditScale  - 10));
        ScaleUpFineCommand      = new RelayCommand(() => EditScale  += 1);
        ScaleDownFineCommand    = new RelayCommand(() => EditScale   = Math.Max(0.01, EditScale  - 1));

        OffsetUpCoarseCommand   = new RelayCommand(() => EditOffset += 10);
        OffsetDownCoarseCommand = new RelayCommand(() => EditOffset -= 10);
        OffsetUpFineCommand     = new RelayCommand(() => EditOffset += 1);
        OffsetDownFineCommand   = new RelayCommand(() => EditOffset -= 1);

        NextBoolChannelCommand = new RelayCommand(CycleBoolChannelNext);
        PrevBoolChannelCommand = new RelayCommand(CycleBoolChannelPrev);

        if (_config != null) RestoreFromConfig(_config);
    }

    // ── Refresh (250 ms timer) ────────────────────────────────────────────────

    public void RefreshValues()
    {
        if (_inputs == null) return;
        ReadAndApplyPot(0, _inputs.Pot1Volts);
        ReadAndApplyPot(1, _inputs.Pot2Volts);
        ReadAndApplyPot(2, _inputs.Pot3Volts);
        ReadAndApplyPot(3, _inputs.Pot4Volts);

        ReadBoolRow(4, _inputs.Switch1?.IsOn ?? _inputs.Switch1On);
        ReadBoolRow(5, _inputs.Switch2?.IsOn ?? _inputs.Switch2On);
        ReadBoolRow(6, _inputs.Button1Down);
        ReadBoolRow(7, _inputs.Button2Down);
    }

    private void ReadAndApplyPot(int index, double voltage)
    {
        var row     = Rows[index];
        var channel = row.AssignedPotChannel;

        if (channel == null)
        {
            row.CurrentValue = $"{voltage:F2}V";
        }
        else
        {
            var physical = channel.MapFromVoltage(voltage);
            row.CurrentValue = $"{voltage:F2}V / {channel.Format(physical)}";
            _applyPotValue?.Invoke(channel.Key, channel.ToNativeValue(physical));
        }
    }

    private void ReadBoolRow(int index, bool state)
    {
        var row     = Rows[index];
        var channel = row.AssignedBoolChannel;

        row.CurrentValue = state ? "ON" : "OFF";

        if (channel != null)
            _applyBoolValue?.Invoke(channel.Key, state);
    }

    // ── Edit lifecycle ────────────────────────────────────────────────────────

    private void BeginEdit(SimulatedInputRow? row)
    {
        if (row == null) return;
        EditingRow = row;

        if (row.Type == InputType.Pot)
        {
            var ch = row.AssignedPotChannel;

            // Restore previously selected PID (match by key), which also resets FilteredQuickPicks
            SelectedPid = ch != null
                ? SupportedPotPids.FirstOrDefault(p => p.Key == ch.Key)
                : null;

            // Restore previously chosen unit preset (match by unit string), or keep auto-selected first
            if (ch != null)
            {
                var match = FilteredQuickPicks.FirstOrDefault(q => q.Unit == ch.Unit);
                SelectedQuickPick = match ?? FilteredQuickPicks.FirstOrDefault();
                // Override with saved scale/offset in case the user had fine-tuned them
                EditScale  = ch.Scale;
                EditOffset = ch.Offset;
            }
        }
        else
        {
            EditBoolChannel = row.AssignedBoolChannel ?? AvailableBoolChannels[0];
        }
    }

    private void UpdateRangePreview()
    {
        var lo = _editOffset;
        var hi = 5.0 * _editScale + _editOffset;
        EditRangePreview = $"{lo:F1} – {hi:F1} {_editUnit}";
    }

    private void CommitEdit()
    {
        if (_editingRow == null) return;

        if (_editingRow.Type == InputType.Pot && _editPid != null)
        {
            var unitType = InputChannelCatalog.UnitTypeFor(_editPid) ?? PotUnitType.Voltage;
            _editingRow.AssignedPotChannel = new PotChannel(
                _editPid.Key,
                $"{_editPid.Name} ({_editUnit})",
                _editUnit, _editScale, _editOffset, unitType);
            _editingRow.AssignedName = $"{_editPid.PidCode} {_editPid.Name}";

            if (_config != null)
            {
                _config.Inputs[_editingRow.Label] = new InputRowConfig
                {
                    Pot = new PotInputConfig
                    {
                        PidKey   = _editPid.Key,
                        Unit     = _editUnit,
                        Scale    = _editScale,
                        Offset   = _editOffset,
                        UnitType = unitType,
                    }
                };
                _saveConfig?.Invoke();
            }
        }
        else if (_editingRow.Type is InputType.Switch or InputType.Button
                 && _editBoolChannel != null)
        {
            _editingRow.AssignedBoolChannel = _editBoolChannel;
            _editingRow.AssignedName = _editBoolChannel.DisplayName;

            if (_config != null)
            {
                _config.Inputs[_editingRow.Label] = new InputRowConfig
                {
                    Bool = new BoolInputConfig { ChannelKey = _editBoolChannel.Key }
                };
                _saveConfig?.Invoke();
            }
        }

        EditingRow = null;
    }

    private void RestoreFromConfig(SimulatorConfig config)
    {
        foreach (var row in Rows)
        {
            if (!config.Inputs.TryGetValue(row.Label, out var saved)) continue;

            if (row.Type == InputType.Pot && saved.Pot is { } p)
            {
                var pid = SupportedPotPids.FirstOrDefault(x => x.Key == p.PidKey);
                if (pid == null) continue;
                row.AssignedPotChannel = new PotChannel(
                    p.PidKey, $"{pid.Name} ({p.Unit})", p.Unit, p.Scale, p.Offset, p.UnitType);
                row.AssignedName = $"{pid.PidCode} {pid.Name}";
            }
            else if (row.Type is InputType.Switch or InputType.Button && saved.Bool is { } b)
            {
                var channel = AvailableBoolChannels.FirstOrDefault(c => c.Key == b.ChannelKey);
                if (channel == null) continue;
                row.AssignedBoolChannel = channel;
                row.AssignedName = channel.DisplayName;
            }
        }
    }

    private void CancelEdit() => EditingRow = null;

    private void CycleBoolChannelNext()
    {
        var list = AvailableBoolChannels;
        if (_editBoolChannel == null) { EditBoolChannel = list[0]; return; }
        var idx = FindIndex(list, _editBoolChannel);
        EditBoolChannel = list[(idx + 1) % list.Count];
    }

    private void CycleBoolChannelPrev()
    {
        var list = AvailableBoolChannels;
        if (_editBoolChannel == null) { EditBoolChannel = list[0]; return; }
        var idx = FindIndex(list, _editBoolChannel);
        EditBoolChannel = list[(idx - 1 + list.Count) % list.Count];
    }

    private static int FindIndex<T>(IReadOnlyList<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
            if (Equals(list[i], item)) return i;
        return 0;
    }

    // ── Allow Toolbox to push bool states ────────────────────────────────────

    public void SetSwitchState(int oneBasedIndex, bool state)
    {
        switch (oneBasedIndex)
        {
            case 1: _inputs.Switch1On = state; break;
            case 2: _inputs.Switch2On = state; break;
        }
    }

    public void SetButtonState(int oneBasedIndex, bool state)
    {
        switch (oneBasedIndex)
        {
            case 1: _inputs.Button1Down = state; break;
            case 2: _inputs.Button2Down = state; break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ── ICommand helpers ──────────────────────────────────────────────────────────

file sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? _) => true;
    public void Execute(object? _)    => execute();
}

file sealed class RelayCommand<T>(Action<T?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? _) => true;
    public void Execute(object? parameter) => execute(parameter is T t ? t : default);
}
