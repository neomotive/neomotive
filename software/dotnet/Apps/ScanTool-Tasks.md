# Neomotive Scan Tool — Phase 1 Task List

## Group A — Shared infrastructure

- [x] **A1** Create `Neomotive.UI.Styles` project at `software/dotnet/Apps/Shared/Neomotive.UI.Styles/`
  - Avalonia styles library, `net10.0`, no code-behind
  - Extract from `ModuleSimulator.UIShared/SharedStyles.axaml`: all brushes, TextBlock styles, Button.tab, Button.action-sm, ScrollViewer/ScrollBar styles

- [x] **A2** Refactor `Neomotive.ModuleSimulator.UIShared` to reference `Neomotive.UI.Styles`
  - Add `<ProjectReference>` to `Neomotive.UI.Styles`
  - Replace extracted content in `SharedStyles.axaml` with a `<StyleInclude>` of the shared library
  - Keep simulator-specific styles in place (`Border.module`, `Button.dtc`, `Button.monitor-row`, `Button.category`)

- [x] **A3** Add `Neomotive.UI.Styles` project to `neomotive simulator.slnx`

- [x] **A4** Verify Simulator still builds and renders correctly after refactor

---

## Group B — Solution and project scaffolding

- [x] **B1** Create `neomotive scantool.slnx` at `software/dotnet/Apps/ScanTool/`

- [x] **B2** Create `Neomotive.ScanTool.Core` project
  - `net10.0` class library, no UI
  - Project ref: `Telematics.J1979` (wilderness)
  - Add to solution

- [x] **B3** Create `Neomotive.ScanTool.UIShared` project
  - `net10.0` Avalonia library
  - Project refs: `Neomotive.ScanTool.Core`, `Neomotive.UI.Styles`
  - NuGet: `Avalonia 12.0.4`, `Avalonia.Themes.Fluent 12.0.4`, `Avalonia.Fonts.Inter 12.0.4`
  - Add to solution

- [x] **B4** Create `Neomotive.ScanTool.Desktop` project
  - `net10.0`, `OutputType=WinExe`, `AvaloniaUseCompiledBindingsByDefault=true`
  - Project refs: `Neomotive.ScanTool.UIShared`, `Meadow.Windows`, `Meadow.Avalonia`, `ICs.CAN.PCanBasic`
  - NuGet: `Avalonia 12.0.4`, `Avalonia.Desktop 12.0.4`, `Avalonia.Fonts.Inter 12.0.4`, `Avalonia.Diagnostics 12.0.4` (Debug only)
  - Add to solution

---

## Group C — Core protocol layer (`Neomotive.ScanTool.Core`)

- [x] **C1** Define models
  - `DiagnosticTroubleCode` — code string, description, status (stored/pending/permanent), type (generic/manufacturer)
  - `ReadinessMonitor` — name, supported bool, ready bool
  - `VehicleInfo` — VIN string, protocol detected, ECU address list

- [x] **C2** Define `IObd2Scanner` interface
  - `Task<string?> ReadVinAsync(CancellationToken ct)`
  - `Task<IReadOnlyList<DiagnosticTroubleCode>> ReadStoredDtcsAsync(CancellationToken ct)`
  - `Task<IReadOnlyList<DiagnosticTroubleCode>> ReadPendingDtcsAsync(CancellationToken ct)`
  - `Task ClearDtcsAsync(CancellationToken ct)`
  - `Task<IReadOnlyList<ReadinessMonitor>> ReadReadinessAsync(CancellationToken ct)`
  - `Task<bool> ConnectAsync(CancellationToken ct)`

- [x] **C3** Implement `Obd2Scanner : IObd2Scanner`
  - Constructor takes `ICanBus`
  - ISO 15765-4 single + multi-frame (first/flow-control/consecutive) over `ICanBus`
  - Mode $09 PID $02 → VIN
  - Mode $01 PID $01 → readiness monitors
  - Mode $03 → stored DTCs
  - Mode $07 → pending DTCs
  - Mode $04 → clear DTCs
  - `Obd2Protocol` static helper extracted for testability
  - Note: 500 kbps only (250 kbps auto-fallback deferred to later)

- [x] **C4** Implement `NullCanBus` (offline no-op, mirrors Simulator pattern)

---

## Group D — UIShared: styles and shell

