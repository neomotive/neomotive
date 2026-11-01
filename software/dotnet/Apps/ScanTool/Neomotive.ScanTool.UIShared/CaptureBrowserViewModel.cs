using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Neomotive.ScanTool.Core.Capture;
using Neomotive.Update;
using Neomotive.Vin.Contracts;

namespace Neomotive.ScanTool.UI;

/// <summary>One row in the saved-captures list: the file name, and nothing else.</summary>
/// <remarks>
/// The path is carried but never shown. On the appliance every capture lives in the same directory,
/// so the path is the same prefix on every row — it pushes the part that differs off the right edge
/// of a 7" panel and tells the operator nothing.
/// </remarks>
public sealed class CaptureFileItem
{
    public required string Path { get; init; }

    public required string FileName { get; init; }

    public override string ToString() => FileName;
}

/// <summary>
/// The "Previous captures" modal: every capture on the device, what each one is, and what can be
/// done with it.
/// </summary>
/// <remarks>
/// Selecting a row reads only the sidecar, not the samples. Loading the full recording is deferred
/// to View, because browsing is mostly the operator scanning down the list for the right run and
/// parsing every sample row of each candidate makes that scan crawl on the Pi.
/// </remarks>
public sealed class CaptureBrowserViewModel : INotifyPropertyChanged
{
    private readonly IVinDecoder? _vinDecoder;

    private bool _isOpen;
    private IReadOnlyList<CaptureFileItem> _items = Array.Empty<CaptureFileItem>();
    private CaptureFileItem? _selectedItem;
    private CaptureMetadata? _metadata;
    private ScanCapture? _scanCapture;
    private bool _isScreenshot;
    private bool _hasUsbDrive;
    private string _status = string.Empty;

    public CaptureBrowserViewModel(IVinDecoder? vinDecoder = null)
    {
        _vinDecoder = vinDecoder;
    }

    /// <summary>Where captures are read from. Set by <see cref="CaptureViewModel"/>.</summary>
    public string DataDirectory { get; set; } = string.Empty;

    public bool IsOpen
    {
        get => _isOpen;
        private set { _isOpen = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<CaptureFileItem> Items
    {
        get => _items;
        private set { _items = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasItems)); OnPropertyChanged(nameof(IsEmpty)); }
    }

    public bool HasItems => _items.Count > 0;

    public bool IsEmpty => _items.Count == 0;

    public CaptureFileItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value;
            _metadata = null;
            _scanCapture = null;
            _isScreenshot = false;

