using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Meadow.Foundation.Telematics.J1979;
using Neomotive.ScanTool.Core;
using Neomotive.ScanTool.Core.Capture;
using Neomotive.ScanTool.Core.Diagnostics;
using Neomotive.ScanTool.Core.Signals;
using Meadow.Foundation.Telematics.Uds;

namespace Neomotive.ScanTool.UI;

/// <summary>
/// Drives event capture: signal selection, trigger configuration, the recording run itself, and
/// browsing what was recorded.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="MainWindowViewModel"/>, which is already large. The capture poll
/// loop runs on a background task and stamps its own timestamps, so everything that touches the UI
/// is marshalled through the dispatcher.
/// </remarks>
public class CaptureViewModel : INotifyPropertyChanged
{
    private readonly IObd2Scanner _scanner;
    private readonly IUdsScanner? _udsScanner;

    private CancellationTokenSource? _cts;
    private CaptureSession? _session;
    private CapturePollLoop? _loop;
    private DispatcherTimer? _statusTimer;

    private string _dataDirectory = string.Empty;
    private string _configDirectory = string.Empty;
    private IReadOnlyList<DiagnosticProfile> _profiles = Array.Empty<DiagnosticProfile>();
    private string? _selectedCategory;
    private DiagnosticProfile? _selectedProfile;
    private string _status = "Idle";
    private string _captureName = string.Empty;
    private bool _isArmed;
    private int _sampleCount;
    private double _achievedRateHz;
    private CaptureState _state = CaptureState.Idle;

    private bool _useThresholdTrigger = true;
    private bool _useCompoundTrigger;
    private string _triggerSignalKey = nameof(Pid.EngineRpm);
    private bool _triggerAbove = true;
    private double _triggerValue = 150;
    private int _triggerDwellMs = 200;

    private string? _triggerSignalKey2;
    private bool _triggerAbove2 = true;
    private double _triggerValue2;

    private bool _useBusWakeTrigger;
    private bool _enableDtcMonitoring = true;
    private bool _applyingProfile;
    private double _preTriggerSeconds = 5;
    private double _maxDurationSeconds = 120;

    private bool _stallStopEnabled = true;
    private string _stallSignalKey = nameof(Pid.EngineRpm);
    private double _stallFloor = 50;
    private int _stallDurationMs = 5000;

    private IReadOnlyList<string> _recordings = Array.Empty<string>();
    private string? _selectedRecording;
    private CaptureRecording? _loadedRecording;

    public CaptureViewModel(IObd2Scanner scanner, IUdsScanner? udsScanner = null)
    {
        _scanner = scanner;
        _udsScanner = udsScanner;

        Picker = new SignalPickerViewModel { Table = Table };
        Picker.Confirmed += OnSignalsChosen;

        TriggerEditor = new TriggerEditorViewModel(scanner) { Table = Table };
        TriggerEditor.Confirmed += OnTriggerConfigured;

        // Profiles load once ConfigDirectory is set; until then offer the built-in library.
        _profiles = DiagnosticProfileLibrary.BuiltIn;
    }

    /// <summary>Everything this vehicle session can read, standard PIDs plus Mode $22.</summary>
    public SignalTable Table { get; private set; } = new(SignalLibrary.BuiltIn);

    /// <summary>Signal keys currently chosen for capture, in table order.</summary>
    public IReadOnlyList<string> SelectedKeys { get; private set; } = [];

    public string SelectedSummary => SelectedKeys.Count == 0
        ? "No signals selected"
        : $"{SelectedKeys.Count} signals: " + string.Join(", ",
            SelectedKeys.Take(4).Select(k => Table.Find(k)?.DisplayName ?? k))
          + (SelectedKeys.Count > 4 ? $" +{SelectedKeys.Count - 4}" : string.Empty);

    /// <summary>Shared search-driven picker, opened as a modal overlay.</summary>
    public SignalPickerViewModel Picker { get; }

    /// <summary>Modal editor for the trigger and stop conditions.</summary>
    public TriggerEditorViewModel TriggerEditor { get; }

    public void OpenPicker() => Picker.Open(SelectedKeys);

