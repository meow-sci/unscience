# Save/load lifecycle integration

## KSA 5482 (5438 → 5482) verification

Verified against `2026.9.22.5482` (NEW) vs `2026.9.10.5438` (OLD) with both supplied decomp trees.
Managed/static only: the full solution build passes, and `saves.tests`, `world-saves.tests` and
`camera-saves.tests` pass. No native save or load was run.
Evidence: [KSA_5482_UPGRADE](../plans/KSA_5482_UPGRADE.md).

- **Worker rename (compile break, fixed).** Rev 5480 renamed `JobSystems.ConcurrentWorkers` to
  `NearestOrbitAndPerformanceWorker` (`JobSystems.cs:12,31`). It is the same single-runner,
  BelowNormal scheduler, running `NearestOrbitPointJob` and `SequencePerformanceJob`.
  - `NativeSaveHooks` joins it after the orbit, vehicle and cloth solvers
    (`ksa-abstractions.lib/Persistence/NativeSaveHooks.cs:185-190`). The `saves.tests` trace label is
    now "join nearest-orbit-and-performance".
  - The explicit join is still required. `PrepareFrame` waits on the worker at frame start
    (`Program.cs:2149-2150`), but it re-queues the hover job at `:2188-2192`, before our handoff
    (`:2207`). Native `Universe.DeserializeSave` (`Universe.cs:2354-2363`) still joins only the orbit,
    vehicle and cloth solvers.
  - The deferred world-change replay also calls `PhysicsFrameHook.JoinOrbitReaders()` first
    (`PhysicsFrameHook.cs:114`).
- **`UncompressedSave.Write()` now returns `bool` (semantic drift, fixed).**
  - In 5438, `void Write()` threw on every failure, so our postfix never ran after a failed write.
  - In 5482, `bool Write()` (`UncompressedSave.cs:108-139`; `GameSave.cs:42`) returns `false` instead of
    throwing. That happens when `SaveDirectory.TryReplace` fails, or when the metadata/universe write
    throws; that exception is caught and logged, and a `TimedAlert` is shown.
  - `TryReplace` fails after 3 IO retries, or when the path is outside the saves root
    (`SaveDirectory.cs:21-116`, rev 5453).
  - After a failed delete, the previous save can survive intact. Publishing the sidecar would then pair
    this session's feature state with the old `universe.xml`, and the hash check would pass on a later load.
  - Fix: `Saved(UncompressedSave, bool __result, bool __runOriginal)` (`NativeSaveHooks.cs:85-93`)
    publishes `Written` only when `__result` is true. Otherwise it invokes the new `WriteFailed` callback.
    `unscience/UnscienceSaves.cs:64-68` then drops the capture and reports
    "KSA could not write save '<id>'; Unscience state was not written."
  - New managed check: "native write returning false reports failure without sidecar". The throwing
    fixture variant is kept.
- **Lazy, caught universe read (rev 5441; load-failure reporting fixed).**
  - The directory constructor no longer reads `universe.xml` (`UncompressedSave.cs:24-30`), and
    `GameSave.UniverseData` defaults to an empty object (`GameSave.cs:7`).
  - `Load()` reads the file inside a try/catch (`:61-79`). On failure it logs, then returns without
    throwing and without calling `DeserializeSave`.
  - Our finalizer therefore saw no exception, so `LoadFailed` never fired. The status line could also
    claim that scene setups would be cleared when nothing had been cleared.
  - Fix: `LoadingSave` clears `_nativeLoadReached` (`NativeSaveHooks.cs:106`), and `ResetBeforeLoad`
    sets it (`:140`). If the load never reached `DeserializeSave`, `FinishedLoading` (`:112-125`) creates
    `InvalidDataException("KSA could not read save '<id>'; the current scene was left unchanged.")`
    and publishes it through `LastLoadError` and `LoadFailed`. It is reported once, and the next
    successful load clears it.
  - `SceneSaveCoordinator.FinishLoad` restores the retained records, because no reset ran.
  - The lazy read itself is harmless to us: `LoadingSave` and `UnscienceSaves.Load` read only
    `save.Directory`, and preflight still validates the parsed data in the `DeserializeSave` prefix.
  - Managed check: "unreadable save reports failure and only clears load context".
