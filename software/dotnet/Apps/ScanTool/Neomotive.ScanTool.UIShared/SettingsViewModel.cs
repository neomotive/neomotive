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
