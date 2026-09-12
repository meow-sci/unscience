# KSA save lifecycle assessment (5402)

Research source: `../ksa-game-assemblies/current/decomp/KSA/` on 2026-09-12. The documented
`decomp/ksa` directory is absent in this checkout. Source references below are relative to the
current game decompilation. This assessment is source-backed; native graphics/physics acceptance
has not been performed.

## Recommendation

Extend the existing directory save with a versioned `unscience.json` sidecar plus a content-addressed
asset directory. Continue using native `GameSaves` UI, `save`/`load` terminal commands, vehicle
serialization, and universe reconstruction. Do not fork native universe serialization or add a
second full save format. A small Unscience panel should expose coverage/last-result warnings and
open the native save UI; existing users should not need a second save action.

Capture a plain immutable snapshot next to native `GameSave.Populate()`, retaining it by the exact
save object/snapshot rather than one global last-save variable. Write after `UncompressedSave.Write`
has created native files. On load, preflight sidecar and asset metadata before destructive work;
reset old feature ownership at `Universe.DeserializeSave`, then restore from validated models after
native reconstruction finishes. A malformed optional feature should produce a specific degraded
restore report rather than turn a valid native save into an unreadable game.

This can persist the entire suite only when every stateful feature supplies an explicit capture,
reset, validate, and restore implementation. Reflection over submods, arbitrary object graphs, or
serializing the old UX tree is not an adequate substitute: runtime handles, original-value ownership,
replay order, and native state overlap must be modeled deliberately.

## Native flow and exact seams

| Operation | Source/member | Behavior relevant to integration |
|---|---|---|
| Open save window | `GameSaves.Toggle`, `GameSavesWindow` | Uses existing name/list/load/delete/overwrite UI. Rejects editor state. |
| New save | `GameSaves.MakeSave` → `MakeUncompressedSave` → `UncompressedSave.Make` | Validates name. Existing name displays native overwrite confirmation. |
| Capture | `GameSave.Populate` → `UniverseData.Create` | Synchronous. Captures time, main base camera, one celestial-system data record, kitten roster. |
| Write | `UncompressedSave.Write` | Deletes destination directory, creates it, writes `meta.toml`, writes `universe.xml`, caches size/date and registers save. |
| Overwrite | `UncompressedSave.Overwrite` | Calls `Delete()` BEFORE `Make(Id)`, so old data can be lost even if later capture fails. |
| Refresh/list discovery | `GameSaves.Refresh` → `UncompressedSave.FromDirectory` | Scans directories. Loads native XML while constructing list entries; this is NOT a world-load event. |
| World load | `UncompressedSave.Load` | Editor guard; loads fresh XML from disk; `Universe.DeserializeSave`; `Program.OnGameLoaded`. |
| Delete | `UncompressedSave.Delete` | Recursively removes whole directory. Sidecar and bundled assets follow native deletion automatically. |
| Terminal save/load | `GameSaves.MakeUncompressedSave`, `LoadSaveGame` | Same underlying save objects; hook core methods rather than just UI callbacks. |

`UniverseData.WriteTo(MemoryStream)` is empty in this build. Unmanaged-memory and BinaryReader load
overloads exist but have no current world-load callers found. There is no established compressed
save implementation to extend. `GameSaves.UniverseSerializer` is a get-only, static XmlSerializer
for a concrete `UniverseData` schema. Injecting arbitrary XML into this file would need separate
read/write hooks and does not provide better lifecycle ownership than a sidecar.

### Suggested hook transaction

1. `GameSave.Populate` postfix captures feature state against `__instance.UniverseData`, which is the
   exact native snapshot containing save-local part IDs. The capture must produce detached data;
   native `UniverseData.Create` itself retains `KittenRoster` by reference, so do not assume every
   native object is immutable or safe to hand to background workers.
2. `UncompressedSave.Write` postfix writes the captured sidecar, computes a SHA-256 of the final
   `universe.xml`, and atomically renames a flushed temporary JSON file to its final name. Sidecar
   hashes prevent accidental pairing with another native XML generation. Capture/write failures
   must be visible as “KSA saved; Unscience state not saved,” never silent success.
