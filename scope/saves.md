# Save/load lifecycle integration

## Runtime bootstrap state

Networking preload/diagnostic results are process startup state only. The source-linked helper holds no scene objects or durable user choices and adds no participant, record, sidecar field or game hook. Existing capture/reset/replay and ownership policies are unchanged; no save migration is needed.

## KSA 5638 shared handoff startup follow-up

`NativeSaveHooks.Apply` requires the shared `PhysicsFrameHook` before installing save patches. A
reported Windows startup failure therefore disabled the native sidecar lifecycle along with Dent
Wizard and welding. The hook now validates its seven native solver calls before compatible foreign
call wrappers (`Priority.First` plus `before` constraints for existing owners), retains the exact
result/snapshot boundary, and reports full installation exceptions and transpiler owner/order data.
Actual Harmony regression tests reproduce the former default-priority conflict, exercise both
installation orders and three failed retries, and verify targeted rollback preserves foreign patches.
The user's native original-build comparison then disables only purr2d2: all three startup failures
disappear, the shared hook installs, and welding works, while purrTTY/gatOS remain enabled. This
isolates a purr2d2 interaction, not the exact transformation. Updated-build coexistence and actual
scene-save behavior remain unverified.

Actual-source follow-up: Purr2D2's old unconditional `Universe.DeserializeSave` cleanup prefix ran
even when Unscience deferred the original. This could discard its session setup before a later
preflight rejection. Its companion last-priority prefix now checks `__runOriginal` and joins
`JobSystems.WaitAll` before actual cleanup; normal file requests already defer earlier at
`UncompressedSave.Load`. The optional source-linked `Purr2D2.Interop` executable combines both
production hooks with native-independent fixtures for the deferral/reset/replay contract. No
participant ID, payload, identity resolver or native/global/session ownership policy changes.

Deferred world changes still replay after result application and the orbit-reader join, **before**
`GetJobSimStep` computes time from the new world. Queue ordering, per-callback isolation, previous-time
timestamps, native capture/reset/replay, feature identities and payload versions are unchanged.
Failed removal discards pending old-world edits and world requests even when foreign code prevents
Harmony from rebuilding; installed state stays true until removal succeeds. The real-KSA IL probe
reads signatures and ordered seams without executing native code. Managed Garry baseline/animation
round trips remain in the same suite; Windows startup and native save/load acceptance are still open.

## KSA 5638 (5482 → 5638) verification

Verified against `2026.10.19.5638` (NEW, `ksa-game-assemblies` `edda844`) vs `2026.9.22.5482` (OLD,
`a2f724f`). Managed/static only: the full solution build passes (0 warnings), and `saves.tests`,
`world-saves.tests`, `camera-saves.tests` and `doh.tests` pass. No native save or load was run.
Line numbers below are NEW (`decomp/KSA/`).

- **Native load now skips bad vehicles instead of crashing (semantic drift, fixed).** Revs 5632/5633
  changed `CelestialSystem.DeserializeSave` to `(CelestialSystemData, ISet<string> skippedVehicles)`
  (`CelestialSystem.cs:655`).
  - A missing parent body or a parent that is not an `IParentBody` is logged and skipped (`:661-692`,
    as at 5482).
  - A vehicle whose `RootPartInstance.FindMissingParts()` (`PartInstance.cs:120-141`) reports templates
    is skipped with the `AlertVehicleMissingParts` alert and added to the set (`:694-699`).
  - A vehicle whose build throws, for example `ModLibrary.Get<CharacterReference>` for an uninstalled
    character in the `KittenEva` constructor (`KittenEva.cs:71-76`), is caught, skipped with
    `AlertVehicleChangedParts` and added to the set (`:701-721`).
  - `Universe.DeserializeSave` (`Universe.cs:2419`) then ends the mission of roster kittens assigned
    to a skipped vehicle (`:2461-2469`).

  At 5482 a missing template or character threw after every vehicle had been destroyed, so the
  preflight refusal was protective. It was now stricter than the game. `NativeSavePreflight.Validate`
  (`ksa-abstractions.lib/Persistence/NativeSavePreflight.cs:27-59`) returns one diagnostic per vehicle
  KSA will skip: a missing parent body, uninstalled part templates (each named once, including
  subparts, which native finds only when the build throws) or an uninstalled character.
  `NativeSaveHooks.ResetBeforeLoad` raises them through the new `LoadWarning` callback after the
  reset join (`NativeSaveHooks.cs:151-162`), so a direct load's fresh diagnostics transaction keeps
  them. `unscience/UnscienceSaves.cs` routes them to `SceneSaveCoordinator.Report`.
