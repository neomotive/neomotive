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
- [x] **T11** Capture UI — `CaptureViewModel` + `CapturePidItem` + `CaptureView` +
  `CaptureReviewPane`, on a new "Capture" tab in `ScanToolView`. Kept out of
  `MainWindowViewModel` (already ~1100 lines) and exposed as `CaptureVm`, mirroring the
  simulator's `InputsVm` pattern.
  - **Diesel hard-start preset** button configures the whole thing in one press: the five
    signals worth watching, trigger on RPM > 150 with 200 ms dwell, 5 s pre-trigger, stall stop.
  - Review pane draws one lane per signal on a shared trigger-relative axis, with cursor
    readout, trigger marker, zoom, pan and jump-to-trigger.
  - **Lanes autoscale to the data range, not the PID's declared range.** Rail pressure declares
    0-655,350 kPa so it can express any common-rail system; drawn against that, a real
    35,000 kPa trace is a flat line on the bottom and the rise rate — the entire point of the
    capture — is invisible.
  - `ShowCapture()` calls `StopPolling()` first: the 2 Hz live-data loop and the capture loop
    must not contend for the bus.
  - Manual trigger stays live alongside the threshold via `AnyTrigger`, so the operator can
    always force a start.
- [x] **T12** Captures write to the `data/` directory both heads already create.
  `MainWindowViewModel.DataDirectory` is set from each `App.axaml.cs` object initializer
  (matching the existing `AdapterHint`/`CanChannelName` idiom) rather than a constructor
  parameter, which avoids disturbing the UDS work's `udsScanner` argument. On the Pi, `baseDir`
  is `/data/app`, so captures land in the only writable location on the device.
  VIN is stamped into each capture's sidecar as it is read.
- [x] **T13** Tune detection — `Obd2Protocol.ParseCalibrationId` / `ParseCvn` /
  `ParseSupportedPids` / `SupportsNextPidRange`, exposed as `ReadCalibrationIdAsync`,
  `ReadCvnAsync` and `ReadSupportedPidsAsync` on `IObd2Scanner`. Analysis lives in
  `TuneAnalysis.cs` (`TuneFingerprint` / `TuneAnalyzer` / `TuneAssessment`), pure and testable.
  UI is a "Check tune" section on the Vehicle tab; it works with the engine off, which is the
  point on a truck that will not stay running.
  - The supported-PID walk stops at the first range whose "next range" bit is clear rather than
    querying all eight, so unsupported ranges do not each cost a 3 s timeout.
  - `ReadinessMonitor` already distinguished `IsNotSupported` from `IsIncomplete`; the analyzer
    reports them separately because that distinction is the actual tell.
  - **Aftertreatment PIDs are matched by name family** (Dpf/DieselParticulate/Scr/Nox/
    ParticulateMatter) rather than a hand-listed set, so the J1979 enum stays the source of truth.
    **EGR is deliberately excluded** — petrol engines have it too, so its presence proves nothing
    about a DPF/SCR delete.
  - The analyzer reports *evidence, not a verdict*. Without a known-good baseline a CALID and CVN
    cannot prove a reflash; what the tool can settle alone is whether the calibration still
    expects hardware that has been removed.
- [x] **T14** Mode $22 channels — `Mode22SignalDefinition` (DID, byte offset/length, signedness,
  scale, offset, tx/rx) plus `Mode22SignalFile` loading `config/mode22-signals.json`. Required a
  **channel abstraction**: `ICaptureChannelSource` with `PidChannelSource` and
  `Mode22ChannelSource`, so Mode $01 and Mode $22 signals are captured identically and land on
  one timeline — which is what makes commanded-vs-actual rail pressure readable.
  `CapturePidBinding` was replaced by `CaptureChannel` + `CaptureChannelSetBuilder` (the builder
  keeps signal indices dense, which the allocation-free ring buffer depends on).
  - No J1979 library change was needed: the UDS work's `ReadDidAsync` supplies the transport.
  - **The shipped template DIDs are placeholders marked UNVERIFIED.** Mode $22 identifiers are
    manufacturer-specific and unpublished; they must be confirmed against the actual ECU before
    any reading is trusted. Nothing was invented for the Titan.
  - Consequence worth remembering: a channel owns its scanner, so channels must be built from
    the same scanner instance the poll loop runs against.
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

**Gotcha:** a `TuneAssessment` property whose type is also named `TuneAssessment` compiles, but
avoid copying that pattern.

**Gotcha:** a running `Neomotive.ScanTool.Desktop` (or Visual Studio) locks the output DLLs and
the Desktop project fails with MSB3021/MSB3027 copy errors. The XAML and C# have already compiled
at that point — close the app and rebuild.

