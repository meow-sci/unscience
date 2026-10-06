# Kitchen Sink

A collection of small KSA fixes, available in Unscience's Kitchen Sink panel.
Only Unscience is shipped; this standalone project is a development host with an F11 window.

## G-load Invincibility

1. Open Kitchen Sink and find **G-load Invincibility**.
2. Open the **Vehicle** dropdown and type to filter vehicle names.
3. Select a vehicle and click **Add G-load Invincible**.
4. Repeat for other vehicles. The active table lists every protected vehicle.
5. Click a row's **Delete** button to remove its protection; this does not delete the vehicle.

Protection disables only KSA's whole-vehicle G-load destruction decision for those exact
vehicle instances, including loads during vehicle, ground and ocean contact. Bepu collisions,
measured G-load telemetry, flight-computer limits, individual part crash tolerances, and
atmospheric/ocean dynamic-pressure destruction continue normally. For collision-based machines,
set suitable part crash tolerances separately.

The list supports multiple vehicles and rejects duplicate entries. Removed or disposed vehicles
are pruned; an unrelated replacement with the same name does not inherit live protection.
Normal Unscience Save/Load saves the selected IDs and explicitly rebinds them to reconstructed
vehicles. Unload, vanilla saves and older saves without this record leave the list empty.
If the game patch cannot be installed, the panel reports protection as unavailable and disables
the add button; save restoration reports the failure and retains the saved record.

## Fix Invisible Subparts

Open the vehicle editor and click **Refresh Vehicle** to call
`PartTree.ReinitializeDerivedValues` on the editor's parts. This is a workaround for invisible
subparts after editing. Since KSA 5482 this call only marks derived data dirty; the game recomputes it
at the next frame's flush, so the result appears one frame later.

## Force IVA Rendering

Enable **Always Render IVA Interiors** to show interiors outside IVA camera mode.
`IvaForceRender` changes loaded model templates and catches newly created models with a
constructor postfix. Disabling the switch or unloading restores the changed template flags.
Internal meshes also stay visible in the vehicle editor preview. On KSA 5482 the editor path uses a
prefix and finalizer on `PartTreeRenderData.Compose`. They reveal internal templates for that one
call, so stock code appends matching instances and dents. KSA 5482's raster composition no longer
calls the `PartModel.AddInstance` method that the 5438 postfix used. Editor part thumbnails no
longer show internal meshes.

## Capsule Glass Experiment

Enable **See Inside Capsule (Experimental)** in Kitchen Sink to try looking into the stock
medium/Gemini capsule from outside. The switch defaults off and affects every instance of the
two stock exterior window models. It hides only those opaque window surfaces, reveals IVA
interiors through the shared visibility helper, and retains the native IVA glass and shader.
Turning it off restores the exterior windows on the next render frame.

Interiors are revealed globally while the experiment is enabled. **Always Render IVA Interiors**
keeps its own setting: turning either switch off leaves interiors visible if the other still
requires them. The existing editor preview reveal remains independent. Hulls, door frames,
other exterior windows and ray tracing shadow proxies retain native behavior. Newly constructed
models use the same visibility ownership, and unloading restores changed internal flags.

This is a rendering experiment, not a confirmed visual fix. Stock glass has strong opacity/tint;
window alignment, depth, culling and exterior lighting require an in-game check. Occupants in
other vehicles still follow KSA's existing crew-submission limits. No shader, material, mesh asset,
collision geometry or crew behavior is changed. Use the main exterior view for this test;
secondary camera feeds omit KSA's glass pass and may show openings without glass.

## Unlock IVA Camera

Enter IVA in flight, then enable **Unlock IVA Camera** in Kitchen Sink. The camera can move
and turn freely while retaining IVA interiors, lighting, audio and the game's configured
ray tracing. **Always Render IVA Interiors** is independent and is not required in IVA.
The feature requires a valid seat on the followed vehicle and applies to the main view only.

Use your normal free cam movement, vertical and sprint bindings. Hold left mouse to look;
Alt releases the cursor. Scroll or use **Move speed (m/s)** to adjust the base speed from
0.01 to 100 m/s (default 0.5). Position and orientation follow the vehicle's body frame as
it moves and rotates. Native terrain clamps still apply; this is a flying camera, with no
cabin collision or walking simulation. The seated kitten's head becomes visible while detached.

