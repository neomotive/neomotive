using Meadow.Foundation.Telematics.J1979;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;

namespace Neomotive.ModuleSimulator.Desktop;

public class SimulatorUi
{
    private enum SettingsView { Data, Monitors, Dtcs }
    private enum SelectedModule { Pcm, Tcu }

    private record DtcHolder(
        Dictionary<string, byte[]> StoredDtcs,
        Dictionary<string, byte[]> PendingDtcs,
        Dictionary<string, byte[]> PermanentDtcs,
        Action Sync);

    private static IRenderable BuildLeftPanel(SelectedModule selected, SimulatorState pcmState, SimulatorTcuState tcuState)
    {
        var tree = new Tree("[bold yellow]Vehicle[/]");

        var pcmLabel = selected == SelectedModule.Pcm ? "[bold green on darkgreen] PCM [/]" : "[green]PCM[/]";
        var pcm = tree.AddNode($"{pcmLabel} [grey]0x7E8[/]");
        pcm.AddNode("[grey]SAE J1979[/]");
        pcm.AddNode($"[grey]DTCs: {pcmState.StoredDtcs.Count}|{pcmState.PendingDtcs.Count}|{pcmState.PermanentDtcs.Count}[/]");

        var tcuLabel = selected == SelectedModule.Tcu ? "[bold cyan on darkblue] TCU [/]" : "[cyan]TCU[/]";
        var tcu = tree.AddNode($"{tcuLabel} [grey]0x7E9[/]");
        tcu.AddNode("[grey]SAE J1979[/]");
        tcu.AddNode($"[grey]DTCs: {tcuState.StoredDtcs.Count}|{tcuState.PendingDtcs.Count}|{tcuState.PermanentDtcs.Count}[/]");

        return new Panel(tree)
            .Header("[bold] Modules [/]")
            .Expand();
    }

    private static IRenderable BuildSettingsPanel(
        SimulatorState pcmState, SimulatorTcuState tcuState,
        SimulatorPcm pcm, SimulatorTcu tcu,
        SettingsView view, SelectedModule selectedModule)
    {
        if (selectedModule == SelectedModule.Tcu)
            return view == SettingsView.Dtcs ? BuildTcuDtcsView(tcuState) : BuildTcuDataView(tcuState, tcu);

        return view switch
        {
            SettingsView.Monitors => BuildMonitorsView(pcmState),
            SettingsView.Dtcs => BuildDtcsView(pcmState),
            _ => BuildDataView(pcmState, pcm),
        };
    }

    private static IRenderable BuildDataView(SimulatorState state, SimulatorPcm pcm)
    {
        var table = new Table()
            .NoBorder()
            .Expand()
            .AddColumn(new TableColumn("[grey]Field[/]").Width(16))
            .AddColumn("[grey]Value[/]");

        table.AddRow("[grey]ECU Name[/]", pcm.EcuName is { } n ? $"[cyan]{Markup.Escape(n)}[/]" : "[grey]—[/]");
        table.AddRow("[grey]Cal ID[/]", pcm.CalibrationId is { } c ? $"[cyan]{Markup.Escape(c)}[/]" : "[grey]—[/]");
        table.AddRow("[grey]CVN[/]", pcm.CalibrationVerificationNumber is { } v ? $"[cyan]0x{v:X8}[/]" : "[grey]—[/]");
        table.AddRow("[grey]VIN[/]", $"[cyan]{Markup.Escape(state.Vin)}[/]");
        table.AddRow("[grey]Coolant Temp[/]", $"[cyan]{state.CoolantTempCelsius:F1}[/][grey] °C[/]");
        table.AddRow("[grey]Engine RPM[/]", $"[cyan]{state.Rpm:F0}[/][grey] rpm[/]");
        table.AddRow("[grey]Speed[/]", $"[cyan]{state.SpeedKph:F1}[/][grey] km/h[/]");
        table.AddRow("[grey]Throttle[/]", $"[cyan]{state.ThrottlePercent:F1}[/][grey] %[/]");

        return new Panel(table)
            .Header("[bold] PCM — Data [/]")
            .Expand();
    }

