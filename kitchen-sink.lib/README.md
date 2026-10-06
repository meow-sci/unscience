# kitchen-sink.lib

Feature implementation shared by Unscience and the standalone development host.
See [Kitchen Sink controls](../kitchen-sink/README.md).

- `KitchenSinkSubmod`: editor refresh, IVA visibility/camera switches, capsule-glass experiment and G-load protection UI.
- `CapsuleGlassExperiment`: one shared `PartRenderFilter.RegisterStaticModel` owner hides only
  `CoreCommandA_Subpart_MediumCapsuleWindowA_Model` and `...WindowB_Model` while enabled.
  `IvaForceRender.SetRequired` reveals interiors globally without changing the user's independent
  `Enabled` preference. Native glass remains submitted. Both hosts register/remove the owner;
  disable/reset/dispose releases its requirement. Ray tracing shadow proxies are not revealed.
- `IvaCameraUnlock` / `UnlockedIvaController`: temporarily replaces the main viewport's IVA
  controller with an IVA subclass delegating movement to a private native `FlyController`.
  Preserves IVA mode/audio and vessel-relative pose; restores the original controller on seat
  return, camera mode change, invalid target, reset and unload. Setter reflection and a targeted
  `IVASeat.IsCameraInThisSeat` postfix are installed/removed by both hosts. Input focus gates
  clear held keys and skip gamepad polling while UI owns input.
- `KitchenSinkSubmod.IvaCamera.cs`: **Unlock IVA Camera**, speed and **Return to Seat** controls.
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
A separate boolean `kitchen-sink-capsule-glass` v1/order 20 saves the experiment, default off.
Reset releases old visibility ownership; replay uses `SetEnabled` after reconstruction and fails
with diagnostics/record retention if rendering is unavailable. Existing payloads are unchanged.
Missing legacy records leave the experiment off. No native references are captured: model/template
caches and shared patch ownership are transient. Managed `CapsuleGlassSaveChecks` exercise the
real adapter/coordinator; `ksa-upgrade.tests` links the real render filter/IVA helper and feature.
`IvaCameraUnlock.Persistence.cs` contributes `kitchen-sink-iva-camera` version 1, order 190:
unlock state, reusable base speed, `SavedPartReference` plus seat module index, body-frame
position and orientation. Captures no native object references. Reset restores the old controller
before native destruction; replay requires the reconstructed native camera's IVA mode and exact
follow target, then resolves the seat and overwrites the detached pose. Required DTO properties,
finite/ranged values and unit quaternions are validated. Missing/ambiguous/invalid targets or
unavailable patches fail with save diagnostics and record retention. Legacy/vanilla saves reset
unlock and speed. Input state and controller/reflection caches are intentionally transient.
The native save owns follow/mode and its ordinary pose; this record owns the unlocked pose
that otherwise would be overwritten by IVA's seat update.
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
No G-load algorithm or saved-record migration is required for 5438 or 5482. On KSA 5482
`PhysicsBubble.DetectStructuralFailure` has a byte-identical body. The shared IVA helper in
`ksa-abstractions.lib` now reveals editor internals through `PartTreeRenderData.Compose`, because
5482 raster composition bypasses the dent-aware `AddInstance` sink that 5438 used.
Full solution compilation checks typed game/UI references. Actual
cart collision behavior and rendered controls require native acceptance; see the
[test README](../kitchen-sink.tests/README.md).
