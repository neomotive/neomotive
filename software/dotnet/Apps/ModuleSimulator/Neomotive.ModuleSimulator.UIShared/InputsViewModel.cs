using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly SimulatorInputs _inputs;
    private readonly Action<string, double>? _applyPotValue;
    private readonly Action<string, bool>? _applyBoolValue;

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

    // Pot edit staging
    private string _editKey = "";
    public string EditKey
    {
        get => _editKey;
        set { _editKey = value; OnPropertyChanged(); }
    }

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

    // Bool edit staging
    private BoolChannel? _editBoolChannel;
    public BoolChannel? EditBoolChannel
    {
        get => _editBoolChannel;
        set { _editBoolChannel = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<PotQuickPick> QuickPicks       => InputChannelCatalog.PotQuickPicks;
    public IReadOnlyList<BoolChannel>  AvailableBoolChannels => InputChannelCatalog.BoolChannels;

    // ── Commands ──────────────────────────────────────────────────────────────

    public ICommand EditCommand          { get; }
    public ICommand SaveEditCommand      { get; }
    public ICommand CancelEditCommand    { get; }
    public ICommand ApplyQuickPickCommand { get; }

    // Coarse steppers (±10)
    public ICommand ScaleUpCoarseCommand   { get; }
    public ICommand ScaleDownCoarseCommand { get; }
    public ICommand OffsetUpCoarseCommand  { get; }
    public ICommand OffsetDownCoarseCommand { get; }

    // Fine steppers (±1)
    public ICommand ScaleUpFineCommand   { get; }
    public ICommand ScaleDownFineCommand { get; }
    public ICommand OffsetUpFineCommand  { get; }
    public ICommand OffsetDownFineCommand { get; }

    // Bool channel steppers (cycle through list)
    public ICommand NextBoolChannelCommand { get; }
    public ICommand PrevBoolChannelCommand { get; }

    public InputsViewModel(SimulatorInputs inputs,
        Action<string, double>? applyPotValue = null,
        Action<string, bool>?   applyBoolValue = null)
    {
        _inputs        = inputs;
        _applyPotValue = applyPotValue;
        _applyBoolValue = applyBoolValue;

        Rows.Add(new SimulatedInputRow("POT1",    InputType.Pot));
        Rows.Add(new SimulatedInputRow("POT2",    InputType.Pot));
        Rows.Add(new SimulatedInputRow("POT3",    InputType.Pot));
        Rows.Add(new SimulatedInputRow("POT4",    InputType.Pot));
        Rows.Add(new SimulatedInputRow("SWITCH1", InputType.Switch));
        Rows.Add(new SimulatedInputRow("SWITCH2", InputType.Switch));
        Rows.Add(new SimulatedInputRow("SWITCH3", InputType.Switch));
        Rows.Add(new SimulatedInputRow("SWITCH4", InputType.Switch));
        Rows.Add(new SimulatedInputRow("BUTTON1", InputType.Button));
        Rows.Add(new SimulatedInputRow("BUTTON2", InputType.Button));
        Rows.Add(new SimulatedInputRow("BUTTON3", InputType.Button));
        Rows.Add(new SimulatedInputRow("BUTTON4", InputType.Button));

        EditCommand           = new RelayCommand<SimulatedInputRow>(BeginEdit);
        SaveEditCommand       = new RelayCommand(CommitEdit,  () => IsEditing);
        CancelEditCommand     = new RelayCommand(CancelEdit,  () => IsEditing);
        ApplyQuickPickCommand = new RelayCommand<PotQuickPick>(ApplyQuickPick);

        ScaleUpCoarseCommand    = new RelayCommand(() => EditScale  += 10);
        ScaleDownCoarseCommand  = new RelayCommand(() => EditScale  = Math.Max(0.01, EditScale  - 10));
        OffsetUpCoarseCommand   = new RelayCommand(() => EditOffset += 10);
        OffsetDownCoarseCommand = new RelayCommand(() => EditOffset -= 10);

        ScaleUpFineCommand    = new RelayCommand(() => EditScale  += 1);
        ScaleDownFineCommand  = new RelayCommand(() => EditScale  = Math.Max(0.01, EditScale  - 1));
        OffsetUpFineCommand   = new RelayCommand(() => EditOffset += 1);
        OffsetDownFineCommand = new RelayCommand(() => EditOffset -= 1);

        NextBoolChannelCommand = new RelayCommand(CycleBoolChannelNext);
        PrevBoolChannelCommand = new RelayCommand(CycleBoolChannelPrev);
    }

    // ── Refresh (250 ms timer) ────────────────────────────────────────────────

    public void RefreshValues()
    {
        ReadAndApplyPot(0, _inputs.Pot1Volts);
        ReadAndApplyPot(1, _inputs.Pot2Volts);
        ReadAndApplyPot(2, _inputs.Pot3Volts);
        ReadAndApplyPot(3, _inputs.Pot4Volts);

        ReadBoolRow(4,  _inputs.Switch1On);
        ReadBoolRow(5,  _inputs.Switch2On);
        ReadBoolRow(6,  _inputs.Switch3On);
        ReadBoolRow(7,  _inputs.Switch4On);
        ReadBoolRow(8,  _inputs.Button1Down);
        ReadBoolRow(9,  _inputs.Button2Down);
        ReadBoolRow(10, _inputs.Button3Down);
        ReadBoolRow(11, _inputs.Button4Down);
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
            _applyPotValue?.Invoke(channel.Key, physical);
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
            EditKey    = ch?.Key    ?? "";
            EditUnit   = ch?.Unit   ?? QuickPicks[0].Unit;
            EditScale  = ch?.Scale  ?? QuickPicks[0].Scale;
            EditOffset = ch?.Offset ?? QuickPicks[0].Offset;
            UpdateRangePreview();
        }
        else
        {
            EditBoolChannel = row.AssignedBoolChannel ?? AvailableBoolChannels[0];
        }
    }

    private void ApplyQuickPick(PotQuickPick? pick)
    {
        if (pick == null) return;
        EditKey    = pick.Key;
        EditUnit   = pick.Unit;
        EditScale  = pick.Scale;
        EditOffset = pick.Offset;
        UpdateRangePreview();
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

        if (_editingRow.Type == InputType.Pot)
        {
            var displayName = string.IsNullOrWhiteSpace(_editUnit)
                ? _editingRow.Label
                : $"{_editKey} [{_editUnit}]";

            _editingRow.AssignedPotChannel = new PotChannel(
                _editKey, displayName, _editUnit, _editScale, _editOffset);
            _editingRow.AssignedName = $"{_editKey} ({_editUnit})";
        }
        else if (_editingRow.Type is InputType.Switch or InputType.Button
                 && _editBoolChannel != null)
        {
            _editingRow.AssignedBoolChannel = _editBoolChannel;
            _editingRow.AssignedName = _editBoolChannel.DisplayName;
        }

        EditingRow = null;
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

    // ── Allow Toolbox to set bool states ─────────────────────────────────────

    public void SetSwitchState(int oneBasedIndex, bool state)
    {
        switch (oneBasedIndex)
        {
            case 1: _inputs.Switch1On = state; break;
            case 2: _inputs.Switch2On = state; break;
            case 3: _inputs.Switch3On = state; break;
            case 4: _inputs.Switch4On = state; break;
        }
    }

    public void SetButtonState(int oneBasedIndex, bool state)
    {
        switch (oneBasedIndex)
        {
            case 1: _inputs.Button1Down = state; break;
            case 2: _inputs.Button2Down = state; break;
            case 3: _inputs.Button3Down = state; break;
            case 4: _inputs.Button4Down = state; break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ── ICommand helpers ──────────────────────────────────────────────────────────

file sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? _) => canExecute?.Invoke() ?? true;
    public void Execute(object? _)    => execute();
}

file sealed class RelayCommand<T>(Action<T?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? _) => true;
    public void Execute(object? parameter) => execute(parameter is T t ? t : default);
}