    private static IRenderable BuildTcuDataView(SimulatorTcuState state, SimulatorTcu tcu)
    {
        var table = new Table()
            .NoBorder()
            .Expand()
            .AddColumn(new TableColumn("[grey]Field[/]").Width(16))
            .AddColumn("[grey]Value[/]");

        table.AddRow("[grey]ECU Name[/]", tcu.EcuName is { } n ? $"[cyan]{Markup.Escape(n)}[/]" : "[grey]—[/]");
        table.AddRow("[grey]Cal ID[/]", tcu.CalibrationId is { } c ? $"[cyan]{Markup.Escape(c)}[/]" : "[grey]—[/]");
        table.AddRow("[grey]CVN[/]", tcu.CalibrationVerificationNumber is { } v ? $"[cyan]0x{v:X8}[/]" : "[grey]—[/]");
        table.AddRow("[grey]Trans Temp[/]", $"[cyan]{state.TransTempCelsius:F1}[/][grey] °C[/]");
        table.AddRow("[grey]Gear[/]", $"[cyan]{Markup.Escape(state.GearPosition)}[/]");

        return new Panel(table)
            .Header("[bold] TCU — Data [/]")
            .Expand();
    }

    private static IRenderable BuildMonitorsView(SimulatorState state)
    {
        var r = state.Readiness;
        bool milOn = state.StoredDtcs.Count > 0;
        var sb = new StringBuilder();

        sb.AppendLine($"  [grey]MIL[/]                    {(milOn ? "[bold red]ON[/]" : "[green]OFF[/]")}");
        sb.AppendLine();

        static string Line(string name, bool supported, bool complete) =>
            supported
                ? $"  [grey]{name,-22}[/] {(complete ? "[green]✓ Ready[/]" : "[yellow]✗ Incomplete[/]")}"
                : $"  [grey]{name,-22} — not supported[/]";

        sb.AppendLine(Line("Misfire", r.MisfireSupported, r.MisfireComplete));
        sb.AppendLine(Line("Fuel System", r.FuelSystemSupported, r.FuelSystemComplete));
        sb.AppendLine(Line("Comprehensive", r.ComprehensiveComponentSupported, r.ComprehensiveComponentComplete));
        sb.AppendLine(Line("Catalyst", r.CatalystSupported, r.CatalystComplete));
        sb.AppendLine(Line("Heated Catalyst", r.HeatedCatalystSupported, r.HeatedCatalystComplete));
        sb.AppendLine(Line("Evap System", r.EvapSystemSupported, r.EvapSystemComplete));
        sb.AppendLine(Line("Secondary Air", r.SecondaryAirSupported, r.SecondaryAirComplete));
        sb.AppendLine(Line("O2 Sensor", r.OxygenSensorSupported, r.OxygenSensorComplete));
        sb.AppendLine(Line("O2 Sensor Heater", r.OxygenSensorHeaterSupported, r.OxygenSensorHeaterComplete));
        sb.AppendLine(Line("EGR System", r.EgrSystemSupported, r.EgrSystemComplete));

        return new Panel(new Markup(sb.ToString()))
            .Header("[bold] PCM — Readiness Monitors [/]")
            .Expand();
    }

    private static IRenderable BuildDtcsView(SimulatorState state)
    {
        var sb = new StringBuilder();
        var total = state.StoredDtcs.Count + state.PendingDtcs.Count + state.PermanentDtcs.Count;

        if (total == 0)
        {
            sb.AppendLine("  [grey]No active DTCs[/]");
        }
        else
        {
            sb.AppendLine("  [bold grey]  Code    Type        Description[/]");
            foreach (var (label, dict) in new (string, Dictionary<string, byte[]>)[] {
                ("Stored",    state.StoredDtcs),
                ("Pending",   state.PendingDtcs),
                ("Permanent", state.PermanentDtcs) })
            {
                foreach (var (code, raw) in dict.OrderBy(kvp => kvp.Key))
                {
                    var desc = SimulatorState.KnownDtcs.FirstOrDefault(d => d.Code == code)?.Description
                        ?? new Dtc(raw).ToReadableErrorCode();
                    sb.AppendLine($"  [red]■[/] [bold red]{code}[/]  [grey]{label,-9}[/]  [white]{desc}[/]");
                }
            }
        }

        return new Panel(new Markup(sb.ToString()))
            .Header("[bold] PCM — Fault Codes [/]")
            .Expand();
    }