- **Overwrite ordering (rev 5453; stale docs corrected below).** `Overwrite()`
  (`UncompressedSave.cs:100-106`) no longer deletes the save up front. The order is now:
  1. `Make(Id)`.
  2. `Populate`, where our capture runs.
  3. `Write`, where `SaveDirectory.TryReplace` deletes and recreates the **same final folder** in place
     (no temp folder, no rename) and the native files are written.

  Our postfix then writes `unscience.json` into that folder after `universe.xml` exists, and the old
  sidecar is deleted along with the folder. A failed deletion leaves the old save and its old sidecar
  intact, which is why the `__result` gate matters. `CacheStrings` and `GameSaves.Register` now run
  inside `Write`, before our postfix (`:136-137`). The listed size therefore excludes `unscience.json`
  until the list refreshes; this is cosmetic.
- **KSA now saves native ground clutter (state owned by KSA, not the sidecar).**
  - `CelestialSystem.SerializeSave` writes `GroundClutterRenderer.SerializeSave` into
    `CelestialSystemData.GroundClutter` (`CelestialSystem.cs:641`).
  - `Universe.DeserializeSave` clears `_clutterExclusions` and resets the renderer (`Universe.cs:2370-2371`)
    before `CelestialSystem.DeserializeSave` reapplies the data (`:778`).
  - Both steps happen between our `Resetting` prefix and our `Restoring` postfix.
  - Its interaction with Pebbles' private placements is covered in [ground-clutter](ground-clutter.md).
- **Payload compatibility.** The fixes in this pass change no participant ID, version or payload.
  Eternal Flame's refill move and Humble Arteest's render-state version are runtime-only. Any Pebbles
  sidecar change is recorded in [ground-clutter](ground-clutter.md).
- **Checked unchanged.**
  - The hook targets: `GameSave.Populate()` (virtual, body unchanged), `UncompressedSave.Load()` (void),
    `Universe.DeserializeSave(UniverseData)` (`:2354`), `Universe.LoadSystem(string)` (`:179`, body
    unchanged), `SystemLibrary.Find` and `GameSaves.RefusedInEditor`.
  - Preflight inputs gained only the additive `ExistsIn` helpers and the `GroundClutter` list. The
    sidecar hash is still computed over `universe.xml`.
  - The seven `PhysicsFrameHook` seams are unique and in order (`Program.cs:2162-2212`).
  - The join set is still complete. Physics islands run inside the `VehicleSolver` job on
    `VehicleWorkerPool`, `BubbleStepJob.cs` was removed, and no new schedulers were added.
    `PlumeTrailLodBuilder` is still not joined, which is a standing issue.
- **Live checks pending (native).**
  - Overwrite while the old folder is locked (Windows): no sidecar is written into the intact old save,
    and the toolbox reports the failure.
  - Normal overwrite: the sidecar is present and the listed size refreshes.
  - Load a save with a corrupted `universe.xml`: the scene is unchanged and the toolbox reports the failure.
  - Deferred load while hovering a flight-plan patch at high warp.
  - The standing A→B→A and vanilla checks below.

## Verification — 5402 → 5438 (historical)

Native GameSave.Populate, UncompressedSave.Write/Load and Universe.DeserializeSave/LoadSystem hook contracts remain compatible; all seven PhysicsFrameHook seams retain order. The shared library now references Planet.Render.Core for KeyHash. Native vehicle serialization now preserves IsDebris and reapplies non-root part scales, which existing adapters inherit. Pyro migrates the two retired template IDs; Free Fallin restores per-canopy native material ownership during cleanup. Managed persistence tests cover transaction logic, while native scene/GPU round trips remain open.

Verified against `2026.9.10.5438` using both supplied source/Content trees.
See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md).
Older catalog text below is updated in place where 5482 changed a contract (marked @5482).

The suite extends native directory saves through `ksa-abstractions.lib/Persistence/NativeSaveHooks`.
Research and ordering evidence: [native lifecycle assessment](../plans/saves-game-lifecycle.md).

