using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace Neomotive.ScanTool.UI.Views;

public partial class LiveDataWaveformPane : UserControl
{
    private DispatcherTimer? _timer;

    // Per-slot controls (indexed 0-3)
    private Canvas[]   _canvases  = new Canvas[4];
    private Polyline[] _primaries = new Polyline[4];
    private Polyline[] _secondaries = new Polyline[4];
    private TextBlock[] _leftLabels  = new TextBlock[4];
    private TextBlock[] _rightLabels = new TextBlock[4];

    public LiveDataWaveformPane() => InitializeComponent();

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        for (int i = 0; i < 4; i++)
        {
            _canvases[i]    = this.FindControl<Canvas>($"Slot{i}Canvas")!;
            _primaries[i]   = this.FindControl<Polyline>($"Slot{i}Primary")!;
            _secondaries[i] = this.FindControl<Polyline>($"Slot{i}Secondary")!;
            _leftLabels[i]  = this.FindControl<TextBlock>($"Slot{i}LeftLabel")!;
            _rightLabels[i] = this.FindControl<TextBlock>($"Slot{i}RightLabel")!;
        }

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

    private void Redraw()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var selected = vm.LivePidItems.Where(p => p.IsSelected).ToList();

        for (int slot = 0; slot < 4; slot++)
        {
            var primary   = slot     < selected.Count ? selected[slot]     : null;
            var secondary = slot + 4 < selected.Count ? selected[slot + 4] : null;

            _leftLabels[slot].Text  = primary?.Descriptor.Name ?? "";
            _rightLabels[slot].Text = secondary?.Descriptor.Name ?? "";

            DrawSeries(_canvases[slot], _primaries[slot],   primary);
            DrawSeries(_canvases[slot], _secondaries[slot], secondary);
        }
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