**Gotcha:** setting `DataContext` on a view also rebinds its `IsVisible`, so the capture tab is
wrapped in a `<Panel IsVisible="...">` that still sees `MainWindowViewModel`.

**Gotcha:** concurrent `dotnet build` runs against these projects produce spurious
`NuGet.targets(782,5): Value cannot be null. (Parameter 'path1')` restore errors on unrelated
projects. Build with `-m:1` when another agent or IDE may be building the same tree.

## Group V — Generalising the capture feature (2026-09-07)

Correction to Group T's framing: the diesel hard-start case was only the first example. The tool
has to diagnose **any** problem, so canned setups are acceptable only as selectable data, never as
built-in behaviour.

- [x] **V1** `DiagnosticProfile` (`Core/Diagnostics/`) — a named, categorised capture setup:
  signals, trigger (manual / threshold / bus-wake), pre-trigger window, max duration and stop
  condition. Profiles are **data, not code**, loaded from `config/diagnostic-profiles.json`.
- [x] **V2** `DiagnosticProfileLibrary` — 14 built-in profiles across General, Starting &
  Charging, Fuel & Air, Drivability, Emissions, Transmission and Thermal. Chosen to cover
  diagnostic *shapes* rather than to be complete: a one-shot event (cranking), something caught
  while driving (misfire, shift quality), and a slow drift (overheating). The old diesel preset is
  now just one entry, `hard-start-common-rail`.
- [x] **V3** `DiagnosticProfileFile` — load/save, defaults written on first run so the format is
  discoverable and shipped profiles can be edited. `MergeNewDefaults` adds profiles from a later
  version **without discarding user edits or user-authored profiles**. A corrupt or unreadable
  file falls back to the built-in library: a bad edit must never leave an empty picker.
- [x] **V4** UI is a category dropdown then a profile dropdown, with the profile's description
  shown before applying. Applying only *fills in* the settings — every field stays editable,
  because no library can anticipate every job.
- [x] **V5** `StallDetector` renamed `ActivityStopCondition` and its language generalised. The
  behaviour was always generic ("signal was active, then went quiet"); the name tied it to engine
  stall. Applies equally to road speed returning to zero, a pump cycling off, or current draw
  ending. `CaptureEventKind.Stalled` became `StopConditionMet`.
- [x] **V6** `PidRegistry` widened with bank-2 trims, EGR command/error, catalyst temperatures,
  absolute load, relative/commanded throttle, ambient air, injection timing and fuel rate — the
  registry is the menu every profile draws from, so profiles were only as good as it was.
- [x] **V7** `TuneAnalyzer` re-scoped in docs as *one specific analysis*, not the tool's general
  mechanism, and its summary no longer assumes a hard-start complaint.
- [x] **V8** 16 profile tests. Beyond round-tripping, they assert the library keeps spanning
  several categories, that every built-in signal exists in `PidRegistry`, and that trigger and
  stop conditions reference signals the profile actually captures — a profile whose trigger names
  an uncaptured signal would arm and never fire.

**Gotcha:** `DiagnosticProfile` is a record with an `IReadOnlyList` member, so record equality
falls back to reference equality for `Signals` — never assert `Assert.Equal(profile, loaded)`
across a round trip.

**Design rule going forward:** anything scenario-specific belongs in a profile or config file, not
in `CaptureViewModel`. The capture engine, poll loop, storage and review pane are domain-agnostic
and must stay that way.

## Group W — Loadable signal table and modal UX (2026-09-07)

- [x] **W1** `SignalDefinition` (`Core/Signals/`) — one shape for Mode $01 and Mode $22, differing
  only in where the bytes come from. Addressed by **numeric** PID/DID rather than an enum name, so
  the table can describe signals the J1979 enum never named.
  - **`ByteOffset`/`ByteLength`/`Signed` were the unlock.** The old decoder read only 1-2 unsigned
    bytes at a fixed offset, which cannot express the multi-value PIDs ($14-$1B pack O2 voltage and
    fuel trim into one response; $24-$2B and $34-$3B pack two 16-bit values). Those are now simply
    two definitions sharing an address.
  - `Mode22SignalDefinition` was folded into this and deleted.
- [x] **W2** `IObd2Scanner.ReadPidDataAsync(byte pid)` returns raw data bytes with the service and
  PID echo stripped, leaving decoding to the signal definition. `ReadPidAsync(Pid)` stays for the
  legacy path.
