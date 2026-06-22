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
