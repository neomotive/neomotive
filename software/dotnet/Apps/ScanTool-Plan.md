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
| Avalonia | 12.0.4 (Fluent theme + Inter fonts) |
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

**Shared controls** (`Controls/`) also live here — Avalonia `UserControl`s that both apps drop in as-is:

- `NetworkStatusBar` + `NetworkStatusViewModel` — the one-line network summary shown at the bottom of
  the Updates tab in both apps: connection icon (RJ45 plug or Wi-Fi arcs, slashed when there is no
  link), hostname, IPv4 address when one is assigned, and the state in words. It polls
  `NetworkInterface` every 5s while attached to the visual tree and supplies its own DataContext, so
  hosts need no bindings. Colours are literal (not `StaticResource`) so it renders correctly even in a
  host that has not merged `Styles.axaml`.

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
- `net10.0`, published self-contained single-file for `linux-arm64` (`AssemblyName=scantool`)
- `AvaloniaMeadowApplication<Meadow.RaspberryPi>`; references `Meadow.Linux` + `ICs.CAN.Mcp2515`
- CAN via `WaveshareDualCanHat` (dual MCP2515 on SPI0, CS pin24/pin26, INT pin16/pin22, 500 kbps),
  registering CAN0 as `ICanBus`; falls back to `NullCanBus` in offline mode
- **Renders via Avalonia's DRM/KMS backend** (`Avalonia.LinuxFramebuffer`, `StartLinuxDrm`) — no
  X server. That means a **single-view lifetime**: `App` sets `MainView` to the shared
  `ScanToolView` in a `Viewbox` rather than a `MainWindow`
- `UpdateService` over A/B slots rooted at `/data/app` (see Phase 5)

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

### Phase 2 — Raspberry Pi *(code complete; hardware verification pending)*

**Goal:** Run the same UI on a Pi with an MCP2515 HAT, deployed onto a
[Pi Appliance Kit](../../../../ctacke/Pi-Appliance-Kit) device.

**Tasks:**
1. ~~Create `Neomotive.ScanTool.RaspberryPi` project~~ ✅
2. ~~Wire MCP2515 SPI driver as `ICanBus`~~ ✅ — `WaveshareDualCanHat`, CAN0
3. ~~Publish/deploy script for Pi (ARM64 self-contained)~~ ✅ — `scripts/publish-scantool-pi.ps1`
4. Test view renders at 800×480 on the Pi display — **blocked on hardware access**

**Deployment model (Pi Appliance Kit):**

Raspberry Pi OS Lite, arm64, read-only overlay rootfs; `/data` is the only writable mount.
An app is a directory containing an executable `run`; `app.service` (pre-enabled, runs as root
with `ProtectSystem=strict`, `ReadWritePaths=/data`, `ProtectHome=yes`) execs it via `app-launch`.
Deploy = `Pi-Appliance-Kit/scripts/install-app.sh <payload> pi@host` (rsync to `/data/app`).

Because the rootfs is read-only and `$HOME` is protected, `scripts/pi/run` redirects `HOME`,
`DOTNET_BUNDLE_EXTRACT_BASE_DIR` and `XDG_RUNTIME_DIR` under `/data/app`.

**Why DRM instead of X11:** the appliance image ships no display stack. Rendering straight to
`/dev/dri/card*` avoids adding an X server, window manager and autologin chain to a
single-daemon image. Cost: single-view lifetime (no `Window`), and the image needs
`libgl1-mesa-dri`, `libegl1`, `libgles2`, `libinput10`, `libfontconfig1` plus
`dtoverlay=vc4-kms-v3d` and `dtoverlay=spi0-0cs` — all added to the kit's
`config/optimizations.yaml`.

Full runbook: `ScanTool/scripts/pi/README.md`.

### Desktop Scaling (recording support)

`MainWindow.axaml`: `ScanToolView` wrapped in `<Viewbox Stretch="Uniform">` with explicit `Width="800" Height="480"` — Avalonia scales the fixed 800×480 layout uniformly to fill any window size.