    private static IRenderable BuildTcuDtcsView(SimulatorTcuState state)
    {
        var sb = new StringBuilder();
        var total = state.StoredDtcs.Count + state.PendingDtcs.Count + state.PermanentDtcs.Count;

        if (total == 0)
        {
            sb.AppendLine("  [grey]No active DTCs[/]");
        }
        else
        {
            sb.AppendLine("  [bold grey]  Code    Type        Description[/]");
            foreach (var (label, dict) in new (string, Dictionary<string, byte[]>)[] {
                ("Stored",    state.StoredDtcs),
                ("Pending",   state.PendingDtcs),
                ("Permanent", state.PermanentDtcs) })
            {
                foreach (var (code, raw) in dict.OrderBy(kvp => kvp.Key))
                {
                    var desc = SimulatorTcuState.KnownDtcs.FirstOrDefault(d => d.Code == code)?.Description
                        ?? new Dtc(raw).ToReadableErrorCode();
                    sb.AppendLine($"  [red]■[/] [bold red]{code}[/]  [grey]{label,-9}[/]  [white]{desc}[/]");
                }
            }
        }

        return new Panel(new Markup(sb.ToString()))
            .Header("[bold] TCU — Fault Codes [/]")
            .Expand();
    }

    private static IRenderable BuildCanLogPanel(CanPacketLog log)
    {
        var packets = log.GetRecent(8);

        if (packets.Count == 0)
        {
            return new Panel(new Markup("[grey italic]Waiting for CAN frames...[/]"))
                .Header("[bold] CAN Packets [/]")
                .Expand();
        }

        var table = new Table()
            .NoBorder()
            .Expand()
            .AddColumn(new TableColumn("[grey]Time[/]").Width(12))
            .AddColumn(new TableColumn("[grey]ID[/]").Width(5))
            .AddColumn(new TableColumn("[grey]Dir[/]").Width(4))
            .AddColumn(new TableColumn("[grey]Data[/]").Width(26))
            .AddColumn("[grey]Description[/]");

        foreach (var pkt in packets)
        {
            var dir = pkt.IsOutgoing ? "[blue]TX[/]" : "[yellow]RX[/]";
            var data = string.Join(" ", pkt.Data.Take(8).Select(b => b.ToString("X2")));
            var desc = DescribePacket(pkt);
            table.AddRow(
                $"[grey]{pkt.Timestamp:HH:mm:ss.fff}[/]",
                $"[white]{pkt.Id:X3}[/]",
                dir,
                $"[grey]{data}[/]",
                $"[white]{Markup.Escape(desc)}[/]");
        }

        return new Panel(table)
            .Header("[bold] CAN Packets [/]")
            .Expand();
    }