    public void OpenTriggerEditor()
    {
        if (SelectedKeys.Count == 0)
        {
            Status = "Select signals before configuring the trigger.";
            return;
        }

        TriggerEditor.Table = Table;

        TriggerEditor.Open(
            SelectedKeys,
            new ProfileTrigger
            {
                Mode = UseBusWakeTrigger ? ProfileTriggerMode.BusWake
                     : UseCompoundTrigger ? ProfileTriggerMode.CompoundThreshold
                     : UseThresholdTrigger ? ProfileTriggerMode.Threshold
                     : ProfileTriggerMode.Manual,
                Signal = TriggerSignalKey,
                Above = TriggerAbove,
                Value = TriggerValue,
                DwellMs = TriggerDwellMs,
                Signal2 = TriggerSignalKey2,
                Above2 = TriggerAbove2,
                Value2 = TriggerValue2,
            },
            new ProfileStop
            {
                Enabled = StallStopEnabled,
                Signal = StallSignalKey,
                Floor = StallFloor,
                DurationMs = StallDurationMs,
            },
            PreTriggerSeconds,
            MaxDurationSeconds);
    }

    private void OnSignalsChosen(IReadOnlyList<string> keys)
    {
        SelectedKeys = keys;
        OnPropertyChanged(nameof(SelectedKeys));
        OnPropertyChanged(nameof(SelectedSummary));
        Status = $"{keys.Count} signals selected.";
    }

    private void OnTriggerConfigured(
        ProfileTrigger trigger, ProfileStop stop, double preTrigger, double maxDuration)
    {
        UseBusWakeTrigger = trigger.Mode == ProfileTriggerMode.BusWake;
        UseThresholdTrigger = trigger.Mode == ProfileTriggerMode.Threshold;
        UseCompoundTrigger = trigger.Mode == ProfileTriggerMode.CompoundThreshold;
        TriggerSignalKey = trigger.Signal ?? string.Empty;
        TriggerAbove = trigger.Above;
        TriggerValue = trigger.Value;
        TriggerDwellMs = trigger.DwellMs;
        TriggerSignalKey2 = trigger.Signal2;
        TriggerAbove2 = trigger.Above2;
        TriggerValue2 = trigger.Value2;

        StallStopEnabled = stop.Enabled;
        StallSignalKey = stop.Signal ?? string.Empty;
        StallFloor = stop.Floor;
        StallDurationMs = stop.DurationMs;

        PreTriggerSeconds = preTrigger;
        MaxDurationSeconds = maxDuration;

        EnsureTriggerSignalSelected();
    }

    /// <summary>
    /// Adds the threshold trigger's own signals to the recorded set if they are missing.
    /// </summary>
    /// <remarks>
    /// The capture loop only polls selected signals, so a trigger on an unselected signal would
    /// never see a value and could never fire — and even if it could, a capture that does not
    /// contain the signal it triggered on cannot be interpreted afterwards.
    /// </remarks>
    private void EnsureTriggerSignalSelected()
    {
        var keysToAdd = new List<string>();

        if ((UseThresholdTrigger || UseCompoundTrigger) && !string.IsNullOrWhiteSpace(TriggerSignalKey))
        {
            if (Table.Find(TriggerSignalKey) is not null
                && !SelectedKeys.Contains(TriggerSignalKey, StringComparer.OrdinalIgnoreCase))
            {
                keysToAdd.Add(TriggerSignalKey);
            }
        }

        if (UseCompoundTrigger && !string.IsNullOrWhiteSpace(TriggerSignalKey2))
        {
            if (Table.Find(TriggerSignalKey2) is not null
                && !SelectedKeys.Contains(TriggerSignalKey2, StringComparer.OrdinalIgnoreCase)
                && !keysToAdd.Contains(TriggerSignalKey2, StringComparer.OrdinalIgnoreCase))
            {
                keysToAdd.Add(TriggerSignalKey2);
            }
        }

        if (keysToAdd.Count == 0)
        {
            return;
        }

        SelectedKeys = SelectedKeys.Concat(keysToAdd).ToArray();
        OnPropertyChanged(nameof(SelectedKeys));
        OnPropertyChanged(nameof(SelectedSummary));
    }

    // ── Diagnostic profiles ──────────────────────────────────────────────────