- [x] **D1** Create `SharedStyles.axaml`
  - StyleInclude of `Neomotive.UI.Styles`
  - ScanTool-specific additions (`.vin`, `.status-ok`, `.status-warn`, `.status-na`)

- [x] **D2** Create `App.axaml` in Desktop (FluentTheme dark, Inter font, SharedStyles merge)

- [x] **D3** Create `MainWindow.axaml`
  - 800×480, `CanResize=False`, `WindowDecorations=None`, `Position=0,0`, `Background=#111418`
  - `x:DataType="local:MainWindowViewModel"`
  - Contains `<views:ScanToolView />`

- [x] **D4** Create `ScanToolView.axaml` — main tab shell
  - Horizontal tab bar: Connection | Vehicle | Emissions | DTCs
  - Each tab: `Button Classes="tab"` + `Classes.active="{Binding IsXxxView}"` + `Click` handler
  - Grid overlay content area: each child `IsVisible="{Binding IsXxxView}"`

---

## Group E — UIShared: views and ViewModels

- [x] **E1** Create `MainWindowViewModel`
  - `INotifyPropertyChanged`, no base class, `CallerMemberName` pattern
  - `ScanView` enum (Connection, Vehicle, Emissions, Dtcs)
  - `IsConnectionView`, `IsVehicleView`, `IsEmissionsView`, `IsDtcsView` boolean properties
  - `ShowConnection()`, `ShowVehicle()`, `ShowEmissions()`, `ShowDtcs()` void methods
  - VIN, readiness monitors list, stored/pending DTC lists, MIL status as notifying properties
  - `IObd2Scanner` injected via constructor
  - `ConnectAsync()` / `Disconnect()` with background Task + `Dispatcher.UIThread.Post`

- [x] **E2** Create `ConnectionView.axaml`
  - Adapter status text, Connect/Disconnect buttons, bitrate display
  - Connect → `IObd2Scanner.ConnectAsync()` on background Task → `Dispatcher.UIThread.Post` result

- [x] **E3** Create `VehicleView.axaml`
  - VIN (large `.vin` text), protocol detected

- [x] **E4** Create `EmissionsView.axaml`
  - `ItemsControl` over readiness monitors list with name + status columns
  - Status uses `.ok` / `.warn` TextBlock classes

- [x] **E5** Create `DtcsView.axaml`
  - MIL status (`.mil-on` / `.mil-off`)
  - Scrollable stored DTC list (`ItemsControl`)
  - Scrollable pending DTC list (`ItemsControl`)
  - "Clear DTCs" button

---

## Group F — Desktop entry point

- [x] **F1** Create `Program.cs`
  - `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args)`

- [x] **F2** Create `App.axaml.cs` extending `AvaloniaMeadowApplication<Meadow.Windows>`
  - `MeadowInitialize()`: try `new PCanUsb().CreateCanBus(CanBitrate.Can_500kbps)` → catch → `NullCanBus`; `Resolver.Services.Add<ICanBus>(bus)`
  - `OnFrameworkInitializationCompleted()`: instantiate `Obd2Scanner(bus)`, then `MainWindowViewModel(scanner)`, set `MainWindow`

- [x] **F3** Create `App.axaml`
  - Fluent dark theme, StyleInclude of UIShared's `SharedStyles.axaml`

---

## Group G — Unit Tests (`Neomotive.ScanTool.Core.Tests`)

