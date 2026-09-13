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
            _metadata = value is null ? null : TryReadMetadata(value.Path);

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CapturedText));
            OnPropertyChanged(nameof(VinText));
            OnPropertyChanged(nameof(VehicleText));
            OnPropertyChanged(nameof(DetailText));
            OnPropertyChanged(nameof(CanExportToUsb));
        }
    }

    public bool HasSelection => _selectedItem is not null;

    // ── Details of the selected capture ──────────────────────────────────────

    public string CapturedText => _metadata is { StartedUtc.Ticks: > 0 } m
        ? m.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : FallbackTimestamp();

    public string VinText => string.IsNullOrWhiteSpace(_metadata?.Vin) ? "—" : _metadata!.Vin!;

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
    public string DetailText => _metadata is { } m
        ? $"{m.SampleCount} samples · {m.Signals.Count} signals · {m.DurationMs / 1000.0:F1} s"
        : "No sidecar — details unavailable";

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
            : CaptureReader.ListCaptures(DataDirectory)
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
    /// Copies the capture and its sidecar to the stick, under a NEOMOTIVE/captures folder.
    /// </summary>
    /// <remarks>
    /// Both files, always. The CSV on its own loads — the reader reconstructs signals from the
    /// column keys — but it arrives with no VIN, no trigger description and no sample rate, which
    /// is most of what makes an exported capture worth reading back at a desk.
    /// </remarks>
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
            var target = Path.Combine(root, "NEOMOTIVE", "captures");
            Directory.CreateDirectory(target);

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

                File.Copy(source, Path.Combine(target, Path.GetFileName(source)), overwrite: true);
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