- **Refusals that remain (the game still cannot survive them).** Each is checked before reset
  (`NativeSavePreflight.cs:38-43,61-84,105-127`):
  - null/invalid `GameTime`, camera reference fields or raw transforms, an undefined camera mode, or a
    null `KittenRoster`. Native dereferences all of these after destruction (`Universe.cs:2444-2472`;
    `Camera.cs:813-850`).
  - not exactly one celestial system, or a different system. Native reads only `CelestialSystems[0]`,
    and `UncompressedSave.Load` (`UncompressedSave.cs:45-80`) never switches systems.
  - a blank or (case-insensitive) duplicate vehicle id. `LookupCollection.Register` ignores the first
    and logs the second (`LookupCollection.cs:110-136`), leaving a constructed but unregistered vehicle
    in its parent's `Children`.
  - a null `ParentBody` record, which native dereferences outside its per-vehicle try/catch (`CelestialSystem.cs:661`).
  - a null child, a cycle, depth over 256 or more than 200000 parts. `CollectMissingParts` recurses through
    `Children` without null or cycle checks.
- **Sidecar replay for skipped vehicles.** Participants already resolve vehicles only through the exact
  `VehicleProvider.FindVehicle` / `SavedPartReference.Resolve` identities, which return null for a
  skipped id. They then `Warn` per entry or `Require` per record; none falls back to the controlled or a
  similarly named vehicle. The coordinator retains every record with warnings, so the next save keeps
  it, and current edits for that feature are not captured until **Use current setup** is pressed. The
  native save itself no longer contains a skipped vehicle after a re-save.
  - The native `_skippedVehicles` set is a private static field (`Universe.cs:102`) and is not passed to
    any public method we patch. Reading it would add a private-reflection dependency; the preflight
    already predicts every skip except an unexpected build exception, which KSA alerts on itself and
    participants still report as unresolved. We therefore do not read it.
  - KSA also ends the mission only for roster kittens whose `AssignedVehicleId` matches a skipped
    vehicle. An EVA kitten (`AssignedVehicleId == "EVA"`) skipped for a missing character stays
    assigned, and so does the crew of a vehicle skipped for a missing parent. This is stock behavior.
- **`JobSystems.WaitAll()` (new) and our join.** `WaitAll` (`JobSystems.cs:61-67`) joins
  `NearestOrbitAndPerformanceWorker`, `OrbitSolvers`, `VehicleSolver` and `ClothSolvers`. Native
  `Universe.DeserializeSave` now calls it first (`Universe.cs:2426`, replacing the 5482 three-scheduler
  join), so native destruction also waits for the hover job. Our prefix still must join first,
  because the `Resetting` callbacks mutate old-world state before the original runs. `LoadSystem`
  (`Universe.cs:236-260`) has no join at all. `ResetAtJoinedBoundary` now calls `JobSystems.WaitAll()`
  (`NativeSaveHooks.cs:191-212`). That is the game's own set and survives scheduler renames such as
  5482's. `PrepareFrame` still queues `NearestOrbitPointJob` (`Program.cs:2330-2335`) before the
  world-change handoff (`GetJobSimStep`, `:2350`).
- **`JobSystems.PlumeTrailLodBuilder` removed (rev 5505).** The standing "not joined" issue is moot.
  No unscience code referenced it.
- **Checked unchanged:** the hook targets `GameSave.Populate()`, `UncompressedSave.Write()` (`bool`),
  `UncompressedSave.Load()` (lazy caught read, `:61-79`), `Universe.DeserializeSave(UniverseData)` and
  `Universe.LoadSystem(string)`. `Camera.DeserializeSave` still falls back to the first vehicle or the
  home body when the followed vehicle was skipped (`Camera.cs:819-836`).
- **Payload compatibility.** No participant ID, version or payload changed. Saves that 5482 refused now
  load with diagnostics.