- [x] **G1** Create `Neomotive.ScanTool.Core.Tests` project
  - `net10.0`, xUnit (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`)
  - Project reference to `Neomotive.ScanTool.Core`

- [x] **G2** Add test project to `neomotive scantool.slnx` under `/Tests/` folder

- [x] **G3** Extract `Obd2Protocol` static helper class from `Obd2Scanner`
  - `public static string? DecodeDtcCode(byte hi, byte lo)`
  - `public static IReadOnlyList<ReadinessMonitor> ParseReadiness(byte a, byte b, byte c, byte d)`
  - `public static string? ParseVin(byte[] responseData)`
  - `public static IReadOnlyList<DiagnosticTroubleCode> ParseDtcs(byte[] responseData, DtcStatus status)`

- [x] **G4** Implement `FakeCanBus` in test project
  - Implements `ICanBus`
  - Records all frames written via `WriteFrame` in `List<StandardDataFrame> SentFrames`
  - Exposes `InjectFrame(ICanFrame)` to fire `FrameReceived` synchronously

- [x] **G5** Write `DtcDecodingTests` — 7 tests (all 4 categories, known codes, zero bytes, manufacturer subtype)

- [x] **G6** Write `ReadinessParsingTests` — 5 tests (all ready, none supported, misfire incomplete, catalyst incomplete, count=11)

- [x] **G7** Write `VinParsingTests` — 4 tests (valid VIN, short data, null input, wrong service byte)

- [x] **G8** Write `Obd2ScannerTests` — 7 tests (mode $03/$07/$04/$01 requests, DTC parsing, readiness parsing, multi-frame VIN with flow control, timeout→null)

---

## Group I — Move shared OBD2 types to Telematics.J1979

- [x] **I1** Add `Obd2Addresses`, `IsoTpFrameType`, `VehicleInfoPid`, `ReadinessMonitorBits` to `wilderness/Meadow.Foundation/.../Telematics.J1979/Driver/` under `Meadow.Foundation.Telematics.J1979` namespace
- [x] **I2** Remove intermediate `Neomotive.OBD2` library (deleted)
- [x] **I3** Remove `Neomotive.OBD2` project reference from `Neomotive.ScanTool.Core.csproj` and `Neomotive.ModuleSimulator.Core.csproj`
- [x] **I4** Types now available to both ScanTool and Simulator via existing J1979 references (Simulator gets it transitively through `Neomotive.ControlModule`)
- [x] **I5** Verify 25/25 unit tests still pass

---

## Group H — Eliminate magic numbers (enums and named constants)

- [x] **H1** Audit J1979 library for existing enums — `Service`, `Pid`, `DtcCategory` already exist in `Meadow.Foundation.Telematics.J1979`
- [x] **H2** Create `IsoTpFrameType` enum in `Neomotive.ScanTool.Core` (Single=0, First=1, Consecutive=2, FlowControl=3)
- [x] **H3** Create `Obd2Addresses` static class in `Neomotive.ScanTool.Core` (FunctionalRequest, EcuResponseBase/Max, EcuPhysicalOffset, ResponseOffset, DtcManufacturerMask, DtcCategoryMask)
- [x] **H4** Create `VehicleInfoPid` enum in `Neomotive.ScanTool.Core` for Service $09 PIDs (Vin, CalibrationId, Cvn, EcuName)
- [x] **H5** Create `ReadinessMonitorBits` static class in `Neomotive.ScanTool.Core` with named bit constants (SAE J1979 PID $01 bit positions)
- [x] **H6** Update `Obd2Scanner.cs` — replace all magic numbers with `Service.*`, `Pid.*`, `VehicleInfoPid.*`, `IsoTpFrameType.*`, `Obd2Addresses.*`
- [x] **H7** Update `Obd2Protocol.cs` — replace all magic numbers with `DtcCategory.*`, `Obd2Addresses.*`, `ReadinessMonitorBits.*`, `Service.*`, `VehicleInfoPid.*`
- [x] **H8** Verify 25/25 unit tests still pass after enum refactor

---

## Verification

- [x] Simulator builds and renders identically after A1–A4 refactor
- [x] `dotnet test Neomotive.ScanTool.Core.Tests` — 25/25 tests pass after H-series enum refactor
- [x] Core and Core.Tests projects build with 0 errors / 0 warnings
- [x] Debug logging added throughout connect flow (App init, SendAndReceive TX/RX/timeout, ConnectAsync result)
- [ ] ScanTool Desktop builds in Release with no errors
- [ ] App opens with no PCAN adapter — Connection tab shows offline/disconnected gracefully (NullCanBus)
- [ ] App connects with PCAN adapter + vehicle — VIN populates, monitors display, DTCs list populates
- [ ] Clear DTCs empties the list and updates MIL status

---

## Group J — Core live data layer

- [x] **J1** `PidDescriptor`, `PidValue`, `PidRegistry` (15 curated PIDs)
- [x] **J2** `IObd2Scanner.ReadPidAsync` + `Obd2Scanner` implementation
  - Sends Mode $01 request, parses response bytes via Scale/Offset/ByteCount
  - Fixed pre-existing `Resolver.Log` null issue (all calls guarded with `?.`)
- [x] **J3** Unit tests for PID parsing: vehicle speed (1-byte, scale=1), engine RPM (2-byte, scale=0.25), coolant temp (offset=-40) — 28/28 total tests pass

---

## Group K — ViewModel & polling

- [x] **K1** `LivePidItem.cs` — INotifyPropertyChanged wrapper with IsSelected, CurrentValue, DisplayValue, SelectionIndicator, History ring buffer (120 samples)
- [x] **K2** `MainWindowViewModel` additions:
  - `ScanView.LiveData`, `LiveSubView` enum (Table/Gauges/Waveform)
  - `IsLiveDataView`, `IsTableView`, `IsGaugesView`, `IsWaveformView` properties
  - `ShowLiveData()`, `ShowTable()`, `ShowGauges()`, `ShowWaveform()`
  - `LivePidItems`, `SelectedLivePids`, `GaugePids`, `HasSelectedPids`, `HasNoSelectedPids`
  - `IsPolling`, `CanStartPolling`, `CanStopPolling`
  - `StartPolling()`, `StopPolling()`, `RunPollingLoopAsync()` — 2Hz background loop
  - `SelectAllPids()`, `SelectNoPids()`
  - Item PropertyChanged subscription (notifies SelectedLivePids, GaugePids, HasSelectedPids on selection change)
  - `StopPolling()` called on Disconnect and when switching away from Live Data tab

---

## Group L — UI views

- [x] **L1** No OxyPlot — using custom Canvas+Polyline waveform (Avalonia 12.0.4 compat uncertain)
- [x] **L2** `Controls/GaugeControl.axaml[.cs]` — arc gauge with StyledProperty Value/Min/Max/Label/Unit, PathGeometry arc calculation, green/yellow/red color by percentage
- [x] **L3** `Views/LiveDataView.axaml[.cs]` — 220px left PID list + right sub-tab panel, Start/Stop polling buttons
- [x] **L4** `Views/LiveDataTablePane.axaml[.cs]` — scrollable table: PID name / value / unit, 44px touch targets
- [x] **L5** `Views/LiveDataGaugePane.axaml[.cs]` — WrapPanel of up to 6 GaugeControls (first 6 selected PIDs)
- [x] **L6** `Views/LiveDataWaveformPane.axaml[.cs]` — 4 stacked Canvas slots (95px each), 250ms DispatcherTimer, selection-order fill (PIDs 1-4=primary, 5-8=secondary), 60s rolling window
- [x] **L7** `SharedStyles.axaml` — `Button.live-pid-row` style (44px, full-width, hover/press states)
- [x] **L8** `Views/ScanToolView.axaml[.cs]` — "Live Data" tab button + `<views:LiveDataView>` wired

---

## Group M — Simulator detection

- [x] **M1** Add `bool IsSimulated { get; }` and `Task<string?> ReadEcuNameAsync(CancellationToken ct)` to `IObd2Scanner`
- [x] **M2** Add `ParseEcuName(byte[] responseData)` static helper to `Obd2Protocol`
  - Layout: `[0x49, 0x0A, 0x01, name[20 bytes ASCII, space-padded]]`
  - Trims null chars and whitespace; returns null if service/PID byte mismatch
- [x] **M3** Implement `ReadEcuNameAsync` in `Obd2Scanner` — Service $09 PID $0A via existing `SendAndReceive` path
- [x] **M4** Update `ConnectAsync` to call `ReadEcuNameAsync` after VIN; set `IsSimulated = true` when ECU name starts with "NEOMOTIVE" (case-insensitive)
  - Simulator already returns `EcuName = "NEOMOTIVE_PCM"` — no changes needed to simulator side
- [x] **M5** Add 9 new tests: 5 `ParseEcuName` protocol tests in `VinParsingTests`, 4 scanner integration tests in `Obd2ScannerTests` (ECU name multi-frame, timeout→null, `IsSimulated=true`, `IsSimulated=false`)
  - 37/37 tests pass

---

## Group N — VIN decode on Vehicle tab

- [x] **N1** Add `Neomotive.Vin` project reference to `Neomotive.ScanTool.UIShared.csproj`
- [x] **N2** Add `IVinDecoder?` optional param to `MainWindowViewModel`; store `_vinDecoder` field
- [x] **N3** Add `VinDecode`, `IsSimulated` properties to VM with `HasVinDecode`, `DisplayVinMake`, `DisplayVinModel`, `DisplayVinYear`, `DisplayVinCountry` computed string props
- [x] **N4** In `RefreshVinAsync`, call `_vinDecoder.DecodeAsync(vin, ct)` after reading VIN; post both Vin + VinDecode to UI thread together
  - Uses `DecodeAsync` for NHTSA fallback on VINs not in local catalog (e.g. US-built Hondas with 1HG WMI)
- [x] **N5** Add Make/Model/Year row + Country row to `VehicleView.axaml` below VIN; show only when `HasVinDecode` (valid VIN + successful decode)
- [x] **N6** Add SIMULATOR badge (green border, ok-styled text) below decode panel; visible only when `IsSimulated`
- [x] **N7** Wire `VinDecoder` in `App.axaml.cs` via direct construction (all classes are public, no DI container needed)

---

## Group O — OBD2 concurrent-request fix

- [x] **O1** Change `RefreshAllAsync` from `Task.WhenAll` to sequential `await`s — OBD2 is request-response; concurrent requests cause intermittent dropped responses
- [x] **O2** Split `RefreshVinAsync` into two UI posts: post `Vin` immediately after `ReadVinAsync`, then post `VinDecode` separately after `DecodeAsync` — prevents NHTSA latency (up to 10 s) from delaying the VIN display itself

---

## Group P — Update mechanism

- [x] **P1** Create `Neomotive.Update` shared library (`software/dotnet/Apps/Shared/Neomotive.Update/`)
  - `UpdateManifest`, `UpdateFileEntry`, `UpdateState`, `UpdateResult` models
  - `UpdatePackage` — zip extraction + SHA256 verification; cleans staging on failure
  - `UpdateApplicator` — A/B slot swap (`app-current/`, `app-previous/`, `app-staging/`); platform-aware restart behavior
  - `IUpdateSource` interface
  - `UsbUpdateSource` — polls removable drives (Windows) or `/media/` (Linux) every 5 s
  - `NetworkUpdateSource` — HTTP GET version manifest; downloads + hash-verifies zip
  - `UpdateService` — top-level orchestrator; USB watcher timer; events: `UpdateFound`, `UpdateApplied`, `UpdateFailed`

- [x] **P2** `Neomotive.Vin` catalog file override
  - Add `ExternalCatalogPath` to `VinOptions`
  - `ManufacturerProvider` and `ModelCatalogProvider` check filesystem path before embedded resource
  - `ServiceCollectionExtensions` passes `VinOptions` into provider constructors

- [x] **P3** ScanTool integration
  - `<Version>1.0.0</Version>` in `Neomotive.ScanTool.Desktop.csproj`
  - `AppConfig` + `neomotive.config.json` reader in `App.axaml.cs`
  - `UpdateService` wired in `App.axaml.cs`; `ExternalCatalogPath` set from base dir
  - `ScanView.Updates` enum value; `IsUpdatesView`, `ShowUpdates()`, `CheckForUpdatesAsync()`; update event handlers
  - `UpdatesView.axaml` + `UpdatesView.axaml.cs` — status box + "Check for Updates" button
  - Tab button wired in `ScanToolView.axaml` + `ScanToolView.axaml.cs`

- [x] **P4** Simulator integration (parallel to P3)
  - `<Version>1.0.0</Version>` in Desktop + RaspberryPi `.csproj`
  - `AppConfig` + `neomotive.config.json` reader in `App.axaml.cs`
  - `UpdateService` wired in `App.axaml.cs`
  - Update properties (`UpdateStatus`, `CanCheckUpdate`, `CheckForUpdatesAsync()`) on `MainWindowViewModel`
  - Updates section added to bottom of `ConfigView.axaml`; `OnCheckForUpdates` handler in `ConfigView.axaml.cs`

- [x] **P5** Pi deployment updated for A/B layout
  - `~/.xinitrc` now execs `/opt/neomotive/app-current/simulator` (not `./simulator`)
  - `setup-autostart.sh` creates `app-current/` and `config/` directories
  - `deployment.md` updated with new directory layout and deploy instructions

- [x] **P6** `create-update-package.ps1` build script (`software/dotnet/scripts/`)
  - Params: `-Target`, `-Platform`, `-Version`, `-OutputDir`
  - `dotnet publish` self-contained → hashes all files → writes `update.json` → zips
  - Updates `version-manifest.json` (served by update server) with version + URL + zip SHA256

---

## Group Q — Desktop scaling for recording

- [x] **Q1** Wrap `ScanToolView` in `<Viewbox Stretch="Uniform">` in `MainWindow.axaml`; set `Width="800" Height="480"` on `ScanToolView` so Viewbox has a natural size to scale from
- [x] **Q2** Add platform detection in `MainWindow.axaml.cs`: on Windows set `CanResize=true`, default `1024×614` (maintains 5:3 ratio), `MinWidth=400 MinHeight=240`; other desktop hosts use AXAML defaults unchanged

---

## Group R — Raspberry Pi (Phase 2)

Target device: Pi 4 on a [Pi Appliance Kit](../../../../ctacke/Pi-Appliance-Kit) image
(Raspberry Pi OS Lite arm64, read-only overlay rootfs, `/data` writable, single `app.service`).
Runbook: `ScanTool/scripts/pi/README.md`.

- [x] **R1** Create `Neomotive.ScanTool.RaspberryPi` (`net10.0`, `AssemblyName=scantool`); project refs
  `Meadow.Linux`, `Meadow.Avalonia`, `ICs.CAN.Mcp2515`, `Meadow.Logging.LogProviders`, `UIShared`;
  added to `neomotive scantool.slnx` (along with `Meadow.Linux` + `ICs.CAN.Mcp2515` under `_refs/`)
- [x] **R2** `WaveshareDualCanHat.cs` — dual MCP2515 on SPI0 (CS pin24/pin26, INT pin16/pin22,
  500 kbps). CAN0 registered as `ICanBus`, wrapped in `LoggingCanBus`; `NullCanBus` fallback
- [x] **R3** `App.axaml.cs` — `AvaloniaMeadowApplication<Meadow.RaspberryPi>`; same scanner /
  `VinDecoder` / VM construction as Desktop, minus `UpdateService`. `UdpLogger` instead of
  `DebugLogProvider` (journald is volatile on this image)
- [x] **R4** DRM/KMS rendering — `Program.cs` uses `.UseSkia().StartLinuxDrm(...)`, no X server.
  Single-view lifetime, so `App` sets `MainView` to an 800×480 `ScanToolView` in a `Viewbox`
  (`IClassicDesktopStyleApplicationLifetime` branch retained for dev-box layout checks).
  `SCANTOOL_DRM_CARD` / `SCANTOOL_DRM_SCALING` env overrides
- [x] **R5** `scripts/pi/run` — appliance entrypoint. Redirects `HOME`,
  `DOTNET_BUNDLE_EXTRACT_BASE_DIR`, `XDG_RUNTIME_DIR` under `/data/app` (read-only root +
  `ProtectHome=yes`); sets `DOTNET_EnableWriteXorExecute=0`; chmods the binary before exec
- [x] **R6** `scripts/publish-scantool-pi.ps1` — force-rebuilds wilderness deps, publishes
  self-contained single-file `linux-arm64`, stages `run` (LF-normalized) + config, `-Deploy`
  shells out to the kit's `install-app.sh`
- [x] **R7** Pi-Appliance-Kit `config/optimizations.yaml` — added `libgl1-mesa-dri`, `libegl1`,
  `libgles2`, `libinput10`, `libfontconfig1`; `hardware_overlays` now `dtoverlay=spi0-0cs`
  (userspace CS for MCP2515) + `dtoverlay=vc4-kms-v3d` (creates `/dev/dri/card*`)
- [x] **R8** Verify solution builds and Core tests still pass (37/37 green)
- [ ] **R9** Hardware bring-up — **blocked, no device reachable** (`pi-appliance` does not
  resolve). Needs: apply kit changes + reboot; confirm `/dev/dri/card*` and `/dev/spidev0.0`;
  deploy; confirm the UI fills 800×480 and `app.service` stays up
- [ ] **R10** CAN end-to-end against ModuleSimulator — VIN `AWWWWWWWWWWW0YEAH`, the five known
  DTCs, readiness monitors, CAN log pane traffic

**Known gotchas (learned during R6):**
- Plain `bash` on Windows resolves to WSL, which cannot see `F:\` paths — the publish script
  resolves git-bash by explicit path instead
- git-bash on NTFS infers the exec bit from content (`#!` → 755, ELF → 644) and `chmod` is a
  silent no-op, so the `scantool` binary cannot be made executable from Windows. `install-app.sh`
  only chmods `run`, which is why `run` chmods the binary itself

## Shared-code extraction (2026-08-05)

- [x] **S1** `Shared/Neomotive.Can.Hardware` — `WaveshareDualCanHat` deduplicated out of
  `ScanTool.RaspberryPi` and `ModuleSimulator.RaspberryPi` (the two copies had drifted:
  ScanTool carried the pin-mapping warning comment, the simulator carried a stale
  "boot.ini" message). Both Pi heads now reference the shared project.
- [x] **S2** `Shared/Neomotive.Can.UI` — `CanView` + `CanLogItem` + new `ICanViewModel`
  interface. The view binds to the interface, so both apps' `MainWindowViewModel`s implement
  it. Shared view is the richer ScanTool variant, so the simulator's CAN tab gains the
  "Log CAN Frames" toggle (`IsLoggingEnabled` defaults to `true` there, preserving today's
  always-logging behavior).
- [x] **S3** Both solutions build clean; ScanTool Core tests 37/37 green.

**Not moved (still duplicated, candidates for a later pass):** `CanPacketLog` /
`CanPacketEntry` / `LoggingCanBus` exist in both `*.Core` projects and are near-identical;
`DescribePacket` differs meaningfully between the two apps, so the log *formatting* stays
app-side.

## Group T — Event capture (Phase 3)

Driven by a Nissan Titan XD (Cummins 5.0) with an emissions delete: extremely hard to start,
dies before it can set a DTC. The event lasts 2-5 s, so the 2 Hz live-data loop describes a
crank in about four samples. Capture needs its own high-rate path.

- [x] **T1** `Core/Capture` model types — `CaptureSignal` (decoupled from `PidDescriptor` so a
  Mode 22 / UDS channel can be described identically), `CaptureSample` (interned signal index,
  per-sample timestamp), `CaptureEvent`, `CaptureState`.
- [x] **T2** `RollingBuffer<T>` — fixed-capacity ring holding pre-trigger history so arming
  before key-on still captures the prime phase.
- [x] **T3** `CaptureTrigger` — `ThresholdTrigger` (with dwell, to reject the starter-engagement
  noise spike), `ManualTrigger`, `BusWakeTrigger`, `AnyTrigger`. `AnyTrigger` deliberately does
  not short-circuit: a dwell trigger must see every sample to accumulate its window.
- [x] **T4** `StallDetector` — requires the signal to rise above the floor before it can report a
  stall, otherwise a capture armed during cranking truncates itself immediately.
- [x] **T5** `CaptureSession` — Idle/Buffering/Recording/Stopped state machine, pre-trigger flush
  in chronological order, stall and max-duration stops, bus-loss markers that do not end the run.
  Clock-free: callers stamp samples, which keeps it testable and ties timestamps to when the OBD
  response actually arrived.
- [x] **T6** `CapturePollLoop` — polls only the armed signals back to back with no inter-sweep
  delay. The scanner's 3 s `ResponseTimeout` is bypassed with caller-side cancellation
  (~150 ms) rather than editing `Obd2Scanner`, so one unanswered request cannot blow a
  three-second hole in the trace. Retries `ConnectAsync` while arming to survive a dark ECU, and
  tolerates mid-capture bus loss from crank brownout.
- [x] **T7** Storage — long-format CSV (`timestamp_ms,signal,value`, one row per real sample) plus
  a JSON sidecar carrying VIN/CALID/CVN, signal definitions, trigger config, measured sample rate
  and event markers. Long format is deliberate: OBD polling is round-robin, so a wide grid would
  force forward-filling and fabricate the very rise-rate timing this feature exists to measure.
  `CaptureReader` tolerates a missing sidecar — a capture cut short by a flat battery is exactly
  when the data matters most.
- [x] **T8** `CaptureExporter.WriteWideCsv` — derived, forward-filled wide export for spreadsheets.
  Explicitly not the source of truth.
- [x] **T9** Diesel channels added to `PidRegistry`: `FuelRailGaugePressure` (0x23, 10 kPa/bit),
  `FuelRailPressureRelativeToManifold` (0x22), `ControlModuleVoltage` (0x42),
  `EngineOilTemperature` (0x5C). The existing `FuelPressure` (0x0A) is the low-side supply and
  caps at 765 kPa — nowhere near common-rail pressures.
- [x] **T10** 72 unit tests across buffer, triggers, stall, session, storage round-trip and poll
  loop. Core suite 109/109 green.

**Remaining:**
- [ ] **T11** `CaptureView` + `CaptureReviewPane` in `UIShared` — arm/trigger config, and a
  strip-chart review with cursor readout, jump-to-trigger and zoom. New tab in `ScanToolView`.
  Pi gets the same panes constrained to 800x480; desktop adds multi-capture overlay (a good
  crank against a bad one is the most diagnostic view available), derived channels (dP/dt,
  commanded-actual) and PNG export.
- [ ] **T12** Wire captures to the `data/` directory, which `App.axaml.cs` already creates but
  nothing writes to. On the Pi only `/data` is writable.
- [ ] **T13** Tune detection: `ReadCalibrationIdAsync` / `ReadCvnAsync` (`VehicleInfoPid` already
  defines 0x04 and 0x06 but `IObd2Scanner` exposes no method), supported-PID bitmap reads, and a
  UI that distinguishes "not supported" from "not complete" in readiness. Touches
  `IObd2Scanner`/`Obd2Scanner` — coordinate with the UDS work.
- [ ] **T14** Mode 22 channels for commanded rail pressure and FCA/MPROP duty. The UDS work has
  already landed `ReadDataByIdentifier` in `Core/Uds`, so this is now mostly a config-driven
  signal-definition file rather than new protocol.
- [x] **T15** `ModuleSimulator` start-attempt scenarios — see Group U.

## Group U — Simulator start-attempt scenarios (bench fixture for capture)

- [x] **U1** `StartScenario` in `ModuleSimulator.Core` — five profiles, each one branch of the
  hard-start diagnostic: `HealthyStart` (reaches injection pressure, catches, idles),
  `WeakLiftPump` (rail asymptotes below threshold, never fires), `RailCollapse` (catches then
  loses pressure and dies — the Titan's reported symptom), `WeakBattery` (voltage sag as the
  primary fault, low rail downstream of it), `NoCrank`.
- [x] **U2** Evaluated as a pure function of elapsed time rather than ticked. PID handlers are
  pulled on demand, so the profile stays smooth at any poll rate, needs no timer, and is
  deterministic under an injected clock. `t=0` is key-on and cranking starts at 0.5 s, so a
  capture armed before key-on has a genuine quiet prime phase in its pre-trigger buffer.
- [x] **U3** `SimulatorState` gains `FuelRailPressureKpa` / `ControlModuleVolts` plus
  `Current*` accessors — a running scenario overrides the manual values, stopping restores them.
- [x] **U4** `SimulatorPcm` overrides `RegisterPids()` to add `FuelRailGaugePressure` (0x23,
  10 kPa/bit) and `ControlModuleVoltage` (0x42, 1 mV/bit). No J1979 library change was needed:
  `RegisterPids` is virtual and `SupportedPids` derives from the handler dictionary, so the new
  channels appear in the supported-PID bitmap the way a real diesel PCM would report them.
- [x] **U5** Start-attempt buttons on the simulator's Data tab; rail pressure, module voltage and
  a live `t+Ns` status added to the data readout, refreshed on the existing 250 ms tick while a
  scenario runs.
- [x] **U6** New `Neomotive.ModuleSimulator.Core.Tests` project (24 tests, registered in the
  simulator slnx — the simulator had no test project before). Tests assert the *diagnostic
  character* of each profile, not exact curve values: that healthy genuinely crosses the
  injection threshold and weak-pump genuinely does not, that rail collapse loses pressure
  *before* RPM falls, and that weak battery sags below the cranking limit.

**Still requires hardware:** running a real ScanTool capture against the simulator over CAN
(PCAN on desktop, MCP2515 on Pi) to confirm the achieved sample rate clears ~10 Hz for five
signals. The fixture is ready; the bench run is not automated.

**Gotcha:** concurrent `dotnet build` runs against these projects produce spurious
`NuGet.targets(782,5): Value cannot be null. (Parameter 'path1')` restore errors on unrelated
projects. Build with `-m:1` when another agent or IDE may be building the same tree.
