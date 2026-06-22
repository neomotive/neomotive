namespace Neomotive.Update;

/// <summary>
/// Top-level orchestrator. Each app owns one instance.
/// USB is polled automatically every 5 s. Network is checked on demand.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly string _appId;
    private readonly string _currentVersion;
    private readonly UpdateApplicator _applicator;
    private readonly UsbUpdateSource _usb = new();
    private NetworkUpdateSource? _network;
    private Timer? _usbTimer;
    private bool _updatePending;

    public event Action<UpdateManifest>? UpdateFound;
    public event Action<UpdateResult.Applied>? UpdateApplied;
    public event Action<UpdateResult.Failed>? UpdateFailed;

    public UpdateService(string appId, string currentVersion, string baseDir)
    {
        _appId = appId;
        _currentVersion = currentVersion;
        _applicator = new UpdateApplicator(baseDir);
    }

    public void Configure(string? networkUrl)
    {
        _network?.Dispose();
        _network = string.IsNullOrWhiteSpace(networkUrl)
            ? null
            : new NetworkUpdateSource(networkUrl);
    }

    /// <summary>Starts the USB polling timer. Call once at app startup.</summary>
    public void StartUsbWatcher()
    {
        _usbTimer?.Dispose();
        _usbTimer = new Timer(_ => _ = CheckUsbAsync(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public async Task CheckUsbAsync(CancellationToken ct = default)
    {
        if (_updatePending) return;
        var result = await _usb.CheckAsync(_appId, _currentVersion, ct);
        if (result.HasValue)
            await ApplyAsync(result.Value.Manifest, result.Value.ZipPath, ct);
    }

    public async Task<UpdateResult> CheckNetworkAsync(CancellationToken ct = default)
    {
        if (_network == null)
            return new UpdateResult.Failed("No update server configured.");

        if (_updatePending)
            return new UpdateResult.Failed("An update is already staged and waiting for restart.");

        (UpdateManifest Manifest, string ZipPath)? result;
        try
        {
            result = await _network.CheckAsync(_appId, _currentVersion, ct);
        }
        catch (Exception ex)
        {
            return new UpdateResult.Failed(ex.Message);
        }

        if (result == null)
            return new UpdateResult.NotAvailable();

        return await ApplyAsync(result.Value.Manifest, result.Value.ZipPath, ct);
    }

    private async Task<UpdateResult> ApplyAsync(UpdateManifest manifest, string zipPath, CancellationToken ct)
    {
        try
        {
            await Task.Run(() =>
                UpdatePackage.ExtractAndVerify(zipPath, manifest, _applicator.StagingDir), ct);

            bool requiresRestart = _applicator.Apply(manifest);

            if (!requiresRestart && OperatingSystem.IsLinux())
                SelfRestart();

            _updatePending = requiresRestart;

            var applied = new UpdateResult.Applied(manifest) { RequiresRestart = requiresRestart };
            UpdateApplied?.Invoke(applied);
            return applied;
        }
        catch (Exception ex)
        {
            var failed = new UpdateResult.Failed(ex.Message);
            UpdateFailed?.Invoke(failed);
            return failed;
        }
    }

    /// <summary>
    /// Called from Program.cs at startup to clear any pending-version marker
    /// left by the previous instance after a staged Windows update.
    /// </summary>
    public void AcknowledgeStartup() => _applicator.ClearPendingState();

    private static void SelfRestart()
    {
        var exe = Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (exe != null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true
            });
        }
        Environment.Exit(0);
    }

    public void Dispose()
    {
        _usbTimer?.Dispose();
        _network?.Dispose();
    }
}