- [x] **W3** `SignalLibrary` — ~110 built-in Mode $01 signals across Engine, Fuel, Air & Boost,
  Emissions, Thermal, Electrical, Vehicle and Diagnostics, written to `config/pid-table.json` on
  first run.
  - **Scaling was taken from the SAE definitions; PIDs whose scaling could not be stated with
    confidence were omitted rather than guessed.** A wrong scale factor is worse than a missing
    signal because it yields a plausible number that quietly misleads. Deliberately excluded:
    status bitmaps/enums ($01, $03, $12, $13, $1C-$1E, $41, $51, $5F) and composite PIDs packing
    3+ sensors ($64, $67, $68, $6B, $6C, $70, $73, $77, $78-$7C, $83, $86). The file format can
    express them once there is a presentation for them.
- [x] **W4** `SignalTable` — merges `pid-table.json` with `mode22-signals.json`, so a Mode $22
  channel is a first-class signal everywhere (search, filter, live, capture, triggers, profiles).
  A Mode $22 entry may deliberately override a standard key. Corrupt files fall back to built-ins.
- [x] **W5** `SignalPickerViewModel` + `SignalPickerView` — **search-first, not a tree.** Search
  matches name, key, unit, tags and the PID number (with or without `0x`, since people read it out
  of a service manual). System filters are **multi-select chips, not tree nodes**, because a signal
  legitimately belongs to several systems — rail pressure is both Fuel and Engine — and a strict
  tree would force it into one branch. Chips also avoid expander hit targets on the 800x480 panel.
- [x] **W6** `TriggerEditorViewModel` + `TriggerEditorView` — modal replacing the cramped sidebar.
  **Polls the chosen signal live while you set the threshold**, warns when the condition is already
  true (which would fire the instant it arms), offers "Use" to take the current reading, and shows
  a plain-language summary of what will happen. Numeric keypad because text entry on the Pi's touch
  panel is painful.
- [x] **W7** Live Data and Capture now share one picker and one table, so a signal means the same
  thing in both. `LivePidItem.Descriptor` became a `SignalDefinition` — the existing
  `Descriptor.Name/Unit/Min/Max` bindings kept working unchanged. Live polling switched to
  `ReadPidDataAsync` + `Decode` so multi-value PIDs read correctly.
- [x] **W8** 27 signal tests: scaling spot-checks against known SAE values, 4-byte and signed
  decoding, multi-value PIDs sharing an address, search by name/tag/PID number, multi-system
  membership, and file load/save/fallback.

**Modal hosting:** overlays inside the view, **not** `Window` dialogs. The Pi runs a single-view
DRM lifetime with no window manager, so a real dialog has nowhere to go.

**Bug found and fixed:** `CaptureVm` was being constructed before `_udsScanner` was assigned, so it
always received null and Mode $22 channels could never have worked.

## Group X — Tab bar and profile-application fixes (2026-09-07)

- [x] **X1** Connection tab replaced with a drawn CAN-link icon whose colour carries the
  connection state (green / amber / dim). The separate "No vehicle" indicator on the right was
  removed as redundant, reclaiming ~210 px of tab bar. `StatusText` moved to the tooltip.
- [x] **X2** Icon uses a `StreamGeometry`, not a font glyph — the Pi runs DRM/KMS with a minimal
  font set, where an emoji or symbol character can render as tofu.
- [x] **X3** Selecting a diagnostic profile now applies it. Previously only the description
  changed until "Apply" was pressed, so the profile looked inert: no signals were selected while
  a trigger from the *defaults* (`EngineRpm above 150 for 200 ms`) stayed on screen, appearing to
  belong to the profile. `ApplyProfile` guards re-entry with `_applyingProfile` since it assigns
  `SelectedProfile` itself. The button became "Reset" (re-apply, discarding hand edits).
- [x] **X4** `EnsureTriggerSignalSelected()` adds a threshold trigger's own signal to the recorded
  set. The capture loop polls only selected signals, so a trigger on an unselected signal could
  never fire — and a capture missing the signal it triggered on cannot be read afterwards.
- [x] **X5** Two library invariants now tested: every built-in profile names signals the table
  defines, and every threshold profile records its own trigger signal. Both passed on the existing
  library, confirming the fault was purely the missing Apply press, not profile/table drift.
- [x] **X6** TextBox styling added (none existed app-wide): Fluent's dark theme inverts a focused
  TextBox to near-white. Overrode the `TextControl*` theme resources so focus reads as a green
  border instead. Affected every text field, not just the UDS DID entry.
- [x] **X7** UDS Quick DID row rebuilt as a `WrapPanel`. It had no elastic member, so exceeding the
  container clipped the last button outright; DID numbers moved from labels to tooltips, freeing
  ~150 px. Needed for 800x480, where the row cannot fit on one line at any label length.
- [x] **X8** Three buttons on the Capture screen were all labelled "Trigger". Now: "Trigger…"
  (configure), "Fire now" (manual fire while armed), "Go to t=0" (playback navigation — centres the
  loaded trace on the trigger instant).

