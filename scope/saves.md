# Save/load lifecycle integration

The suite extends native directory saves through `ksa-abstractions.lib/Persistence/NativeSaveHooks`.
Research and ordering evidence: [native lifecycle assessment](../plans/saves-game-lifecycle.md).

| Hook / direct dependency | Purpose and ordering contract |
|---|---|
| Harmony postfix `GameSave.Populate()` | Capture detached feature state against the completed native UniverseData snapshot. Never mutate live physics for capture. |
| Harmony postfix `UncompressedSave.Write()` | Write sidecar only after successful native file output. Failed native writes do not publish sidecar success. |
| Harmony prefix + finalizer `UncompressedSave.Load()` | Preflight and scope the file transaction; skip editor refusal; clear context on success or exceptions without suppressing native errors. |
| Harmony prefix + postfix `Universe.DeserializeSave(UniverseData)` | Join workers, discard stale queued edits and reset old ownership before native destruction; replay after reconstructed vehicles/camera and before next solver dispatch. Direct loads also reset baseline. |
| Harmony prefix `Universe.LoadSystem(string)` | Validate system name via `SystemLibrary.Find` before cleanup on world replacement. No replay from previous save. |
| `JobSystems.OrbitSolvers`, `VehicleSolver`, `ClothSolvers` / `JobScheduler.Wait()` | Explicit joins before prefix cleanup: the original's internal joins are too late for prefix mutations. Requires `Brutal.Concurrency` reference. |
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
