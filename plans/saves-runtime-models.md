# Save persistence: assessment of the abandoned UX branch

Research baseline: main `72add8e`, `feature/new-ux` `5d485cd`, common ancestor
`e148a60`. The UX branch has 11 unique commits and main has 33 at assessment time.
Inspected with `git show` and `git diff`; the branch was not checked out or merged.

## Conclusion

The branch contains useful persistence and ownership patterns, but it does **not**
already solve restoring active Unscience modifications. Its contract deliberately
persists authoring forms independently of applied effects. Its
`docs/WORKSPACE.md` explicitly says “Runtime records are session data, not game-save
persistence.” Loading a workspace leaves welds, decals, rings, lights and playback
untouched. A save feature should borrow selected data boundaries, then implement
new capture/apply adapters over main's current runtime owners.

Keep main's existing UI and projects. Do not cherry-pick the large UX commits:
`f5c12d6` changes feature presentation, standalone distribution, library boundaries,
patch ownership and runtime state together. The branch also removes Blinky and
many historical projects. Main subsequently added Sphinx, Iron Man, Godzilla,
shared imported-file catalogs, new light behavior, current scale ownership and
vehicle weld fixes. Replacing branch files wholesale would lose these changes.

## Reusable patterns and their limits

| Branch source | Useful idea | Required adaptation for actual saves |
|---|---|---|
| `unscience-contracts.lib/DraftState.cs` | Game-independent envelopes, stable feature keys, detached JSON snapshots, explicit participant interface | Save **applied** recipes and exact targets. Do not confuse current form fields with previously applied values. Give each feature payload its own schema version. |
| `unscience-contracts.lib/WorkspaceStore.cs` | Bounded documents, temporary file plus flush/replace, previous-file backup, malformed-file reporting | Bind data to the matching KSA save generation; two independent file renames do not make a native-save/sidecar transaction. Handle write failures explicitly. Preserve unknown payloads. |
| `unscience-contracts.lib/WorkspaceRestore.cs` | Decode/validate every participant before assignment; capture rollback state first | Native actions are not simple setters. Schedule at safe game phases, retain failed work and diagnostics, and prevent a rollback exception from stopping other cleanup or masking the original failure. |
| `ksa-abstractions.lib/Workspace/DraftBindings.cs` | Explicit typed registration instead of serializing live object graphs; detached validation | Useful as a recipe-authoring reference, but this file depends on ImGui and mixes filters/scroll state with values. A save core should have no ImGui dependency. |
| `ksa-abstractions.lib/Workspace/PartIdentity.cs` | Vehicle identity plus topology fingerprint and root/subpart path; never persist a live list index | Remove its ImGui frame dependency. Verify fingerprint fields against current native save/load. Reject ambiguous or changed topology rather than selecting the first matching template. |
| `camera-controller-override.lib/Animation/AnimationRecipe.cs` | Tagged animation data reconstructs behavior without serializing delegates/controllers | Useful for sequence recipes. Running sequence state additionally needs time basis, cursor, original camera pose, target resolution and restart/resume semantics. Prefer named parameters over positional numeric arrays for future migrations. |
| `free-fallin.lib/CanopyMaterialController.cs` | `AppliedSettings` is a detached clone committed after successful GPU application | Strong model for singleton overrides: capture successfully applied state, not the UI draft; allocate replacements before releasing originals. Port against main's current shared PNG library. |
| `pyro.lib/PyroSubmod.TemplateDraft.cs` | Records template ID, baseline and applied recipe separately | Persist modified shared template recipes once per template; rebuild resources rather than serializing native handles. Baselines across native load require an explicit policy. |
| `unscience-contracts.lib/RuntimeActivation.cs` and `SharedRestoration.cs` | Failed release retains ownership; multiple owners share one baseline; retry is deliberate | Useful lifecycle behavior, especially after partial restore. Dynamic patch unloading itself is unrelated to saves and need not be ported. |
| `kiwis-marbles.lib/KiwisMarblesSubmod.cs` | Failed orbit restoration remains queued instead of being discarded | Native restore needs a safe solver phase and retryable completion; do not report load complete merely because work was queued. |

`LiveIdentity` is specifically **not** a durable save identity: it uses a weak-table
random GUID associated with a runtime object. Likewise, `ILiveStateItem` is an
inspector projection containing an object and render delegate, not a serializable
runtime contract. `*.Workspace.cs` files typically capture pending fields and
target selections; `GarrysTorchSubmod.Workspace.cs`, for example, captures pending
offset/rotation/scale but never serializes the active `_welds` collection.

## Target and ownership implications