3. `UncompressedSave.Load` prefix preflights sidecar format, limits, native XML binding, system ID,
   and assets. It must not clear or mutate runtime state here: the original can reject an open
   editor or fail parsing XML without changing the current world. Establish a narrowly scoped
   context containing source directory and preflight result.
4. `Universe.DeserializeSave` prefix is the destructive transition boundary. Guard null system and
   invalid input before cleanup. Explicitly join `JobSystems.OrbitSolvers`, `VehicleSolver`, and
   `ClothSolvers` BEFORE resetting providers: the original's joins occur inside the method, after
   a prefix would already have run. Reset all owned world/runtime state, including when loading
   a vanilla save with no sidecar. This also covers future/direct callers that bypass `Load`.
5. `Universe.DeserializeSave` postfix restores synchronously in dependency order. Native vehicles,
   parts, roster, main camera, and system per-frame state now exist; native load has not queued new
   solver jobs. Thus physics-sensitive changes complete before the next frame's solver snapshots.
   Rebuild renderer resources through existing feature APIs and coalesce expensive rebuilds.
6. `UncompressedSave.Load` finalizer clears scoped context on both success and failure, without
   swallowing the game's own exception. A failed native reconstruction is not a successful restore.

`Program.OnGameLoaded` merely closes menus and resets `_menuPauseActive`; it adds no readiness,
renderer initialization, or physics handoff. Restoring in `Universe.DeserializeSave` postfix covers
more callers and avoids dependency on this incidental UI cleanup. If reporting uses the outer
Load postfix, it should report the already completed transaction rather than replay twice.

Do not reset on `UniverseData.LoadFrom`: refreshing the save browser calls that method for every
save and would destroy the active session. Do not reset on individual `Vehicle.DeserializeSave`:
that is too late for global ownership and too early for inter-vehicle references.

## What native saves actually preserve

`CelestialSystemData` contains only system ID and vehicles. It does NOT serialize celestial masses,
parent/orbit edits, rings, clutter graphs, statics, shader/GPU changes, sound players, overlays,
feature settings, constraint ownership, or extension runtime state.

`Universe.DeserializeSave` does not recreate the solar system: it retains `CurrentSystem`, joins
workers, destroys all vehicles and physics bubbles, clears particles/plume trails, unfollows every
game viewport's base/map camera, resets simulation time and the global running-ID allocator,
updates existing celestial orbits at loaded time, creates saved vehicles, restores roster and the
main base camera, resets orbit display flags, and sets simulation speed to 1.

Consequently loading a clean save after editing a planet does not revert that planet without an
Unscience reset. A pointer-change detector for `Universe.CurrentSystem` cannot detect normal
save loads. Celestial original-state captures must survive across save transitions until explicitly
restored, while per-vehicle original-state captures must be discarded when old vehicles die.

`Vehicle.SerializeSave` preserves current orbital/kinematic state, body rates, vehicle ID, parent,
engine on/throttle, lights on, targets/control part, flight computer, part tree, module save data,
active sequences, resource links and several render/glint options. Native dynamic parts generally
survive if their template remains available. `Part.GetReferenceWithChildren` serializes full-part
position/rotation/scale, stage/sequence fields, module save payloads, subpart module data and child
hierarchy. Subpart transforms are not saved in its `SubPartInstances` records.

Persisting a behavior is distinct from saving its latest output. A weld's current position and scale
may already be in XML, but the constraint, settings, animation phase and pre-weld restoration values
are absent. A native keyframe module saves current time and goal; Zippo's cycle recipe, ownership
and original goal are absent. Replaying a scale multiplier onto already scaled XML would compound
it; restore must reinstate exact targets or carry original baselines, not blindly rerun UI actions.

