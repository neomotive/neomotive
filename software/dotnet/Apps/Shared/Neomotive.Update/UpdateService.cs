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
    private bool _usbDriveWasPresent;
    private (UpdateManifest Manifest, string ZipPath)? _pendingUsbUpdate;

    public event Action<UpdateManifest>? UpdateFound;
    public event Action<UpdateResult.Applied>? UpdateApplied;
    public event Action<UpdateResult.Failed>? UpdateFailed;
    public event Action? UsbDriveInserted;
    public event Action? UsbDriveRemoved;
    public event Action<UpdateManifest>? UsbUpdateAvailable;
    public event Action<int>? UsbMultipleUpdatesFound;
    public event Action? UsbNoUpdateOnDrive;

    /// <summary>
    /// Where a device checks when nothing local overrides it: the rolling manifest
    /// the release workflow republishes on every tag. The repo is public, so this
    /// needs no credentials, and a stock device updates from the internet with no
    /// per-device configuration at all.
    /// </summary>
    public const string DefaultManifestUrl =
        "https://github.com/neomotive/neomotive/releases/download/updates-latest/version-manifest.json";

    public UpdateService(string appId, string currentVersion, string baseDir)
    {
        _appId = appId;
        // Normalized here so the version this compares against and the version the
        // Updates screen shows are the same clean MAJOR.MINOR.PATCH string.
        _currentVersion = UsbUpdateSource.Normalize(currentVersion);
        _applicator = new UpdateApplicator(baseDir);
    }

    /// <summary>The running version, as shown on the Updates screen.</summary>
    public string CurrentVersion => _currentVersion;

    /// <summary>The manifest endpoint currently in use.</summary>
    public string ManifestUrl { get; private set; } = DefaultManifestUrl;

    /// <summary>
    /// Points the network source at <paramref name="networkUrl"/>, or at
    /// <see cref="DefaultManifestUrl"/> when nothing is supplied. A device only
    /// needs neomotive.config.json to override the default — never to enable it.
    /// </summary>
    public void Configure(string? networkUrl)
    {
        _network?.Dispose();
        ManifestUrl = string.IsNullOrWhiteSpace(networkUrl) ? DefaultManifestUrl : networkUrl.Trim();
        _network = new NetworkUpdateSource(ManifestUrl);
    }

    public event Action<string>? StatusChanged;

    /// <summary>Starts the USB polling timer. Call once at app startup.</summary>
    public void StartUsbWatcher()
    {
        _usbTimer?.Dispose();
        _usbTimer = new Timer(_ => CheckUsbBackground(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private void CheckUsbBackground()
    {
        Task.Run(async () =>
        {
            try   { await CheckUsbAsync(); }
            catch (Exception ex) { UpdateFailed?.Invoke(new UpdateResult.Failed($"USB scan error: {ex.Message}")); }
        });
    }

    public async Task CheckUsbAsync(CancellationToken ct = default)
    {
        if (_updatePending) return;

        var drivePresent = UsbUpdateSource.HasRemovableDrive();

        if (!drivePresent)
        {
            if (_usbDriveWasPresent)
            {
                _usbDriveWasPresent = false;
                _pendingUsbUpdate = null;
                UsbDriveRemoved?.Invoke();
            }
            return;
        }

        if (!_usbDriveWasPresent)
        {
            _usbDriveWasPresent = true;
            UsbDriveInserted?.Invoke();
        }

        var matches = await _usb.ScanAsync(_appId, _currentVersion, ct);

        if (matches.Count == 0)
        {
            if (_pendingUsbUpdate != null)
            {
                _pendingUsbUpdate = null;
                UsbNoUpdateOnDrive?.Invoke();
            }
        }
        else if (matches.Count == 1)
        {
            var match = matches[0];
            if (_pendingUsbUpdate?.Manifest.Version != match.Manifest.Version)
            {
                _pendingUsbUpdate = match;
                UsbUpdateAvailable?.Invoke(match.Manifest);
            }
        }
        else
        {
            UsbMultipleUpdatesFound?.Invoke(matches.Count);
        }
    }

    public async Task ApplyUsbUpdateAsync(CancellationToken ct = default)
    {
        if (_pendingUsbUpdate == null) return;
        var pending = _pendingUsbUpdate.Value;
        _pendingUsbUpdate = null;
        await ApplyAsync(pending.Manifest, pending.ZipPath, ct);
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

    /// <summary>
    /// Exit code the launcher watches for. Both Pi launchers (ScanTool's `run`
    /// and the simulator's .xinitrc) supervise the app in a loop and relaunch on
    /// this code; anything else is treated as the app's own exit and passed on.
    /// </summary>
    public const int RestartExitCode = 42;

    private static void SelfRestart()
    {
        // Deliberately not Process.Start: the slot swap has already happened, so
        // Environment.ProcessPath now points into app-previous, and a child
        // spawned from a dying process on the Pi is orphaned into a session with
        // no display. Exiting with an agreed code lets the launcher — which
        // still owns the tty/DRM handle — start the new binary cleanly.
        Environment.Exit(RestartExitCode);
    }

    public void Dispose()
    {
        _usbTimer?.Dispose();
        _network?.Dispose();
    }
}