The branch documents that KSA regenerates `Part.InstanceId` when loading vehicles.
Its fallback encodes vehicle ID, SHA-256 of paths/template IDs/part IDs, and a
root/subpart path. Changed topology becomes unresolved. This is conservative but
cannot distinguish an identical replacement at the same path in a vehicle with
the same identity. Editor-only parts use a session key and cannot survive restart.
The save implementation should capture a target map at native save time and resolve
against the newly loaded universe, with tests for root reordering, identical parts,
subparts, removed vehicles and duplicate IDs. A saved “controlled vehicle” choice
is suitable for a reusable recipe, but applied effects require the concrete vehicle
that actually owned the effect at save time.

Runtime state consists of distinct categories:

1. **Desired configuration**: detached values sufficient to recreate an effect.
2. **Bindings**: durable identities resolving to a vehicle/body/part/module/asset.
3. **Native resources**: material handles, meshes, Bepu shapes, renderer bindings and
   delegates; reconstruct after load, never serialize.
4. **Ownership baselines**: values needed to undo an effect later. Decide per field
   whether native saves already carry modified values, otherwise a post-load
   snapshot can accidentally treat the modification as the original baseline.
5. **Progress**: animation timing and queue state. Preserve only well-defined state;
   do not replay destructive commands or mouse gestures.

Multiple features can affect a shared object. Ring creation precedes a ring mesh
overlay; imported parts must exist before native vehicle deserialization; weld
recreation must respect current Godzilla scale ownership. Restoration order belongs
in an explicit dependency/phase contract, not incidental submod UI ordering.

Imported assets need content identity and availability checks. Rebuilding from a
filename whose content changed can create a materially different scene. Main's
shared GLB catalog and frozen path/hash mesh IDs already improve on the branch;
retain them. Copy/share workflows need either an asset manifest or a clear missing
asset report. Import file persistence alone does not persist where effects were
applied.

## Suggested extraction approach

Add a small game-independent save contract/store and a game-facing coordinator.
Each feature provides explicit capture, validate/resolve, clear and apply adapters
against its existing runtime owner. A feature's saved result should be detached
and safe to inspect without invoking the game. Preserve unsupported or temporarily
unresolved payloads across save cycles so a missing mod or asset does not silently
erase user work. Surface per-feature restored/skipped/failed status in the existing
Unscience panel.

Do not port the branch's workspace windows, form layout, visibility redesign,
mandatory Apply buttons, distribution changes, feature removals or dynamic patch
ownership. Small recipe/ownership improvements should be reviewed independently
and kept only where needed to make capture and reconstruction truthful.

## Verification strategy

Current `Directory.Build.props` targets `net10.0`, language version 13, nullable
enabled and warnings as errors. Existing `.tests` projects are executable managed
checks rather than an external test framework. `pebbles.tests` links production
models/file catalogs; `godzilla.tests` and `iron-man.tests` link production code
against small KSA fixtures with real Brutal numerics/Harmony as needed. Follow
this established pattern for the save core and integration patches.

The branch's `unscience-contracts.tests/Program.cs` is a useful starting checklist:
detached capture, input immutability, Unicode name collisions, overwrite identity,
backup, unknown feature payloads, malformed documents, prepare-only behavior,
missing-feature defaults and rollback. Its runtime ownership tests cover failed
activation, cleanup retry, idempotent release and shared baseline restoration.
These tests establish authoring persistence only, so they are insufficient for
runtime scene restoration.

Add behavioral tests covering:

- Saving takes one detached snapshot; later runtime edits do not mutate it.
- Native-save generation matching rejects stale metadata after failed/overwritten
  saves and allows intentional copies under their documented policy.
- Unsupported versions, null required fields, oversized input, non-finite vectors,
  unknown feature payloads and duplicate stable identities have explicit outcomes.
- Every target resolves exactly; missing/ambiguous targets never become another
  craft, part, body or asset. Load against newly allocated instances.
- Repeated load replaces prior mod-owned state without duplicating welds, plumes,
  statics, decals or shared policies. Loading a vanilla save clears prior effects.
- Preparation failures cannot mutate live state; partial application failure
  preserves diagnostics and retryable ownership without deleting retained payloads.
- Native job boundaries and deferred work complete before the coordinator reports
  success. Hide the UI/HUD and verify the same lifecycle.
- Effects already represented in native saves do not double-apply on restoration,
  especially scaling, orbit mutations, light/module values and custom EVA nodes.
- Animation pause/resume/time-warp/backward-clock behavior follows the documented
  save policy; no stale gesture, teleport, ignition or one-shot command is replayed.
- Missing or changed assets skip only affected items with recoverable details.
- Round-trip each supported feature through **production** capture/apply code;
  core-schema round-trips alone cannot establish feature coverage.

Required compilation is `dotnet build` (with a writable distribution override such
as `-p:UNSCIENCE_DIST_DIR=/tmp/unscience-saves-dist` when needed). Managed tests do
not prove native KSA save/load, rendering, GPU retirement, Bepu collision behavior
or scheduler phase safety; retain a concrete in-game acceptance matrix for those.