| Hook / direct dependency | Purpose and ordering contract |
|---|---|
| Harmony postfix `GameSave.Populate()` | Capture detached feature state against the completed native UniverseData snapshot. Never mutate live physics for capture. |
| Harmony postfix `UncompressedSave.Write()` (`bool` @5482; bound by name + `Type.EmptyTypes`) | Write sidecar only after successful native file output: `Written` fires only when the original returned `true` (`__result`) and was not skipped. A `false` return (@5482: `SaveDirectory.TryReplace` or native write failure, old folder possibly intact) invokes `WriteFailed`, which discards the capture and reports failure; a throwing write publishes nothing. |
| Harmony prefix + finalizer `UncompressedSave.Load()` | Preflight and scope the file transaction; skip editor refusal; clear context on success or exceptions without suppressing native errors. @5482 the universe read is lazy and caught inside `Load` (logged, returns before `DeserializeSave`); the finalizer turns a load that never reached `DeserializeSave` into `LastLoadError`/`LoadFailed` (`InvalidDataException`). |
| Harmony prefix + postfix + finalizer `Universe.DeserializeSave(UniverseData)` | Join workers, discard stale queued edits and reset old ownership before native destruction; replay after reconstructed vehicles/camera and before next solver dispatch. Direct loads also reset baseline. |
| Harmony prefix + finalizer `Universe.LoadSystem(string)` | Validate system name via `SystemLibrary.Find` before cleanup on world replacement. No replay from previous save. |
| `JobSystems.OrbitSolvers`, `VehicleSolver`, `ClothSolvers`, `NearestOrbitAndPerformanceWorker` (renamed from `ConcurrentWorkers` @5482) / `JobScheduler.Wait()` | Explicit joins before prefix cleanup: the original's internal joins are too late for prefix mutations. Requires `Brutal.Concurrency` reference. |
| `Program.IsEditorOpen`, `Universe.CurrentSystem` | Preserve active setup on editor-refused requests and no-world early failures. |
| `PhysicsFrameHook.ClearPending()` | Discard queued old-world mutations both before and after cleanup callbacks. BeforePhysics subscriptions remain registered. |

No XML-deserializer hook is used: listing saves reads XML without loading a world. No dependency on
`Program.OnGameLoaded` readiness is introduced; it only closes menus. Native overwrite remains
non-transactional across native files and sidecar. @5482 `Overwrite` captures first, then
`SaveDirectory.TryReplace` deletes and recreates the same final folder in place inside `Write` (no temp
folder or rename) before native files and our sidecar are written; a failed replace leaves the old
save and its old sidecar intact and publishes nothing new. JSON atomicity does not change that limitation. Callback exceptions are logged individually and do not suppress native operations
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
the exception. Nested native deserialization reports once through its file transaction. @5482 an
unreadable `universe.xml` no longer throws; the finalizer synthesizes the failure when the file
transaction never reached `DeserializeSave`.

The early frame boundary still follows submission of `NearestOrbitPointJob` (`Program.cs:2188-2192`
@5482); it traverses the old world's vehicles and orbital state. `NearestOrbitAndPerformanceWorker.Wait`
(`ConcurrentWorkers` before 5482) therefore joins that work as well as the normal orbit/vehicle/cloth
solvers. Any failed join aborts before reset or native destruction.

`SavedPartReference` captures vehicle ID, tree/subpart indices, target template and a structural
SHA-256 signature of the entire ordered vehicle tree. Capture/replay uses a transaction-local
`PartIdentitySnapshot` cache to traverse each vehicle once even with many grid cells. Changes to
transforms/runtime IDs do not affect identity; changed topology refuses replay. Identical-template,
identical-topology sibling swaps are inherently indistinguishable without native persistent IDs;
this is a save-local address, not a general cross-craft matching mechanism. `VehicleProvider.FindVehicle`
rejects ambiguous IDs rather than selecting the first match.

See [coverage and native acceptance](../plans/saves-acceptance.md) for implemented feature policies
and the remaining live-game checks.

## Dent Wizard transient policy