    private static string DescribePacket(CanPacketEntry pkt)
    {
        var d = pkt.Data;
        if (d.Length < 2) return "";

        if (!pkt.IsOutgoing)
        {
            byte len = d[0];
            byte svc = d.Length > 1 ? d[1] : (byte)0;

            if (len == 1)
                return svc switch
                {
                    0x03 => "Read Stored DTCs",
                    0x04 => "Clear DTCs",
                    0x07 => "Read Pending DTCs",
                    0x0A => "Read Permanent DTCs",
                    _ => $"Service {svc:X2}",
                };

            byte pid = d.Length > 2 ? d[2] : (byte)0;
            return svc switch
            {
                0x01 => pid switch
                {
                    0x00 => "Query Supported PIDs",
                    0x01 => "Query Monitor Status",
                    0x05 => "Query Coolant Temp",
                    0x0C => "Query Engine RPM",
                    0x0D => "Query Vehicle Speed",
                    0x11 => "Query Throttle",
                    0x20 => "Query Supported PIDs 21-40",
                    0x40 => "Query Supported PIDs 41-60",
                    0x5C => "Query Trans Fluid Temp",
                    _ => $"Query PID {pid:X2}",
                },
                0x09 => pid switch
                {
                    0x00 => "Query Veh Info PIDs",
                    0x02 => "Query VIN",
                    0x04 => "Query Cal ID",
                    0x06 => "Query CVN",
                    0x0A => "Query ECU Name",
                    _ => $"Query Veh Info {pid:X2}",
                },
                _ => $"Service {svc:X2} PID {pid:X2}",
            };
        }
        else
        {
            byte isoType = (byte)(d[0] & 0xF0);

            byte svcByte;
            int offset;
            if (isoType == 0x10) { svcByte = d.Length > 2 ? d[2] : (byte)0; offset = 3; }
            else if (isoType == 0x20) return "ISO-TP Continuation";
            else if (isoType == 0x30) return "ISO-TP Flow Control";
            else { svcByte = d.Length > 1 ? d[1] : (byte)0; offset = 2; }

            byte service = (byte)(svcByte - 0x40);
            return service switch
            {
                0x01 => d.Length > offset
                    ? (d[offset]) switch
                    {
                        0x00 => "Supported PIDs",
                        0x01 => "Monitor Status",
                        0x05 => "Coolant Temp",
                        0x0C => "Engine RPM",
                        0x0D => "Vehicle Speed",
                        0x11 => "Throttle Position",
                        0x20 => "Supported PIDs 21-40",
                        0x5C => "Trans Fluid Temp",
                        var p => $"PID {p:X2} Data",
                    }
                    : "Current Data",
                0x03 => $"Stored DTCs ({(d.Length > offset ? d[offset] : 0)})",
                0x04 => "DTCs Cleared",
                0x07 => $"Pending DTCs ({(d.Length > offset ? d[offset] : 0)})",
                0x09 => d.Length > offset ? d[offset] switch
                {
                    0x02 => "VIN",
                    0x04 => "Cal ID",
                    0x06 => "CVN",
                    0x0A => "ECU Name",
                    _ => "Vehicle Info",
                } : "Vehicle Info",
                0x0A => $"Permanent DTCs ({(d.Length > offset ? d[offset] : 0)})",
                _ => $"Response {svcByte:X2}",
            };
        }
    }

    private static IRenderable BuildPromptPanel(string input, string? feedback)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[grey]select pcm|tcu   show data|monitors|dtcs   set vin|rpm|temp|speed|throttle <val>   set gear P|R|N|D|1-8   set transtemp <val>   set dtc [[cat]] <code>   set monitor <name> ready|incomplete   clear dtc <code>|all   exit[/]");
        sb.Append($"[bold white]>[/] {Markup.Escape(input)}[white]▌[/]");
        if (feedback != null)
            sb.Append($"   [grey]{Markup.Escape(feedback)}[/]");

