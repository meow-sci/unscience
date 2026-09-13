# Garry's Torch library

Shared vehicle welding, part-relative placement, XYZ scaling, preset storage and queued weld
animation for the standalone Garry's Torch mod and Unscience. See the
[mod README](../garrys-torch/README.md) for features, controls and usage.

## Runtime integration

Hosts create/initialize `GarrysTorchSubmod`, install `GarrysTorchPatches` and `KittenScalePatches`
on their Harmony instance, render the submod UI, then remove patches and dispose the submod.
The `CreateWeld`, `ModifyWeld`, `RemoveWeld`, `AnimateWeld` and preset APIs edit the weld configuration.
The red **Delete All Welds** UI button beside **Create Weld** uses the existing per-weld removal
path for every entry, cancelling animations, restoring source scales and releasing scale ownership.
It is disabled when the weld list is empty; normal collisions resume at the next physics snapshot.
`Update(dt)` does not advance physics. The old public `UpdateWelds(dt)` and single-argument
`UpdateBeforeVehicleSolvers(dt)` methods were removed; hosts must install the shared frame hook.

`GarrysTorchPatches` registers a callback with `ksa-abstractions.lib/PhysicsFrameHook`, which replaces the one `Universe.GetJobSimStep(double)` call inside private
`Program.PrepareFrame(double,double)` with a wrapper. It computes the same step, invokes the
internal weld update using player delta and `step.PreviousTime`, and returns that step unchanged.
The patch requires unique ordered ApplyOrbit/Vehicle/ClothSolvers, GetJobSimStep and
ExecuteNextCloth/Vehicle/OrbitSolvers calls. A changed layout fails installation rather than
reintroducing unsafe UI teleports. Runtime errors are logged without preventing game scheduling.

This lets completed actuator module states commit before `Vehicle.Teleport` removes each source
from its physics bubble. The next tick reattaches the source with its welded pose and committed
module states. The former UI update removed it before result application, losing actuator progress.
`WeldEngine.UpdateWeld(entry, stateTime)` now requires the committed state time explicitly.
Destroyed source/target welds are removed before animation updates; scale restoration skips disposed
sources. Weld animation retains player-time pacing, including during pause and time warp. F2 does not
affect the frame hook, and kitten animation targeting is unchanged.

## Validation

`dotnet run --project garrys-torch.tests/garrys-torch.tests.csproj` exercises the production
Harmony patch on a managed fixture. Build the whole solution with `dotnet build`.
The [integration scope](../scope/vehicle-physics.md) records the current game-source evidence.

Native KSA physics/rendering still needs an in-game smoke pass:

1. Weld a light craft to a target at a non-overlapping offset, initially at identity scale.
   Actuate it in both directions through the stock control and Zippo Disco. Compare with unwelded.
2. Toggle **Weld Enabled**, hide/show the HUD with F2, pause/resume and change time warp.
   Actuation should follow simulation time; queued weld interpolation keeps its existing player time.
3. Exercise a weld chain, a moving target-part anchor, rotation locking and animated XYZ scale.
4. Remove the source/target, unweld and unload; watch for collection/shape-lock errors,
   `SnapToLeader` time mismatches and structural part failure.

`WeldEntry.Collisions` defaults to false. `WeldCollisionPatches` brackets both
`ConstraintSim.DetectCollisions(double)` and `Simulate(double, in SimStep)` with a Harmony prefix
and finalizer. Before each pass, enabled collision-free sources temporarily become shapeless via
Bepu `BodyReference.SetShape(default)`, removing their broad-phase entries. The finalizer restores
the exact shapes, even on exceptions. Vehicle/module state, collider geometry and animation updates
are retained. This avoids patching the aggressively inlined generic narrow-phase callbacks.

