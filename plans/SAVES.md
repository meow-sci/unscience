# Unscience save/load implementation plan

## Outcome

Ordinary KSA saves should remember Unscience scene setups and restore them when loaded, without a second save browser or the abandoned UX redesign. Implement on `feature/saves`. Persist detached, versioned feature data beside `universe.xml`; let KSA remain responsible for vehicles, crew, native module state and native camera pose. Keep existing global presets, themes and imported libraries in their current locations.

Research evidence is collected in [native lifecycle](saves-game-lifecycle.md), [feature coverage](saves-state-inventory.md), and [runtime-model assessment](saves-runtime-models.md). The authoritative source baseline is the adjacent `ksa-game-assemblies/current/decomp` tree (KSA 5402), not the older repository decomp snapshot.

## Architecture and sequencing

1. Add a small shared save-participant contract and explicit DTO JSON helpers to ksa-abstractions.lib. Every persisted feature owns capture, cleanup, validation and replay through its normal APIs. Never serialize an arbitrary live object graph, delegates, graphics handles, raw pointers, template instances or reflection caches.
2. Store one versioned `unscience.json` in the native save directory. Capture with native Populate, then write only after native Write succeeds. Bind the sidecar to SHA-256 of the actual universe.xml; write via flushed temporary file and atomic replacement. Native overwrite deletes its directory first: our extension must not imply that KSA's entire save operation becomes atomic. Include bounded file/payload sizes and explicit diagnostics for corrupt, unsupported and mismatched saves.
3. Scope incoming state to the actual UncompressedSave.Load operation. Preflight before destructive load. At Universe.DeserializeSave, wait for native orbit/vehicle/cloth jobs, clear queued old-world mutations and reset old feature ownership before native objects are destroyed. Restore after synchronous native reconstruction, before the next simulation dispatch. Direct/native loads without Unscience data still clear old scene state. Clear load context in a finalizer on success or exception. Handle LoadSystem/new universe as a separate reset boundary.
4. Use vehicle IDs plus full-part tree paths and subpart paths with template validation. Runtime Part.InstanceId and ordinary Part.Id are not durable identifiers. Never substitute the controlled vehicle or a similar part when a target cannot be found. Report unresolved targets by feature.
5. Preserve baseline data for operations with undo/restore semantics. Native saves already contain transformed full-part geometry and created grid parts: rebind ownership and restore captured original baselines rather than multiplying again or respawning duplicates. Planet/template changes need cleanup because native load reuses celestial objects.
6. Restore in dependency order: global material/planet settings; native-created object ownership and scale baselines; relationships/welds; per-object appearance; placements/cameras; playback configuration. Preflight runtime part-template dependencies before native reconstruction; external PNG/GLB/audio resources remain explicit dependencies and missing files produce actionable diagnostics. Do not silently import arbitrary paths or recreate missing native vehicles.
7. Retain unknown/future feature records so an older build does not silently discard them on save. Isolate feature failures, preserve their original payload for a future compatible restore, and make partial restoration visible. Reject future whole-document versions rather than guessing. Native save/load remains available if a sidecar is unusable.
8. Add a compact Saves status section to the existing toolbox, showing last capture/restore result and warnings; normal KSA Save/Load is the user workflow. Keep window-layout autosave clearly separate from scene saving. Avoid auto-playing sound or resuming one-shot animations without a deliberate policy; preserve reusable configuration and make restart/pause behavior explicit.

## Feature coverage and implementation checkpoints

- Research checkpoint: commit this plan and the three investigations.
- Foundation checkpoint: contracts, bounded/versioned storage, identity resolution, native lifecycle integration and status UI; tests for corruption, unsupported versions, hash mismatch, missing sidecars, unknown blocks, exceptions and stale context.
- Vehicle checkpoint: fuel/render tracking, welds and scale baselines, celestial welds, grid ownership/configuration, kitten/native ownership and Iron Man activation.
- Appearance/world checkpoint: lights/Disco, material and visor changes, plumes, decals/parachutes, rings/clutter/statics and custom quads.
- Camera/settings checkpoint: mounted cameras, lens, reusable animation settings, music configuration and global feature preferences. Global preset/import libraries are dependencies, not duplicate save-owned copies.
- Validation checkpoint: run meaningful managed round-trip and lifecycle tests, full `dotnet build`, inspect coverage against all 28 registrations, update root/project README files and scope indexes. Commit stable groups using the git-commit skill.

## Required edge-case checks

- Save A → edit → load A; A → B → A; Unscience save → vanilla save; repeated load and overwrite; duplicate native vehicle names/parts; missing/deleted/debris targets; UI hidden (F2); editor-refused load; no loaded universe.
- Native save failure, sidecar write failure, truncated JSON, duplicate feature IDs, oversized/deep data, invalid numbers, unknown schema/feature versions, stale copied sidecar, missing assets and templates; never replace a good record with a falsely successful empty record.
- Scaled/welded craft retains exact size and Restore/Unweld returns to the original; grids retain part count and cell mapping; transformations do not compound. Native kitten spawn and Iron Man connectors are not duplicated.
- Reset old-world mutation queues and active players; no stale GPU/FMOD/viewport ownership; restore repeated effects through normal lifetime APIs. Deferred renderer/physics operations must not run against a later world.
- Ring and clutter restoration across reused celestial objects, including original template ownership; weld dependency cycles and parent mismatches; secondary-camera slot exhaustion; missing PNG/GLB/sound dependencies; save transfer to another computer.
- Preserve feature failures visibly and document actual supported recovery behavior rather than claiming an exact simulation checkpoint. GPU/native in-game validation remains a separate acceptance pass from managed compilation/tests.

## Scope boundaries

This is scene/setup persistence, not a replacement physics serializer, a portable asset bundle, or an import of the new-UX shell. Reuse explicit runtime DTO/validation ideas from that branch selectively. The final coverage report must describe any unsupported/transient state precisely; implementation should cover every durable setup that can be reconstructed safely and expose limitations to users.

## Implementation refinement from managed and graphics review

The initial synchronous caller plan was refined: WHOLE world replacement is deferred to the
next PhysicsFrameHook boundary before GetJobSimStep. Console loads occur after current ImGui
texture references can be emitted; deferred cleanup avoids invalidating them. Native load finalizers
still clear transaction state on failure. Explicit serializer metadata excludes writable vector
swizzles, and primitive converters reject numeric-overflow infinities. Managed round-trips caught
both issues before delivery. Weld/light authored queues preserve active start/elapsed and queued
recipes, continuing without offline catch-up; camera sequences restore stopped and audio paused.

## Implemented result and verification

All 28 bundled submods expose explicit save adapters (29 records including Pyro shared templates).
Native saves carry hash-bound sidecars; loads reset/rebind at the safe frame boundary. Required
native dependencies are checked before destructive reset; deferred failures reach the toolbox.
Missing or incompatible feature data remains recoverable with explicit current-setup replacement.
The abandoned UX branch informed detached state/validation patterns; its UX was not imported.

The full solution builds with zero warnings/errors and the 12 managed suites listed in
[the final coverage report](saves-acceptance.md) pass. That report is the implementation specification
for playback policies, unsupported/transient state, external assets and retained-record tradeoffs.
Its native acceptance checklist remains open: this macOS ARM workspace cannot execute the x64
KSA native graphics/physics runtime. No claim of live-game acceptance is made from fixture tests.