            if (value is not null)
            {
                if (value.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    _isScreenshot = true;
                }
                else if (value.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                         value.FileName.StartsWith("scan-", StringComparison.OrdinalIgnoreCase))
                {
                    _scanCapture = CaptureReader.LoadScanCapture(value.Path);
                }
                else
                {
                    _metadata = TryReadMetadata(value.Path);
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanView));
            OnPropertyChanged(nameof(CapturedText));
            OnPropertyChanged(nameof(VinText));
            OnPropertyChanged(nameof(VehicleText));
            OnPropertyChanged(nameof(DetailText));
            OnPropertyChanged(nameof(CanExportToUsb));
        }
    }

    public bool HasSelection => _selectedItem is not null;

    /// <summary>Only time-series CSV recordings can be viewed in the graphical review pane.</summary>
    public bool CanView => HasSelection && (_selectedItem?.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true);

    // ── Details of the selected capture ──────────────────────────────────────

    public string CapturedText
    {
        get
        {
            if (_isScreenshot)
            {
                return FallbackTimestamp();
            }

            if (_scanCapture is not null)
            {
                return _scanCapture.CapturedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }

            return _metadata is { StartedUtc.Ticks: > 0 } m
                ? m.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : FallbackTimestamp();
        }
    }

    public string VinText
    {
        get
        {
            if (_isScreenshot)
            {
                return "—";
            }

            if (_scanCapture is not null)
            {
                return string.IsNullOrWhiteSpace(_scanCapture.Vin) ? "—" : _scanCapture.Vin;
            }

            return string.IsNullOrWhiteSpace(_metadata?.Vin) ? "—" : _metadata!.Vin!;
        }
    }

    /// <summary>
    /// Year/make/model for the captured VIN, decoded locally.
    /// </summary>
    /// <remarks>
    /// Local decode only, never the network call: this list is browsed on a tool plugged into a car
    /// in a bay, which is exactly where there is no route to NHTSA, and a row that takes a timeout
    /// to answer is worse than one that says "Unknown vehicle".
    /// </remarks>
    public string VehicleText
    {
        get
        {
            if (_isScreenshot)
            {
                return "Screen capture";
            }

            if (_scanCapture is not null)
            {
                var scanParts = new[] { _scanCapture.Year?.ToString(), _scanCapture.Make, _scanCapture.Model }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToArray();

                if (scanParts.Length > 0)
                {
                    return string.Join(" ", scanParts);
                }

                if (!string.IsNullOrWhiteSpace(_scanCapture.Vin) && _vinDecoder is not null)
                {
                    try
                    {
                        var decode = _vinDecoder.DecodeLocal(_scanCapture.Vin);
                        var parts = new[] { decode.Year?.ToString(), decode.Make, decode.Model }
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .ToArray();
                        if (parts.Length > 0) return string.Join(" ", parts);
                    }
                    catch { }
                }

                return "Unknown vehicle";
            }

            var vin = _metadata?.Vin;

            if (string.IsNullOrWhiteSpace(vin) || _vinDecoder is null)
            {
                return "Unknown vehicle";
            }

            try
            {
                var decode = _vinDecoder.DecodeLocal(vin);

                var parts = new[] { decode.Year?.ToString(), decode.Make, decode.Model }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToArray();

                return parts.Length == 0 ? "Unknown vehicle" : string.Join(" ", parts);
            }
            catch
            {
                return "Unknown vehicle";
            }
        }
    }

    /// <summary>Size of the run, so the operator can tell a two-second misfire from a long drive.</summary>
    public string DetailText
    {
        get
        {
            if (_isScreenshot)
            {
                return "PNG screenshot image";
            }

            if (_scanCapture is not null)
            {
                var faultPart = _scanCapture.TotalFaultCount switch
                {
                    0 => "No codes",
                    1 => "1 code",
                    _ => $"{_scanCapture.TotalFaultCount} codes"
                };
                return $"{faultPart} across {_scanCapture.Modules.Count} modules · {_scanCapture.ReadinessMonitors.Count} monitors";
            }

            return _metadata is { } m
                ? $"{m.SampleCount} samples · {m.Signals.Count} signals · {m.DurationMs / 1000.0:F1} s"
                : "No sidecar — details unavailable";
        }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    // ── What can be done with it ─────────────────────────────────────────────

    /// <summary>
    /// True only while a stick is actually mounted. Re-checked when the modal opens and on Refresh,
    /// not on a timer — a disabled Export that stays disabled after the operator plugs in is the
    /// failure mode worth avoiding, and both of those are things they do anyway.
    /// </summary>
    public bool HasUsbDrive
    {
        get => _hasUsbDrive;
        private set { _hasUsbDrive = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportToUsb)); }
    }

    public bool CanExportToUsb => HasSelection && HasUsbDrive;

    /// <summary>Raised when the operator picks View: the host loads the file and shows Review.</summary>
    public event Action<string>? ViewRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Actions ──────────────────────────────────────────────────────────────

    public void Open()
    {
        Status = string.Empty;
        Refresh();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Refresh()
    {
        var previous = _selectedItem?.Path;

        Items = string.IsNullOrWhiteSpace(DataDirectory)
            ? Array.Empty<CaptureFileItem>()
            : CaptureReader.ListAllCaptures(DataDirectory)
                .Select(p => new CaptureFileItem { Path = p, FileName = Path.GetFileName(p) })
                .ToArray();

        HasUsbDrive = UsbUpdateSource.HasRemovableDrive();

        SelectedItem = previous is null
            ? null
            : Items.FirstOrDefault(i => string.Equals(i.Path, previous, StringComparison.OrdinalIgnoreCase));
    }

    public void View()
    {
        if (_selectedItem is not { } item)
        {
            return;
        }

        IsOpen = false;
        ViewRequested?.Invoke(item.Path);
    }

    /// <summary>
    /// Copies the selected capture (live CSV + sidecar, scan capture files, or screenshot PNG)
    /// to the connected USB drive under the appropriate NEOMOTIVE directory.
    /// </summary>
    public void ExportToUsb()
    {
        if (_selectedItem is not { } item)
        {
            return;
        }

        var root = UsbUpdateSource.GetRemovableRoots().FirstOrDefault();

        if (root is null)
        {
            HasUsbDrive = false;
            Status = "No USB drive found";
            return;
        }

        try
        {
            if (item.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                var target = Path.Combine(root, "NEOMOTIVE", "screenshots");
                Directory.CreateDirectory(target);
                File.Copy(item.Path, Path.Combine(target, item.FileName), overwrite: true);
                Status = $"Exported {item.FileName} to USB";
                return;
            }

            if (item.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                item.FileName.StartsWith("scan-", StringComparison.OrdinalIgnoreCase))
            {
                var target = Path.Combine(root, "NEOMOTIVE", "scans");
                if (ScanCaptureWriter.ExportToUsb(item.Path, target))
                {
                    Status = $"Exported {item.FileName} to USB";
                }
                else
                {
                    Status = "Export failed: missing scan files";
                }
                return;
            }

            var liveTarget = Path.Combine(root, "NEOMOTIVE", "captures");
            Directory.CreateDirectory(liveTarget);

            var stem = Path.Combine(
                Path.GetDirectoryName(item.Path) ?? string.Empty,
                Path.GetFileNameWithoutExtension(item.Path));

            var copied = 0;

            foreach (var extension in new[] { CaptureFile.CsvExtension, CaptureFile.SidecarExtension })
            {
                var source = stem + extension;

                if (!File.Exists(source))
                {
                    continue;
                }

                File.Copy(source, Path.Combine(liveTarget, Path.GetFileName(source)), overwrite: true);
                copied++;
            }

            Status = copied == 0
                ? "Nothing to export — capture files are missing"
                : $"Exported {Path.GetFileNameWithoutExtension(item.Path)} to USB";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }

    private static CaptureMetadata? TryReadMetadata(string path)
    {
        try
        {
            return CaptureReader.LoadMetadata(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The file's own timestamp, for a capture whose sidecar never made it to disk — which is what
    /// happens when the run ends with the key turned off, the case the trace is most wanted for.
    /// </summary>
    private string FallbackTimestamp()
    {
        if (_selectedItem is not { } item)
        {
            return "—";
        }

        try
        {
            return File.GetLastWriteTime(item.Path).ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return "—";
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
