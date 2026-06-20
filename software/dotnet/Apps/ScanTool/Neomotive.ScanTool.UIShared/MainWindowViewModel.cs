using Avalonia.Threading;
using Meadow;
using Meadow.Hardware;
using Neomotive.ScanTool.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
        private set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotConnected)); OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanRefresh)); }
    }

    public bool IsNotConnected => !_isConnected;

    public bool IsConnecting
    {
        get => _isConnecting;
        private set { _isConnecting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanRefresh)); }
    }

    public bool CanConnect => !_isConnecting;
    public bool IsIdle     => !_isConnected && !_isConnecting;

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
        IsConnected = false;
        StatusText = "Disconnected";
        Vin = null;
        Protocol = "";
        ReadinessMonitors = [];
        ModuleDtcGroups = [];
        Modules = [];
    }

    // ── Modules ───────────────────────────────────────────────────────────────

    private IReadOnlyList<VehicleModule> _modules = [];
    public IReadOnlyList<VehicleModule> Modules
    {
        get => _modules;
        private set { _modules = value; OnPropertyChanged(); }
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

    public bool MilOn  => _moduleDtcGroups.Any(g => g.HasStoredDtcs);
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
        await Task.WhenAll(
            RefreshVinAsync(ct),
            RefreshReadinessAsync(ct),
            RefreshDtcsByModuleAsync(ct));
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
