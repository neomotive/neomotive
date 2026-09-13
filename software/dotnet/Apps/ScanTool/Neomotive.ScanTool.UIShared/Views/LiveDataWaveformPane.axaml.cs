using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace Neomotive.ScanTool.UI.Views;

public partial class LiveDataWaveformPane : UserControl
{
    private const int SlotCount = 4;

    private DispatcherTimer? _timer;

    // Per-slot controls (indexed 0-3)
    private Grid? _slotGrid;
    private Border[]    _slots       = new Border[SlotCount];
    private Canvas[]    _canvases    = new Canvas[SlotCount];
    private Polyline[]  _primaries   = new Polyline[SlotCount];
    private Polyline[]  _secondaries = new Polyline[SlotCount];
    private TextBlock[] _leftLabels  = new TextBlock[SlotCount];
    private TextBlock[] _rightLabels = new TextBlock[SlotCount];
    private TextBlock[] _leftValues  = new TextBlock[SlotCount];
    private TextBlock[] _rightValues = new TextBlock[SlotCount];
    private Button[]    _toggles     = new Button[SlotCount];

    /// <summary>
    /// Which tracks are showing only their numbers. View state, kept here rather than on the view
    /// model for the same reason the draw loop is here: nothing outside this pane has an opinion
    /// about it. It survives tab switches because the shell builds every view once and toggles
    /// visibility rather than recreating them.
    /// </summary>
    private readonly bool[] _collapsed = new bool[SlotCount];

    /// <summary>Set when a slot carries no signal at all, so it can give up its share of height.</summary>
    private readonly bool[] _unused = new bool[SlotCount];

    public LiveDataWaveformPane() => InitializeComponent();

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _slotGrid = this.FindControl<Grid>("SlotGrid")!;

        for (int i = 0; i < SlotCount; i++)
        {
            _slots[i]       = this.FindControl<Border>($"Slot{i}")!;
            _canvases[i]    = this.FindControl<Canvas>($"Slot{i}Canvas")!;
            _primaries[i]   = this.FindControl<Polyline>($"Slot{i}Primary")!;
            _secondaries[i] = this.FindControl<Polyline>($"Slot{i}Secondary")!;
            _leftLabels[i]  = this.FindControl<TextBlock>($"Slot{i}LeftLabel")!;
            _rightLabels[i] = this.FindControl<TextBlock>($"Slot{i}RightLabel")!;
            _leftValues[i]  = this.FindControl<TextBlock>($"Slot{i}LeftValue")!;
            _rightValues[i] = this.FindControl<TextBlock>($"Slot{i}RightValue")!;
            _toggles[i]     = this.FindControl<Button>($"Slot{i}Toggle")!;
        }

        ApplyLayout();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Redraw();
        _timer.Start();
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _timer?.Stop();
        _timer = null;
    }

    private void OnToggleSlot(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int slot)) return;
        if (slot < 0 || slot >= SlotCount) return;

        _collapsed[slot] = !_collapsed[slot];
        ApplyLayout();
        Redraw();
    }

    /// <summary>
    /// Gives every expanded track an equal share of the height and leaves collapsed or unused ones
    /// at the height of their header. Collapsing one of four tracks is what makes the other three
    /// taller — without this the chart would merely vanish and leave the space empty.
    /// </summary>
    private void ApplyLayout()
    {
        if (_slotGrid == null) return;

        for (int i = 0; i < SlotCount; i++)
        {
            bool showChart = !_collapsed[i] && !_unused[i];

            _canvases[i].IsVisible = showChart;
            _slots[i].IsVisible    = !_unused[i];

            // An unused slot has no chart to talk about, so it offers no toggle either.
            _toggles[i].IsVisible = !_unused[i];
            _toggles[i].Content   = _collapsed[i] ? "Expand" : "Collapse";

            _slotGrid.RowDefinitions[i].Height = showChart
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto;
        }
    }

    private void Redraw()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var selected = vm.LivePidItems.Where(p => p.IsSelected).ToList();

        // Names, numbers and which slots are in use are refreshed whether or not the loop is
        // running; only the traces are skipped when it is stopped. A stopped pane still has to
        // show what it last read and lay itself out correctly, or collapsing a track before
        // pressing start would appear to do nothing.
        bool polling = vm.IsPolling;

        bool layoutStale = false;

        for (int slot = 0; slot < SlotCount; slot++)
        {
            var primary   = slot            < selected.Count ? selected[slot]            : null;
            var secondary = slot + SlotCount < selected.Count ? selected[slot + SlotCount] : null;

            bool unused = primary == null && secondary == null;
            if (unused != _unused[slot])
            {
                _unused[slot] = unused;
                layoutStale = true;
            }

            _leftLabels[slot].Text  = primary?.Descriptor.Name ?? "";
            _rightLabels[slot].Text = secondary?.Descriptor.Name ?? "";

            // DisplayValue, not ValueText: on a collapsed track the number is the entire readout,
            // and a bare figure with no unit is a number the tech has to remember the meaning of.
            _leftValues[slot].Text  = primary?.DisplayValue ?? "";
            _rightValues[slot].Text = secondary?.DisplayValue ?? "";

            if (_collapsed[slot] || unused)
            {
                _primaries[slot].Points   = EmptyPoints;
                _secondaries[slot].Points = EmptyPoints;
                continue;
            }

            // A stopped loop leaves the last trace on screen rather than blanking it; the tech
            // stopped polling to look at what is already drawn.
            if (!polling) continue;

            DrawSeries(_canvases[slot], _primaries[slot],   primary);
            DrawSeries(_canvases[slot], _secondaries[slot], secondary);
        }

        if (layoutStale) ApplyLayout();
    }

    private static readonly Points EmptyPoints = new();

    private static void DrawSeries(Canvas canvas, Polyline line, LivePidItem? item)
    {
        if (item == null || canvas.Bounds.Width < 1 || canvas.Bounds.Height < 1)
        {
            line.Points = EmptyPoints;
            return;
        }

        var history = item.History.ToList();
        if (history.Count < 2)
        {
            line.Points = EmptyPoints;
            return;
        }

        double w = canvas.Bounds.Width;
        double h = canvas.Bounds.Height;
        double min = item.Descriptor.Min;
        double max = item.Descriptor.Max;
        double range = max > min ? max - min : 1;

        var now = DateTime.UtcNow;
        const double windowSecs = 60.0;

        var points = new Points();
        foreach (var (ts, value) in history)
        {
            double ageSecs = (now - ts).TotalSeconds;
            if (ageSecs > windowSecs) continue;
            double x = w * (1.0 - ageSecs / windowSecs);
            double y = h * (1.0 - Math.Clamp((value - min) / range, 0, 1));
            points.Add(new Point(x, y));
        }

        line.Points = points.Count > 0 ? points : EmptyPoints;
    }
}
