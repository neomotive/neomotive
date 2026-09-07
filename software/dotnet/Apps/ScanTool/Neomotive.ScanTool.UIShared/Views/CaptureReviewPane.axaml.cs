using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Neomotive.ScanTool.Core.Capture;

namespace Neomotive.ScanTool.UI.Views;

/// <summary>
/// Post-capture review: one stacked trace per captured signal on a shared, trigger-relative time
/// axis, with a cursor readout, zoom and pan.
/// </summary>
public partial class CaptureReviewPane : UserControl
{
    private static readonly string[] LaneColors =
    [
        "#4CAF50", "#2196F3", "#FF9800", "#9C27B0", "#E91E63", "#00BCD4", "#CDDC39", "#FF5722",
    ];

    private static readonly Points EmptyPoints = new();

    private readonly List<Lane> _lanes = new();

    private CaptureViewModel? _vm;
    private CaptureRecording? _recording;

    // Visible window, in trigger-relative milliseconds.
    private double _viewStartMs;
    private double _viewEndMs = 1;

    public CaptureReviewPane() => InitializeComponent();

    private sealed record Lane(
        CaptureSignal Signal,
        Canvas Canvas,
        Polyline Trace,
        Line TriggerMark,
        Line Cursor,
        TextBlock NameLabel,
        TextBlock RangeLabel,
        IReadOnlyList<CaptureSample> Samples,
        double Min,
        double Max);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.RecordingChanged -= Rebuild;
        }

        _vm = DataContext as CaptureViewModel;

        if (_vm is not null)
        {
            _vm.RecordingChanged += Rebuild;
        }

        Rebuild();
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_vm is not null)
        {
            _vm.RecordingChanged -= Rebuild;
        }
    }

    private void Rebuild()
    {
        var host = this.FindControl<StackPanel>("LanesHost")!;
        host.Children.Clear();
        _lanes.Clear();

        _recording = _vm?.LoadedRecording;

        if (_recording is null || _recording.Samples.Count == 0)
        {
            SetRangeLabel();
            return;
        }

        var signals = _recording.Metadata.Signals.OrderBy(s => s.Index).ToArray();

        for (var i = 0; i < signals.Length; i++)
        {
            var signal = signals[i];
            var samples = _recording.Samples.Where(s => s.SignalIndex == signal.Index).ToArray();

            if (samples.Length == 0)
            {
                continue;
            }

            var (min, max) = DataRange(samples);
            var color = Color.Parse(LaneColors[i % LaneColors.Length]);

            var trace = new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.5,
                Points = EmptyPoints,
            };

            // Trigger instant: time zero, and the reference every reading is taken against.
            var triggerMark = new Line
            {
                Stroke = new SolidColorBrush(Color.Parse("#FFCC00")),
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 3, 3 },
                IsVisible = false,
            };

            var cursor = new Line
            {
                Stroke = new SolidColorBrush(Color.Parse("#8899AA")),
                StrokeThickness = 1,
                IsVisible = false,
            };

            var canvas = new Canvas { ClipToBounds = true };
            canvas.Children.Add(triggerMark);
            canvas.Children.Add(trace);
            canvas.Children.Add(cursor);

            var nameLabel = new TextBlock
            {
                Classes = { "xs", "dim" },
                Text = $"{signal.Name} ({signal.Unit})",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 2, 0, 0),
                Foreground = new SolidColorBrush(color),
            };

            var rangeLabel = new TextBlock
            {
                Classes = { "xs", "dim" },
                Text = $"{min:G4} – {max:G4}",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 0),
            };

            var grid = new Grid();
            grid.Children.Add(canvas);
            grid.Children.Add(nameLabel);
            grid.Children.Add(rangeLabel);

            host.Children.Add(new Border
            {
                Height = 90,
                Background = new SolidColorBrush(Color.Parse("#0E1117")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1E2530")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            });

            _lanes.Add(new Lane(
                signal, canvas, trace, triggerMark, cursor, nameLabel, rangeLabel, samples, min, max));
        }

        Fit();
    }

    /// <summary>
    /// Scales each lane to the range actually present in the data rather than the PID's declared
    /// range. Rail pressure declares a 0-655,350 kPa range so it can express any common-rail
    /// system; drawn against that, a real 35,000 kPa trace is a flat line along the bottom and the
    /// rise-rate — the whole point of the capture — is invisible.
    /// </summary>
    private static (double Min, double Max) DataRange(IReadOnlyList<CaptureSample> samples)
    {
        var min = samples.Min(s => s.Value);
        var max = samples.Max(s => s.Value);

        if (Math.Abs(max - min) < 1e-9)
        {
            // Flat trace: give it a band so it draws through the middle instead of on an edge.
            var pad = Math.Abs(max) > 1e-9 ? Math.Abs(max) * 0.1 : 1;
            return (min - pad, max + pad);
        }

        var headroom = (max - min) * 0.08;
        return (min - headroom, max + headroom);
    }

    private void Fit()
    {
        if (_recording is null || _lanes.Count == 0)
        {
            _viewStartMs = 0;
            _viewEndMs = 1;
            SetRangeLabel();
            return;
        }

        var times = _recording.Samples.Select(s => (double)_recording.ToTriggerRelative(s.TimestampMs)).ToArray();

        _viewStartMs = times.Min();
        _viewEndMs = times.Max();

        if (_viewEndMs - _viewStartMs < 1)
        {
            _viewEndMs = _viewStartMs + 1;
        }

        Redraw();
    }

    private void Redraw()
    {
        if (_recording is null)
        {
            return;
        }

        var span = _viewEndMs - _viewStartMs;

        foreach (var lane in _lanes)
        {
            var w = lane.Canvas.Bounds.Width;
            var h = lane.Canvas.Bounds.Height;

            if (w < 1 || h < 1 || span <= 0)
            {
                lane.Trace.Points = EmptyPoints;
                continue;
            }

            var range = lane.Max - lane.Min;
            var points = new Points();

            foreach (var sample in lane.Samples)
            {
                var t = _recording.ToTriggerRelative(sample.TimestampMs);

                if (t < _viewStartMs || t > _viewEndMs)
                {
                    continue;
                }

                var x = w * ((t - _viewStartMs) / span);
                var y = h * (1.0 - Math.Clamp((sample.Value - lane.Min) / range, 0, 1));
                points.Add(new Point(x, y));
            }

            lane.Trace.Points = points.Count > 0 ? points : EmptyPoints;

            // Trigger line sits at t=0 when the trigger fired and is in view.
            var showTrigger = _recording.Metadata.TriggerTimestampMs >= 0
                && _viewStartMs <= 0 && _viewEndMs >= 0;

            lane.TriggerMark.IsVisible = showTrigger;

            if (showTrigger)
            {
                var tx = w * ((0 - _viewStartMs) / span);
                lane.TriggerMark.StartPoint = new Point(tx, 0);
                lane.TriggerMark.EndPoint = new Point(tx, h);
            }
        }

        SetRangeLabel();
    }

    private void SetRangeLabel()
    {
        var label = this.FindControl<TextBlock>("RangeLabel");

        if (label is null)
        {
            return;
        }

        label.Text = _recording is null
            ? "no capture loaded"
            : $"{_viewStartMs / 1000.0:F2} s … {_viewEndMs / 1000.0:F2} s (relative to trigger)";
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_recording is null || _lanes.Count == 0)
        {
            return;
        }

        var first = _lanes[0];
        var pos = e.GetPosition(first.Canvas);
        var w = first.Canvas.Bounds.Width;

        if (w < 1 || pos.X < 0 || pos.X > w)
        {
            HideCursor();
            return;
        }

        var span = _viewEndMs - _viewStartMs;
        var timeMs = _viewStartMs + span * (pos.X / w);

        var values = new List<string>();

        foreach (var lane in _lanes)
        {
            lane.Cursor.IsVisible = true;
            lane.Cursor.StartPoint = new Point(pos.X, 0);
            lane.Cursor.EndPoint = new Point(pos.X, lane.Canvas.Bounds.Height);

            var nearest = NearestSample(lane, timeMs);

            if (nearest is not null)
            {
                values.Add($"{lane.Signal.Name} {nearest.Value.Value:G5} {lane.Signal.Unit}");
            }
        }

        var cursorTime = this.FindControl<TextBlock>("CursorTime");
        var cursorValues = this.FindControl<TextBlock>("CursorValues");

        if (cursorTime is not null)
        {
            cursorTime.Text = (timeMs / 1000.0).ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " s";
        }

        if (cursorValues is not null)
        {
            cursorValues.Text = string.Join("   ", values);
        }
    }

    private CaptureSample? NearestSample(Lane lane, double timeMs)
    {
        CaptureSample? best = null;
        var bestDistance = double.MaxValue;

        foreach (var sample in lane.Samples)
        {
            var t = _recording!.ToTriggerRelative(sample.TimestampMs);
            var distance = Math.Abs(t - timeMs);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = sample;
            }
        }

        return best;
    }

    private void HideCursor()
    {
        foreach (var lane in _lanes)
        {
            lane.Cursor.IsVisible = false;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HideCursor();
    }

    // ── View controls ────────────────────────────────────────────────────────

    private void OnFit(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Fit();

    private void OnZoomIn(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Zoom(0.5);

    private void OnZoomOut(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Zoom(2.0);

    private void Zoom(double factor)
    {
        var centre = (_viewStartMs + _viewEndMs) / 2;
        var half = (_viewEndMs - _viewStartMs) / 2 * factor;

        if (half < 5)
        {
            half = 5;
        }

        _viewStartMs = centre - half;
        _viewEndMs = centre + half;
        Redraw();
    }

    private void OnPanLeft(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Pan(-0.25);

    private void OnPanRight(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Pan(0.25);

    private void Pan(double fraction)
    {
        var shift = (_viewEndMs - _viewStartMs) * fraction;
        _viewStartMs += shift;
        _viewEndMs += shift;
        Redraw();
    }

    /// <summary>Centres the view on the trigger, which is where interpretation starts.</summary>
    private void OnJumpToTrigger(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_recording is null || _recording.Metadata.TriggerTimestampMs < 0)
        {
            return;
        }

        var half = (_viewEndMs - _viewStartMs) / 2;
        _viewStartMs = -half;
        _viewEndMs = half;
        Redraw();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        // Canvas bounds are only known after layout, so the first meaningful draw happens here.
        Redraw();
        return size;
    }
}