**Layout gotcha:** a row of fixed-width controls with no `*` column cannot degrade — it clips. Any
such row needs either a star spacer or a WrapPanel before it reaches the Pi's 800x480.

---

## Group Y — UDS moved to shared libraries, simulator gained a UDS server

The UDS client had no bench target: nothing on the CAN bus answered ISO 14229, so the UDS view could
only be exercised against a real vehicle. Three moves fixed that.

- [x] **Y1** New Meadow library `Telematics.Uds`
  (`wilderness/Meadow.Foundation/.../Telematics.Uds/Driver/`, assembly `Uds`, namespace
  `Meadow.Foundation.Telematics.Uds`). References `Telematics.J1979` so `IsoTp`, `IsoTpFrameType`
  and `Obd2Addresses` are reused rather than duplicated.
- [x] **Y2** The generic client moved there out of `ScanTool.Core`: `UdsProtocol`, `UdsScanner`,
  `IUdsScanner`, `UdsDtc`, `UdsModuleInfo`, `UdsDidValue` and the five enums. Names unchanged, so
  the call sites needed only a `using`. `Models/Uds/` and `Uds/` are gone from ScanTool.
- [x] **Y3** Descriptions became injectable. `UdsProtocol` no longer holds tables or references
  `Neomotive.Obd2`; it takes an optional `IUdsDescriptionProvider` (DID name and formatting, DTC
  text, fault-type and NRC descriptions). `NullUdsDescriptions` is the tables-free fallback —
  printable ASCII or hex, inferred from the bytes.
- [x] **Y4** New server side: `UdsServer` (services $10, $14, $19/$01/$02/$0A, $22, $3E; NRC $11
  for anything else), `IUdsDataSource`, `UdsDtcRecord`, `UdsSessionState` (S3 timeout),
  `IsoTpResponder`. A functionally addressed request that would be rejected gets silence, per ISO
  14229-1 §7.5 — otherwise a `0x7DF` sweep collects refusals as if they were modules.
- [x] **Y5** New shared library `Apps/Shared/Neomotive.Uds` — the UDS **database**. `UdsCatalog`
  implements `IUdsDescriptionProvider` over three layers: an embedded seed
  (`Data/uds-catalog.default.json`, transcribed from the old switch tables), then every
  `uds-catalog*.json` in the data directory in filename order, then runtime `Upsert`/`Remove`.
  `Import` (JSON or `did,name` CSV), `Export` (whole table or overrides only), `Save` and `Reload`
  mean a new DID never needs a rebuild. A malformed overlay is reported in `LoadErrors` and skipped;
  the rest still load.
- [x] **Y6** ScanTool passes `UdsCatalog.Shared` to `UdsScanner`, and `ConfigDirectory` now also
  points the catalog at its overlay files. `UdsView.axaml` binds `UdsModuleInfo`/`UdsDtc` from the
  `Uds` assembly.
- [x] **Y7** Simulator gained `Uds/` in `ModuleSimulator.Core`: `UdsConfig`/`UdsModuleProfile`
  (config-driven modules, DIDs and static DTCs), `SimulatorUdsDataSource` (mirrors the PCM/TCU DTC
  stores so the toolbox fault buttons drive OBD-II and UDS alike; serves the VIN live) and
  `UdsModuleHost`. Default config: PCM `0x7E8`, TCU `0x7E9`, plus UDS-only BCM `0x7EA`, ABS `0x7EC`
  and SRS `0x7ED`. `SimulatorConfig.Uds` persists it in `neoteric.config.json`.
- [x] **Y8** A UDS clear calls back into the VM, which re-syncs the J1979 modules — otherwise the
  OBD-II side would keep reporting faults the tester just cleared.
- [x] **Y9** Tests: 24 in `Uds.Unit.Tests` (server byte-for-byte, plus client↔server loopback over
  an in-memory bus covering multi-frame and flow control), 35 in `Neomotive.Uds.Tests` (the 20
  moved protocol/scanner tests pass unchanged — the parity proof — plus catalog layering,
  import/export and malformed-file handling), 52 in the simulator tests including host-level
  discovery against the real `UdsScanner`.

**Bug found and fixed:** `IsoTpResponder` (and the `ControllerBase.SendIsoTpResponse` it was modelled
on) subscribed to flow control *after* transmitting the first frame. A tester that replies
immediately beats the subscription and the message strands until the 1 s timeout. Real CAN hides it
because delivery is asynchronous; a loopback bus does not. Subscribe first, then transmit.

**Known, not fixed:** `IsoTp.Encode` numbers the first consecutive frame `0x20` instead of `0x21`.
Our client ignores sequence numbers so the bench passes, but a third-party scan tool would reject
the frame.
