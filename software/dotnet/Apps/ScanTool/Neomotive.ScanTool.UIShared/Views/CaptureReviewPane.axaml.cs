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
        double Max,
        List<(CaptureEvent Event, Line MarkerLine, TextBlock? Label)> EventMarkers,
        Border Container,
        bool HasData)
    {
        /// <summary>Lanes switched off from the chip strip keep their data but stop drawing.</summary>
        public bool IsShown => Container.IsVisible;
    }

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
        var chips = this.FindControl<StackPanel>("LaneToggles")!;
        var chipsHost = this.FindControl<Border>("LaneTogglesHost")!;
        host.Children.Clear();
        chips.Children.Clear();
        chipsHost.IsVisible = false;
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

            // A signal the ECU never answered used to be dropped from the review entirely, which
            // reads as "I only asked for two signals" rather than "three PIDs went unanswered".
            // Draw it as an empty lane instead, so the gap is visible and attributable.
            var hasData = samples.Length > 0;
            var (min, max) = hasData ? DataRange(samples) : (0d, 1d);
            var color = hasData
                ? Color.Parse(LaneColors[i % LaneColors.Length])
                : Color.Parse("#5A6472");

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

            var eventMarkers = new List<(CaptureEvent Event, Line MarkerLine, TextBlock? Label)>();

            // Render noteworthy timeline events (DTC sets/clears, bus drops) across lanes.
            foreach (var evt in _recording.Metadata.Events)
            {
                if (evt.Kind is CaptureEventKind.Armed or CaptureEventKind.Triggered or CaptureEventKind.Stopped)
                {
                    continue;
                }

                var strokeColor = evt.Kind switch
                {
                    CaptureEventKind.DtcSet => Color.Parse("#FF5252"),
                    CaptureEventKind.DtcCleared => Color.Parse("#448AFF"),
                    CaptureEventKind.BusLost => Color.Parse("#FFAB00"),
                    CaptureEventKind.BusRestored => Color.Parse("#69F0AE"),
                    CaptureEventKind.StopConditionMet => Color.Parse("#E040FB"),
                    _ => Color.Parse("#B0BEC5"),
                };

                var evtLine = new Line
                {
                    Stroke = new SolidColorBrush(strokeColor),
                    StrokeThickness = 1,
                    StrokeDashArray = new AvaloniaList<double> { 2, 2 },
                    IsVisible = false,
                };
                canvas.Children.Add(evtLine);

                TextBlock? evtLabel = null;
                // Place text badge on the first lane only to avoid cluttering every lane.
                if (i == 0)
                {
                    var text = evt.Kind switch
                    {
                        CaptureEventKind.DtcSet => $"DTC +{evt.Detail}",
                        CaptureEventKind.DtcCleared => $"DTC −{evt.Detail}",
                        CaptureEventKind.BusLost => "Bus Lost",
                        CaptureEventKind.BusRestored => "Bus Restored",
                        CaptureEventKind.StopConditionMet => "Stop Cond",
                        CaptureEventKind.DurationReached => "Max Dur",
                        _ => evt.Kind.ToString(),
                    };

                    evtLabel = new TextBlock
                    {
                        Classes = { "xs" },
                        Foreground = new SolidColorBrush(strokeColor),
                        Text = text,
                        IsVisible = false,
                    };
                    canvas.Children.Add(evtLabel);
                }

                eventMarkers.Add((evt, evtLine, evtLabel));
            }

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
                Text = hasData ? $"{min:G4} – {max:G4}" : "no data — ECU did not respond",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 0),
            };

            var grid = new Grid();
            grid.Children.Add(canvas);
            grid.Children.Add(nameLabel);
            grid.Children.Add(rangeLabel);

            var container = new Border
            {
                Height = 90,
                Background = new SolidColorBrush(Color.Parse("#0E1117")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1E2530")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            };

            host.Children.Add(container);

            var lane = new Lane(
                signal, canvas, trace, triggerMark, cursor, nameLabel, rangeLabel, samples, min, max,
                eventMarkers, container, hasData);

            _lanes.Add(lane);
            chips.Children.Add(BuildChip(lane, color));
        }

        chipsHost.IsVisible = _lanes.Count > 1;

        Fit();
    }

    /// <summary>
    /// Scales each lane to the range actually present in the data rather than the PID's declared
    /// range. Rail pressure declares a 0-655,350 kPa range so it can express any common-rail
    /// system; drawn against that, a real 35,000 kPa trace is a flat line along the bottom and the
    /// rise-rate — the whole point of the capture — is invisible.
    /// </summary>
    /// <summary>
    /// One tappable chip per lane. The signal name is always spelled out on the chip — the
    /// appliance is touch-only, so there is no hover to explain an icon — and on/off state is
    /// carried by the swatch fill and the text colour beside it.
    /// </summary>
    private Border BuildChip(Lane lane, Color color)
    {
        var brush = new SolidColorBrush(color);

        var swatch = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(2),
            BorderBrush = brush,
            BorderThickness = new Thickness(2),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Classes = { "xs" },
            Text = lane.HasData ? lane.Signal.Name : $"{lane.Signal.Name} (no data)",
            Foreground = brush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var chip = new Border
        {
            MinHeight = 40,
            MinWidth = 44,
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.Parse("#1A2030")),
            BorderBrush = brush,
            BorderThickness = new Thickness(2),
            Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { swatch, label },
            },
        };

        chip.PointerPressed += (_, _) =>
        {
            var showing = !lane.IsShown;
            lane.Container.IsVisible = showing;

            swatch.Background = showing ? brush : Brushes.Transparent;
            label.Foreground = showing ? brush : new SolidColorBrush(Color.Parse("#5A6472"));
            chip.Background = new SolidColorBrush(Color.Parse(showing ? "#1A2030" : "#0E1117"));
            chip.BorderBrush = showing ? brush : new SolidColorBrush(Color.Parse("#2A3240"));

            if (!showing)
            {
                lane.Cursor.IsVisible = false;
            }

            Redraw();
        };

        return chip;
    }

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
            if (!lane.IsShown)
            {
                continue;
            }

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

            // Position event marker lines and text badges
            foreach (var (evt, line, label) in lane.EventMarkers)
            {
                var et = _recording.ToTriggerRelative(evt.TimestampMs);
                var inView = et >= _viewStartMs && et <= _viewEndMs;

                line.IsVisible = inView;
                if (label is not null)
                {
                    label.IsVisible = inView;
                }

                if (inView)
                {
                    var ex = w * ((et - _viewStartMs) / span);
                    line.StartPoint = new Point(ex, 0);
                    line.EndPoint = new Point(ex, h);

                    if (label is not null)
                    {
                        Canvas.SetLeft(label, Math.Min(ex + 3, w - 80));
                        Canvas.SetTop(label, 16);
                    }
                }
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

        var first = _lanes.FirstOrDefault(l => l.IsShown);

        if (first is null)
        {
            HideCursor();
            return;
        }

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
            if (!lane.IsShown)
            {
                lane.Cursor.IsVisible = false;
                continue;
            }

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