Iron Man already embeds versioned connector metadata into `PartInstance.Id` via a serialization
postfix and reconstructs connector templates in the Part constructor before native connection
regeneration (`iron-man.lib/IronManConnectorPatches.cs`). Preserve that early constructor path;
a late sidecar replay cannot replace it because connected part indices must be valid during native
reconstruction. This creates an explicit compatibility exception: some saves require Unscience
installed even if the new generic sidecar is optional.

## Stable addressing

- `Vehicle.Id` is written into native `VehicleData.Id` and used to reconstruct the vehicle, so it is
  a suitable snapshot-scoped identifier. It is mutable: `CelestialSystem.Rename` deregisters, renames,
  then registers the vehicle. Capture current names from object references; never persist stale UI
  names. Exact ordinal resolution is preferable to fuzzy matching and wrong-target restores.
- `Part.InstanceId` is runtime-only: the constructor calls `Universe.GetNextRunningId`; loading calls
  `ResetRunningId` and constructs parts anew. Numeric collisions with unrelated old parts are likely.
- `PartInstance.LocalInstanceId` is serialized and scoped to the native vehicle snapshot. It is
  allocated together with module/subpart IDs, so it is not equivalent to flat part-list index.
- `PartInstance.GlobalInstanceId` is `[XmlIgnore]`. During capture it maps a serialized full part back
  to the live `Part.InstanceId`; `Part.RegenerateFromPartInstance` updates it to the NEW runtime ID
  on load. Use this mapping with a vehicle ID and local ID to bind new runtime objects.
- Native `Vehicle.SerializeLocalIdData` and `CelestialSystem.DeserializeSave` already resolve control
  and target parts by paired native `PartTreeIterator` / `PartInstanceTreeIterator` traversal.
  These iterators walk full-part `TreeChildren`, not subparts.
- For subpart targets, store full-part local ID plus explicit subpart index/path and template identity
  checks; subpart `GlobalInstanceId` is not set by `GetReferenceWithChildren`. Module addresses need
  module type/template plus occurrence index under the resolved part, with strict validation.
- `Part.Id` alone is unsafe: native template instances can share IDs. Serialized part IDs also carry
  Iron Man metadata. Never use substring matching as an identity fallback.
- Do not persist pointers, LookupIndex, object hash codes, GPU handles, material buffer indices,
  camera lease slots or native shape indices. Recreate resources from recipes through owning APIs.

Store template and expected path diagnostics alongside exact identity. If a game/mod update changes
part topology, skip the affected record and explain it rather than applying to a merely similar part.
Docking, staging and debris between saves are naturally handled by capturing current ownership;
restoring a snapshot targets only the topology in that snapshot.

## Threading, ordering and cleanup

`Program.PrepareFrame` waits and applies the previous orbit/vehicle/cloth solver results, processes
input, then dispatches the next cloth/vehicle/orbit solvers. Existing `PhysicsFrameHook` is the
suite's joined handoff. A synchronous full load itself joins workers, and post-load restore can
run before the next dispatch. Saving should read published state; if a provider must mutate or
normalize physics-visible state to capture it, queue that action through the established handoff
or explicitly join first. Never do temporary live-world “undo, serialize, redo” inside a normal
save prefix: it risks discarded solver results, visible flashes, and changed user state on failure.

Suggested restoration phases: reset old ownership; apply global definitions/templates needed for
construction where necessary; native reconstruction; resolve identities; restore exact transforms
and per-object appearance; restore global celestial/render state; restore constraints and long-lived
behaviors; rebuild/coalesce native derived data; recreate secondary cameras/audio; publish status.
If changed celestial mass/parents affect reconstruction, validate whether their definitions need an
earlier phase rather than assuming post-load application is equivalent. Most runtime render recipes
belong after native objects exist. Constraints should validate endpoint existence and acyclic
ordering before any apply.

Stop old music/channels, release viewports, restore private material bindings, remove injected GPU
resources/parts, detach welds, clear animation processors and drop dead-object dictionaries during
reset. Cleanup should be dependency-aware and idempotent. New restores must capture clean native
baselines or use explicitly saved pre-effect baselines; the old world's baselines are not reusable
on newly constructed objects. The last loaded file must remain available for retry/diagnostics
when a partial feature fails, but unknown/unrestored data must not silently become current state.

