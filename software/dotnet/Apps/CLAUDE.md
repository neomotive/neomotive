# ScanTool & ModuleSimulator — Developer Guide

## Overview

Two companion apps that work together over a physical CAN bus (Peak PCAN USB adapter):

- **ScanTool** — OBD2 diagnostic client (reads DTCs, VIN, emissions readiness from a real vehicle or the simulator); runs on Windows desktop or as a Raspberry Pi appliance
- **ModuleSimulator** — Emulates a vehicle's PCM + TCU modules; runs on Windows desktop or Raspberry Pi headless

Solution file: `ScanTool/neomotive scantool.slnx`

---

## ScanTool

### Project layout

| Project | Purpose |
|---|---|
| `Neomotive.ScanTool.Core` | OBD2 protocol logic — no UI dependency, fully unit-testable |
| `Neomotive.ScanTool.UIShared` | Avalonia views, `MainWindowViewModel`, styles |
| `Neomotive.ScanTool.Desktop` | WinExe entry point (`Program.cs` → `AppBuilder`), PCAN USB |
| `Neomotive.ScanTool.RaspberryPi` | Pi entry point — MCP2515 CAN HAT, Avalonia DRM/KMS (no X). Deploys to a Pi Appliance Kit device; see `ScanTool/scripts/pi/README.md` |
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

### Shared libraries (`Apps/Shared/`)

| Project | Purpose |
|---|---|
| `Neomotive.Can.Hardware` | `WaveshareDualCanHat` — dual MCP2515 CAN HAT for the Pi (used by both RaspberryPi heads) |
| `Neomotive.Can.UI` | `CanView` (CAN bus health + packet log tab), `CanLogItem`, `ICanViewModel` |
| `Neomotive.Obd2` | `DtcDescriptions` |
| `Neomotive.UI.Styles` | Common Avalonia styles |
| `Neomotive.Update` | Update service (USB / network sources) |
| `Neomotive.Vin` | VIN decode / generate |

`CanView` binds to `ICanViewModel`, not a concrete VM — both `MainWindowViewModel`s implement
it. Adding a control to the CAN tab means adding the member to `ICanViewModel` and to both VMs.

# context-mode — MANDATORY routing rules

You have context-mode MCP tools available. These rules are NOT optional — they protect your context window from flooding. A single unrouted command can dump 56 KB into context and waste the entire session.

## BLOCKED commands — do NOT attempt these

### curl / wget — BLOCKED
Any Bash command containing `curl` or `wget` is intercepted and replaced with an error message. Do NOT retry.
Instead use:
- `ctx_fetch_and_index(url, source)` to fetch and index web pages
- `ctx_execute(language: "javascript", code: "const r = await fetch(...)")` to run HTTP calls in sandbox

### Inline HTTP — BLOCKED
Any Bash command containing `fetch('http`, `requests.get(`, `requests.post(`, `http.get(`, or `http.request(` is intercepted and replaced with an error message. Do NOT retry with Bash.
Instead use:
- `ctx_execute(language, code)` to run HTTP calls in sandbox — only stdout enters context

### WebFetch — BLOCKED
WebFetch calls are denied entirely. The URL is extracted and you are told to use `ctx_fetch_and_index` instead.
Instead use:
- `ctx_fetch_and_index(url, source)` then `ctx_search(queries)` to query the indexed content

## REDIRECTED tools — use sandbox equivalents

### Bash (>20 lines output)
Bash is ONLY for: `git`, `mkdir`, `rm`, `mv`, `cd`, `ls`, `npm install`, `pip install`, and other short-output commands.
For everything else, use:
- `ctx_batch_execute(commands, queries)` — run multiple commands + search in ONE call
- `ctx_execute(language: "shell", code: "...")` — run in sandbox, only stdout enters context

### Read (for analysis)
If you are reading a file to **Edit** it → Read is correct (Edit needs content in context).
If you are reading to **analyze, explore, or summarize** → use `ctx_execute_file(path, language, code)` instead. Only your printed summary enters context. The raw file content stays in the sandbox.

### Grep (large results)
Grep results can flood context. Use `ctx_execute(language: "shell", code: "grep ...")` to run searches in sandbox. Only your printed summary enters context.

## Tool selection hierarchy

1. **GATHER**: `ctx_batch_execute(commands, queries)` — Primary tool. Runs all commands, auto-indexes output, returns search results. ONE call replaces 30+ individual calls.
2. **FOLLOW-UP**: `ctx_search(queries: ["q1", "q2", ...])` — Query indexed content. Pass ALL questions as array in ONE call.
3. **PROCESSING**: `ctx_execute(language, code)` | `ctx_execute_file(path, language, code)` — Sandbox execution. Only stdout enters context.
4. **WEB**: `ctx_fetch_and_index(url, source)` then `ctx_search(queries)` — Fetch, chunk, index, query. Raw HTML never enters context.
5. **INDEX**: `ctx_index(content, source)` — Store content in FTS5 knowledge base for later search.

## Subagent routing

When spawning subagents (Agent/Task tool), the routing block is automatically injected into their prompt. Bash-type subagents are upgraded to general-purpose so they have access to MCP tools. You do NOT need to manually instruct subagents about context-mode.

## Output constraints

- Keep responses under 500 words.
- Write artifacts (code, configs, PRDs) to FILES — never return them as inline text. Return only: file path + 1-line description.
- When indexing content, use descriptive source labels so others can `ctx_search(source: "label")` later.

## ctx commands

| Command | Action |
|---------|--------|
| `ctx stats` | Call the `ctx_stats` MCP tool and display the full output verbatim |
| `ctx doctor` | Call the `ctx_doctor` MCP tool, run the returned shell command, display as checklist |
| `ctx upgrade` | Call the `ctx_upgrade` MCP tool, run the returned shell command, display as checklist |