`MainWindow.axaml.cs`: `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` → `CanResize=true`, default `1024×614` (perfect 5:3 fill), `MinWidth=400 MinHeight=240`. Non-Windows desktop hosts: AXAML defaults unchanged (800×480, non-resizable).

The Pi does **not** use `MainWindow` at all — the DRM single-view lifetime has no `Window`. It
applies the same treatment itself in `App.OnFrameworkInitializationCompleted`: an 800×480
`ScanToolView` inside a `Viewbox Stretch="Uniform"`, so a panel reporting a different mode
letterboxes instead of stretching.

---

### Phase 3 — Live Data, Graphing, Recording

**Goal:** Stream real-time PIDs with charts and optional logging to file.

**Completed:**
- `PidDescriptor`, `PidValue`, `PidRegistry` (15 curated PIDs with scale/offset/unit/range)
- `IObd2Scanner.ReadPidAsync` — generic Mode $01 PID request using descriptor metadata
- `LivePidItem` — VM wrapper with IsSelected, CurrentValue, 120-sample ring buffer history
- `LiveDataView` — 220px left PID list + right tabbed panel (Table/Gauges/Waveform)
- `GaugeControl` — custom arc gauge (no library dependency), green/yellow/red by %
- `LiveDataTablePane` — scrollable table view, 44px touch targets
- `LiveDataGaugePane` — WrapPanel of up to 6 gauges (first 6 selected PIDs)
- `LiveDataWaveformPane` — 4 stacked canvas slots, 250ms refresh timer, 60s rolling window
- 2Hz polling loop in MainWindowViewModel, stops on disconnect or tab switch
- Charting: Canvas+Polyline approach (OxyPlot skipped — Avalonia 12.0.4 compat unverified)
- Fixed pre-existing Resolver.Log null issue in Obd2Scanner — 28/28 tests pass
- Fixed Avalonia 12.0.4 crash: `Polyline.Points = null` → `new Points()` (ArgumentNullException in PolylineGeometry)

**Phase 3 — event capture (in progress, see Tasks Group T):**

Capture engine, high-rate poll loop and storage are complete and tested (109/109 Core tests).
`Neomotive.ScanTool.Core/Capture/` provides a rolling pre-trigger buffer, threshold/manual/
bus-wake triggers, stall auto-stop, and long-format CSV + JSON sidecar storage. The capture path
polls only the armed signals with no inter-sweep delay, reaching roughly 10 Hz against ~2 Hz for
the live-data loop — the difference between four samples across a crank and forty.

3. ~~CSV/JSON recording with start/stop controls~~ — done (T7/T11/T12)
4. ~~Playback of recorded sessions (offline review)~~ — done (T11)
5. ~~Tune detection via Mode 09 CALID/CVN and supported-PID bitmaps~~ — done (T13)
6. ~~Mode 22 channels via UDS `ReadDataByIdentifier`~~ — done (T14); the shipped DID template is
   marked UNVERIFIED and must be confirmed against the actual ECU

**Phase 3 is code complete.** 144 Core tests plus 24 simulator tests green; both solutions build.
What remains is bench and vehicle verification, which needs hardware:

- Confirm the capture loop clears ~10 Hz for five signals against ModuleSimulator over real CAN
- Run each simulator start profile end to end and confirm triggers, pre-trigger retention and
  stall auto-stop behave on the wire as they do in tests
- Confirm captures land in `/data` on the Pi and survive an update
- Verify the 260 px capture sidebar is usable on the Pi's 800×480 panel

### Phase 4 — UDS Support

**Goal:** UDS (ISO 14229) DTCs, live data, configuration reads, actuator control.

**Tasks:**
1. ~~Add UDS service layer to `Neomotive.ScanTool.Core`~~ — done, then moved out. The generic
   client and server now live in the Meadow library `Telematics.Uds` (assembly `Uds`, namespace
   `Meadow.Foundation.Telematics.Uds`); ScanTool references it. Services covered client-side:
   $10, $14, $19, $22, $3E. Still to add: $2E, $2F, $31.
