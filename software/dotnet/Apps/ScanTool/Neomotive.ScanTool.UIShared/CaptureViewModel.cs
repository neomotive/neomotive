using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private string _triggerSignalKey = nameof(Pid.EngineRpm);
    private bool _triggerAbove = true;
    private double _triggerValue = 150;
    private int _triggerDwellMs = 200;

    private bool _useBusWakeTrigger;
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

        AvailablePids = PidRegistry.CommonPids
            .Select(d => new CapturePidItem(d))
            .ToList();

        // Profiles load once ConfigDirectory is set; until then offer the built-in library.
        _profiles = DiagnosticProfileLibrary.BuiltIn;
    }

    public IReadOnlyList<CapturePidItem> AvailablePids { get; }

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

    /// <summary>User-defined Mode $22 channels, loaded from the config directory.</summary>
    public System.Collections.ObjectModel.ObservableCollection<Mode22SignalItem> Mode22Signals { get; } = new();

    public bool HasMode22Signals => Mode22Signals.Count > 0;

    /// <summary>
    /// Where <c>mode22-signals.json</c> lives. Setting it loads the definitions; a template is
    /// written on first run so the format is discoverable on the device.
    /// </summary>
    public string ConfigDirectory
    {
        get => _configDirectory;
        set
        {
            _configDirectory = value;
            OnPropertyChanged();
            LoadMode22Signals();
            LoadProfiles();
        }
    }

    private void LoadMode22Signals()
    {
        Mode22Signals.Clear();

        if (string.IsNullOrWhiteSpace(_configDirectory))
        {
            OnPropertyChanged(nameof(HasMode22Signals));
            return;
        }

        var path = System.IO.Path.Combine(_configDirectory, Mode22SignalFile.DefaultFileName);

        try
        {
            Mode22SignalFile.WriteTemplateIfMissing(path);
        }
        catch (Exception)
        {
            // Read-only config directory; the definitions below simply stay empty.
        }

        foreach (var definition in Mode22SignalFile.Load(path))
        {
            Mode22Signals.Add(new Mode22SignalItem(definition));
        }

        OnPropertyChanged(nameof(HasMode22Signals));
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

            if (!UseThresholdTrigger)
            {
                return "manual start";
            }

            var direction = TriggerAbove ? "above" : "below";
            var dwell = TriggerDwellMs > 0 ? $" for {TriggerDwellMs} ms" : string.Empty;

            return $"{TriggerSignalKey} {direction} {TriggerValue:G6}{dwell}";
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
        var wanted = profile.Signals.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = 0;

        foreach (var item in AvailablePids)
        {
            item.IsSelected = wanted.Contains(item.Key);
            if (item.IsSelected) matched++;
        }

        foreach (var item in Mode22Signals)
        {
            item.IsSelected = wanted.Contains(item.Definition.Key);
            if (item.IsSelected) matched++;
        }

        UseBusWakeTrigger = profile.Trigger.Mode == ProfileTriggerMode.BusWake;
        UseThresholdTrigger = profile.Trigger.Mode == ProfileTriggerMode.Threshold;

        if (profile.Trigger.Mode == ProfileTriggerMode.Threshold)
        {
            TriggerSignalKey = profile.Trigger.Signal ?? string.Empty;
            TriggerAbove = profile.Trigger.Above;
            TriggerValue = profile.Trigger.Value;
            TriggerDwellMs = profile.Trigger.DwellMs;
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

        // A profile can name signals this vehicle does not report, or Mode $22 channels that are
        // not configured here. Those are skipped rather than treated as an error, so say what was
        // actually selected instead of silently under-capturing.
        var missing = profile.Signals.Count - matched;

        Status = missing > 0
            ? $"Applied '{profile.Name}' — {matched} of {profile.Signals.Count} signals available "
              + $"({missing} not found on this vehicle)."
            : $"Applied '{profile.Name}' — {matched} signals selected.";
    }

    public void SelectAll()
    {
        foreach (var item in AvailablePids)
        {
            item.IsSelected = true;
        }
    }

    public void SelectNone()
    {
        foreach (var item in AvailablePids)
        {
            item.IsSelected = false;
        }
    }

    /// <summary>Arms the capture and starts polling on a background task.</summary>
    public void Arm()
    {
        if (IsArmed)
        {
            return;
        }

        var selected = AvailablePids.Where(p => p.IsSelected).Select(p => p.Descriptor.Id).ToArray();

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

            foreach (var pid in selected)
            {
                builder.AddPid(_scanner, pid);
            }

            // Mode $22 channels ride alongside the standard PIDs in the same sweep, so commanded
            // and actual rail pressure land on one timeline.
            foreach (var definition in Mode22Signals.Where(d => d.IsSelected))
            {
                if (_udsScanner is null)
                {
                    Status = "Mode $22 channels selected but no UDS scanner is available.";
                    return;
                }

                builder.AddMode22(_udsScanner, definition.Definition);
            }

            channels = builder.Build();
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
