using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Neomotive.ScanTool.Core;
using Neomotive.ScanTool.Core.Diagnostics;
using Neomotive.ScanTool.Core.Signals;

namespace Neomotive.ScanTool.UI;

/// <summary>
/// Modal editor for when a capture starts and stops.
/// </summary>
/// <remarks>
/// Shows the chosen signal's current value while the threshold is being set. Configuring
/// "RPM above 150" blind, against a signal reading zero, is how you end up with a trigger that
/// never fires; seeing the live number turns guesswork into a decision.
/// </remarks>
public class TriggerEditorViewModel : INotifyPropertyChanged
{
    private readonly IObd2Scanner _scanner;

    private SignalTable _table = new(SignalLibrary.BuiltIn);
    private CancellationTokenSource? _liveCts;

    private bool _isOpen;
    private ProfileTriggerMode _mode = ProfileTriggerMode.Manual;
    private string? _triggerSignal;
    private bool _above = true;
    private double _value;
    private int _dwellMs;
    private string? _triggerSignal2;
    private bool _above2 = true;
    private double _value2;
    private double _preTriggerSeconds = 5;
    private double _maxDurationSeconds = 120;

    private bool _stopEnabled;
    private string? _stopSignal;
    private double _stopFloor;
    private int _stopDurationMs = 5000;

    private double? _liveValue;
    private string _liveText = "—";

    public TriggerEditorViewModel(IObd2Scanner scanner) => _scanner = scanner;