2. [x] `UdsView` — ECU selector, UDS DTC list, DID reads
3. Security access flow (seed/key) UI — not started; `UdsServer` answers $27 with NRC $11 today
4. ODX/CDD file import for PID/DTC descriptions *(stretch goal)* — partly superseded: the
   `Neomotive.Uds` catalog already imports DID definitions from JSON or CSV at runtime, so an ODX
   importer only has to emit that shape.

**Bench target (new):** ModuleSimulator now runs a UDS server, so discovery, $19 DTC read, $14
clear, $22 DID read and $10/$3E session handling can all be exercised without a vehicle. Modules
and their DIDs come from `SimulatorConfig.Uds` in `neoteric.config.json` — adding an ECU is a
config edit. See Group Y in `ScanTool-Tasks.md`.

**Where UDS lives now:**

| Concern | Home |
|---|---|
| Protocol — client, server, ISO-TP, enums, models | `Telematics.Uds` (Meadow library) |
| Database — DID names/formatting, FTB and NRC text | `Apps/Shared/Neomotive.Uds` (`UdsCatalog`) |
| Simulated ECUs — modules, DIDs, faults | `ModuleSimulator.Core/Uds/` + config profile |
| UI | `ScanTool.UIShared/Views/UdsView.axaml` |

The catalog is file-backed and extensible without a rebuild: drop a `uds-catalog*.json` (or import
a `did,name` CSV) into the ScanTool config directory, and `Export` writes the merged table back out.

### Phase 5 — Update Mechanism

**Goal:** Reliable, rollback-safe OTA and USB updates for app binaries and config files (VIN catalog). Applies to both ScanTool and ModuleSimulator independently.

**Completed:**
- `Neomotive.Update` shared library (`software/dotnet/Apps/Shared/Neomotive.Update/`)
  - A/B slot swap (`app-current/` ↔ `app-previous/` with `app-staging/` as extraction target)
  - SHA256 verification of every file before committing a swap
  - `UsbUpdateSource` — polls removable drives every 5 s; auto-detects `neomotive-update*.zip`
  - `NetworkUpdateSource` — HTTP GET version manifest; downloads + verifies zip on demand
  - `UpdateService` orchestrator with `UpdateFound` / `UpdateApplied` / `UpdateFailed` events
- `Neomotive.Vin` catalog override: `VinOptions.ExternalCatalogPath` checked before embedded resources
- ScanTool: "Updates" 7th tab (`UpdatesView`); `UpdateService` wired in `App.axaml.cs`; `neomotive.config.json` for server URL
- Simulator: Updates section in `ConfigView`; same `UpdateService` wiring in Desktop + RaspberryPi
- Pi scripts: `xinitrc` launches `app-current/simulator`; `setup-autostart.sh` creates slot dirs; `deployment.md` updated
- `create-update-package.ps1` — publishes self-contained, hashes files, writes `update.json`, zips, updates `version-manifest.json`
- CI releases (2026-09-09): `.github/workflows/release.yml` turns a `can-scan-vX.Y.Z` /
  `can-sim-vX.Y.Z` tag into a published package and merges its entry into a single rolling
  `updates-latest/version-manifest.json`. Devices poll that one URL forever.
- Internet-by-default (2026-09-09): `UpdateService.DefaultManifestUrl` points at that manifest,
  so a stock device updates from GitHub with no per-device config. `updateServerUrl` in
  `neomotive.config.json` is an override for bench testing against a local server.
- Updates screen shows the installed version and the manifest URL it will query.
- Pi Appliance Kit correctness (2026-09-09): `app.service` runs the app under
  `ProtectSystem=strict` + `ReadWritePaths=/data`, so `/data` is the only writable path in the
  unit's namespace — `/tmp` included. `NetworkUpdateSource` therefore takes its download
  directory from the caller (`/data/app/app-downloads`) instead of `Path.GetTempPath()`, which
  had made every network update on the appliance fail with "access denied". `UpdateApplicator`
  gained `EnsureLayout()` (creates the writable dirs, clears interrupted staging) and
  `FreeSpaceBytes()`; `UpdateService` refuses an update that cannot fit in 3x the package and
  deletes its own download after applying. See `docs/updates/release-and-update.md` §6.
