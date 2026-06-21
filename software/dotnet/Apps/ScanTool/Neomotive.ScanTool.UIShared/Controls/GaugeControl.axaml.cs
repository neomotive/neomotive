using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Neomotive.ScanTool.UI.Controls;

public partial class GaugeControl : UserControl
{
    private const double CX = 75, CY = 75, R = 62;
    private const double StartAngle = 150.0;
    private const double TotalSweep = 240.0;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<GaugeControl, double>(nameof(Value), double.MinValue);
    public static readonly StyledProperty<double> MinProperty =
        AvaloniaProperty.Register<GaugeControl, double>(nameof(Min), 0.0);
    public static readonly StyledProperty<double> MaxProperty =
        AvaloniaProperty.Register<GaugeControl, double>(nameof(Max), 100.0);
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<GaugeControl, string>(nameof(Label), "");
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<GaugeControl, string>(nameof(Unit), "");

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Min   { get => GetValue(MinProperty);   set => SetValue(MinProperty, value); }
    public double Max   { get => GetValue(MaxProperty);   set => SetValue(MaxProperty, value); }
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Unit  { get => GetValue(UnitProperty);  set => SetValue(UnitProperty, value); }

    private readonly Path _bgArc;
    private readonly Path _valArc;
    private readonly TextBlock _valueText;
    private readonly TextBlock _unitText;
    private readonly TextBlock _nameText;

    static GaugeControl()
    {
        ValueProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateGauge());
        MinProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateGauge());
        MaxProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateGauge());
        LabelProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateLabels());
        UnitProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateLabels());
    }

    public GaugeControl()
    {
        Width = 170;
        Height = 160;

        _bgArc  = new Path { Stroke = new SolidColorBrush(Color.Parse("#2A2F3A")), StrokeThickness = 8, Fill = Brushes.Transparent, StrokeLineCap = PenLineCap.Round };
        _valArc = new Path { Stroke = new SolidColorBrush(Color.Parse("#4CAF50")), StrokeThickness = 8, Fill = Brushes.Transparent, StrokeLineCap = PenLineCap.Round };

        var canvas = new Canvas
        {
            Width = 150, Height = 150,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0),
            ClipToBounds = true
        };
        canvas.Children.Add(_bgArc);
        canvas.Children.Add(_valArc);

        var mono   = new FontFamily("Consolas, Cascadia Code, Monospace");
        var subtle = new SolidColorBrush(Color.Parse("#4D5566"));

        _valueText = new TextBlock { FontFamily = mono, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#D8DEE9")), HorizontalAlignment = HorizontalAlignment.Center };
        _unitText  = new TextBlock { FontFamily = mono, FontSize = 10, Foreground = subtle, HorizontalAlignment = HorizontalAlignment.Center };
        _nameText  = new TextBlock { FontFamily = mono, FontSize = 10, Foreground = subtle, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 130, TextAlignment = TextAlignment.Center };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 0,
            Margin = new Thickness(0, -8, 0, 0)
        };
        stack.Children.Add(_valueText);
        stack.Children.Add(_unitText);
        stack.Children.Add(_nameText);

        var grid = new Grid();
        grid.Children.Add(canvas);
        grid.Children.Add(stack);

        Content = grid;

        UpdateGauge();
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        _nameText.Text = Label;
        _unitText.Text = Unit;
    }

    private void UpdateGauge()
    {
        _bgArc.Data = BuildArc(CX, CY, R, StartAngle, TotalSweep);

        double value = Value;
        if (value == double.MinValue)
        {
            _valArc.Data = null;
            _valueText.Text = "—";
            _valArc.Stroke = new SolidColorBrush(Color.Parse("#4CAF50"));
        }
        else
        {
            double pct = Max > Min ? Math.Clamp((value - Min) / (Max - Min), 0.0, 1.0) : 0;
            double sweep = pct * TotalSweep;
            _valArc.Data = sweep > 0.5 ? BuildArc(CX, CY, R, StartAngle, sweep) : null;
            _valArc.Stroke = pct switch
            {
                >= 0.90 => new SolidColorBrush(Color.Parse("#F44336")),
                >= 0.70 => new SolidColorBrush(Color.Parse("#FFC107")),
                _       => new SolidColorBrush(Color.Parse("#4CAF50"))
            };
            _valueText.Text = $"{value:F1}";
        }
    }

    private static PathGeometry BuildArc(double cx, double cy, double r, double startDeg, double sweepDeg)
    {
        if (sweepDeg <= 0) return new PathGeometry();
        double startRad = startDeg * Math.PI / 180.0;
        double endRad   = (startDeg + sweepDeg) * Math.PI / 180.0;
        var start = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
        var end   = new Point(cx + r * Math.Cos(endRad),   cy + r * Math.Sin(endRad));
        var seg = new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDeg > 180
        };
        var fig = new PathFigure { StartPoint = start, IsClosed = false, Segments = new PathSegments { seg } };
        return new PathGeometry { Figures = new PathFigures { fig } };
    }
}
