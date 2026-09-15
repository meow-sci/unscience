# kitchen-sink.lib

Feature implementation shared by Unscience and the standalone development host.
See [Kitchen Sink controls](../kitchen-sink/README.md).

- `KitchenSinkSubmod`: editor refresh, IVA switch and G-load protection UI.
- `GLoadProtection`: concurrent live registry using exact vehicle reference identity;
  multiple targets, duplicate prevention, missing/disposed pruning and reset.
- `GLoadProtectionPatches`: Apply/Remove Harmony helper used by both hosts. The transpiler
  requires exactly one `StructuralLoad.GLoadFraction` getter in the private static
  `PhysicsBubble.DetectStructuralFailure(VehicleUpdateState)` method. It filters only the
  comparison input for registered targets, preserving load telemetry, part damage, fluid-pressure
  damage and Bepu contacts. Unexpected method layouts reject installation explicitly.
- `KitchenSinkSubmod.GLoadProtection.cs`: filtered vehicle picker, add button and active table
  with a per-row Delete action. Patch readiness gates adding entries.

## Scene saves

The existing `SaveParticipant<bool>` (`kitchen-sink`, version 1) continues to save IVA visibility.
A second `SaveParticipant<string[]>` (`kitchen-sink-g-load`, version 1, order 20) saves protected
vehicle IDs. Capture rejects ambiguous/stale identities; validation rejects duplicate, blank or
oversized target lists. Reset clears registrations and the picker before native reconstruction.
Replay uses VehicleProvider.FindVehicle to bind exact reconstructed vehicles, warns on missing,
disposed or ambiguous targets, and fails explicitly if the patch is unavailable. The coordinator
retains unresolved/failed records. Missing legacy records leave no protection; no old boolean
payload changes. Dispose/unpatch clears live references; updates prune missing/disposed targets.
The removed Flexo diagnostics no longer own any part-transform or solver-resync state.

## Validation

`dotnet run --project kitchen-sink.tests` links the production registry/transpiler into managed
KSA 5438 decision fixtures (the detector and end-frame caller are unchanged from 5402).
The shared IVA helper retains upstream's explicit dent-aware AddInstance binding and paired
instance/dent submission. No G-load algorithm or saved-record migration is required for 5438.
Full solution compilation checks typed game/UI references. Actual
cart collision behavior and rendered controls require native acceptance; see the
[test README](../kitchen-sink.tests/README.md).