Turn the toggle off or click **Return to Seat** to return to the original seat. Seat switching
is suspended while unlocked. Switching camera modes, losing the vehicle/seat, loading another
scene or unloading releases the custom controller. Typing, modal windows and another input
viewport suspend movement and clear held keys. Ray tracing still requires supported hardware
and the game's IVA ray tracing setting. Native visual/input acceptance is pending.

## Scene saves

The existing `kitchen-sink` boolean record still stores the Force IVA Rendering switch.
A separate version-1 boolean `kitchen-sink-capsule-glass` record saves the experiment. Reset
releases its IVA requirement before world replacement; replay reapplies the normal toggle after
native reconstruction. Legacy/vanilla saves leave it off. Malformed/future records and unavailable
render patches are diagnosed and retained for recovery. Template references, patch registrations
and per-frame render lists are transient and never serialized; there are no per-vehicle targets.
A separate version-1 `kitchen-sink-iva-camera` record stores the unlock state, base speed,
stable seat part/module identity, and detached body-relative camera pose. Native KSA owns
the followed vehicle and camera mode; replay requires those to match, restores the exact seat,
and applies the pose once after reconstruction. Missing/ambiguous targets, incompatible mode,
unavailable patches and invalid payloads are diagnosed and retained for recovery. Vanilla or
older saves without this record reset the controller and speed. Held input, mouse smoothing,
controller objects and reflection caches are transient. Native camera pose is also saved by
KSA; the unlocked pose is explicitly replayed because stock IVA would pin it back to the seat.
A separate version-1 `kitchen-sink-g-load` record stores the protected vehicle IDs. Reset clears
old references and picker state before native reconstruction; replay resolves exact, unambiguous
IDs through VehicleProvider and restores protection before simulation resumes. Missing/disposed
or ambiguous vehicles are reported, and the original record is retained for recovery.
Older boolean-only saves remain compatible and restore with no G-load registrations. The picker
filter and one-shot editor refresh are transient. The defunct Flexo panels and solver hook are removed.

## Implementation and checks

- `kitchen-sink.lib/KitchenSinkLib.cs`: submod lifecycle and existing fix panels.
- `KitchenSinkSubmod.GLoadProtection.cs`: filtered picker and active table.
- `GLoadProtection.cs`: concurrent registry keyed by vehicle reference identity.
- `GLoadProtectionPatches.cs`: guarded Harmony transpiler on
  `PhysicsBubble.DetectStructuralFailure(VehicleUpdateState)`; gates only the
  `StructuralLoad.GLoadFraction` value consumed by the destruction comparison.
- Both `unscience/Patcher.cs` and the standalone `Patcher.cs` install/remove this patch.
- `KitchenSinkSubmod.Saves.cs`: backward-compatible IVA record and G-load target capture/reset/replay.
- `CapsuleGlassExperiment.cs`: exact static-model filter and independent IVA visibility ownership; both hosts install/remove it.
- `ksa-upgrade.tests`: production model filtering and IVA ownership/cleanup checks.
- `IvaCameraUnlock.cs`, `UnlockedIvaController.cs`: scoped controller replacement, free movement,
  seat return, focus/input cleanup and the seat head-visibility patch.
- `IvaCameraUnlock.Persistence.cs`: detached capture, validation and exact target replay.

Build the solution with `dotnet build`. Run `dotnet run --project kitchen-sink.tests` for the
production patch's managed fixtures. These cover isolation, removal, telemetry, damage thresholds,
contact causes, cleanup and unpatching. Native ImGui/Bepu cart acceptance remains an in-game check.
The G-load detector and end-frame caller are unchanged from 5402 to 5438 and 5482; protection and
both version-1 save records need no migration. See the [reconciliation record](../plans/KSA_5438_RECONCILIATION.md).
See [game integration](../scope/ui-customization.md#kitchen-sink) and
[destruction investigation](../plans/VEHICLE_DESTRUCTION_INVESTIGATION.md).
