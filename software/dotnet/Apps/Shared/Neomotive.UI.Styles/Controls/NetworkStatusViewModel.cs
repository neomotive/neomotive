using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Neomotive.UI.Controls;

public enum NetworkState
{
    /// <summary>No usable link on any non-loopback interface.</summary>
    Disconnected,
    /// <summary>Link is up but no routable IPv4 address was assigned.</summary>
    NoAddress,
    /// <summary>Link is up and a routable IPv4 address is assigned.</summary>
    Connected,
}

/// <summary>
/// Polls the local network interfaces and exposes a single-line summary:
/// state, hostname, and (when valid) the active IPv4 address.
/// Self-contained — the control creates its own instance, so hosts only need
/// to drop <c>NetworkStatusBar</c> into a view.
/// </summary>
public class NetworkStatusViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _timer;

    private NetworkState _state = NetworkState.Disconnected;
    private string _hostName = "";
    private string _ipAddress = "";
    private bool _isWireless;

    public NetworkStatusViewModel()
    {
        try { _hostName = Dns.GetHostName(); } catch { _hostName = "unknown"; }

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Refresh();

        Refresh();
    }

    /// <summary>Begins polling. Called when the host control becomes visible.</summary>
    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    /// <summary>Stops polling. Called when the host control goes away.</summary>
    public void Stop() => _timer.Stop();

    public NetworkState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(IsNoAddress));
            OnPropertyChanged(nameof(HasAddress));
        }
    }

    public string HostName
    {
        get => _hostName;
        private set { if (_hostName == value) return; _hostName = value; OnPropertyChanged(); }
    }

    /// <summary>The active IPv4 address, or empty when none is assigned.</summary>
    public string IpAddress
    {
        get => _ipAddress;
        private set
        {
            if (_ipAddress == value) return;
            _ipAddress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAddress));
        }
    }

    public bool IsWireless
    {
        get => _isWireless;
        private set
        {
            if (_isWireless == value) return;
            _isWireless = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWired));
        }
    }

    public bool IsWired => !_isWireless;

    public bool HasAddress => _ipAddress.Length > 0;
    public bool IsConnected => _state == NetworkState.Connected;
    public bool IsDisconnected => _state == NetworkState.Disconnected;
    public bool IsNoAddress => _state == NetworkState.NoAddress;

    /// <summary>
    /// Visible wording carries the meaning — the Pi has no hover, so the icon
    /// colour alone must never be the only signal.
    /// </summary>
    public string StateText => _state switch
    {
        NetworkState.Connected => IsWireless ? "Wi-Fi" : "Wired",
        NetworkState.NoAddress => "No IP address",
        _                      => "Disconnected",
    };

    public void Refresh()
    {
        try
        {
            HostName = Dns.GetHostName();
        }
        catch
        {
            // Leave the last known name in place.
        }

        NetworkInterface[] nics;
        try
        {
            nics = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            State = NetworkState.Disconnected;
            IpAddress = "";
            return;
        }

        var candidates = nics
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                     && n.OperationalStatus == OperationalStatus.Up)
            // Prefer wired over wireless when both are up.
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
            .ToList();

        if (candidates.Count == 0)
        {
            State = NetworkState.Disconnected;
            IpAddress = "";
            return;
        }

        foreach (var nic in candidates)
        {
            var address = FirstRoutableIPv4(nic);
            if (address == null) continue;

            IsWireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            IpAddress = address.ToString();
            State = NetworkState.Connected;
            return;
        }

        // Link is up somewhere, but nothing handed us a usable address (no DHCP
        // lease yet, or only a 169.254.x.x self-assigned one).
        IsWireless = candidates[0].NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        IpAddress = "";
        State = NetworkState.NoAddress;
    }

    private static IPAddress? FirstRoutableIPv4(NetworkInterface nic)
    {
        try
        {
            foreach (var info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(info.Address)) continue;

                // Skip APIPA (169.254.0.0/16) — link-local means no real network.
                var bytes = info.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue;

                return info.Address;
            }
        }
        catch
        {
            // Some virtual adapters throw when queried; treat as address-less.
        }

        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
}
