# Proposal: one Faults page, protocol-agnostic

## Problem

`DtcsView` (OBD-II mode $03/$07) and `UdsView` (UDS $19) both answer "what is wrong
with this vehicle." A tech does not choose a protocol; they ask that one question.
Today they must visit two tabs, and neither tab tells them the other exists.

The split also hides overlap: the OBD-II powertrain codes a vehicle reports on $03
are usually the same faults the ECM reports on $19, seen through a narrower window.

## Shape

Replace the `DTCs` and `UDS` tabs with a single **Faults** tab. Keep the UDS
two-column layout as the skeleton — it already scales to many modules and fits
800x480 — and make the module list protocol-agnostic.

### Left column: modules

One list, every module, whatever answered:

- Legislated OBD-II modules discovered on $03/$07 (the ECM, sometimes TCM)
- UDS modules discovered by probe or full scan
- Remembered-not-yet-confirmed modules (unchanged from UdsView)

Each row keeps its DTC-count badge. Protocol is shown as a small dim tag on the
address line (`7E0 · OBD-II`, `760 · UDS 11-bit`), not as a grouping: it is an
attribute of the module, not a category the tech navigates by. When one physical
module answers on both, merge to one row and tag it `OBD-II + UDS`.

The list is headed by a vehicle-level summary strip that used to be DtcsView's
top block — MIL state, total code count, "new since last visit" — because those
are whole-vehicle facts and belong above any module selection.

### Right column: selected module

Unchanged in structure from UdsView: identification DIDs, DTC list, DID inspector.
Show the DID panels only for modules that speak UDS; for an OBD-II-only module the
row simply is not there rather than showing empty fields.

DTC rows carry the richer UDS presentation (code, description, status badges).
OBD-II codes map cleanly onto it: stored → `CONFIRMED`, pending → `PENDING`,
MIL bit → `MIL`. The STORED/PENDING section headings go away; the badges say it
per-code and survive mixed sources.

### Actions

- `Refresh` — re-reads codes from every discovered module, both protocols
- `Clear module` — on the selected module
- `Clear all` — vehicle-wide, $04 plus UDS $14 as applicable
- `Probe known` / `Full scan` stay where they are, on the module list header

## Work

1. Introduce a UI-level `FaultModule` / `FaultCode` pair in Core that both
   `ModuleDtcGroup` and `UdsModuleInfo`/`UdsDtc` project into. This is the whole
   job — the views are thin over it.
2. New `FaultsView.axaml` from UdsView, bound to the unified collections.
3. `MainWindowViewModel`: merge `IsDtcsView`/`IsUdsView` into `IsFaultsView`,
   merge the discovery and refresh paths so one Refresh covers both.
4. Delete `DtcsView` and `UdsView`; drop one tab from `ScanToolView`.

Freeing a tab slot also relieves the 800px tab-row crowding.

## Open question

Whether "Faults" or "Trouble Codes" reads better on the tab. "DTCs" is what techs
say, but it is the name of the old narrower page and may read as unchanged.
