# ScanTool & ModuleSimulator — Developer Guide

## Overview

Two companion apps that work together over a physical CAN bus (Peak PCAN USB adapter):

- **ScanTool** — Windows desktop OBD2 diagnostic client (reads DTCs, VIN, emissions readiness from a real vehicle or the simulator)
- **ModuleSimulator** — Emulates a vehicle's PCM + TCU modules; runs on Windows desktop or Raspberry Pi headless

Solution file: `ScanTool/neomotive scantool.slnx`

---

## ScanTool

### Project layout

| Project | Purpose |
|---|---|
| `Neomotive.ScanTool.Core` | OBD2 protocol logic — no UI dependency, fully unit-testable |
| `Neomotive.ScanTool.UIShared` | Avalonia views, `MainWindowViewModel`, styles |
| `Neomotive.ScanTool.Desktop` | WinExe entry point (`Program.cs` → `AppBuilder`) |
| `Neomotive.ScanTool.Core.Tests` | xUnit tests for decoding and scanner behavior |

### Key classes

- **`IObd2Scanner`** — async interface: `ConnectAsync`, `ReadVinAsync`, `ReadStoredDtcsAsync`, `ReadPendingDtcsAsync`, `ReadReadinessAsync`, `ClearDtcsAsync`
- **`Obd2Scanner`** — implements `IObd2Scanner`; owns ISO-TP framing, flow control, timeout logic; depends on `ICanBus`
- **`Obd2Protocol`** — pure static decoder: `ParseDtcs`, `ParseVin`, `ParseReadiness`, `DecodeDtcCode` — no side effects, easy to test
- **`NullCanBus`** — no-op `ICanBus` for tests (never returns frames)
- **`MainWindowViewModel`** — MVVM hub; enum-driven view switching (`Connection / Vehicle / Emissions / Dtcs`); calls `RefreshAllAsync` after connect or manual refresh

### OBD2 / ISO-TP protocol facts

- **CAN speed**: 500 kbps
- **Request ID**: `0x7DF` (broadcast); **response ID**: `0x7E8` (PCM), `0x7E9` (TCU)
- **Physical ID** for flow control replies: `0x7E0`
- **Services used**: `0x01` live PIDs, `0x03` stored DTCs, `0x07` pending DTCs, `0x04` clear DTCs, `0x09` PID `0x02` = VIN
- **ISO-TP frames**: Single (low nibble = length), First (`0x10`), Consecutive (`0x2x`), Flow Control (`0x30` sent to `0x7E0`)
- **Timeout**: 3 s per request; returns `null` gracefully on timeout

### UI pattern

Views are `UserControl` with `x:DataType="local:MainWindowViewModel"`. Click events go to code-behind → call async VM methods (`_ = Vm.FooAsync()`). No commands/bindings for click events — all are routed event handlers. View visibility is controlled by `IsConnectionView`, `IsVehicleView`, `IsEmissionsView`, `IsDtcsView` bool properties on the VM.

### Testing

`FakeCanBus` injects `StandardDataFrame` responses by raising `FrameReceived`. Tests cover: DTC decoding, VIN multi-frame reassembly, flow control, timeout, readiness parsing.

Run tests: `dotnet test` from `Neomotive.ScanTool.Core.Tests/`

---

## ModuleSimulator

### Project layout

| Project | Purpose |
|---|---|
| `Neomotive.ModuleSimulator.Core` | `SimulatorState`, `SimulatorPcm`, `SimulatorTcu` |
| `Neomotive.ControlModule` | Base classes `PrimaryControlModule`, `TransmissionControlModule` |
| `Neomotive.ModuleSimulator.Desktop` | Avalonia UI (`ToolboxView` + `ToolboxViewModel`) |
| `Neomotive.ModuleSimulator.RaspberryPi` | Headless entry point for Pi hardware |

### Key classes

- **`SimulatorState`** — in-memory vehicle state: VIN, RPM, speed, coolant temp, throttle, readiness flags, DTC stores per module
- **`SimulatorPcm`** — extends `PcmBase`; serves engine metrics from `SimulatorState`; calls `SyncDtcsFromState()` to atomically reload DTCs; handles `OnDtcsCleared`
- **`SimulatorTcu`** — extends `TransmissionControlModule`; serves trans fluid temp; supports PIDs `MonitorStatus`, `OilTemp`
- **`LoggingCanBus`** — wraps real `ICanBus` and logs all frames (useful for debugging protocol issues)

### Hardcoded test data

- **VIN**: `"AWWWWWWWWWWW0YEAH"`
- **Known DTCs**: P0300, P0171, P0420, P0442, P0507 (with descriptions)
- **CAN addresses**: PCM = `0x7E8`, TCU = `0x7E9`

### Running end-to-end

1. Plug in Peak PCAN USB adapter
2. Run **ModuleSimulator.Desktop** — starts PCM + TCU responding on CAN
3. Run **ScanTool.Desktop** → Connect → reads VIN/DTCs/readiness from the simulator

---

## Shared patterns

- Both use Meadow `ICanBus` abstraction (`Meadow.Hardware.ICanBus`)
- Both depend on `Meadow.Foundation.Telematics.J1979` for OBD2 base types
- Avalonia 12.0.4 with standard MVVM (`INotifyPropertyChanged`, no ReactiveUI)
- `.NET 10.0` target for all projects
- CAN adapter driver: `ICS.CAN.PCanBasic` (Peak PCAN USB)
