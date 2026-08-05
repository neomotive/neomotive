using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Neomotive.Can.UI;

/// <summary>
/// Everything <see cref="Views.CanView"/> needs from a host view model.
/// Implemented by both the ScanTool and ModuleSimulator MainWindowViewModels.
/// </summary>
public interface ICanViewModel : INotifyPropertyChanged
{
    // ── Adapter ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Which CAN interface is in use — e.g. "CAN0 · CS pin24 · INT pin16" on the
    /// Pi HAT, or "PCAN USB" on the desktop. Set by the platform head, which is
    /// the only place that knows what it opened.
    /// </summary>
    string CanChannelName { get; }

    // ── Bus health ────────────────────────────────────────────────────────────
    bool AutoReconnect { get; set; }
    int AutoReconnectCount { get; }
    bool HasAutoReconnects { get; }
    int CanBusErrorCount { get; }
    int CanLastTxErrors { get; }
    int CanLastRxErrors { get; }
    bool CanBusStuck { get; }
    bool CanTxErrorSevere { get; }
    bool HasCanHealthData { get; }
    bool HasNoCanErrors { get; }
    void ResetCanErrors();

    // ── Packet log ────────────────────────────────────────────────────────────
    bool IsLoggingEnabled { get; set; }
    IReadOnlyList<CanLogItem> CanLogItems { get; }
    bool HasCanPackets { get; }
    bool ShowWaitingMessage { get; }
    void ClearCanLog();

    /// <summary>Raised after <see cref="CanLogItems"/> changes so the view can auto-scroll.</summary>
    event Action? CanLogUpdated;
}
