# Neomotive Scan Tool — Implementation Plan

## Overview

A phased automotive diagnostic application built in C# / .NET 10, sharing styling and infrastructure with the Neomotive Module Simulator.

---

## Existing Simulator Reference Points

| Concern | Simulator value |
|---|---|
| Window size | 800 × 480, `CanResize="False"`, `SystemDecorations="None"`, `Position="0,0"` |
| Background | `#111418` |
| Font | Consolas, Cascadia Code, monospace — 13 px default |
| Avalonia | 11.3.9 (Fluent theme + Inter fonts) |
| PCAN driver | `ICs.CAN.PCanBasic` project reference (wilderness/Meadow.Foundation) |
| Meadow platform | `Meadow.Windows` + `Meadow.Avalonia` (project refs) |

All ScanTool UI must match these values exactly.

---

## Repository Layout (target state)

```
software/dotnet/Apps/
├── ModuleSimulator/          (existing — unchanged in Phase 1)
├── ScanTool/
│   ├── Neomotive.ScanTool.Core/               # protocol logic, no UI
│   ├── Neomotive.ScanTool.UIShared/           # views, styles, VMs shared across platforms
│   ├── Neomotive.ScanTool.Desktop/            # Phase 1 — Windows WinExe entry point
│   ├── Neomotive.ScanTool.RaspberryPi/        # Phase 2 — Pi entry point
│   └── neomotive scantool.slnx
└── Shared/
    └── Neomotive.UI.Styles/                   # extracted shared palette + typography
```

---

## Shared Library: Neomotive.UI.Styles

Extract from `ModuleSimulator.UIShared/SharedStyles.axaml` into a standalone Avalonia styles library that both apps reference.

**Contents:**
- All `SolidColorBrush` resource keys (`BrushFgDefault`, `BrushBgModule`, etc.)
- Base `TextBlock` typography styles (font family, sizes, semantic classes: `.label`, `.value`, `.ok`, `.warn`, `.error`, `.heading`, etc.)
- `Button.tab` and `Button.action-sm` styles
- `ScrollViewer` / `ScrollBar` touch-friendly styles

**Does NOT include** simulator-specific styles (`Border.module`, `Button.dtc`, `Button.monitor-row`) — those stay in `ModuleSimulator.UIShared`.

The simulator's `SharedStyles.axaml` is refactored to merge `Neomotive.UI.Styles` and add its own local overrides.

---

## Project Descriptions

### Neomotive.ScanTool.Core
- `net10.0` class library, no UI dependencies
- Defines interfaces: `IScanTool`, `IDtcReader`, `IVinReader`, `IEmissionsReader`, `ILiveDataReader`
- OBD2 service implementations (Mode $01–$09)
- J1939 PGN abstractions (via Meadow.Foundation J1939)
- DTC model (`DiagnosticTroubleCode` — code, description, status, type)
- VIN decoder
- Readiness monitor model

### Neomotive.ScanTool.UIShared
- `net10.0` Avalonia class library
- Imports `Neomotive.UI.Styles`
- `MainWindow.axaml` — 800×480, no decorations, `#111418` background (mirrors simulator)
- Views (each a UserControl):
  - `ConnectionView` — adapter selection, connect/disconnect, bus status
  - `VehicleView` — VIN display, protocol detected, ECU count
  - `EmissionsView` — readiness monitors grid
  - `DtcsView` — DTC list, clear button, MIL status
  - `LiveDataView` *(Phase 3)*
  - `UdsView` *(Phase 4)*
- `SharedStyles.axaml` — ScanTool-specific style additions on top of `Neomotive.UI.Styles`
- ViewModels for each view (MVVM, ReactiveUI or plain `INotifyPropertyChanged`)

### Neomotive.ScanTool.Desktop
- `net10.0-windows`, `OutputType=WinExe`
- Same Avalonia + Meadow package refs as `Neomotive.ModuleSimulator.Desktop`
- References `ICs.CAN.PCanBasic` (Peak USB driver)
- Entry point only — sets up DI, wires `PCanBasicController` into `IScanTool`

### Neomotive.ScanTool.RaspberryPi *(Phase 2)*
- `net10.0`, Linux ARM target
- References MCP2515 HAT driver (Meadow.Foundation SPI CAN)
- Same entry point pattern as Desktop — swaps hardware adapter only

---

## Phase Plans