The frame handoff publishes an immutable source-identity set after weld validation. Workers use that
snapshot rather than reading mutable UI/weld lists. Create/modify and direct field edits take effect
on the next snapshot. Disabled, removed, disposed or parent-mismatched welds are excluded; unload
clears the set and unpatches both passes. `Collisions = true` leaves the stock collision behavior.
The option is available in create/edit UI, both API scale overloads and saved presets; missing TOML
`collisions` means false. It controls rigid-body contacts, not ocean/aerodynamic forces or all damage.

Managed tests use the game-version Bepu assembly to verify warmed-up Harmony patches, vehicle and
static contacts, unaffected other pairs, continued simulation, immutable snapshots, opt-in, suspension,
unweld, destruction, parent mismatch, exception restoration and unload. Preset tests check legacy
migration and round-trip. The native KSA acceptance pass should additionally overlap two crafts,
actuate a welded light, toggle collisions and Weld Enabled, test terrain/scenery and a weld chain,
and check animated/scaled collider restoration. Unweld or opt-in at a safe offset to inspect normal
contact behavior.

The caller transpiler now lives in `ksa-abstractions.lib/PhysicsFrameHook`; Garry's Torch registers
its weld callback. Queued Godzilla edits run before this callback. Source scale ownership is exclusive:
restore Godzilla before welding, or unweld before applying Godzilla. Managed checks also cover queued
mutation ordering, reentrant deferral, exception isolation and stale-system queue disposal.

## Scale preservation

Scale controls retain a 0.05–20 mouse-drag range, but typed values, APIs, presets and animations
can exceed it. Any positive, finite scale is accepted. `WeldScale.Minimum`/`Maximum` describe
widget bounds, not validation constraints.

`WeldEngine.Scaling` owns a weak per-source `WeldScaleSnapshot`, captured during `CreateWeld`
even at identity. Full-part scale edits multiply captured XYZ values; SubPart local scale,
position and rotation stay game-owned. Descendant matrix caches are invalidated after parent
scale edits so nested render transforms/anchors update. This keeps Flexo authored proportions
and avoids multiplying the weld factor once per nested child. Full-part spacing remains fixed.
The existing raw scaling path's module/collider limitations remain; this change does not add
physics/module rescaling or alter mutation scheduling.

UI/API/presets and queued animations all use the same `ApplyVehicleScale` implementation.
`RestoreVehicleScale` restores only changed full parts still in the source, restores the captured
kitten avatar scalar and clears its axis correction, and releases the snapshot even for a disposed
source (without accessing disposed parts). Added full parts are captured at their first scale edit;
detached parts are left alone. Restore without a snapshot is a no-op. Low-level callers must pair
`ApplyVehicleScale` with `RestoreVehicleScale`; identity returns to baseline but keeps the session.
The compatibility `SetPartScaleRecursive` helper remains an explicit absolute override and is
not used by welding. Only `_characterAvatar` remains reflected; Core/Scale access is typed.

Managed checks link these production implementations, animation queue and real kitten Harmony
patch with the game numerics. Live-check Flexo nested custom scales with identity weld/unweld,
unequal-axis edits, queued animations, automatic removal and unload; verify animated local
transforms continue and original proportions return.

## Scene saves

Unscience's ordinary KSA save integration restores weld targets/part anchors, pose, XYZ factors,
rotation lock, collision and enabled flags. Explicit original per-part and kitten-avatar baselines
make repeated loads noncumulative and keep Unweld's original-size restoration accurate. Active and queued weld animations retain complete recipes and elapsed progress and resume
on the normal physics cadence without offline catch-up.
Native reconstruction precedes adapter replay; missing anchors/parent conflicts are reported.
`GarrysTorchSubmod.Persistence.cs` owns the DTO and `WeldScaleSnapshot.Persistence.cs` imports
baselines without recapturing already scaled native parts. Managed regression checks cover JSON
round-trip, repeated loads and exact original/kitten restoration; native physics acceptance remains.

Replay keeps native effective full-part sizes, including newly attached parts not yet scaled by an
idle weld; only the separate kitten avatar correction is reapplied. Later scale edits continue to
use the imported original baseline.