- **Managed checks (`saves.tests`, 18 lifecycle checks).** The fixture native load joins via `WaitAll` and
  skips the same vehicles. New: "vehicles KSA skips load with warnings instead of refusal" (file and
  direct loads, ordering after reset, one name per missing template) and "skipped vehicle stays
  unresolved through the real coordinator" (repeated loads, retained record, next save). Structural
  refusals now include a null parent record, null child, cycle and over-deep tree.
- **Live checks pending (native).** Load a save whose vessel uses an uninstalled part mod and one with an
  uninstalled character kitten. Check the KSA alerts, the toolbox warnings, the
  unresolved-target warnings, roster state and camera fallback. Re-save, then confirm the retained
  records survive and that **Use current setup** releases them.

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
    `PlumeTrailLodBuilder` is still not joined, which is a standing issue (@5638 moot: removed in rev 5505).
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
| Harmony prefix + postfix + finalizer `Universe.DeserializeSave(UniverseData)` | Join workers, discard stale queued edits and reset old ownership before native destruction; replay after reconstructed vehicles/camera and before next solver dispatch. Direct loads also reset baseline. @5638 the prefix raises preflight `LoadWarning`s for vehicles native will skip after the reset. |
| Harmony prefix + finalizer `Universe.LoadSystem(string)` | Validate system name via `SystemLibrary.Find` before cleanup on world replacement. No replay from previous save. |
| `JobSystems.WaitAll()` (@5638; was the four `JobScheduler.Wait()` calls on `OrbitSolvers`, `VehicleSolver`, `ClothSolvers`, `NearestOrbitAndPerformanceWorker`) | Explicit join before prefix cleanup: the original's own `WaitAll` (`Universe.cs:2426`) is too late for prefix mutations, and `LoadSystem` has none. Requires `Brutal.Concurrency` reference. |
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
unique vehicle IDs and a parent-body record per vehicle, with a bounded acyclic `PartInstance.Children` /
`SubPartInstances` traversal, before ownership reset. @5638 a missing `IParentBody`, `CharacterReference`
or used `PartTemplate` (resolved through `ModLibrary.Get<T>`) is no longer a refusal: KSA skips that
vehicle itself, so the preflight returns a per-vehicle diagnostic that `NativeSaveHooks` raises as
`LoadWarning` after reset (see the 5638 section). It cannot predict every native reconstruction
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

## Kitchen Sink capsule glass saves

`kitchen-sink-capsule-glass` is a separate version-1 boolean record, restore order 20. Existing
`kitchen-sink` (independent IVA preference), `kitchen-sink-g-load` and `kitchen-sink-iva-camera`
payloads/versions are unchanged. Capture detaches the desired experiment state; typed boolean
validation rejects malformed values and the coordinator retains unsupported versions. Reset calls
`SetEnabled(false)` before old-world destruction, releasing only the experiment's global IVA
requirement. Replay calls the same API after native reconstruction; enabled replay requires both
the IVA patches and operational render filter or reports a retained failure. Missing legacy/vanilla
records leave it off. Repeat/cross-scene loads do not compound ownership.

