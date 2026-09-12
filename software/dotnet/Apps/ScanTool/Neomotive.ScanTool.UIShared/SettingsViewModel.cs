using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Neomotive.ScanTool.UI;

/// <summary>
/// Backs the Settings page. Every change writes the file immediately — the appliance is powered
/// down by pulling the plug, so there is no "apply" step to hang a save off and no shutdown hook
/// to trust.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private AppSettings _settings = new();
    private string _dataDirectory = string.Empty;
    private string _status = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when a setting that other views react to has changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Where <c>settings.json</c> lives. Setting it loads the file, so the host assigns this
    /// during startup exactly as it does for the capture directory.
    /// </summary>
    public string DataDirectory
    {
        get => _dataDirectory;
        set
        {
            _dataDirectory = value ?? string.Empty;
            _settings = AppSettings.Load(SettingsPath);
            OnPropertyChanged(nameof(ShowToolTips));
            OnPropertyChanged(nameof(LivePollPeriodMs));
            OnPropertyChanged(nameof(LivePollPeriodText));
            OnPropertyChanged(nameof(SettingsPath));
            Changed?.Invoke();
        }
    }

    public string SettingsPath => string.IsNullOrEmpty(_dataDirectory)
        ? string.Empty
        : Path.Combine(_dataDirectory, "settings.json");

    public bool ShowToolTips
    {
        get => _settings.ShowToolTips;
        set
        {
            if (_settings.ShowToolTips == value)
            {
                return;
            }

            _settings.ShowToolTips = value;
            OnPropertyChanged();
            Changed?.Invoke();
            Save();
        }
    }

    /// <summary>
    /// Target live-data sweep period. Clamped to 20–2000 ms: below 20 ms the loop outruns any
    /// CAN module worth polling, above 2 s it is no longer live data.
    /// </summary>
    public int LivePollPeriodMs
    {
        get => _settings.LivePollPeriodMs;
        set
        {
            var clamped = Math.Clamp(value, 20, 2000);

            if (_settings.LivePollPeriodMs == clamped)
            {
                return;
            }

            _settings.LivePollPeriodMs = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LivePollPeriodText));
            Changed?.Invoke();
            Save();
        }
    }

    /// <summary>
    /// The period spelled out with the rate it implies. The panel is touch-only, so the number and
    /// its meaning both have to be on screen — there is no hover to explain what 100 ms buys.
    /// </summary>
    public string LivePollPeriodText => $"{LivePollPeriodMs} ms per sweep ({1000.0 / LivePollPeriodMs:F1} Hz)";

    /// <summary>Steps the period through the values worth offering on a touch panel.</summary>
    public void StepLivePollPeriod(int direction)
    {
        int[] steps = [20, 50, 100, 200, 500, 1000, 2000];
        var index = Array.IndexOf(steps, LivePollPeriodMs);

        if (index < 0)
        {
            // A hand-edited settings.json can hold any value; snap to the nearest offered step.
            index = 0;
            for (var i = 1; i < steps.Length; i++)
            {
                if (Math.Abs(steps[i] - LivePollPeriodMs) < Math.Abs(steps[index] - LivePollPeriodMs)) index = i;
            }
        }

        LivePollPeriodMs = steps[Math.Clamp(index + direction, 0, steps.Length - 1)];
    }

    /// <summary>
    /// The remembered-vehicle store, handed over by the shell once the data directory is known.
    /// Settings owns the two destructive actions on it, which is the one place a "forget" belongs.
    /// </summary>
    public Core.Vehicles.VehicleStore? VehicleStore
    {
        get => _vehicleStore;
        set
        {
            _vehicleStore = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RememberedVehiclesText));
        }
    }

    private Core.Vehicles.VehicleStore? _vehicleStore;

    public string RememberedVehiclesText => _vehicleStore is null
        ? "No data directory — nothing is being remembered."
        : _vehicleStore.Count switch
        {
            0 => "No vehicles remembered yet.",
            1 => "1 vehicle remembered.",
            var n => $"{n} vehicles remembered."
        };

    /// <summary>Re-reads the store's count after someone else has changed it.</summary>
    public void OnVehicleStoreChanged() => OnPropertyChanged(nameof(RememberedVehiclesText));

    /// <summary>Forgets every remembered vehicle, its class profiles included.</summary>
    public void ForgetAllVehicles()
    {
        if (_vehicleStore is null)
        {
            Status = "No data directory — nothing to forget.";
            return;
        }

        Status = _vehicleStore.ForgetAll()
            ? "Forgot all remembered vehicles."
            : $"Could not forget: {_vehicleStore.LastError}";

        OnPropertyChanged(nameof(RememberedVehiclesText));
    }

    /// <summary>Result of the last write, shown on the page so a failed save is not silent.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    private void Save()
    {
        if (string.IsNullOrEmpty(SettingsPath))
        {
            Status = "No data directory — settings cannot be saved.";
            return;
        }

        Status = _settings.TrySave(SettingsPath, out var error)
            ? $"Saved to {SettingsPath}"
            : $"Could not save: {error}";
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