- Launchers ship inside the payload (2026-09-09): `$APP_DIR/run` lives outside the A/B slots, so
  update packages now carry `app/launcher/`, and `run` hands off to `app-current/launcher/run`
  when it is present and passes `sh -n`. The simulator gained an appliance-kit `run`, and its
  `xinitrc` resolves the app root instead of hardcoding the now-read-only `/opt/neomotive`.

**Package format:**
```
neomotive-update-{version}-{target}-{platform}.zip
├── update.json          ← manifest (version, target, platform, type, file hashes)
├── app/                 ← optional: full self-contained publish output
│   └── launcher/        ← Pi launcher scripts (run; + xinitrc for the simulator)
└── config/              ← optional: catalog JSON overrides
```

**Windows behavior:** Update staged while app is running; applied on next restart (can't replace running EXE).
**Pi behavior:** Update applied immediately; the app exits 42 and its launcher starts the newly
promoted slot. The app cannot relaunch itself — after the swap its own path points into
`app-previous`, and a child spawned from a dying process loses the display.

**ScanTool on Pi (2026-09-09):** now wired, reversing the earlier "not applicable" call. The A/B
root moved under `/data` (`/data/app/app-current`, the rootfs being a read-only overlay), `run`
supervises rather than `exec`s, and `publish-scantool-pi.ps1` deploys into the slot. The
workstation deploy remains the bootstrap path — a device on pre-update software has no updater to
invoke — but field updates no longer need one.

**USB on ScanTool (2026-09-09):** the appliance image auto-mounts nothing, and
`HasRemovableDrive()` accepts only `/media/usb`, so USB updates were inert on that device.
`scripts/pi/setup-usb-updates.sh` installs the udev rule and mount helper; it must run with the
read-only overlay lifted or it lands in RAM. `Neomotive.Update.Tests` covers the version gate
and hash verification.

**Remaining:**
- Hardening: HTTPS + package signing for cloud distribution
- Automated health-check rollback (currently manual via slot swap)
- Verify the ScanTool USB path against real hardware — never exercised on a device

---

### Phase 6 — Cloud Integration

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
| `Avalonia` 12.0.4 | UI framework |
| `Avalonia.Desktop` 12.0.4 | Desktop host |
| `Avalonia.Themes.Fluent` 12.0.4 | Theme base |
| `Avalonia.Fonts.Inter` 12.0.4 | Font pack |
| `Avalonia.Diagnostics` 12.0.4 (Debug only) | Dev tools |
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

---

## Shared Library Layout (as built)

`Apps/Shared/` holds everything used by both ScanTool and ModuleSimulator. Each app's solution
includes these under a `/Shared/` solution folder.

| Project | Contents |
|---|---|
| `Neomotive.Can.Hardware` | `WaveshareDualCanHat` — dual MCP2515 CAN HAT wiring for the Pi |
| `Neomotive.Can.UI` | `CanView` (bus-health header + packet log), `CanLogItem`, `ICanViewModel` |
| `Neomotive.Obd2` | `DtcDescriptions` |
| `Neomotive.UI.Styles` | Common Avalonia styles |
| `Neomotive.Update` | USB / network update service |
| `Neomotive.Vin` | VIN decode / generate |

**Sharing an Avalonia view across apps** — the pattern established by `CanView`: put the view in
the shared project with `x:DataType` pointing at an *interface* (`ICanViewModel`), and have each
app's `MainWindowViewModel` implement it. Host views reference it via
`xmlns:canviews="clr-namespace:Neomotive.Can.UI.Views;assembly=Neomotive.Can.UI"`. Styles stay
resolved at app level, so `Classes="heading"` etc. still work.
