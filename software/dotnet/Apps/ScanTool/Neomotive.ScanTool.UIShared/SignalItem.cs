using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Neomotive.ScanTool.Core.Signals;

namespace Neomotive.ScanTool.UI;

/// <summary>
/// Whether the connected vehicle reports serving a signal.
/// <para>
/// Three states, not two. The Mode 01 support bitmap only speaks for Mode 01, so a Mode $22 UDS
/// signal has nothing to consult and is honestly <see cref="Unknown"/> rather than quietly treated
/// as missing — and a vehicle that has not been read yet leaves everything unknown.
/// </para>
/// </summary>
public enum SignalSupport { Unknown, Supported, Unsupported }

/// <summary>A signal offered in the picker, with its selection state.</summary>
public class SignalItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public SignalItem(SignalDefinition definition, SignalSupport support = SignalSupport.Unknown)
    {
        Definition = definition;
        Support = support;
    }

    public SignalDefinition Definition { get; }

    /// <summary>What the vehicle said about this signal when the support bitmap was read.</summary>
    public SignalSupport Support { get; }

    /// <summary>
    /// The support state in words. The panel is touch-only and has no hover, and colour alone is
    /// not a label, so the row carries the state as text.
    /// </summary>
    public string SupportText => Support switch
    {
        SignalSupport.Supported => "supported",
        SignalSupport.Unsupported => "not supported",
        _ => ""
    };

    public bool IsUnsupported => Support == SignalSupport.Unsupported;

    public bool HasSupportText => SupportText.Length > 0;

    public string Key => Definition.Key;

    public string Name => Definition.Name;

    public string Unit => Definition.Unit;

    public string AddressText => Definition.AddressText;

    /// <summary>Leading column in the picker — the number a tech already knows.</summary>
    public string ShortAddressText => Definition.ShortAddressText;

    /// <summary>Systems this signal belongs to, for the row's trailing label.</summary>
    public string SystemsText => string.Join(" · ", Definition.Systems);

    /// <summary>
    /// Row tooltip, shown only when the operator has switched tooltips on. Carries the full
    /// address, which the row abbreviates to fit; the systems list is a visible column.
    /// </summary>
    public string DetailText => $"{Definition.AddressText} · {SystemsText}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionIndicator));
            SelectionChanged?.Invoke(this);
        }
    }

    public string SelectionIndicator => _isSelected ? "●" : "○";

    /// <summary>Raised so the owning picker can keep its counter and filters current.</summary>
    public event Action<SignalItem>? SelectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A system name used as a filter chip.</summary>
public class SystemFilterItem : INotifyPropertyChanged
{
    private bool _isActive;

    public SystemFilterItem(string name, Action changed)
    {
        Name = name;
        Changed = changed;
    }

    public string Name { get; }

    private Action Changed { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Indicator));
            OnPropertyChanged(nameof(Label));
            Changed();
        }
    }

    public string Indicator => _isActive ? "●" : "○";

    /// <summary>Indicator and name in one run of text, so the chip stays narrow enough to wrap.</summary>
    public string Label => $"{Indicator} {Name}";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
