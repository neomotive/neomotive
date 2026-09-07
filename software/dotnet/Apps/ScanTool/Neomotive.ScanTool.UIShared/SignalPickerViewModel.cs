using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Neomotive.ScanTool.Core.Signals;

namespace Neomotive.ScanTool.UI;

/// <summary>
/// Search-driven signal selection, shared by live view and capture.
/// </summary>
/// <remarks>
/// Search plus multi-select system filters, rather than a tree. A signal can genuinely belong to
/// several systems — rail pressure is both Fuel and Engine — and a strict tree would force it into
/// one branch. Filters also avoid expander targets, which are awkward on the 800x480 touch panel.
/// </remarks>
public class SignalPickerViewModel : INotifyPropertyChanged
{
    private SignalTable _table = new(SignalLibrary.BuiltIn);
    private string _searchText = string.Empty;
    private bool _showSelectedOnly;
    private bool _isOpen;

    /// <summary>Keys selected when the picker was opened, for Cancel.</summary>
    private string[] _openedWith = [];

    public SignalPickerViewModel()
    {
        Rebuild();
    }

    public ObservableCollection<SignalItem> Results { get; } = new();

    public ObservableCollection<SystemFilterItem> SystemFilters { get; } = new();

    /// <summary>The full set of currently selected keys, in table order.</summary>
    public IReadOnlyList<string> SelectedKeys { get; private set; } = [];

    public SignalTable Table
    {
        get => _table;
        set
        {
            _table = value;
            BuildFilters();
            Rebuild();
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set { _isOpen = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); Rebuild(); }
    }

    public bool ShowSelectedOnly
    {
        get => _showSelectedOnly;
        set { _showSelectedOnly = value; OnPropertyChanged(); Rebuild(); }
    }

    public int SelectedCount => _selected.Count;

    public string SelectedCountText => $"{_selected.Count} selected";

    public string ResultCountText => $"{Results.Count} of {_table.All.Count}";

    /// <summary>Raised when the operator confirms; carries the chosen keys.</summary>
    public event Action<IReadOnlyList<string>>? Confirmed;

    public event Action? Cancelled;

    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens the picker seeded with the given selection.</summary>
    public void Open(IEnumerable<string> currentKeys)
    {
        _selected.Clear();

        foreach (var key in currentKeys)
        {
            _selected.Add(key);
        }

        _openedWith = _selected.ToArray();
        SearchText = string.Empty;
        ShowSelectedOnly = false;
        IsOpen = true;

        Rebuild();
    }

    public void Confirm()
    {
        IsOpen = false;
        SelectedKeys = OrderedSelection();
        Confirmed?.Invoke(SelectedKeys);
    }

    public void Cancel()
    {
        IsOpen = false;

        _selected.Clear();

        foreach (var key in _openedWith)
        {
            _selected.Add(key);
        }

        Cancelled?.Invoke();
    }

    public void ClearSelection()
    {
        _selected.Clear();
        Rebuild();
    }

    public void ClearFilters()
    {
        SearchText = string.Empty;

        foreach (var filter in SystemFilters)
        {
            filter.IsActive = false;
        }
    }

    /// <summary>Selects everything currently listed — "select all" scoped to the active filter.</summary>
    public void SelectAllResults()
    {
        foreach (var item in Results)
        {
            _selected.Add(item.Key);
            item.IsSelected = true;
        }

        NotifyCounts();
    }

    private string[] OrderedSelection()
        => _table.All.Where(s => _selected.Contains(s.Key)).Select(s => s.Key).ToArray();

    private void BuildFilters()
    {
        SystemFilters.Clear();

        foreach (var system in _table.Systems)
        {
            SystemFilters.Add(new SystemFilterItem(system, Rebuild));
        }
    }

    private void Rebuild()
    {
        var active = SystemFilters.Where(f => f.IsActive).Select(f => f.Name).ToArray();
        var matches = _table.Search(_searchText, active);

        if (_showSelectedOnly)
        {
            matches = matches.Where(s => _selected.Contains(s.Key)).ToArray();
        }

        Results.Clear();

        foreach (var definition in matches)
        {
            var item = new SignalItem(definition) { IsSelected = _selected.Contains(definition.Key) };
            item.SelectionChanged += OnItemSelectionChanged;
            Results.Add(item);
        }

        NotifyCounts();
    }

    private void OnItemSelectionChanged(SignalItem item)
    {
        if (item.IsSelected)
        {
            _selected.Add(item.Key);
        }
        else
        {
            _selected.Remove(item.Key);
        }

        NotifyCounts();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(ResultCountText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