This scene toggle is sidecar-owned, not a global preference or native save field. Exact stock
model IDs are integration constants, with no per-vehicle registrations to resolve. Template/native
references, Harmony registrations and per-frame range lists are transient; the adapter never
serializes them. `kitchen-sink.tests` adds 17 actual adapter/coordinator checks (including invalid,
legacy, unavailable and retained-record recovery); `ksa-upgrade.tests` separately adds 20 checks
using the production filter, IVA helper and constructor/compose patches. Native visual/load
acceptance remains pending; see [feature scope](ui-customization.md#kitchen-sink-capsule-glass-experiment-5541).

## Kitchen Sink IVA camera saves

The accompanying Graffiti 5541 descriptor migration only changes transient GPU bindings;
`graffiti` save ID/version, recipe payload and reset/replay are unchanged.

`kitchen-sink-iva-camera` v1/order 190 captures detached unlock state, base speed, exact
`SavedPartReference` and seat module index, plus body-relative position/quaternion. The native
save owns camera follow/mode and its ordinary pose; this record replays unlocked pose because
native IVA otherwise pins it to a seat. Reset restores the original controller and clears private
FlyController input before native reconstruction. Replay requires IVA mode and the exact native
followed vehicle, resolves the seat using shared identity helpers and assigns the saved pose once.
It never selects the controlled vehicle as a fallback or creates extra cameras. Missing/ambiguous
targets, topology/seat/mode mismatch, unavailable patches, malformed/future records fail with
diagnostics and retention. Absent records reset to locked and 0.5 m/s. Disabled records still
save reusable speed. Held keys, mouse smoothing, controller objects and reflection delegates
are transient and reconstructed. Legacy `kitchen-sink` and `kitchen-sink-g-load` remain unchanged.
Managed coverage uses production adapter/reset/rebind, repeated/cross-scene loads, real identity
resolvers, malformed/missing records and native-boundary fixtures. Native acceptance pending.

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

## The Tick indestructibility saves

`TheTickSubmod` registers `the-tick`, a version-1 string-array record of protected vehicle IDs
(restore order 20), following the Kitchen Sink G-load lifecycle above: capture validates unique live
identities (10,000 nonblank unique IDs max) and refuses ambiguous ones; reset clears the registry and
picker before native reconstruction; replay binds exact reconstructed targets through
`VehicleProvider.FindVehicle` before solvers resume. Missing/disposed/ambiguous targets warn, patch
unavailability fails visibly and the record is retained. Saves without the record and vanilla saves
restore no protection. Patch installation is process-global and is not scene state. No new native
hook. Managed checks: `the-tick.tests` (24 adapter checks). Native save/load acceptance is open.

Kitchen Sink's managed checks link its real adapter, JSON helpers, vehicle resolver and coordinator
for round-trip, A-B-A/repeated loads, legacy/vanilla cleanup, invalid targets and retained-state
recovery. Native cart collision and save/load acceptance remain in-game.

## Save Yourself auto saves

Save Yourself registers **no** save participant. Its prefix, enabled flag and interval are a global
Unscience preference in `.unscience/save-yourself.toml` (`AutoSaveSettingsStore`, next to the window
layout), loaded at init and written through on every change; a missing or malformed file yields
defaults, wrong types are ignored and out-of-range values clamp. This is an intentional exclusion:
auto saving is a session preference, not scene state, so native loads never change it and the
countdown continues across loads. Countdown position, last-save name and counters are transient.

The feature is also a **producer** of native saves: every N wall-clock seconds it calls
`GameSaveProvider.TryMakeSave` (`SaveName.TryAccept` → `UncompressedSave.Make`, the terminal
`save` path), so `GameSave.Populate` and `UncompressedSave.Write` run and `NativeSaveHooks`
captures and writes the sidecar exactly as for a manual save. It never calls the hooks directly
and adds no new native hook. Saving is skipped while `Universe.CurrentSystem` is null or
`Program.IsEditorOpen` is true, mirroring `GameSaves.RefusedInEditor`. Managed checks:
`save-yourself.tests` (13 settings-file checks). Native save acceptance is open.

## DOH kitten and material-slot saves

`DohSubmod` keeps its version-1 `doh` record (restore order 60). Native KSA saves and rebuilds the
spawned `KittenEva` vehicles, so the record never spawns anything. It rebinds each saved kitten ID
to its reconstructed kitten, re-clones or reuses the tinted material set, and reapplies each saved
material color by source/name.

The additive version-1 `SpawnName` field saves the editable name / batch prefix for both spawn
buttons. Missing legacy fields and loads with no record reset to `Kitten_doh_`; blank input is
preserved as automatic naming. Validation rejects null, embedded NUL and text of 256 or more UTF-8
bytes before replay into the 256-byte ImGui buffer, retaining malformed records with diagnostics.
Names of existing kittens remain native-owned; restore never renames or respawns them. The numeric
counter is a transient search hint, with uniqueness enforced against native `All.TryGet` on each
spawn after loading. The real-adapter checks cover detached Unicode/blank capture, repeated and
cross-scene replay, legacy/missing fields and invalid names.

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

@5638: a saved DOH kitten whose character is no longer installed is skipped by KSA itself. The
preflight then warns, and restore reports "DOH kitten missing" and releases its detached set as for
any missing kitten. Despawn, prune and orphan removal go through
`Universe.DestroyVehicle(kitten, CrewDisposition.Boarded)` (see
[character-and-materials.md](character-and-materials.md#doh) #28). This leaves the save adapter, its
reset order and the stale-set release unchanged. `doh.tests` (34 checks) adds teardown coverage.