## File integrity and portability

Native saves are not transactional. `Write` deletes the destination before writing either native
file; `Overwrite` deletes even earlier. A sidecar-only atomic rename protects only that JSON file,
not the whole save. A crash between native write and sidecar completion leaves a vanilla save;
interruption during native XML write can corrupt the native save itself. This must be stated honestly.

The practical first increment should stage/validate mod capture before native Write, bind the sidecar
to XML with a cryptographic hash, write sidecar last, and never reuse a stale sidecar when capture
fails. A later robust full-save transaction can stage all files under a sibling directory and
swap/backup directories, but needs additional native hooks because readonly `Directory` and
`SaveMetaData._path` assume the final path, while `Overwrite` performs early deletion. A backup hook
before native overwrite, with bounded rotation, is less intrusive than replacing native serialization.

Imported PNG/GLB/audio files should be copied by content hash into the save and referenced by safe
relative paths. Resolve paths under the expected asset root; reject absolute paths/traversal and
oversized manifests. Hash validation detects stale/missing imports. Document save-size impact for
large sounds/meshes. Native size accounting enumerates all files recursively, so bundled assets
naturally count if written before its final refresh (outer `Make` refresh happens after Write).

Support explicit envelope/schema versions, per-feature versions, maximum lengths/counts/depth,
finite numeric validation, duplicate identity detection, missing-feature reports, and unknown data
preservation only under a clear roundtrip policy. Do not deserialize arbitrary CLR type names.
A copied whole save directory should be portable; renaming the directory should not invalidate the
content binding. Missing mods or native templates can still make native loading fail before a
sidecar gets a chance to help. A sidecar cannot fix incompatible game-native XML formats.

## UX and edge-case acceptance matrix

| Scenario | Required outcome |
|---|---|
| Ordinary save/load with suite installed | One native save action captures every implemented provider and reports exceptions visibly. |
| Save modified A, modify to B, load A | A restored exactly; B channels/resources/ownership released. |
| Load vanilla save after modded session | Session world modifications reset, no previous-save state leaks. |
| Repeated load/save/load | Idempotent scales/constraints/parts; no duplicate instances or original-value drift. |
| Load while editor is open | Native rejection leaves all feature state unchanged. |
| Bad native XML or failed sidecar preflight | No cleanup before a destructive native transition; clear status. |
| Native reconstruction fails partway | Clear partial-native-load failure; no successful-mod-load claim; context always cleared. |
| Missing feature/asset/object | Valid independent entries restore; detailed skips and retry/export diagnostics. |
| Duplicate vehicle templates or renamed vehicles | Exact snapshot identities, no template-name guessing. |
| Weld chains/debris/docking/disposed source | Resolve full graph, reject bad endpoints/cycles; no stale references. |
| Imported assets moved/deleted outside save | Bundled recipes continue working or explicit missing-asset warning. |
| Sidecar replaced from another save | Native XML hash mismatch prevents applying unrelated state. |
| HUD hidden / paused / high warp | Restore is load-hook driven; no dependency on GUI rendering. |
| Empty/default feature state | Explicit reset semantics; absence never means retain previous session. |
| Save moved/copied/renamed | Whole-directory copy retains assets; no absolute user paths required. |
| Interrupted overwrite | Native risk documented; sidecar never implies whole-save atomicity. |
| Unscience disabled on load | Optional recipes unavailable; connector/template-dependent native exceptions documented. |

No autosave, quicksave or quickload implementation/reference was found by case-insensitive search
of the current KSA decompilation. The existing surface is manual UI and terminal saves. Do not
advertise automatic save coverage as a current native feature; core hooks should naturally cover
future routes that use these same methods. `Universe.LoadSystem(string)` and `LoadDefaultSystem`
construct a new celestial system and reset roster; add lifecycle cleanup there if runtime system
switching becomes supported. On normal startup the mod can initialize empty state; no save replay
should occur simply from save-list scanning.
