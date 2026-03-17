using ICs.IOExpanders.PCanBasic;
using Meadow.Hardware;
using Neomotive.ModuleSimulator.Desktop;
using Spectre.Console;

// Header
AnsiConsole.MarkupLine("[bold yellow]Neomotive Module Simulator[/]  [grey]v0.1[/]");
AnsiConsole.MarkupLine("[grey]─────────────────────────────[/]");

// Connect to CAN bus (fall back to offline mode if hardware unavailable)
ICanBus rawBus;
try
{
    var expander = new PCanUsb();
    rawBus = expander.CreateCanBus(CanBitrate.Can_500kbps);
    AnsiConsole.MarkupLine("[green]✓[/] CAN bus connected [grey](PCanUsb, 500 kbps)[/]");
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[yellow]⚠[/]  CAN hardware unavailable: [grey]{Markup.Escape(ex.Message)}[/]");
    AnsiConsole.MarkupLine("[grey]   Running in offline mode — no frames will be sent/received.[/]");
    rawBus = new NullCanBus();
}

AnsiConsole.WriteLine();

var log      = new CanPacketLog();
var bus      = new LoggingCanBus(rawBus, log);
var pcmState = new SimulatorState();
var tcuState = new SimulatorTcuState();
var pcm      = new SimulatorPcm(bus, pcmState);
var tcu      = new SimulatorTcu(bus, tcuState);

new SimulatorUi().Run(pcm, pcmState, tcu, tcuState, log);