### Phase 1 — Desktop OBD2 (MVP)

**Goal:** Connect to a vehicle via Peak PCAN USB, read VIN, emission readiness monitors, and DTCs.

**Tasks:**
1. Create `Neomotive.UI.Styles` shared library; refactor simulator to use it
2. Create `neomotive scantool.slnx` solution
3. Create `Neomotive.ScanTool.Core` — OBD2 protocol, DTC/VIN/readiness models
4. Create `Neomotive.ScanTool.UIShared` — MainWindow + 4 views (Connection, Vehicle, Emissions, DTCs)
5. Create `Neomotive.ScanTool.Desktop` — PCAN wiring + DI setup
6. Implement OBD2 services: Mode $09 (VIN), Mode $01 (readiness), Mode $03/$07 (DTCs), Mode $04 (clear DTCs)
7. Tab-bar navigation matching simulator pattern

**Key decisions:**
- MVVM with plain `INotifyPropertyChanged` (no extra framework unless simulator already uses one)
- CAN communication runs on a background `Task`; results marshalled to UI thread via `Dispatcher.UIThread.Post`
- Protocol auto-detection: try CAN 500k first (ISO 15765-4), fall back to 250k

### Phase 2 — Raspberry Pi

**Goal:** Run the same UI on a Pi with an MCP2515 HAT.

**Tasks:**
1. Create `Neomotive.ScanTool.RaspberryPi` project
2. Wire MCP2515 SPI driver as `ICanController`
3. Publish/deploy script for Pi (ARM64 self-contained)
4. Test window renders at 800×480 on Pi display

### Phase 3 — Live Data, Graphing, Recording

**Goal:** Stream real-time PIDs with charts and optional logging to file.

**Tasks:**
1. Add `LiveDataView` — PID selector, scrolling strip chart (OxyPlot or LiveCharts2 for Avalonia)
2. Add `ILiveDataReader` polling loop with configurable PID set
3. CSV/JSON recording with start/stop controls
4. Playback of recorded sessions (offline review)

### Phase 4 — UDS Support

**Goal:** UDS (ISO 14229) DTCs, live data, configuration reads, actuator control.

**Tasks:**
1. Add UDS service layer to `Neomotive.ScanTool.Core` (services $14, $19, $22, $2E, $2F, $31)
2. Add `UdsView` — ECU selector, UDS DTC list, snapshot data, actuator commands
3. Security access flow (seed/key) UI
4. ODX/CDD file import for PID/DTC descriptions *(stretch goal)*

### Phase 5 — Cloud Integration

**Goal:** Push session data and DTCs to a cloud service (details TBD).

**Tasks:**
1. Define cloud API contract (REST or MQTT)
2. Add `Neomotive.ScanTool.Cloud` library — upload service, auth, retry
3. Session sync: automatic upload on connect/disconnect
4. Fleet view: history of vehicles scanned (web dashboard, spec TBD)

---

## NuGet / Project References Summary (Phase 1 Desktop)

| Package / Reference | Purpose |
|---|---|
| `Avalonia` 11.3.9 | UI framework |
| `Avalonia.Desktop` 11.3.9 | Desktop host |
| `Avalonia.Themes.Fluent` 11.3.9 | Theme base |
| `Avalonia.Fonts.Inter` 11.3.9 | Font pack |
| `Avalonia.Diagnostics` 11.3.9 (Debug only) | Dev tools |
| `Meadow.Windows` (project ref) | Meadow Windows platform |
| `Meadow.Avalonia` (project ref) | Meadow Avalonia integration |
| `ICs.CAN.PCanBasic` (project ref) | Peak PCAN USB driver |
| `Meadow.Foundation.J1939` | J1939 / OBD2 message handling |

---

## Open Questions (resolve before Phase 1 implementation)

1. **Solution scope:** Should ScanTool and Simulator share one `.slnx` or remain separate solutions?
2. **Meadow.Foundation J1939 NuGet vs. project ref:** Is J1939 published to NuGet or only available as a source project reference (like PCAN)?
3. **MVVM framework:** Simulator uses plain ViewModels — confirm no ReactiveUI / CommunityToolkit dependency before ScanTool adopts one.
4. **Shared library refactor timing:** Refactor `ModuleSimulator` to use `Neomotive.UI.Styles` in the same PR as Phase 1, or as a prerequisite step?
