# Save/load lifecycle integration

## Current verification — 5402 → 5438

Native GameSave.Populate, UncompressedSave.Write/Load and Universe.DeserializeSave/LoadSystem hook contracts remain compatible; all seven PhysicsFrameHook seams retain order. The shared library now references Planet.Render.Core for KeyHash. Native vehicle serialization now preserves IsDebris and reapplies non-root part scales, which existing adapters inherit. Pyro migrates the two retired template IDs; Free Fallin restores per-canopy native material ownership during cleanup. Managed persistence tests cover transaction logic, while native scene/GPU round trips remain open.

Verified against `2026.9.10.5438` using both supplied source/Content trees.
See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md).
Older catalog tables below retain their explicitly cited build/line numbers; this section records the current delta.

The suite extends native directory saves through `ksa-abstractions.lib/Persistence/NativeSaveHooks`.
Research and ordering evidence: [native lifecycle assessment](../plans/saves-game-lifecycle.md).

| Hook / direct dependency | Purpose and ordering contract |
|---|---|
| Harmony postfix `GameSave.Populate()` | Capture detached feature state against the completed native UniverseData snapshot. Never mutate live physics for capture. |
| Harmony postfix `UncompressedSave.Write()` | Write sidecar only after successful native file output. Failed native writes do not publish sidecar success. |
| Harmony prefix + finalizer `UncompressedSave.Load()` | Preflight and scope the file transaction; skip editor refusal; clear context on success or exceptions without suppressing native errors. |
| Harmony prefix + postfix + finalizer `Universe.DeserializeSave(UniverseData)` | Join workers, discard stale queued edits and reset old ownership before native destruction; replay after reconstructed vehicles/camera and before next solver dispatch. Direct loads also reset baseline. |
| Harmony prefix + finalizer `Universe.LoadSystem(string)` | Validate system name via `SystemLibrary.Find` before cleanup on world replacement. No replay from previous save. |
| `JobSystems.OrbitSolvers`, `VehicleSolver`, `ClothSolvers`, `ConcurrentWorkers` / `JobScheduler.Wait()` | Explicit joins before prefix cleanup: the original's internal joins are too late for prefix mutations. Requires `Brutal.Concurrency` reference. |
| `Program.IsEditorOpen`, `Universe.CurrentSystem` | Preserve active setup on editor-refused requests and no-world early failures. |
| `PhysicsFrameHook.ClearPending()` | Discard queued old-world mutations both before and after cleanup callbacks. BeforePhysics subscriptions remain registered. |

No XML-deserializer hook is used: listing saves reads XML without loading a world. No dependency on
`Program.OnGameLoaded` readiness is introduced; it only closes menus. Native overwrite remains
non-transactional and deletes the previous save before capture/write. JSON atomicity does not change
that limitation. Callback exceptions are logged individually and do not suppress native operations
or the native exception. Harmony installation rolls back its own installed methods on failure and
removes only these precise patches on unload.

Managed `saves.tests` links the production hook into native-signature fixtures and verifies capture/
write ordering, failure paths, worker-join/reset ordering, queue invalidation, editor rejection,
direct load, invalid/new system, repeated load, callback exception isolation and precise removal.
These checks do not initialize native physics/graphics. Native A→B→A, vanilla load after modded
state, hidden HUD, high warp, renderer lifetime and corrupt/incompatible native-save acceptance
remain required in-game checks.

### Required frame deferral (supersedes synchronous caller execution)

A `Load` request can come from late console rendering after ImGui has retained texture references.
Whole `UncompressedSave.Load`, direct `Universe.DeserializeSave`, and existing-world `LoadSystem`
requests are therefore deferred via `PhysicsFrameHook.EnqueueWorldChange`. Latest request wins.
At its existing `Program.PrepareFrame` handoff, PhysicsFrameHook calls the queued method BEFORE
`Universe.GetJobSimStep`, under `IsReplayingWorldChange`, then computes time from the new world.
Only that guarded replay invokes the preflight/reset/restore callbacks. No ImGui frame is still
being built when resource-owning participants release old previews. Startup LoadSystem remains
synchronous; invalid-system/editor refusal retains native behavior.

`NativeSaveHooks.Apply` requires and idempotently installs `PhysicsFrameHook.Apply` before installing
its own methods; failed frame-hook installation leaves native save/load unpatched. The existing
Garry's Torch hook shares this installation and normal ownership/unload path. NativeSaveHooks.Remove
cancels queued world requests. A deferred native exception is preserved by the load finalizer and
logged by the dispatcher (the original UI callback has already returned). Production-hook timing
checks in `garrys-torch.tests` verify loaded-time computation, latest-request replacement, old action
invalidation and guard release on exception; `saves.tests` verifies the native transaction remains
untouched until replay and stock startup remains immediate.

## Native dependency and identity preflight

`NativeSavePreflight` checks the incoming `UniverseData` against the current celestial system,
unique vehicle IDs, existing `IParentBody` targets, required `CharacterReference` and used
`PartTemplate` assets through `ModLibrary.Get<T>`. Bounded acyclic `PartInstance.Children` /
`SubPartInstances` traversal runs before ownership reset. This prevents known missing native
asset failures from destroying the current scene; it cannot predict every native reconstruction
failure. `GameTime.IsValid`, camera reference fields/raw transforms/mode and `KittenRoster` are also
checked before reset. `UniverseData.IsValid` is deliberately avoided: its CameraData.IsValid requires
a nonempty Following identity, but native saves explicitly support unfollowed/free cameras.
`LoadFailed` and `LastLoadError` surface deferred native exceptions to the toolbox. LastLoadError
is set before LoadFinished rolls back a non-destructive failed transaction; LoadFailed then reports
the exception. Nested native deserialization reports once through its file transaction.

The early frame boundary still follows submission of `NearestOrbitPointJob`; it traverses the old
world's vehicles and orbital state. `ConcurrentWorkers.Wait` therefore joins that work as well as
the normal orbit/vehicle/cloth solvers. Any failed join aborts before reset or native destruction.

`SavedPartReference` captures vehicle ID, tree/subpart indices, target template and a structural
SHA-256 signature of the entire ordered vehicle tree. Capture/replay uses a transaction-local
`PartIdentitySnapshot` cache to traverse each vehicle once even with many grid cells. Changes to
transforms/runtime IDs do not affect identity; changed topology refuses replay. Identical-template,
identical-topology sibling swaps are inherently indistinguishable without native persistent IDs;
this is a save-local address, not a general cross-craft matching mechanism. `VehicleProvider.FindVehicle`
rejects ambiguous IDs rather than selecting the first match.

See [coverage and native acceptance](../plans/saves-acceptance.md) for implemented feature policies
and the remaining live-game checks.
