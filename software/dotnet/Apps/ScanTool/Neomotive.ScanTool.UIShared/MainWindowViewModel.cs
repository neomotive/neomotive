using Avalonia.Threading;
using Meadow;
using Meadow.Hardware;
using Neomotive.ScanTool.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.UI;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private enum ScanView { Connection, Vehicle, Emissions, Dtcs }

    private ScanView _view = ScanView.Connection;
    private readonly IObd2Scanner _scanner;
    private CancellationTokenSource? _opCts;

    public MainWindowViewModel(IObd2Scanner scanner)
    {
        _scanner = scanner;
    }

    // ── View selection ────────────────────────────────────────────────────────

    public bool IsConnectionView => _view == ScanView.Connection;
    public bool IsVehicleView    => _view == ScanView.Vehicle;
    public bool IsEmissionsView  => _view == ScanView.Emissions;
    public bool IsDtcsView       => _view == ScanView.Dtcs;

    public void ShowConnection() { _view = ScanView.Connection; NotifyViewChanged(); }
    public void ShowVehicle()    { _view = ScanView.Vehicle;    NotifyViewChanged(); }
    public void ShowEmissions()  { _view = ScanView.Emissions;  NotifyViewChanged(); }
    public void ShowDtcs()       { _view = ScanView.Dtcs;       NotifyViewChanged(); }

    private void NotifyViewChanged()
    {
        OnPropertyChanged(nameof(IsConnectionView));
        OnPropertyChanged(nameof(IsVehicleView));
        OnPropertyChanged(nameof(IsEmissionsView));
        OnPropertyChanged(nameof(IsDtcsView));
    }

    // ── Connection state ──────────────────────────────────────────────────────

    private bool _isConnected;
    private bool _isConnecting;
    private string _statusText = "Not connected";

    public bool IsConnected
    {
        get => _isConnected;
        private set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotConnected)); }
    }

    public bool IsNotConnected => !_isConnected;

    public bool IsConnecting
    {
        get => _isConnecting;
        private set { _isConnecting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
    }

    public bool CanConnect => !_isConnecting;

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

                if (ok) _ = RefreshAllAsync(_opCts.Token);
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
        IsConnected = false;
        StatusText = "Disconnected";
        Vin = null;
        Protocol = "";
        ReadinessMonitors = [];
        StoredDtcs = [];
        PendingDtcs = [];
    }

    // ── Vehicle data ──────────────────────────────────────────────────────────

    private string? _vin;
    public string? Vin
    {
        get => _vin;
        private set { _vin = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayVin)); }
    }
    public string DisplayVin => _vin ?? "—";

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

    // ── DTCs ──────────────────────────────────────────────────────────────────

    public bool MilOn  => _storedDtcs.Count > 0;
    public bool MilOff => _storedDtcs.Count == 0;

    private IReadOnlyList<DiagnosticTroubleCode> _storedDtcs = [];
    public IReadOnlyList<DiagnosticTroubleCode> StoredDtcs
    {
        get => _storedDtcs;
        private set { _storedDtcs = value; OnPropertyChanged(); OnPropertyChanged(nameof(MilOn)); OnPropertyChanged(nameof(MilOff)); OnPropertyChanged(nameof(HasStoredDtcs)); }
    }

    private IReadOnlyList<DiagnosticTroubleCode> _pendingDtcs = [];
    public IReadOnlyList<DiagnosticTroubleCode> PendingDtcs
    {
        get => _pendingDtcs;
        private set { _pendingDtcs = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPendingDtcs)); }
    }

    public bool HasStoredDtcs  => _storedDtcs.Count > 0;
    public bool HasPendingDtcs => _pendingDtcs.Count > 0;

    public async Task ClearDtcsAsync()
    {
        if (!_isConnected) return;
        StatusText = "Clearing DTCs…";
        try
        {
            await Task.Run(() => _scanner.ClearDtcsAsync());
            await RefreshDtcsAsync(CancellationToken.None);
            StatusText = "DTCs cleared";
        }
        catch (Exception ex)
        {
            StatusText = $"Clear failed: {ex.Message}";
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            RefreshVinAsync(ct),
            RefreshReadinessAsync(ct),
            RefreshDtcsAsync(ct));
        Dispatcher.UIThread.Post(() => Protocol = "ISO 15765-4 (CAN)");
    }

    private async Task RefreshVinAsync(CancellationToken ct)
    {
        try
        {
            var vin = await Task.Run(() => _scanner.ReadVinAsync(ct), ct);
            Dispatcher.UIThread.Post(() => Vin = vin);
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

    private async Task RefreshDtcsAsync(CancellationToken ct)
    {
        try
        {
            var stored  = await Task.Run(() => _scanner.ReadStoredDtcsAsync(ct), ct);
            var pending = await Task.Run(() => _scanner.ReadPendingDtcsAsync(ct), ct);
            Dispatcher.UIThread.Post(() => { StoredDtcs = stored; PendingDtcs = pending; });
        }
        catch (OperationCanceledException) { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
