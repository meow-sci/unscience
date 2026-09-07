# Garry's Torch library

Shared vehicle welding, part-relative placement, XYZ scaling, preset storage and queued weld
animation for the standalone Garry's Torch mod and Unscience. See the
[mod README](../garrys-torch/README.md) for features, controls and usage.

## Runtime integration

Hosts create/initialize `GarrysTorchSubmod`, install `GarrysTorchPatches` and `KittenScalePatches`
on their Harmony instance, render the submod UI, then remove patches and dispose the submod.
The `CreateWeld`, `ModifyWeld`, `RemoveWeld`, `AnimateWeld` and preset APIs edit the weld configuration.
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