    public SignalTable Table
    {
        get => _table;
        set { _table = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvailableSignals)); }
    }

    /// <summary>
    /// Signals the capture is actually recording. A trigger on anything else would never see a
    /// sample, so the editor only offers these.
    /// </summary>
    public IReadOnlyList<string> AvailableSignals { get; private set; } = [];

    public bool IsOpen
    {
        get => _isOpen;
        private set { _isOpen = value; OnPropertyChanged(); }
    }

    public ProfileTriggerMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManual));
            OnPropertyChanged(nameof(IsThreshold));
            OnPropertyChanged(nameof(IsBusWake));
            OnPropertyChanged(nameof(IsCompound));
            OnPropertyChanged(nameof(IsThresholdOrCompound));
            OnPropertyChanged(nameof(Summary));
            RestartLiveRead();
        }
    }

    public bool IsManual => _mode == ProfileTriggerMode.Manual;

    public bool IsThreshold => _mode == ProfileTriggerMode.Threshold;

    public bool IsCompound => _mode == ProfileTriggerMode.CompoundThreshold;

    public bool IsThresholdOrCompound => _mode is ProfileTriggerMode.Threshold or ProfileTriggerMode.CompoundThreshold;

    public bool IsBusWake => _mode == ProfileTriggerMode.BusWake;

    public string? TriggerSignal
    {
        get => _triggerSignal;
        set
        {
            _triggerSignal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(TriggerUnit));
            RestartLiveRead();
        }
    }

    public string TriggerUnit => _table.Find(_triggerSignal)?.Unit ?? string.Empty;

    public bool Above
    {
        get => _above;
        set { _above = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValueText)); OnPropertyChanged(nameof(Summary)); }
    }

    /// <summary>Value as text, so the on-screen keypad can edit it digit by digit.</summary>
    public string ValueText => _value.ToString("0.###", CultureInfo.InvariantCulture);

    public int DwellMs
    {
        get => _dwellMs;
        set { _dwellMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string? TriggerSignal2
    {
        get => _triggerSignal2;
        set { _triggerSignal2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string TriggerUnit2 => _table.Find(_triggerSignal2)?.Unit ?? string.Empty;

    public bool Above2
    {
        get => _above2;
        set { _above2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double Value2
    {
        get => _value2;
        set { _value2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValueText2)); OnPropertyChanged(nameof(Summary)); }
    }

    public string ValueText2 => _value2.ToString("0.###", CultureInfo.InvariantCulture);

    public double PreTriggerSeconds
    {
        get => _preTriggerSeconds;
        set { _preTriggerSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double MaxDurationSeconds
    {
        get => _maxDurationSeconds;
        set { _maxDurationSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public bool StopEnabled
    {
        get => _stopEnabled;
        set { _stopEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string? StopSignal
    {
        get => _stopSignal;
        set { _stopSignal = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double StopFloor
    {
        get => _stopFloor;
        set { _stopFloor = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public int StopDurationMs
    {
        get => _stopDurationMs;
        set { _stopDurationMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    /// <summary>Live reading of the trigger signal, so a sane threshold can be chosen.</summary>
    public string LiveText
    {
        get => _liveText;
        private set { _liveText = value; OnPropertyChanged(); OnPropertyChanged(nameof(ThresholdHint)); }
    }

    /// <summary>Warns when the condition is already true, which would fire the instant it arms.</summary>
    public string ThresholdHint
    {
        get
        {
            if (!IsThreshold || _liveValue is not { } live)
            {
                return string.Empty;
            }

            var satisfied = _above ? live > _value : live < _value;

            return satisfied
                ? "This condition is already true — the capture would trigger immediately."
                : string.Empty;
        }
    }

    /// <summary>Plain-language description of what will happen.</summary>
    public string Summary
    {
        get
        {
            var start = _mode switch
            {
                ProfileTriggerMode.BusWake => "Start recording as soon as the ECU responds.",
                ProfileTriggerMode.CompoundThreshold when !string.IsNullOrEmpty(_triggerSignal) && !string.IsNullOrEmpty(_triggerSignal2) =>
                    $"Start recording when {Label(_triggerSignal)} "
                    + $"{(_above ? "rises above" : "falls below")} {_value:0.###} {TriggerUnit}"
                    + $" AND {Label(_triggerSignal2)} "
                    + $"{(_above2 ? "rises above" : "falls below")} {_value2:0.###} {TriggerUnit2}"
                    + (_dwellMs > 0 ? $" and stays there for {_dwellMs} ms." : "."),
                ProfileTriggerMode.CompoundThreshold => "Choose signals for both conditions.",
                ProfileTriggerMode.Threshold when !string.IsNullOrEmpty(_triggerSignal) =>
                    $"Start recording when {Label(_triggerSignal)} "
                    + $"{(_above ? "rises above" : "falls below")} {_value:0.###} {TriggerUnit}"
                    + (_dwellMs > 0 ? $" and stays there for {_dwellMs} ms." : "."),
                ProfileTriggerMode.Threshold => "Choose a signal for the threshold.",
                _ => "Start recording when you press Trigger.",
            };

            var keep = _preTriggerSeconds > 0
                ? $" Keep the {_preTriggerSeconds:0.#} s leading up to it."
                : string.Empty;

            var stop = _stopEnabled && !string.IsNullOrEmpty(_stopSignal)
                ? $" Stop once {Label(_stopSignal)} has been active and then stays below "
                  + $"{_stopFloor:0.###} for {_stopDurationMs / 1000.0:0.#} s."
                : $" Stop after {_maxDurationSeconds:0.#} s or when you press Stop.";

            return start + keep + stop;
        }
    }

    private string Label(string? key) => _table.Find(key)?.Name ?? key ?? "(none)";

    public event Action<ProfileTrigger, ProfileStop, double, double>? Confirmed;

    public event Action? Cancelled;

    /// <summary>Opens the editor seeded from the current capture settings.</summary>
    public void Open(
        IReadOnlyList<string> availableSignals,
        ProfileTrigger trigger,
        ProfileStop stop,
        double preTriggerSeconds,
        double maxDurationSeconds)
    {
        AvailableSignals = availableSignals;
        OnPropertyChanged(nameof(AvailableSignals));

        Mode = trigger.Mode;
        TriggerSignal = trigger.Signal ?? availableSignals.FirstOrDefault();
        Above = trigger.Above;
        Value = trigger.Value;
        DwellMs = trigger.DwellMs;
        TriggerSignal2 = trigger.Signal2 ?? availableSignals.FirstOrDefault();
        Above2 = trigger.Above2;
        Value2 = trigger.Value2;

        StopEnabled = stop.Enabled;
        StopSignal = stop.Signal ?? availableSignals.FirstOrDefault();
        StopFloor = stop.Floor;
        StopDurationMs = stop.DurationMs;

        PreTriggerSeconds = preTriggerSeconds;
        MaxDurationSeconds = maxDurationSeconds;

        IsOpen = true;
        RestartLiveRead();
    }

    public void Confirm()
    {
        IsOpen = false;
        StopLiveRead();

        Confirmed?.Invoke(
            new ProfileTrigger
            {
                Mode = _mode,
                Signal = _triggerSignal,
                Above = _above,
                Value = _value,
                DwellMs = _dwellMs,
                Signal2 = _triggerSignal2,
                Above2 = _above2,
                Value2 = _value2,
            },
            new ProfileStop
            {
                Enabled = _stopEnabled,
                Signal = _stopSignal,
                Floor = _stopFloor,
                DurationMs = _stopDurationMs,
            },
            _preTriggerSeconds,
            _maxDurationSeconds);
    }

    public void Cancel()
    {
        IsOpen = false;
        StopLiveRead();
        Cancelled?.Invoke();
    }

    // ── Numeric keypad ───────────────────────────────────────────────────────
    // Text entry on the Pi's touch panel is painful, so the value is edited digit by digit.

    public void KeypadAppend(string digit)
    {
        var text = ValueText == "0" ? string.Empty : ValueText;

        if (digit == "." && text.Contains('.'))
        {
            return;
        }

        SetFromText(text + digit);
    }

    public void KeypadBackspace()
    {
        var text = ValueText;
        SetFromText(text.Length > 1 ? text[..^1] : "0");
    }

    public void KeypadClear() => Value = 0;

    public void KeypadNegate() => Value = -Value;

    public void KeypadAppend2(string digit)
    {
        var text = ValueText2 == "0" ? string.Empty : ValueText2;

        if (digit == "." && text.Contains('.'))
        {
            return;
        }

        SetFromText2(text + digit);
    }

    public void KeypadBackspace2()
    {
        var text = ValueText2;
        SetFromText2(text.Length > 1 ? text[..^1] : "0");
    }

    public void KeypadClear2() => Value2 = 0;

    public void KeypadNegate2() => Value2 = -Value2;

    /// <summary>Uses the signal's current reading as the threshold, then nudges it.</summary>
    public void UseLiveValue()
    {
        if (_liveValue is { } live)
        {
            Value = Math.Round(live, 3);
        }
    }

    private void SetFromText(string text)
    {
        if (string.IsNullOrEmpty(text) || text == "-")
        {
            Value = 0;
            return;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            Value = parsed;
        }
    }

    private void SetFromText2(string text)
    {
        if (string.IsNullOrEmpty(text) || text == "-")
        {
            Value2 = 0;
            return;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            Value2 = parsed;
        }
    }

    // ── Live reading ─────────────────────────────────────────────────────────

    private void RestartLiveRead()
    {
        StopLiveRead();

        if (!IsOpen || !IsThreshold || string.IsNullOrEmpty(_triggerSignal))
        {
            LiveText = "—";
            return;
        }

        var definition = _table.Find(_triggerSignal);

        if (definition is null || definition.Source != SignalSource.Mode01)
        {
            // Only standard PIDs are polled here; a Mode $22 channel would need the UDS client
            // and is not worth holding a session open for while configuring.
            LiveText = "—";
            return;
        }

        _liveCts = new CancellationTokenSource();
        _ = PollLiveAsync(definition, _liveCts.Token);
    }

    private void StopLiveRead()
    {
        _liveCts?.Cancel();
        _liveCts = null;
        _liveValue = null;
    }

    private async Task PollLiveAsync(SignalDefinition definition, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            double? value = null;

            try
            {
                value = definition.Decode(await _scanner.ReadPidDataAsync((byte)definition.Address, ct));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Not answering; shown as "—" below.
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                _liveValue = value;

                LiveText = value is { } v
                    ? $"{v.ToString("0." + new string('#', Math.Max(definition.Decimals, 1)), CultureInfo.InvariantCulture)} {definition.Unit}"
                    : "—";
            });

            try
            {
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
