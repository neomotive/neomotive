using Avalonia.Threading;
using Neomotive.ScanTool.Core;
using Neomotive.Update;
using Neomotive.Vin.Contracts;
using Neomotive.Vin.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.UI;

public record CanLogItem(string Time, string Id, bool IsOutgoing, string Data, string Description);

public class MainWindowViewModel : INotifyPropertyChanged
{
    private enum ScanView { Connection, Vehicle, Emissions, Dtcs, CanLog, LiveData, Updates }
    private enum LiveSubView { Table, Gauges, Waveform }

    private ScanView _view = ScanView.Connection;
    private LiveSubView _liveSubView = LiveSubView.Table;
    private readonly IObd2Scanner _scanner;
    private readonly LoggingCanBus? _loggingBus;
    private readonly CanPacketLog? _log;
    private DispatcherTimer? _logTimer;
    private CancellationTokenSource? _opCts;
    private CancellationTokenSource? _pollCts;
    private readonly IVinDecoder? _vinDecoder;
    private readonly UpdateService? _updateService;

    public MainWindowViewModel(IObd2Scanner scanner, LoggingCanBus? loggingBus = null, IVinDecoder? vinDecoder = null, UpdateService? updateService = null)
    {
        _scanner = scanner;
        _loggingBus = loggingBus;
        _vinDecoder = vinDecoder;
        _updateService = updateService;
        if (_loggingBus != null)
            _log = _loggingBus.Log;

        if (_updateService != null)
        {
            _updateService.UpdateFound             += m => Dispatcher.UIThread.Post(() => OnUpdateFound(m));
            _updateService.UpdateApplied           += r => Dispatcher.UIThread.Post(() => OnUpdateApplied(r));
            _updateService.UpdateFailed            += r => Dispatcher.UIThread.Post(() => UpdateStatus = $"Update failed: {r.Reason}");
            _updateService.StatusChanged           += s => Dispatcher.UIThread.Post(() => UpdateStatus = s);
            _updateService.UsbDriveInserted        += () => Dispatcher.UIThread.Post(() => OnUsbDriveInserted());
            _updateService.UsbDriveRemoved         += () => Dispatcher.UIThread.Post(() => OnUsbDriveRemoved());
            _updateService.UsbUpdateAvailable      += m  => Dispatcher.UIThread.Post(() => OnUsbUpdateAvailable(m));
            _updateService.UsbMultipleUpdatesFound += n  => Dispatcher.UIThread.Post(() => OnUsbMultipleFound());
            _updateService.UsbNoUpdateOnDrive      += () => Dispatcher.UIThread.Post(() => OnUsbNoUpdate());
        }

        LivePidItems = PidRegistry.CommonPids.Select(d => new LivePidItem(d)).ToList();
        foreach (var item in LivePidItems)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LivePidItem.IsSelected))
                {
                    OnPropertyChanged(nameof(HasSelectedPids));
                    OnPropertyChanged(nameof(HasNoSelectedPids));
                    OnPropertyChanged(nameof(SelectedLivePids));
                    OnPropertyChanged(nameof(GaugePids));
                    OnPropertyChanged(nameof(CanStartPolling));
                }
            };
    }

    public void StartCanLogTimer()
    {
        if (_log == null) return;
        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _logTimer.Tick += (_, _) => RefreshCanLog();
        _logTimer.Start();
    }

    // ── View selection ────────────────────────────────────────────────────────

    public bool IsConnectionView => _view == ScanView.Connection;
    public bool IsVehicleView => _view == ScanView.Vehicle;
    public bool IsEmissionsView => _view == ScanView.Emissions;
    public bool IsDtcsView => _view == ScanView.Dtcs;
    public bool IsCanLogView => _view == ScanView.CanLog;
    public bool IsLiveDataView => _view == ScanView.LiveData;
    public bool IsUpdatesView => _view == ScanView.Updates;

    public void ShowConnection() { StopPolling(); _view = ScanView.Connection; NotifyViewChanged(); }
    public void ShowVehicle() { StopPolling(); _view = ScanView.Vehicle; NotifyViewChanged(); }
    public void ShowEmissions() { StopPolling(); _view = ScanView.Emissions; NotifyViewChanged(); }
    public void ShowDtcs() { StopPolling(); _view = ScanView.Dtcs; NotifyViewChanged(); }
    public void ShowCanLog() { StopPolling(); _view = ScanView.CanLog; NotifyViewChanged(); }
    public void ShowLiveData() { _view = ScanView.LiveData; NotifyViewChanged(); }
    public void ShowUpdates() { StopPolling(); _view = ScanView.Updates; NotifyViewChanged(); }

    private void NotifyViewChanged()
    {
        OnPropertyChanged(nameof(IsConnectionView));
        OnPropertyChanged(nameof(IsVehicleView));
        OnPropertyChanged(nameof(IsEmissionsView));
        OnPropertyChanged(nameof(IsDtcsView));
        OnPropertyChanged(nameof(IsCanLogView));
        OnPropertyChanged(nameof(IsLiveDataView));
        OnPropertyChanged(nameof(IsUpdatesView));
    }

    // ── Update service ────────────────────────────────────────────────────────

    private string _updateStatus = "No update check performed.";
    private bool _isCheckingUpdate;
    private bool _hasUpdate;

    public string UpdateStatus
    {
        get => _updateStatus;
        private set { _updateStatus = value; OnPropertyChanged(); }
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set { _isCheckingUpdate = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCheckUpdate)); }
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        private set { _hasUpdate = value; OnPropertyChanged(); }
    }

    public bool CanCheckUpdate => !_isCheckingUpdate && _updateService != null;

    public async Task CheckForUpdatesAsync()
    {
        if (_updateService == null || _isCheckingUpdate) return;
        IsCheckingUpdate = true;
        UpdateStatus = "Checking for updates…";
        HasUpdate = false;
        try
        {
            var result = await _updateService.CheckNetworkAsync();
            switch (result)
            {
                case UpdateResult.NotAvailable:
                    UpdateStatus = "You are up to date.";
                    break;
                case UpdateResult.Applied r:
                    OnUpdateApplied(r);
                    break;
                case UpdateResult.Failed f:
                    UpdateStatus = $"Update check failed: {f.Reason}";
                    break;
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private void OnUpdateFound(UpdateManifest manifest)
    {
        HasUpdate = true;
        UpdateStatus = $"v{manifest.Version} ready.";
    }

    private void OnUpdateApplied(UpdateResult.Applied result)
    {
        HasUpdate = false;
        UpdateStatus = result.RequiresRestart
            ? $"v{result.Manifest.Version} staged. Restart to apply."
            : $"v{result.Manifest.Version} applied.";
    }

    // ── USB update state ──────────────────────────────────────────────────────

    private bool _usbHasDrive;
    private bool _usbUpdateReady;
    private bool _usbMultipleFound;
    private string _usbUpdateVersion = "";

    public string UsbStatusText => (_usbHasDrive, _usbUpdateReady, _usbMultipleFound) switch
    {
        (false, _, _) => "Insert a USB drive containing a neomotive-update*.zip package.",
        (_, _, true)  => "Multiple update packages found — remove extras and reinsert.",
        (_, true, _)  => $"Update v{_usbUpdateVersion} ready to install.",
        _             => "USB drive detected — no matching update found.",
    };

    public bool UsbUpdateReady    => _usbUpdateReady;
    public bool CanApplyUsbUpdate => _usbUpdateReady && !_isCheckingUpdate;

    private void OnUsbDriveInserted()  { _usbHasDrive = true;  _usbUpdateReady = false; _usbMultipleFound = false; NotifyUsbChanged(); }
    private void OnUsbDriveRemoved()   { _usbHasDrive = false; _usbUpdateReady = false; _usbMultipleFound = false; NotifyUsbChanged(); }
    private void OnUsbNoUpdate()       { _usbUpdateReady = false; _usbMultipleFound = false; NotifyUsbChanged(); }
    private void OnUsbMultipleFound()  { _usbUpdateReady = false; _usbMultipleFound = true;  NotifyUsbChanged(); }
    private void OnUsbUpdateAvailable(UpdateManifest m) { _usbUpdateReady = true; _usbMultipleFound = false; _usbUpdateVersion = m.Version; NotifyUsbChanged(); }

    private void NotifyUsbChanged()
    {
        OnPropertyChanged(nameof(UsbStatusText));
        OnPropertyChanged(nameof(UsbUpdateReady));
        OnPropertyChanged(nameof(CanApplyUsbUpdate));
    }

    public async Task ApplyUsbUpdateAsync()
    {
        if (_updateService == null || !CanApplyUsbUpdate) return;
        IsCheckingUpdate = true;
        NotifyUsbChanged();
        try   { await _updateService.ApplyUsbUpdateAsync(); }
        finally { IsCheckingUpdate = false; NotifyUsbChanged(); }
    }

    // ── Live Data sub-views ───────────────────────────────────────────────────

    public bool IsTableView => _liveSubView == LiveSubView.Table;
    public bool IsGaugesView => _liveSubView == LiveSubView.Gauges;
    public bool IsWaveformView => _liveSubView == LiveSubView.Waveform;

    public void ShowTable() { _liveSubView = LiveSubView.Table; NotifyLiveSubViewChanged(); }
    public void ShowGauges() { _liveSubView = LiveSubView.Gauges; NotifyLiveSubViewChanged(); }
    public void ShowWaveform() { _liveSubView = LiveSubView.Waveform; NotifyLiveSubViewChanged(); }

    private void NotifyLiveSubViewChanged()
    {
        OnPropertyChanged(nameof(IsTableView));
        OnPropertyChanged(nameof(IsGaugesView));
        OnPropertyChanged(nameof(IsWaveformView));
    }

    // ── Connection state ──────────────────────────────────────────────────────

    private bool _isConnected;
    private bool _isConnecting;
    private string _statusText = "Not connected";

    public bool IsConnected
    {
        get => _isConnected;
        private set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotConnected)); OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanRefresh)); OnPropertyChanged(nameof(CanStartPolling)); }
    }

    public bool IsNotConnected => !_isConnected;

    public bool IsConnecting
    {
        get => _isConnecting;
        private set { _isConnecting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanRefresh)); }
    }

    public bool CanConnect => !_isConnecting;
    public bool IsIdle => !_isConnected && !_isConnecting;

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public async Task ConnectAsync()
    {
        if (_isConnecting) return;

        _opCts?.Cancel();
        _opCts = new CancellationTokenSource();

        IsConnecting = true;
        StatusText = "Connecting…";

        try
        {
            var ok = await Task.Run(() => _scanner.ConnectAsync(_opCts.Token));
            Dispatcher.UIThread.Post(() =>
            {
                IsConnected = ok;
                StatusText = ok ? "Connected" : "No vehicle detected";
                IsConnecting = false;

                if (ok)
                {
                    IsSimulated = _scanner.IsSimulated;
                    ShowVehicle();
                    _ = RefreshAllAsync(_opCts.Token);
                }
            });
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = "Cancelled";
                IsConnecting = false;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"Error: {ex.Message}";
                IsConnecting = false;
            });
        }
    }

    public void Disconnect()
    {
        _opCts?.Cancel();
        StopPolling();
        IsConnected = false;
        IsSimulated = false;
        StatusText = "Disconnected";
        Vin = null;
        VinDecode = null;
        Protocol = "";
        ReadinessMonitors = [];
        ModuleDtcGroups = [];
        Modules = [];
        foreach (var item in LivePidItems)
            item.Reset();
    }

    // ── Modules ───────────────────────────────────────────────────────────────

    private IReadOnlyList<VehicleModule> _modules = [];
    public IReadOnlyList<VehicleModule> Modules
    {
        get => _modules;
        private set { _modules = value; OnPropertyChanged(); }
    }

    // ── Vehicle data ──────────────────────────────────────────────────────────

    private bool _isSimulated;
    public bool IsSimulated
    {
        get => _isSimulated;
        private set { _isSimulated = value; OnPropertyChanged(); }
    }

    private string? _vin;
    public string? Vin
    {
        get => _vin;
        private set { _vin = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayVin)); }
    }
    public string DisplayVin => _vin ?? "—";

    private VinDecodeResult? _vinDecode;
    public VinDecodeResult? VinDecode
    {
        get => _vinDecode;
        private set
        {
            _vinDecode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVinDecode));
            OnPropertyChanged(nameof(DisplayVinMake));
            OnPropertyChanged(nameof(DisplayVinModel));
            OnPropertyChanged(nameof(DisplayVinYear));
            OnPropertyChanged(nameof(DisplayVinCountry));
        }
    }

    public bool HasVinDecode => _vinDecode is { Validation.IsValid: true };
    public string DisplayVinMake => _vinDecode?.Make ?? "—";
    public string DisplayVinModel => _vinDecode?.Model ?? "—";
    public string DisplayVinYear => _vinDecode?.Year?.ToString() ?? "—";
    public string DisplayVinCountry => _vinDecode?.Country ?? "—";

    private string _protocol = "";
    public string Protocol
    {
        get => _protocol;
        private set { _protocol = value; OnPropertyChanged(); }
    }

    // ── Readiness monitors ────────────────────────────────────────────────────

    private IReadOnlyList<ReadinessMonitor> _readinessMonitors = [];
    public IReadOnlyList<ReadinessMonitor> ReadinessMonitors
    {
        get => _readinessMonitors;
        private set { _readinessMonitors = value; OnPropertyChanged(); }
    }

    // ── DTCs (per module) ─────────────────────────────────────────────────────

    private IReadOnlyList<ModuleDtcGroup> _moduleDtcGroups = [];
    public IReadOnlyList<ModuleDtcGroup> ModuleDtcGroups
    {
        get => _moduleDtcGroups;
        private set
        {
            _moduleDtcGroups = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MilOn));
            OnPropertyChanged(nameof(MilOff));
        }
    }

    public bool MilOn => _moduleDtcGroups.Any(g => g.HasStoredDtcs);
    public bool MilOff => !MilOn;

    public async Task ClearDtcsAsync()
    {
        if (!_isConnected) return;
        StatusText = "Clearing all DTCs…";
        try
        {
            await Task.Run(() => _scanner.ClearDtcsAsync());
            await RefreshDtcsByModuleAsync(CancellationToken.None);
            StatusText = "DTCs cleared";
        }
        catch (Exception ex)
        {
            StatusText = $"Clear failed: {ex.Message}";
        }
    }

    public async Task ClearModuleDtcsAsync(VehicleModule module)
    {
        if (!_isConnected) return;
        StatusText = $"Clearing {module.Name} DTCs…";
        try
        {
            await Task.Run(() => _scanner.ClearModuleDtcsAsync(module.Address));
            await RefreshDtcsByModuleAsync(CancellationToken.None);
            StatusText = "Connected";
        }
        catch (Exception ex)
        {
            StatusText = $"Clear failed: {ex.Message}";
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set { _isRefreshing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRefresh)); }
    }
    public bool CanRefresh => _isConnected && !_isRefreshing;

    public async Task RefreshAsync()
    {
        if (!_isConnected || _isRefreshing) return;

        _opCts?.Cancel();
        _opCts = new CancellationTokenSource();

        IsRefreshing = true;
        StatusText = "Refreshing…";

        try
        {
            await RefreshAllAsync(_opCts.Token);
            StatusText = "Connected";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Refresh cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh error: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        await RefreshVinAsync(ct);
        await RefreshReadinessAsync(ct);
        await RefreshDtcsByModuleAsync(ct);
        Dispatcher.UIThread.Post(() => Protocol = "ISO 15765-4 (CAN)");
    }

    private async Task RefreshVinAsync(CancellationToken ct)
    {
        try
        {
            var vin = await Task.Run(() => _scanner.ReadVinAsync(ct), ct);
            Dispatcher.UIThread.Post(() => Vin = vin);
            if (vin != null && _vinDecoder != null)
            {
                var decode = await _vinDecoder.DecodeAsync(vin, ct);
                Dispatcher.UIThread.Post(() => VinDecode = decode);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshReadinessAsync(CancellationToken ct)
    {
        try
        {
            var monitors = await Task.Run(() => _scanner.ReadReadinessAsync(ct), ct);
            Dispatcher.UIThread.Post(() => ReadinessMonitors = monitors);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshDtcsByModuleAsync(CancellationToken ct)
    {
        try
        {
            var groups = await Task.Run(() => _scanner.ReadDtcsByModuleAsync(ct), ct);
            Dispatcher.UIThread.Post(() =>
            {
                ModuleDtcGroups = groups;
                Modules = groups.Select(g => g.Module).ToList();
            });
        }
        catch (OperationCanceledException) { }
    }

    // ── Live Data ─────────────────────────────────────────────────────────────

    public IReadOnlyList<LivePidItem> LivePidItems { get; }

    public IEnumerable<LivePidItem> SelectedLivePids => LivePidItems.Where(p => p.IsSelected);
    public IEnumerable<LivePidItem> GaugePids => LivePidItems.Where(p => p.IsSelected).Take(6);
    public bool HasSelectedPids => LivePidItems.Any(p => p.IsSelected);
    public bool HasNoSelectedPids => !HasSelectedPids;

    private bool _isPolling;
    public bool IsPolling
    {
        get => _isPolling;
        private set { _isPolling = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartPolling)); OnPropertyChanged(nameof(CanStopPolling)); }
    }

    public bool CanStartPolling => _isConnected && !_isPolling;
    public bool CanStopPolling => _isPolling;

    public void StartPolling()
    {
        if (!_isConnected || _isPolling) return;
        _pollCts = new CancellationTokenSource();
        IsPolling = true;
        _ = RunPollingLoopAsync(_pollCts.Token);
    }

    public void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts = null;
        IsPolling = false;
    }

    private async Task RunPollingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var selected = LivePidItems.Where(p => p.IsSelected).ToList();
                foreach (var item in selected)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var pidValue = await Task.Run(() => _scanner.ReadPidAsync(item.Descriptor.Id, ct), ct);
                        if (pidValue != null)
                        {
                            var captured = item;
                            var v = pidValue.Value;
                            Dispatcher.UIThread.Post(() => captured.UpdateValue(v));
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { /* skip unresponsive PID */ }
                }
                await Task.Delay(500, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Dispatcher.UIThread.Post(() => IsPolling = false);
        }
    }

    public void SelectAllPids()
    {
        foreach (var item in LivePidItems)
            item.IsSelected = true;
    }

    public void SelectNoPids()
    {
        foreach (var item in LivePidItems)
            item.IsSelected = false;
    }

    // ── CAN Logging ───────────────────────────────────────────────────────────

    private bool _isLoggingEnabled;
    public bool IsLoggingEnabled
    {
        get => _isLoggingEnabled;
        set
        {
            _isLoggingEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowWaitingMessage));
            if (value) RefreshCanLog();
        }
    }

    private IReadOnlyList<CanLogItem> _canLogItems = [];
    public IReadOnlyList<CanLogItem> CanLogItems
    {
        get => _canLogItems;
        private set { _canLogItems = value; OnPropertyChanged(); }
    }

    private bool _hasCanPackets;
    public bool HasCanPackets
    {
        get => _hasCanPackets;
        private set { _hasCanPackets = value; OnPropertyChanged(); }
    }

    private bool _hasNoCanPackets = true;
    public bool HasNoCanPackets
    {
        get => _hasNoCanPackets;
        private set { _hasNoCanPackets = value; OnPropertyChanged(); }
    }

    public bool ShowWaitingMessage => IsLoggingEnabled && HasNoCanPackets;

    public event Action? CanLogUpdated;

    public void ClearCanLog()
    {
        _log?.Clear();
        CanLogItems = [];
        HasCanPackets = false;
        HasNoCanPackets = true;
        OnPropertyChanged(nameof(HasCanPackets));
        OnPropertyChanged(nameof(HasNoCanPackets));
        OnPropertyChanged(nameof(ShowWaitingMessage));
        CanLogUpdated?.Invoke();
    }

    private DateTime _lastCanTimestamp = DateTime.MinValue;
    private int _lastCanCount = 0;

    private void RefreshCanLog()
    {
        if (_log == null || !_isLoggingEnabled) return;
        var all = _log.GetAll();
        var latest = all.Count > 0 ? all[^1].Timestamp : DateTime.MinValue;
        if (latest == _lastCanTimestamp && all.Count == _lastCanCount) return;
        _lastCanTimestamp = latest;
        _lastCanCount = all.Count;

        CanLogItems = BuildCanLog(all);

        var hasPackets = CanLogItems.Count > 0;
        if (HasCanPackets != hasPackets)
        {
            HasCanPackets = hasPackets;
            HasNoCanPackets = !hasPackets;
            OnPropertyChanged(nameof(HasCanPackets));
            OnPropertyChanged(nameof(HasNoCanPackets));
            OnPropertyChanged(nameof(ShowWaitingMessage));
        }
        CanLogUpdated?.Invoke();
    }

    private IReadOnlyList<CanLogItem> BuildCanLog(IReadOnlyList<CanPacketEntry> packets)
    {
        return packets.Select(pkt => new CanLogItem(
            pkt.Timestamp.ToString("HH:mm:ss.fff"),
            pkt.Id.ToString("X3"),
            pkt.IsOutgoing,
            string.Join(" ", pkt.Data.Take(8).Select(b => b.ToString("X2"))),
            DescribePacket(pkt)
        )).ToList();
    }

    private static string DescribePacket(CanPacketEntry pkt)
    {
        var d = pkt.Data;
        if (d.Length < 2) return "";

        byte lenByte = d[0];
        byte isoType = (byte)(lenByte & 0xF0);
        byte svcByte;
        int offset;
        if (isoType == 0x10) { svcByte = d.Length > 2 ? d[2] : (byte)0; offset = 3; }
        else if (isoType == 0x20) return "ISO-TP Continuation";
        else if (isoType == 0x30) return "ISO-TP Flow Control";
        else { svcByte = d.Length > 1 ? d[1] : (byte)0; offset = 2; }

        if (pkt.IsOutgoing)
        {
            // Client Request
            return svcByte switch
            {
                0x01 => d.Length > offset ? (d[offset]) switch
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
                    var p => $"Query PID {p:X2}"
                } : "Query Current Data",
                0x03 => "Query Stored DTCs",
                0x04 => "Clear DTCs",
                0x07 => "Query Pending DTCs",
                0x09 => d.Length > offset ? (d[offset]) switch
                {
                    0x00 => "Query Veh Info PIDs",
                    0x02 => "Query VIN",
                    0x04 => "Query Cal ID",
                    0x06 => "Query CVN",
                    0x0A => "Query ECU Name",
                    var p => $"Query Veh Info {p:X2}"
                } : "Query Vehicle Info",
                0x0A => "Query Permanent DTCs",
                _ => $"Service {svcByte:X2}"
            };
        }
        else
        {
            // ECU Response
            if (svcByte >= 0x40)
            {
                byte service = (byte)(svcByte - 0x40);
                return service switch
                {
                    0x01 => d.Length > offset ? (d[offset]) switch
                    {
                        0x00 => "Supported PIDs Response",
                        0x01 => "Monitor Status Data",
                        0x05 => "Coolant Temp Data",
                        0x0C => "Engine RPM Data",
                        0x0D => "Vehicle Speed Data",
                        0x11 => "Throttle Position Data",
                        0x20 => "Supported PIDs 21-40 Response",
                        0x5C => "Trans Fluid Temp Data",
                        var p => $"PID {p:X2} Response"
                    } : "Current Data Response",
                    0x03 => "Stored DTCs Response",
                    0x04 => "DTCs Cleared Confirmation",
                    0x07 => "Pending DTCs Response",
                    0x09 => d.Length > offset ? (d[offset]) switch
                    {
                        0x02 => "VIN Response",
                        0x04 => "Cal ID Response",
                        0x06 => "CVN Response",
                        0x0A => "ECU Name Response",
                        _ => "Vehicle Info Response"
                    } : "Vehicle Info Response",
                    0x0A => "Permanent DTCs Response",
                    _ => $"Response {svcByte:X2}"
                };
            }
            else if (svcByte == 0x7F)
            {
                byte reqSvc = d.Length > 2 ? d[2] : (byte)0;
                byte nrc = d.Length > 3 ? d[3] : (byte)0;
                string nrcName = nrc switch
                {
                    0x10 => "General Reject",
                    0x11 => "Service Not Supported",
                    0x12 => "SubFunction Not Supported",
                    0x13 => "Incorrect Message Length Or Invalid Format",
                    0x21 => "Busy Repeat Request",
                    0x22 => "Conditions Not Correct",
                    0x24 => "Request Sequence Error",
                    0x78 => "Request Correctly Received-Response Pending",
                    _ => $"Error {nrc:X2}"
                };
                return $"Negative Response: Service {reqSvc:X2} - {nrcName}";
            }
            else
            {
                return $"RX Service {svcByte:X2}";
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
