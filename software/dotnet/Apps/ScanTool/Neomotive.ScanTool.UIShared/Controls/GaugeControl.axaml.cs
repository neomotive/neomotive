using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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

    static GaugeControl()
    {
        ValueProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateGauge());
        MinProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateGauge());
        MaxProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateGauge());
        LabelProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateLabels());
        UnitProperty.Changed.AddClassHandler<GaugeControl>((g, _) => g.UpdateLabels());
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdateGauge();
        UpdateLabels();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateGauge();
        UpdateLabels();
    }

    private Path? BgArc    => this.FindControl<Path>("PART_BgArc");
    private Path? ValArc   => this.FindControl<Path>("PART_ValArc");
    private TextBlock? ValueText => this.FindControl<TextBlock>("PART_Value");
    private TextBlock? UnitText  => this.FindControl<TextBlock>("PART_Unit");
    private TextBlock? NameText  => this.FindControl<TextBlock>("PART_Name");

    private void UpdateLabels()
    {
        if (NameText != null) NameText.Text = Label;
        if (UnitText  != null) UnitText.Text  = Unit;
    }

    private void UpdateGauge()
    {
        var bgArc = BgArc;
        var valArc = ValArc;
        if (bgArc == null || valArc == null) return;

        bgArc.Data = BuildArc(CX, CY, R, StartAngle, TotalSweep);

        double value = Value;
        bool noData = value == double.MinValue;

        if (noData)
        {
            valArc.Data = null;
            if (ValueText != null) ValueText.Text = "—";
            valArc.Stroke = new SolidColorBrush(Color.Parse("#4CAF50"));
        }
        else
        {
            double pct = Max > Min ? Math.Clamp((value - Min) / (Max - Min), 0.0, 1.0) : 0;
            double sweep = pct * TotalSweep;
            valArc.Data = sweep > 0.5 ? BuildArc(CX, CY, R, StartAngle, sweep) : null;
            valArc.Stroke = pct switch
            {
                >= 0.90 => new SolidColorBrush(Color.Parse("#F44336")),
                >= 0.70 => new SolidColorBrush(Color.Parse("#FFC107")),
                _       => new SolidColorBrush(Color.Parse("#4CAF50"))
            };
            if (ValueText != null) ValueText.Text = $"{value:F1}";
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
        var fig = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            Segments = new PathSegments { seg }
        };
        return new PathGeometry { Figures = new PathFigures { fig } };
    }
}