`DentWizardSubmod` registers `ISaveParticipant` ID `dent-wizard`, version 1. Capture is an empty
object; `ResetState` clears source references, speed (back to 5), automatic/armed gesture, status and pending shot.
Automatic mode only keeps the mouse gesture armed between clicks; it never generates timed shots
or restores active input capture. The version-1 empty record remains unchanged. Source mass labels are derived live from
`Vehicle.TotalMass`; no duplicate mass value is saved.
Restore replays no action. The launched vessel uses ordinary native KSA position/velocity state;
no custom force or launch history is persisted. Selection/speed configure only the next one-shot
action rather than an ongoing scene registration. Kitchen Sink independently owns any G-load
protection record; a launch preserves that vessel reference and its protection. World replacement must call reset before the
next `PhysicsFrameHook.BeforePhysics`; execution also rejects old-world source/target objects.

## Kitchen Sink G-load protection saves

KitchenSinkSubmod retains its existing version-1 boolean IVA record (`kitchen-sink`) and adds
`kitchen-sink-g-load`, a version-1 string-array record of protected vehicle IDs (restore order 20).
Capture validates unique live identities; validation bounds the list to 10,000 nonblank unique IDs.
Reset clears registrations and picker state before native reconstruction; replay uses the shared
VehicleProvider.FindVehicle resolver to bind the exact reconstructed targets before solvers resume.
Missing/disposed/ambiguous targets warn, patch unavailability fails visibly, and the coordinator
retains the original record. Legacy/vanilla saves lacking the new record restore with no protection.
Dispose/unpatch clears runtime references; ordinary updates prune missing/disposed objects.
No new native save hook or changed legacy payload. Only picker state is transient.

Reconciled against 5438 and re-checked at 5482 (detector body identical, `PhysicsBubble.cs:958`,
GLoadFraction read `:975`): the detector, readonly vehicle identity and shared replay lifecycle
remain compatible. Both version-1 records are unchanged. All 14 managed suites pass, including
26 Kitchen Sink adapter checks; see [the reconciliation record](../plans/KSA_5438_RECONCILIATION.md).

Kitchen Sink's managed checks link its real adapter, JSON helpers, vehicle resolver and coordinator
for round-trip, A-B-A/repeated loads, legacy/vanilla cleanup, invalid targets and retained-state
recovery. Native cart collision and save/load acceptance remain in-game.

## DOH kitten and material-slot saves

`DohSubmod` keeps its version-1 `doh` record (restore order 60). Native KSA saves and rebuilds the
spawned `KittenEva` vehicles, so the record never spawns anything. It rebinds each saved kitten ID
to its reconstructed kitten, re-clones or reuses the tinted material set, and reapplies each saved
material color by source/name.

Each saved kitten now carries an optional `MaterialGroup`: the set ID it rendered with when saved.
Kittens with the same group get one shared set on restore, the way a non-unique batch was spawned.
The field is additive, so the version and record ID stay the same. Saves without it restore every
tinted kitten with its own set, as before. A blank group is malformed and the coordinator rejects and
retains it.

Reset moves each live set into a detached cache keyed by kitten ID and character. Restore reuses a
cached set only once, unless the saved group says to share it, so kittens that were unique in the
save are never merged. When restore finishes, any cached set that no kitten rebound is released back
to KSA's fixed-size GPU material pool (see [character-and-materials.md](character-and-materials.md#doh)
#33-#35). Loads with no DOH record (vanilla saves, new systems) reset without a restore. Their cached
sets are released on the next frame's `Update` instead. Reset and restore run inside one native call,
so `Update` never sees a cache that a restore still needs. A released set is never reused, because its slots
may already belong to other materials. GPU handles and cloned asset names are never saved as
identities.

`doh.tests` links the real adapter, registry, material-set and release code with JSON helpers and
the scene coordinator. It checks group capture, same-save reuse without new allocations, legacy
saves without the group field, missing kittens releasing their sets, vanilla-load cleanup before
frame, no reuse after release, idempotent release, and rejection of a malformed group. Cloning
itself, GPU writes and the native load are checked in-game.