        return new Panel(new Markup(sb.ToString())).NoBorder();
    }

    private static void UpdateLayout(
        Layout layout,
        SimulatorState pcmState, SimulatorTcuState tcuState,
        SimulatorPcm pcm, SimulatorTcu tcu,
        CanPacketLog log, string input, string? feedback,
        SettingsView view, SelectedModule selectedModule)
    {
        layout["Left"].Update(BuildLeftPanel(selectedModule, pcmState, tcuState));
        layout["Settings"].Update(BuildSettingsPanel(pcmState, tcuState, pcm, tcu, view, selectedModule));
        layout["CanLog"].Update(BuildCanLogPanel(log));
        layout["Prompt"].Update(BuildPromptPanel(input, feedback));
    }

    private static string? ProcessCommand(
        string cmd,
        SimulatorPcm pcm, SimulatorState pcmState,
        SimulatorTcu tcu, SimulatorTcuState tcuState,
        ref SettingsView view, ref SelectedModule selectedModule)
    {
        var parts = cmd.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var dtcHolder = selectedModule == SelectedModule.Tcu
            ? new DtcHolder(tcuState.StoredDtcs, tcuState.PendingDtcs, tcuState.PermanentDtcs, () => tcu.SyncDtcsFromState())
            : new DtcHolder(pcmState.StoredDtcs, pcmState.PendingDtcs, pcmState.PermanentDtcs, () => pcm.SyncDtcsFromState());

        switch (parts[0].ToLowerInvariant())
        {
            case "select" when parts.Length >= 2:
                switch (parts[1].ToLowerInvariant())
                {
                    case "pcm": selectedModule = SelectedModule.Pcm; return "Selected PCM";
                    case "tcu": selectedModule = SelectedModule.Tcu; return "Selected TCU";
                    default: return $"Unknown module '{parts[1]}' — use: pcm  or  tcu";
                }

            case "set" when parts.Length >= 4
                         && parts[1].Equals("monitor", StringComparison.OrdinalIgnoreCase):
                view = SettingsView.Monitors;
                return HandleSetMonitor(parts[2], parts[3], pcmState);

            case "set" when parts.Length >= 3
                         && parts[1].Equals("dtc", StringComparison.OrdinalIgnoreCase):
                {
                    view = SettingsView.Dtcs;
                    var (cat, codeArg) = IsCategory(parts[2]) && parts.Length >= 4
                        ? (parts[2].ToLowerInvariant(), parts[3])
                        : ("stored", parts[2]);
                    return HandleDtcSet(cat, codeArg, dtcHolder);
                }

            case "set" when parts.Length >= 3
                         && parts[1].Equals("gear", StringComparison.OrdinalIgnoreCase):
                {
                    var validGears = new[] { "P", "R", "N", "D", "1", "2", "3", "4", "5", "6", "7", "8" };
                    var gear = parts[2].ToUpperInvariant();
                    if (!validGears.Contains(gear))
                        return $"Invalid gear '{parts[2]}' — use: P R N D 1-8";
                    tcuState.GearPosition = gear;
                    selectedModule = SelectedModule.Tcu;
                    view = SettingsView.Data;
                    return $"Gear = {gear}";
                }

            case "set" when parts.Length >= 3
                         && parts[1].Equals("transtemp", StringComparison.OrdinalIgnoreCase):
                {
                    if (!double.TryParse(parts[2], out var transTemp) || transTemp < -40 || transTemp > 215)
                        return "Trans temp must be -40 to 215 °C";
                    tcuState.TransTempCelsius = transTemp;
                    selectedModule = SelectedModule.Tcu;
                    view = SettingsView.Data;
                    return $"Trans Temp = {transTemp:F1} °C";
                }

            case "set" when parts.Length >= 3:
                return HandleSet(parts[1], parts[2], pcmState);
            case "set":
                return "Usage: set <field> <value>  |  set dtc [[cat]] <code>  |  set monitor <name> ready|incomplete";

            case "clear" when parts.Length >= 3
                           && parts[1].Equals("dtc", StringComparison.OrdinalIgnoreCase)
                           && parts[2].Equals("all", StringComparison.OrdinalIgnoreCase):
                view = SettingsView.Dtcs;
                return HandleDtcClearAll(dtcHolder);
            case "clear" when parts.Length >= 3
                           && parts[1].Equals("dtc", StringComparison.OrdinalIgnoreCase):
                view = SettingsView.Dtcs;
                return HandleDtcClear(parts[2], dtcHolder);
            case "clear":
                return "Usage: clear dtc <code>  |  clear dtc all";

            case "help":
                return "select pcm|tcu  |  show data|monitors|dtcs  |  set vin|rpm|temp|speed|throttle <val>  |  set gear P|R|N|D|1-8  |  set transtemp <val>  |  set dtc [[cat]] <code>  |  clear dtc <code>|all  |  exit";

            default:
                return $"Unknown command '{parts[0]}' — type 'help'";
        }
    }

    private static string HandleSet(string field, string value, SimulatorState state)
    {
        switch (field.ToLowerInvariant())
        {
            case "vin":
                if (value.Length < 1 || value.Length > 17)
                    return "VIN must be 1-17 characters";
                state.Vin = value.ToUpperInvariant();
                return $"VIN set to {state.Vin}";

            case "rpm":
                if (!float.TryParse(value, out var rpm) || rpm < 0 || rpm > 20000)
                    return "RPM must be 0-20000";
                state.Rpm = rpm;
                return $"RPM = {rpm:F0}";

            case "temp":
                if (!double.TryParse(value, out var temp) || temp < -40 || temp > 215)
                    return "Temp must be -40 to 215 °C";
                state.CoolantTempCelsius = temp;
                return $"Coolant = {temp:F1} °C";

            case "speed":
                if (!double.TryParse(value, out var speed) || speed < 0 || speed > 300)
                    return "Speed must be 0-300 km/h";
                state.SpeedKph = speed;
                return $"Speed = {speed:F1} km/h";

            case "throttle":
                if (!float.TryParse(value, out var throttle) || throttle < 0 || throttle > 100)
                    return "Throttle must be 0-100 %";
                state.ThrottlePercent = throttle;
                return $"Throttle = {throttle:F1} %";

            default:
                return $"Unknown field '{field}' — use: vin rpm temp speed throttle  (or: gear transtemp for TCU)";
        }
    }

    private static bool TryParseDtcCode(string input, out string normalized, out byte[] raw)
    {
        normalized = input.ToUpperInvariant().Trim();
        raw = Array.Empty<byte>();

        if (normalized.Length != 5) return false;

        byte category = normalized[0] switch { 'P' => 0, 'C' => 1, 'B' => 2, 'U' => 3, _ => 255 };
        if (category == 255) return false;

        if (!char.IsDigit(normalized[1]) || !char.IsDigit(normalized[2]) ||
            !char.IsDigit(normalized[3]) || !char.IsDigit(normalized[4]))
            return false;

        byte subtype = (byte)(normalized[1] - '0');
        byte d1 = (byte)(normalized[2] - '0');
        byte d2 = (byte)(normalized[3] - '0');
        byte d3 = (byte)(normalized[4] - '0');

        raw = [(byte)((category << 6) | (subtype << 4) | d1), (byte)((d2 << 4) | d3)];
        return true;
    }

    private static string HandleSetMonitor(string monitorName, string stateStr, SimulatorState state)
    {
        bool? complete = stateStr.ToLowerInvariant() switch
        {
            "ready" or "complete" or "on" => true,
            "incomplete" or "off" or "not" => false,
            _ => null
        };
        if (complete is null)
            return "State must be: ready  or  incomplete";

        var r = state.Readiness;
        switch (monitorName.ToLowerInvariant())
        {
            case "misfire": r.MisfireComplete = complete.Value; break;
            case "fuel": r.FuelSystemComplete = complete.Value; break;
            case "comprehensive": r.ComprehensiveComponentComplete = complete.Value; break;
            case "catalyst": r.CatalystComplete = complete.Value; break;
            case "heatedcatalyst" or "hcat": r.HeatedCatalystComplete = complete.Value; break;
            case "evap": r.EvapSystemComplete = complete.Value; break;
            case "secondaryair" or "air": r.SecondaryAirComplete = complete.Value; break;
            case "o2" or "o2sensor": r.OxygenSensorComplete = complete.Value; break;
            case "o2heater": r.OxygenSensorHeaterComplete = complete.Value; break;
            case "egr": r.EgrSystemComplete = complete.Value; break;
            case "all":
                r.MisfireComplete = r.FuelSystemComplete = r.ComprehensiveComponentComplete =
                r.CatalystComplete = r.HeatedCatalystComplete = r.EvapSystemComplete =
                r.SecondaryAirComplete = r.AcRefrigerantComplete =
                r.OxygenSensorComplete = r.OxygenSensorHeaterComplete = r.EgrSystemComplete
                    = complete.Value;
                break;
            default:
                return $"Unknown monitor '{monitorName}'. Use: misfire fuel comprehensive catalyst evap o2 o2heater egr all";
        }

        return $"Monitor '{monitorName}' set to {(complete.Value ? "ready" : "incomplete")}";
    }

    private static bool IsCategory(string s) =>
        s.Equals("stored", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("permanent", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, byte[]> GetDtcDict(string category, DtcHolder holder) =>
        category.ToLowerInvariant() switch
        {
            "pending" => holder.PendingDtcs,
            "permanent" => holder.PermanentDtcs,
            _ => holder.StoredDtcs,
        };

    private static string HandleDtcSet(string category, string codeStr, DtcHolder holder)
    {
        if (!TryParseDtcCode(codeStr, out var code, out var raw))
            return $"Invalid DTC code '{codeStr}' — expected format P0300, C1234, etc.";

        GetDtcDict(category, holder)[code] = raw;
        holder.Sync();
        return $"Set {code} ({category})";
    }

    private static string HandleDtcClear(string codeStr, DtcHolder holder)
    {
        if (!TryParseDtcCode(codeStr, out var code, out _))
            return $"Invalid DTC code '{codeStr}'";

        var cleared = new List<string>();
        foreach (var (label, dict) in new[] {
            ("stored", holder.StoredDtcs), ("pending", holder.PendingDtcs), ("permanent", holder.PermanentDtcs) })
        {
            if (dict.Remove(code)) cleared.Add(label);
        }

        if (cleared.Count == 0) return $"{code} was not set";
        holder.Sync();
        return $"Cleared {code} ({string.Join(", ", cleared)})";
    }

    private static string HandleDtcClearAll(DtcHolder holder)
    {
        var count = holder.StoredDtcs.Count + holder.PendingDtcs.Count + holder.PermanentDtcs.Count;
        holder.StoredDtcs.Clear();
        holder.PendingDtcs.Clear();
        holder.PermanentDtcs.Clear();
        holder.Sync();
        return count > 0 ? $"Cleared {count} DTC(s)" : "No DTCs were set";
    }

    public void Run(
        SimulatorPcm pcm, SimulatorState pcmState,
        SimulatorTcu tcu, SimulatorTcuState tcuState,
        CanPacketLog log)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Content"),
                new Layout("Prompt").Size(4)
            );

        layout["Content"].SplitColumns(
            new Layout("Left").Size(26),
            new Layout("Right")
        );

        layout["Right"].SplitRows(
            new Layout("Settings"),
            new Layout("CanLog").Size(12)
        );

        var input = new StringBuilder();
        string? feedback = null;
        var exit = false;
        var view = SettingsView.Data;
        var selectedModule = SelectedModule.Pcm;

        AnsiConsole.Live(layout)
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Bottom)
            .Start(ctx =>
            {
                while (!exit)
                {
                    UpdateLayout(layout, pcmState, tcuState, pcm, tcu, log, input.ToString(), feedback, view, selectedModule);
                    ctx.Refresh();

                    var deadline = DateTime.UtcNow.AddMilliseconds(150);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (!Console.KeyAvailable)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        var key = Console.ReadKey(intercept: true);
                        feedback = null;

                        switch (key.Key)
                        {
                            case ConsoleKey.Enter:
                                var cmd = input.ToString().Trim();
                                input.Clear();
                                if (cmd is "exit" or "quit")
                                    exit = true;
                                else if (cmd.Equals("show data", StringComparison.OrdinalIgnoreCase)) { view = SettingsView.Data; feedback = null; }
                                else if (cmd.Equals("show monitors", StringComparison.OrdinalIgnoreCase)) { view = SettingsView.Monitors; feedback = null; }
                                else if (cmd.Equals("show dtcs", StringComparison.OrdinalIgnoreCase)) { view = SettingsView.Dtcs; feedback = null; }
                                else if (cmd.Length > 0)
                                    feedback = ProcessCommand(cmd, pcm, pcmState, tcu, tcuState, ref view, ref selectedModule);
                                break;

                            case ConsoleKey.Backspace:
                                if (input.Length > 0)
                                    input.Length--;
                                break;

                            case ConsoleKey.Escape:
                                input.Clear();
                                break;

                            default:
                                if (key.KeyChar >= ' ')
                                    input.Append(key.KeyChar);
                                break;
                        }

                        break; // re-render immediately after any key
                    }
                }
            });
    }
}