    /// <summary>Every profile in the library, built-in plus anything the user has added.</summary>
    public IReadOnlyList<DiagnosticProfile> Profiles
    {
        get => _profiles;
        private set
        {
            _profiles = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Categories));
            OnPropertyChanged(nameof(ProfilesInCategory));
        }
    }

    /// <summary>Category names, for the first level of the profile picker.</summary>
    public IReadOnlyList<string> Categories
        => _profiles.Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();

    public string? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfilesInCategory));
        }
    }

    /// <summary>Profiles in the selected category, for the second level of the picker.</summary>
    public IReadOnlyList<DiagnosticProfile> ProfilesInCategory
        => string.IsNullOrEmpty(_selectedCategory)
            ? _profiles
            : _profiles.Where(p => string.Equals(p.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase))
                .ToArray();

    public DiagnosticProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProfileDescription));
            OnPropertyChanged(nameof(HasSelectedProfile));

            // Choosing a profile applies it. Requiring a separate Apply press made the profile
            // look inert — the description changed while the signals and trigger did not, so the
            // trigger on screen belonged to no profile at all.
            if (value is not null && !_applyingProfile)
            {
                ApplyProfile(value);
            }
        }
    }

    public bool HasSelectedProfile => _selectedProfile is not null;

    public string SelectedProfileDescription => _selectedProfile?.Description ?? string.Empty;

    /// <summary>Applies the profile currently chosen in the picker.</summary>
    public void ApplySelectedProfile()
    {
        if (_selectedProfile is not null)
        {
            ApplyProfile(_selectedProfile);
        }
    }

    private void LoadProfiles()
    {
        if (string.IsNullOrWhiteSpace(_configDirectory))
        {
            Profiles = DiagnosticProfileLibrary.BuiltIn;
            return;
        }

        var path = System.IO.Path.Combine(_configDirectory, DiagnosticProfileFile.DefaultFileName);

        try
        {
            DiagnosticProfileFile.WriteDefaultsIfMissing(path);
        }
        catch (Exception)
        {
            // Read-only config directory; the built-in library still loads below.
        }

        // Merge rather than overwrite, so profiles added by a later version appear without
        // discarding anything the user has written or edited.
        Profiles = DiagnosticProfileFile.MergeNewDefaults(path, DiagnosticProfileFile.Load(path));

        SelectedCategory ??= Categories.FirstOrDefault();
        SelectedProfile ??= ProfilesInCategory.FirstOrDefault();
    }

    /// <summary>
    /// Where <c>pid-table.json</c> and <c>mode22-signals.json</c> live. Setting it loads the
    /// signal table, writing the built-in PID table on first run so it is discoverable and
    /// correctable on the device.
    /// </summary>
    public string ConfigDirectory
    {
        get => _configDirectory;
        set
        {
            _configDirectory = value;
            OnPropertyChanged();
            LoadSignalTable();
            LoadProfiles();
        }
    }

    private void LoadSignalTable()
    {
        Table = SignalTable.Load(_configDirectory);
        Picker.Table = Table;
        TriggerEditor.Table = Table;
        OnPropertyChanged(nameof(Table));

        // Drop any selection the reloaded table no longer defines.
        SelectedKeys = SelectedKeys.Where(k => Table.Find(k) is not null).ToArray();
        OnPropertyChanged(nameof(SelectedKeys));
        OnPropertyChanged(nameof(SelectedSummary));
    }

    /// <summary>Where captures are written. Set by the host app at startup.</summary>
    public string DataDirectory
    {
        get => _dataDirectory;
        set
        {
            _dataDirectory = value;
            OnPropertyChanged();
            RefreshRecordings();
        }
    }

    // ── Trigger configuration ────────────────────────────────────────────────

    public bool UseThresholdTrigger
    {
        get => _useThresholdTrigger;
        set { _useThresholdTrigger = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public bool UseCompoundTrigger
    {
        get => _useCompoundTrigger;
        set { _useCompoundTrigger = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public bool UseBusWakeTrigger
    {
        get => _useBusWakeTrigger;
        set { _useBusWakeTrigger = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public string TriggerSignalKey
    {
        get => _triggerSignalKey;
        set { _triggerSignalKey = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public bool TriggerAbove
    {
        get => _triggerAbove;
        set { _triggerAbove = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public double TriggerValue
    {
        get => _triggerValue;
        set { _triggerValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public int TriggerDwellMs
    {
        get => _triggerDwellMs;
        set { _triggerDwellMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public string? TriggerSignalKey2
    {
        get => _triggerSignalKey2;
        set { _triggerSignalKey2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public bool TriggerAbove2
    {
        get => _triggerAbove2;
        set { _triggerAbove2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public double TriggerValue2
    {
        get => _triggerValue2;
        set { _triggerValue2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
    }

    public bool EnableDtcMonitoring
    {
        get => _enableDtcMonitoring;
        set { _enableDtcMonitoring = value; OnPropertyChanged(); }
    }

    public double PreTriggerSeconds
    {
        get => _preTriggerSeconds;
        set { _preTriggerSeconds = value; OnPropertyChanged(); }
    }

    public double MaxDurationSeconds
    {
        get => _maxDurationSeconds;
        set { _maxDurationSeconds = value; OnPropertyChanged(); }
    }

    public bool StallStopEnabled
    {
        get => _stallStopEnabled;
        set { _stallStopEnabled = value; OnPropertyChanged(); }
    }

    public string StallSignalKey
    {
        get => _stallSignalKey;
        set { _stallSignalKey = value; OnPropertyChanged(); }
    }

    public double StallFloor
    {
        get => _stallFloor;
        set { _stallFloor = value; OnPropertyChanged(); }
    }

    public int StallDurationMs
    {
        get => _stallDurationMs;
        set { _stallDurationMs = value; OnPropertyChanged(); }
    }

    public string CaptureName
    {
        get => _captureName;
        set { _captureName = value; OnPropertyChanged(); }
    }

    public string TriggerSummary
    {
        get
        {
            if (UseBusWakeTrigger)
            {
                return "on first response from the ECU";
            }

            if (UseCompoundTrigger && !string.IsNullOrEmpty(TriggerSignalKey2))
            {
                var dir1 = TriggerAbove ? "above" : "below";
                var dir2 = TriggerAbove2 ? "above" : "below";
                var dwell = TriggerDwellMs > 0 ? $" for {TriggerDwellMs} ms" : string.Empty;
                return $"{TriggerSignalKey} {dir1} {TriggerValue:G6} AND {TriggerSignalKey2} {dir2} {TriggerValue2:G6}{dwell}";
            }

            if (!UseThresholdTrigger)
            {
                return "manual start";
            }

            var direction = TriggerAbove ? "above" : "below";
            var dwellSingle = TriggerDwellMs > 0 ? $" for {TriggerDwellMs} ms" : string.Empty;

            return $"{TriggerSignalKey} {direction} {TriggerValue:G6}{dwellSingle}";
        }
    }

    // ── Run state ────────────────────────────────────────────────────────────

    public bool IsArmed
    {
        get => _isArmed;
        private set
        {
            _isArmed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanArm));
        }
    }

    public bool CanArm => !_isArmed;

    public CaptureState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsWaitingForTrigger));
        }
    }

    public bool IsRecording => _state == CaptureState.Recording;

    public bool IsWaitingForTrigger => _state == CaptureState.Buffering;

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public int SampleCount
    {
        get => _sampleCount;
        private set { _sampleCount = value; OnPropertyChanged(); }
    }

    public double AchievedRateHz
    {
        get => _achievedRateHz;
        private set { _achievedRateHz = value; OnPropertyChanged(); OnPropertyChanged(nameof(RateText)); }
    }

    public string RateText => _achievedRateHz <= 0 ? "—" : $"{_achievedRateHz:F1} Hz/signal";

    // ── Saved captures ───────────────────────────────────────────────────────

    public IReadOnlyList<string> Recordings
    {
        get => _recordings;
        private set { _recordings = value; OnPropertyChanged(); }
    }

    public string? SelectedRecording
    {
        get => _selectedRecording;
        set
        {
            _selectedRecording = value;
            OnPropertyChanged();

            if (!string.IsNullOrEmpty(value))
            {
                LoadRecording(value);
            }
        }
    }

    public CaptureRecording? LoadedRecording
    {
        get => _loadedRecording;
        private set
        {
            _loadedRecording = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLoadedRecording));
            OnPropertyChanged(nameof(LoadedSummary));
            RecordingChanged?.Invoke();
        }
    }

    public bool HasLoadedRecording => _loadedRecording is not null;

    public string LoadedSummary
    {
        get
        {
            if (_loadedRecording is not { } r)
            {
                return string.Empty;
            }

            var stall = r.Metadata.Events.Any(e => e.Kind == CaptureEventKind.StopConditionMet)
                ? " · stalled"
                : string.Empty;

            return $"{r.Samples.Count} samples · {r.Metadata.Signals.Count} signals · "
                 + $"{r.Metadata.DurationMs / 1000.0:F1} s · {r.Metadata.AchievedSampleRateHz:F1} Hz{stall}";
        }
    }

    /// <summary>Raised when a different capture is loaded, so the chart can redraw.</summary>
    public event Action? RecordingChanged;

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a profile: selects its signals and fills in the trigger, window and stop settings.
    /// </summary>
    /// <remarks>
    /// A profile is a starting point, not a lock. Everything it sets remains editable, because no
    /// fixed library can anticipate every diagnostic.
    /// </remarks>
    public void ApplyProfile(DiagnosticProfile profile)
    {
        _applyingProfile = true;

        try
        {
            ApplyProfileCore(profile);
        }
        finally
        {
            _applyingProfile = false;
        }
    }

    private void ApplyProfileCore(DiagnosticProfile profile)
    {
        // Keys the table does not define are dropped rather than treated as an error: a profile
        // may name a Mode $22 channel this vehicle has not had configured.
        var resolved = profile.Signals.Where(k => Table.Find(k) is not null).ToArray();
        var matched = resolved.Length;

        SelectedKeys = resolved;
        OnPropertyChanged(nameof(SelectedKeys));
        OnPropertyChanged(nameof(SelectedSummary));

        UseBusWakeTrigger = profile.Trigger.Mode == ProfileTriggerMode.BusWake;
        UseThresholdTrigger = profile.Trigger.Mode == ProfileTriggerMode.Threshold;
        UseCompoundTrigger = profile.Trigger.Mode == ProfileTriggerMode.CompoundThreshold;

        if (profile.Trigger.Mode is ProfileTriggerMode.Threshold or ProfileTriggerMode.CompoundThreshold)
        {
            TriggerSignalKey = profile.Trigger.Signal ?? string.Empty;
            TriggerAbove = profile.Trigger.Above;
            TriggerValue = profile.Trigger.Value;
            TriggerDwellMs = profile.Trigger.DwellMs;
        }

        if (profile.Trigger.Mode == ProfileTriggerMode.CompoundThreshold)
        {
            TriggerSignalKey2 = profile.Trigger.Signal2 ?? string.Empty;
            TriggerAbove2 = profile.Trigger.Above2;
            TriggerValue2 = profile.Trigger.Value2;
        }

        PreTriggerSeconds = profile.PreTriggerSeconds;
        MaxDurationSeconds = profile.MaxDurationSeconds;

        StallStopEnabled = profile.Stop.Enabled;

        if (profile.Stop.Enabled)
        {
            StallSignalKey = profile.Stop.Signal ?? string.Empty;
            StallFloor = profile.Stop.Floor;
            StallDurationMs = profile.Stop.DurationMs;
        }

        CaptureName = profile.CaptureName ?? profile.Key;
        SelectedProfile = profile;

        EnsureTriggerSignalSelected();

        // A profile can name signals this vehicle does not report, or Mode $22 channels that are
        // not configured here. Those are skipped rather than treated as an error, so say what was
        // actually selected instead of silently under-capturing.
        var missing = profile.Signals.Count - matched;

        Status = missing > 0
            ? $"Applied '{profile.Name}' — {matched} of {profile.Signals.Count} signals available "
              + $"({missing} not found on this vehicle)."
            : $"Applied '{profile.Name}' — {matched} signals selected.";
    }

    public void SelectNone()
    {
        SelectedKeys = [];
        OnPropertyChanged(nameof(SelectedKeys));
        OnPropertyChanged(nameof(SelectedSummary));
    }

    /// <summary>Arms the capture and starts polling on a background task.</summary>
    public void Arm()
    {
        if (IsArmed)
        {
            return;
        }

        var selected = SelectedKeys.Select(k => Table.Find(k)).OfType<SignalDefinition>().ToArray();

        if (selected.Length == 0)
        {
            Status = "Select at least one signal to capture.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DataDirectory))
        {
            Status = "No data directory configured.";
            return;
        }

        IReadOnlyList<CaptureChannel> channels;

        try
        {
            var builder = new CaptureChannelSetBuilder();
            var skipped = 0;

            // Mode $01 and Mode $22 signals ride in the same sweep, so commanded and actual land
            // on one timeline.
            foreach (var definition in selected)
            {
                if (!builder.TryAdd(definition, _scanner, _udsScanner))
                {
                    skipped++;
                }
            }

            channels = builder.Build();

            if (channels.Count == 0)
            {
                Status = "No selected signal can be read on this connection.";
                return;
            }

            if (skipped > 0)
            {
                Status = $"{skipped} Mode $22 signal(s) skipped — no UDS client available.";
            }
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
            return;
        }

        CaptureTrigger trigger;

        try
        {
            trigger = BuildTrigger();
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
            return;
        }

        var stall = StallStopEnabled
            ? new ActivityStopCondition(StallSignalKey, StallFloor, StallDurationMs)
            : null;

        var session = new CaptureSession(new CaptureSessionOptions(
            channels.Signals(),
            trigger,
            PreTriggerSeconds,
            // Rough sizing hint for the pre-trigger ring; the real rate is measured as we go.
            ExpectedSampleRateHz: 20,
            MaxDurationSeconds > 0 ? MaxDurationSeconds : null,
            stall));

        // Validate the trigger and stall signal keys before anything starts, so a typo surfaces
        // here rather than as a silent capture that never fires.
        try
        {
            trigger.Bind(channels.Signals());
            stall?.Bind(channels.Signals());
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
            return;
        }

        _session = session;
        _cts = new CancellationTokenSource();
        _loop = new CapturePollLoop(_scanner, session, channels);

        IsArmed = true;
        SampleCount = 0;
        AchievedRateHz = 0;
        State = CaptureState.Idle;
        Status = "Waiting for the vehicle to respond…";

        StartStatusTimer();

        // Start UDS session keepalive if any Mode $22 channels were included.
        var mode22TxIds = selected
            .Where(s => s.Source == SignalSource.Mode22)
            .Select(s => s.TxId)
            .Distinct()
            .ToArray();

        if (_udsScanner is not null && mode22TxIds.Length > 0)
        {
            var keepAlive = new UdsSessionKeepAlive(_udsScanner, mode22TxIds);
            _ = Task.Run(() => keepAlive.RunAsync(_cts.Token));
        }

        _ = Task.Run(() => RunCaptureAsync(session, channels, _loop, _cts.Token));
    }

    /// <summary>Ends the capture. Whatever was recorded up to this point is kept.</summary>
    public void Stop()
    {
        _session?.Stop(_loop?.ElapsedMs ?? 0);
        _cts?.Cancel();
    }

    /// <summary>Fires a manual trigger, when the trigger mode is manual.</summary>
    public void TriggerNow()
    {
        if (_session is { State: CaptureState.Buffering } && _manualTrigger is not null)
        {
            _manualTrigger.Fire();
        }
    }

    private ManualTrigger? _manualTrigger;

    private CaptureTrigger BuildTrigger()
    {
        _manualTrigger = new ManualTrigger();

        if (UseBusWakeTrigger)
        {
            return new BusWakeTrigger();
        }

        if (UseCompoundTrigger && !string.IsNullOrEmpty(TriggerSignalKey2))
        {
            var threshold1 = new ThresholdTrigger(
                TriggerSignalKey,
                TriggerAbove ? ThresholdComparison.Above : ThresholdComparison.Below,
                TriggerValue,
                TriggerDwellMs);

            var threshold2 = new ThresholdTrigger(
                TriggerSignalKey2,
                TriggerAbove2 ? ThresholdComparison.Above : ThresholdComparison.Below,
                TriggerValue2,
                dwellMs: 0);

            // Manual stays live alongside the compound trigger so the operator can always force a start.
            return new AnyTrigger(_manualTrigger, new AllTrigger(threshold1, threshold2));
        }

        if (!UseThresholdTrigger)
        {
            return _manualTrigger;
        }

        var threshold = new ThresholdTrigger(
            TriggerSignalKey,
            TriggerAbove ? ThresholdComparison.Above : ThresholdComparison.Below,
            TriggerValue,
            TriggerDwellMs);

        // Manual stays live alongside the threshold so the operator can always force a start.
        return new AnyTrigger(_manualTrigger, threshold);
    }

    private async Task RunCaptureAsync(
        CaptureSession session,
        IReadOnlyList<CaptureChannel> channels,
        CapturePollLoop loop,
        CancellationToken ct)
    {
        var stem = CaptureFile.BuildStem(DateTime.UtcNow, CaptureName);
        CaptureWriter? writer = null;

        try
        {
            writer = new CaptureWriter(DataDirectory, stem, channels.Signals());

            // Written straight through on the poll thread so an interrupted capture — a flat
            // battery, a yanked connector — still leaves usable data on disk.
            var localWriter = writer;
            session.SampleRecorded += sample => localWriter.Write(sample);
            session.StateChanged += state => Dispatcher.UIThread.Post(() => OnStateChanged(state));

            // Start DTC monitoring if enabled. Runs alongside the poll loop on a slow cadence.
            if (_enableDtcMonitoring)
            {
                var dtcChannel = new DtcPollingChannel(_scanner, session, () => loop.ElapsedMs);
                _ = Task.Run(() => dtcChannel.RunAsync(ct));
            }

            await loop.RunAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Operator stopped it; the samples already written stand.
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => Status = $"Capture failed: {ex.Message}");
        }
        finally
        {
            try
            {
                writer?.Complete(loop.BuildMetadata(TriggerSummary, CaptureName) with
                {
                    Vin = VehicleVin,
                    CalibrationId = VehicleCalibrationId,
                    Cvn = VehicleCvn,
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => Status = $"Could not write sidecar: {ex.Message}");
            }

            var csvPath = writer?.CsvPath;
            var count = writer?.SampleCount ?? 0;
            var rate = loop.AchievedSampleRateHz;
            writer?.Dispose();

            Dispatcher.UIThread.Post(() =>
            {
                StopStatusTimer();
                IsArmed = false;
                SampleCount = count;
                AchievedRateHz = rate;
                State = session.State;
                Status = count == 0
                    ? "Stopped — nothing recorded (trigger never fired)."
                    : $"Saved {count} samples to {Path.GetFileName(csvPath)}";

                RefreshRecordings();

                if (csvPath is not null && count > 0)
                {
                    SelectedRecording = csvPath;
                }
            });
        }
    }

    /// <summary>Vehicle identity stamped into each capture; set by the host view model.</summary>
    public string? VehicleVin { get; set; }

    public string? VehicleCalibrationId { get; set; }

    public string? VehicleCvn { get; set; }

    private void OnStateChanged(CaptureState state)
    {
        State = state;

        Status = state switch
        {
            CaptureState.Buffering => "Armed — buffering, waiting for trigger…",
            CaptureState.Recording => "Triggered — recording",
            CaptureState.Stopped => "Capture stopped",
            _ => Status,
        };
    }

    private void StartStatusTimer()
    {
        StopStatusTimer();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _statusTimer.Tick += (_, _) =>
        {
            if (_session is null || _loop is null)
            {
                return;
            }

            SampleCount = _session.RecordedSamples.Count;
            AchievedRateHz = _loop.AchievedSampleRateHz;
        };

        _statusTimer.Start();
    }

    private void StopStatusTimer()
    {
        _statusTimer?.Stop();
        _statusTimer = null;
    }

    public void RefreshRecordings()
    {
        Recordings = string.IsNullOrWhiteSpace(DataDirectory)
            ? Array.Empty<string>()
            : CaptureReader.ListCaptures(DataDirectory);
    }

    private void LoadRecording(string path)
    {
        try
        {
            LoadedRecording = CaptureReader.Load(path);
            Status = $"Loaded {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            LoadedRecording = null;
            Status = $"Could not load capture: {ex.Message}";
        }
    }

    /// <summary>Writes the loaded capture as a forward-filled wide CSV for spreadsheet use.</summary>
    public void ExportWideCsv()
    {
        if (_loadedRecording is null || string.IsNullOrEmpty(_selectedRecording))
        {
            return;
        }

        try
        {
            var path = Path.Combine(
                Path.GetDirectoryName(_selectedRecording)!,
                Path.GetFileNameWithoutExtension(_selectedRecording) + "-wide.csv");

            CaptureExporter.WriteWideCsv(_loadedRecording, path);
            Status = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
