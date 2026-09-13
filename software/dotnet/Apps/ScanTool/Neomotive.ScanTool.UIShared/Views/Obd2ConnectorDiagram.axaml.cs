using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;

namespace Neomotive.ScanTool.UI.Views;

/// <summary>What a pin is for, which decides how it is drawn and what the legend says about it.</summary>
public enum Obd2PinRole
{
    /// <summary>Carries CAN — the only thing this tool can read.</summary>
    Can,

    /// <summary>Power or ground. Not a protocol, but the tool will not run without them.</summary>
    Power,

    /// <summary>A pre-CAN protocol this tool has no hardware for.</summary>
    OtherProtocol,

    /// <summary>Manufacturer discretionary, or unused.</summary>
    Unused
}

/// <summary>One pin of the SAE J1962 connector.</summary>
public sealed class Obd2Pin
{
    public Obd2Pin(int number, string label, Obd2PinRole role)
    {
        Number = number;
        Label = label;
        Role = role;
    }

    public int Number { get; }

    /// <summary>
    /// What the pin carries, in words under the pin. The panel is touch-only with no hover, so a
    /// pin that is merely coloured differently says nothing — the label has to carry the meaning.
    /// </summary>
    public string Label { get; }

    public Obd2PinRole Role { get; }

    public string NumberText => Number.ToString();

    public bool IsCan => Role == Obd2PinRole.Can;

    public IBrush Fill => Role switch
    {
        Obd2PinRole.Can => new SolidColorBrush(Color.Parse("#16351F")),
        Obd2PinRole.Power => new SolidColorBrush(Color.Parse("#2A2F3A")),
        Obd2PinRole.OtherProtocol => new SolidColorBrush(Color.Parse("#3A2E18")),
        _ => new SolidColorBrush(Color.Parse("#1A1F28"))
    };

    public IBrush Stroke => Role switch
    {
        Obd2PinRole.Can => new SolidColorBrush(Color.Parse("#22C55E")),
        Obd2PinRole.Power => new SolidColorBrush(Color.Parse("#6B7686")),
        Obd2PinRole.OtherProtocol => new SolidColorBrush(Color.Parse("#F5A524")),
        _ => new SolidColorBrush(Color.Parse("#2E3440"))
    };

    public double StrokeWidth => Role == Obd2PinRole.Can ? 2 : 1;

    public IBrush Foreground => Role switch
    {
        Obd2PinRole.Can => new SolidColorBrush(Color.Parse("#22C55E")),
        Obd2PinRole.Power => new SolidColorBrush(Color.Parse("#C8D0DA")),
        Obd2PinRole.OtherProtocol => new SolidColorBrush(Color.Parse("#F5A524")),
        _ => new SolidColorBrush(Color.Parse("#5A6472"))
    };

    public FontWeight Weight => Role == Obd2PinRole.Can ? FontWeight.Bold : FontWeight.Normal;
}

/// <summary>
/// The OBD-II (SAE J1962) diagnostic socket, drawn rather than photographed.
/// </summary>
/// <remarks>
/// Vector, not a bitmap: it scales cleanly to the 800x480 panel and to a desktop window, needs no
/// asset file in the update package, and stays legible at the viewing angle the panel actually gets
/// used at.
/// <para>
/// The pin assignments below are the J1962 standard. Pins 6 and 14 are the entire reason this
/// control exists — if they are empty on the vehicle in front of you, no amount of configuration
/// will make this tool talk to it.
/// </para>
/// </remarks>
public partial class Obd2ConnectorDiagram : UserControl
{
    public Obd2ConnectorDiagram()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>Pins 1-8, the row nearest the top of the socket as you face it.</summary>
    public IReadOnlyList<Obd2Pin> TopRow { get; } =
    [
        new(1, "mfr", Obd2PinRole.Unused),
        new(2, "J1850", Obd2PinRole.OtherProtocol),
        new(3, "mfr", Obd2PinRole.Unused),
        new(4, "chassis\nground", Obd2PinRole.Power),
        new(5, "signal\nground", Obd2PinRole.Power),
        new(6, "CAN\nHIGH", Obd2PinRole.Can),
        new(7, "K-line", Obd2PinRole.OtherProtocol),
        new(8, "mfr", Obd2PinRole.Unused)
    ];

    /// <summary>Pins 9-16, the row nearest the bottom.</summary>
    public IReadOnlyList<Obd2Pin> BottomRow { get; } =
    [
        new(9, "mfr", Obd2PinRole.Unused),
        new(10, "J1850", Obd2PinRole.OtherProtocol),
        new(11, "mfr", Obd2PinRole.Unused),
        new(12, "mfr", Obd2PinRole.Unused),
        new(13, "mfr", Obd2PinRole.Unused),
        new(14, "CAN\nLOW", Obd2PinRole.Can),
        new(15, "L-line", Obd2PinRole.OtherProtocol),
        new(16, "battery\n+12 V", Obd2PinRole.Power)
    ];
}
