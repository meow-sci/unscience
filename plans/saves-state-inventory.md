# Unscience save coverage inventory

Research baseline: KSA 5402, main's 28 shipped submods, September 2026. This is a design inventory,
not a claim that every row is already implemented. The authoritative list is `unscience/Mod.cs`
and its explicit project references. Each feature README and the corresponding `scope/` area
describe its current limitations; abandoned UX code is not required to support persistence.

For the final implemented policies, exact exclusions, recovery limits and pending native checks,
see [Scene save implementation coverage and acceptance](saves-acceptance.md).

## Native baseline and identity

The game already supplies the hard part: a real universe snapshot and native object reconstruction.
`UniverseData.Create` captures game time, main camera, system vehicles and kitten roster.
`CelestialSystem.SerializeSave` enumerates vehicles (including `KittenEva`); it does **not** save
celestial orbit/template changes. `Part.GetReferenceWithChildren` captures full-part template,
position, rotation, scale, stage, sequence data, connections and module save records.

There are critical omissions. Ordinary `Part.Id` is not assigned to its saved `PartInstance`.
Full parts get a saved `GlobalInstanceId`, but the live constructor allocates a new `InstanceId`.
Subpart save records have neither a saved transform nor a global instance id. Therefore a mod's
runtime part id, ordinary part name, template id alone, GPU material handle, module instance id,
or list-selection index is not a durable address. A reference needs system/vehicle id plus the
full-part structural path and subpart path, with expected template/name discriminators; optionally
use the native local/global save-id mapping while constructing the exact snapshot. Ambiguous or
mismatched references must become unresolved records, never silently target another repeated part.

Sources: `../ksa-game-assemblies/current/decomp/KSA/{UniverseData,CelestialSystem,Part,VehicleSaveData}.cs`.
The nominal `decomp/ksa` directory is absent in this checkout; use the adjacent authoritative tree.

## Complete feature coverage matrix

“Native partial” means some resulting game objects/values survive, while the mod's intent,
ownership, baseline or continuation does not. “Global” means process-global today, not that it
should leak from one saved scene into another. User preferences and reusable asset/preset catalogs
can remain global; **applied** scene state belongs to the save.

| Feature | Durable state required | Existing/native coverage | Restore entry point and risks |
|---|---|---|---|
| **Blinky** | Every `(vehicle, grid)` association, dimensions/cell-to-part references, part names, active pixel mask, render meshes flag, scrolling pixels/speed/phase, saved build recipe for later editing | Native partial: created engine parts and module state survive. No manager/scroll metadata. Native omits `pixel_*` ordinary ids, so scan-only recovery is insufficient. | `BlinkyGridManager.Register`, `DisplayStatic`, `StartScroll`; `PixelGrid.BuildFromPartGroups`. Rebind exact existing saved parts, restore names if necessary, repair declared propellant feed links/resource managers with `LcdGridBuilder.RepairFuelFeeds`. Do not recreate an already saved grid. No reconfiguration of fuel mix; native throttle/Auto mode still controls whether pixels can light. Incomplete/damaged pairs need a visible partial-grid report. |
| **Bloomin' Onion** | Body id + detached full `RingDefinition`, painted stripes/noise seed, asset ids, original-ring semantics | Preset TOML persists authored definitions only. Body-template mutations survive same-process reload but not a restart. | `Controller.Apply`, `RemoveAll`; restore definition before Rocky overlays. Capture originals from clean stock, resolve all assets and validate before GPU rebuild. Batch rebuilds where possible. Root-body ecliptic restriction, rings-disabled settings and GPU density budgets remain applicable. |
| **BYO Music** | Target vehicle, copied sound filename, repeat/gap/volume/range; playing/gap/stopped status and optional seek position | Shared copied sound catalog persists; playback and ownership do not. | Construct `VesselSound` after target/audio readiness; add headless registration API to submod. Exclude finished one-shots from auto-restart. Explicitly state whether active sounds restart at beginning or seek. Preserve gap remaining if supported; pause saved playback while loading, no catch-up for offline time. Missing/unsupported file becomes retained error record. |
| **Camera Controller Override** | All animation recipes including parallel groups, durations/easing, keyframe order, return-to-start options; optional current playback/start pose/index/elapsed | Native camera position/controller persists independently; configured sequences do not. | `SequencePlayer.Keyframes` / `AddKeyframe`; explicit tagged animation DTOs. Recreating via `Play` intentionally restarts and captures a new starting offset; it is not exact continuation. Persist authored sequence even when playback is stopped. Resume only after native main camera/target restore; do not serialize controller references or random generator objects. |
| **Doh** | Registry of existing spawned vehicle + character ids, cloned material tint and each named material color/source; editable spawn defaults optional | Native saves the already spawned `KittenEva` and its character/part tree. Registry and cloned GPU colors are lost. | Rebind registry to native kitten; expose existing material-clone path separately from `Spawn`. Recreate unique materials then apply named colors, rebuilding GPU handles. **Never replay spawn requests on load.** Preserve uncolored spawned-kitten ownership so Despawn still works. Seated/despawned kittens and character-layout changes require dormant/unresolved treatment. |
| **Don't Stifle Me** | `EditorScaleSettings.Enabled`, `Snap`, `EditorLimitSettings.JplSaidNoClamps` | Native part dimensions/configurable values may save; editor policy flags are session-only. | Assign known settings. Distinguish saved scene policy from global user preference explicitly; do not reset actual saved part dimensions. |
| **Eternal Flame** | Monitored vehicle ids, per-vehicle fuel/electric toggles, `RefillIntervalMs` | Native records current consumables, not future refill policy. | `FuelManager.AddVehicle` / monitoring list and interval. Timer can restart with no catch-up. Restore before the next solver snapshot; defer unresolved/debris targets by identity. |
| **Garry's Torch** | Source/target/target-part ids, offsets/XYZ scale, rotation lock, collisions, enabled; animation active/queued recipes and progress; original per-full-part scales and kitten avatar scale | TOML weld presets exist. Native saves already transformed source parts/pose, not weld ownership or the baseline. | `CreateWeld`, `ModifyWeld`, animation manager, safe physics handoff. Restore baseline snapshots explicitly before replay or import baseline into new ownership without applying twice. Disabled welds retain their data; chains need topological ordering/cycle rejection. Source cannot simultaneously be Godzilla-owned. Source/target parent mismatch and staging/debris must follow existing cleanup. |
| **Godzilla** | Per-vehicle Smart/Basic mode, factors, both physics/collider channels; original full/subpart scale, full-part layout, pivot and avatar baseline | Native saves physical full-part transforms but not visual-only patches, subpart scales, collision ownership or original snapshots. | `RequestApply`, `RequestRestore` plus explicit snapshot import/export. Smart scaling on the loaded result otherwise multiplies twice; Basic destroys information so division is not a general inverse. Restore must preserve original custom parts/animated subparts and nondefault kitten size. Topology mismatch must not mutate detached pieces. |
| **Glass** | Enabled flag + FOV degrees | Native camera may retain current FOV, but override policy is not retained. | `FovController.SetFov` / `DisableOverride`. Apply to main camera after its creation, not mounted secondary cameras. |
| **Graffiti** | Every decal's image, anchor kind, target/part path, local pose, dimensions/depth/alpha/brightness/visible; terrain lat/lon; parachute canopy index, triangle indices, barycentric weights and normal sign | PNG catalog persists. Entries and GPU resources do not. | Add direct validated entry registration alongside `PlaceAtCursor`; restore through existing texture cache and per-frame resolver. Never redo cursor picking. Recompute terrain radius/cache and matrices. Parachute runtime module id cannot be reused; bind by durable parent + canopy index and validate cloth topology. Stowed chutes/missing assets remain dormant, not deleted. |
| **Free Fallin** | Applied enabled flag and exact `CanopyMaterialSettings`: texture mode/name, tint/brightness, full-canopy rotation, centered scale and PBR controls | Copied PNG persists; global material handle/settings do not. Current UI draft can differ from last applied settings. | Add detached applied-state capture to `CanopyMaterialController`; `Apply`/`RestoreStock`. Save last successfully applied recipe, not form values. Rebuild images/material handles; failed replacement must retain prior successful state. |
| **Hot Pursuit** | Per-camera durable part target, mount point/normal/tangent, translation/rotation, FOV/resolution, visible and closed/dormant intent | Session-only; four shared secondary viewport slots available. | Headless entry registration + existing lease/rebind path. Never serialize viewport index, owner token or camera object. Slot shortage should keep entries dormant with reopen/retry status; respect intentionally closed cameras and hidden-camera lease semantics. |
| **Humble Arteest** | Kitten material colors keyed by material identity, visor-hidden flag; global/per-engine emissive temperature/TFI; experimental paint global/template/part colors and blend mode | GPU/material/render overrides are not native save data. Vehicle Paint is deliberately unavailable on current KSA. | `KittenColor.ApplyToMaterial` via re-resolved names (not handles), visor setting; `EngineEmissive.SetEngine`, global settings; `VehiclePaint` APIs. Add capture ledgers where only direct GPU writes exist. Preserve unavailable paint data but report inactive, never imply working rendering. Material clones from Doh must be initialized first; engine model references need durable part/model mapping. |
| **I Feel Seen** | Tracked vehicle ids and `SeeMe` flags | Session-only render tracking. | `VehicleTracker.AddVehicle`, then set flag. Resolve native loaded vehicle; no saved camera-relative render positions. |
| **Iron Man** | Configured kitten set, explicit EVA versus Iron Man flight mode, mode-specific control settings/baselines; connector edits | Existing `IronManConnectorPatches` embeds versioned connector payload in `PartInstance.Id` before serialization and decodes during part construction. Native equipment/character already save. Flight mode currently resets to EVA. | Preserve existing early constructor support; rebind native kitten then invoke mode/configuration paths at safe handoff. Do not author default connectors over loaded custom nodes. Flight snapshot distinguishes user rocket settings from captured native EVA state. Save without Unscience can lose required connection semantics; dependencies must be visible. |
| **Its So Shiny** | Grid associations/cell references and names, appearance, active mask, scroll data/phase and build recipe | Native partial: created light parts survive, not manager or `shiny_*` ids. Appearance currently writes shared templates. | `ShinyGridManager.Register`, `SetAppearance`, `DisplayStatic`, `StartScroll`. Rebind existing parts, rebuild light power connections if needed. Replaying each grid's differing colors cannot create isolation the existing shared-template implementation lacks; capture effective template values once and distinguish intended per-grid appearance. |
| **Kitchen Sink** | Force IVA rendering enabled flag | Editor repair button has no ongoing state; native captures any resulting ordinary transforms/configuration. IVA templates are process-global mutable state. | `IvaForceRender.Enable/Disable` equivalent existing API; cleanup must restore original `Template.Internal`. Do not replay “fix invisible subparts” as a scene operation. |
| **Kitten Animations** | Follow-controlled versus pinned target, selected clip stable catalog key, override/paused/rate/blend, ear/eye/personality/reactive controls; expression type/variant/timing/latch; modified locomotion tuning values | Native locomotion reconstructs itself. Forced clip, owned processors, expression and global tuning are not preserved. | Expose driver/expression detached state and explicit catalog lookup. Bind target first, then `Play`, strengths, expression. Preserve captured tuning baseline for reset. A one-shot expression needs saved remaining phase or documented restart; missing/replaced clip must not choose another by list index. |
| **Kiwi's Marbles** | Source body + tagged celestial/vehicle target, offset, original orbit and parent | Native universe saves vehicles, not celestial orbit mutations or weld list. | Expose weld create/import/remove through engine/submod. Build/reparent in topological order before source-dependent scene effects. Restored original orbit should be native clean orbit or explicit serialized orbital elements/epoch; never preserve a live `Orbit` pointer. Cross-parent body children and descendants require existing refresh logic. Reject cycles and impossible self/root configurations. |
| **Parts Now** | Referenced installed bundle/mod ids, manifest/path/content identity, required templates and asset dependency status | Installs write folders/manifest; settings TOML persists headroom/hotkey. Live loader jobs/registry/memory are session state. | Preflight dependency presence **before native load** resolves template ids. Prefer boot-installed bundle reuse; runtime load must finish before native reconstruction. Never replay install/overwrite/unload blindly. In-progress jobs cannot be snapshotted. Headroom requires restart; live reload leaks shared mesh allocation by design. |
| **Pebbles** | Each `ClutterLiveRecord.BodyId` + detached complete recipe; selected types, meshes/materials, custom colliders, distribution and budgets; important exclusion masks for replaced/destroyed clutter | Session-owned records; shared GLB catalog persists. Runtime stores original/owned template and renderer arrays. | `Controller.Live`/`Capture`, `QueueApply`, `QueueRestore`; validate and resolve GLBs before safe CPU/GPU transaction. Capture applied recipe, not workshop draft. Exclusion masks are keyed by ecotype name + separation and must only replay on compatible grids; recipe-only replay can resurrect destroyed clutter. Renderer-disabled is deferred status. Rings and Pebbles template cloning must not overwrite each other's fields during cleanup. |
| **Pyro** | Plume anchor/settings/enabled; nozzle, template id, absorption/refraction; cyclic on/off timing/phase; **shared exhaust-template edits** | Presets persist individual plume recipes only, deliberately omit cycle. Shared template edits currently last until restart and affect real engines too. | `CreatePlume`/`ApplyPreset`; reconstruct exhaust instance after template edits. Capture whitelisted edited template fields in `PyroSubmod.TemplateUi` (absorption/emission gradient/Mach diamonds/noise/length/quality), store originals for scene cleanup, call `TemplateRefresher.NotifyTemplateChanged`. Cycle uses simulation time; rebase phase to loaded time. Do not serialize exhaust transient objects. |
| **Rocky McRock Face** | Applied per-body `RingSelection`, LOD mesh ids, material/band textures, size/density/distance/thickness | Mutates XML-backed live ring references; not native save. UI selections may be uncommitted drafts. | `RingSwapController.Apply`/`Restore` after Bloomin; add successful-apply ledger because original snapshot map alone is insufficient. Restore overlay before removing underlying custom ring. Resolve assets before swapping and rebuild renderer once where feasible. |
| **Skittles** | Active theme or exact edited style (including unsaved colors/variables), if scene snapshots include UI preferences | Theme/config TOML already persists active named theme; unsaved editor changes do not. | `ThemeDefinition.CaptureFromImGui`/`ApplyToImGui`; keep global preference storage separate from save-scene application. UI layouts/filters are optional convenience, not a prerequisite for restoring game modifications. |
| **Sphinx** | Body geodetic anchor, GLB identity/name, PNG override, visibility/alignment, XYZ transform, UV mapping, collision mode | GLB/PNG files persist, placements/resources do not. | Add detached entry capture/import to existing queued placement path. Resolve/import model then recreate render/physics resources; recalculate terrain/frame/bounds. Never serialize native Bepu handles, pointers or buffers. Preserve collision budgets, wrong-body checks, safe retirement and last-successful transform on failures. |
| **Thug Life** | Durable part/subpart anchor, position/rotation/size/visible, optional slide start/end/duration/elapsed | Runtime entries only; texture generated by code. | `ThugLifeRenderManager` add/remove entry path, lazy GPU init after renderer readiness. Save sampled current pose even if slide continuation is deferred; no render-resource payload. |
| **Zippo** | Ordinary applied template light colors/intensity and part switch state; queued transitions/progress; per-light Disco cloned recipes/paused/enabled/elapsed/seed and channel phase; ownership baselines | Native saves some switch/actuator values; ordinary color/intensity edits change shared templates and do not save. Disco templates are instance-private. | `SetLightState`, `QueueAnimation`; expose Disco start/import and active enumeration. Save template-wide ordinary effective values once, then apply instance Disco clones. Same light cannot run queue and Disco. Each Disco instance randomly selects a seed and channel offsets, so persist them rather than generating another pattern on restore. Preserve baseline switch/actuator values to make Stop restore the original rather than the loaded animation frame. |

## Recommended restore transaction

1. Parse/version-check bounded payload; resolve asset identities and required runtime bundles before
   touching the active scene. Validate finite vectors, enum values, entry counts, payload size,
   relative asset paths, duplicate ids and dependency graphs. Retain unknown feature sections.
2. Stop authored continuations and wait for native orbit/vehicle/cloth workers before scene teardown.
   Restore process-global template overrides in reverse dependency order, release viewport/audio/
   render/physics ownership and invalidate queued work using a scene generation token. Merely clearing
   a dictionary leaks changes and loses cleanup baselines.
3. Let native save loading recreate universe vehicles, parts, modules, camera and time. Do not rerun
   spawns, grid creation, teleports or editor actions that native data already represents.
4. Rebind all durable references and native-owned feature registries. Restore explicit scale/layout
   baselines, custom subpart state and module/connector prerequisites at a safe physics handoff.
5. Apply shared templates/material defaults; establish celestial welds; construct Bloomin rings then
   Rocky overlays, and Pebbles without clobbering other template changes. Build asset-backed statics
   and decals only when renderer resources are available. Doh material clones precede per-material edits.
6. Restore physical scaling/weld ownership with cycle and conflict checks. Capture/import original
   snapshots before applying any multiplier. Native full-part values are already the saved effective
   values; repeated load must neither compound them nor make “Restore original” restore a modified size.
7. Restore switches/refill/render policies, authored animation recipes, camera leases and audio.
   Activate continuations only after every required target is ready. Preserve elapsed phase; do not
   advance by wall time spent in the loading screen or with the game closed.
8. Present one result with restored/deferred/unavailable/failed counts and actionable reasons. Keep
   unresolved records in the scene document so save-after-partial-load does not erase recoverable work.
   Retry missing optional resources on request/readiness rather than every frame without a bound.

## Capture and smooth user behavior

Use the normal save/load controls as the primary path. Capture the game and mod snapshot together
at a safe frame; asynchronous disk writing must receive detached immutable data. A separate Unscience
scene export/import can reuse the same adapters, but “apply to current universe” is a different operation
from loading its native universe and needs explicit replace/merge semantics.

The per-feature contract should expose stable feature id, schema version, capture/validate/restore/clear
operations and typed data models. Do not reflectively serialize `ISubmod`: its private fields include
UI drafts, unmanaged ownership, live objects and caches, and omit global direct mutations. Reuse useful
runtime model abstractions from the old UX branch without porting its shell or UI design.

Pending UI edits require a defined capture boundary: drain already accepted safe-frame requests before
capture, or capture last applied state and visibly indicate the pending work. A renderer/asset job still
in progress cannot be represented as a successful applied entry. Save cancellation/failure must not
replace the previous valid mod payload; rename/copy/compress/decompress must keep native and mod
data paired. An old save with no extension should clear the previous scene's applied mod state.

Original baselines are user data, not disposable caches: scale/layout, original light/actuator values,
original global template values and celestial orbit/parent matter to an accurate undo after load.
Where a clean native reconstruction supplies the original (e.g. stock rings), recapture it after old
scene cleanup. Where native serializes the modified result (e.g. part scaling or actuator position),
store the original separately or adjust the detached native serialization; inverse arithmetic cannot
recover overwritten Basic scales or original light settings.

Asset catalogs should remain reusable and avoid embedding GPU data. Save a logical catalog name plus
content hash and required mod/template identity. A portable export can include copied PNG/GLB/sound
bytes and installed-bundle manifest; normal saves may reference the local immutable copied catalog.
Missing files should yield recoverable unresolved entries. Never silently substitute a same-name file
whose content changed, nor permit payload paths outside approved catalogs.

## Acceptance matrix

- Each populated feature: save, change, reload same process; save, restart game, load; load twice.
- Empty/native old save after heavily modified scene: no surviving template/light/ring/IVA state.
- Repeated templates, nested animated subparts, duplicated vehicle part names: exact anchor rebinding.
- Blinky/Shiny grids: no extra parts, cell identities retained, fuel/power wiring restored, active pattern.
- Doh: no duplicate kittens; restored registry supports recolor/despawn; per-material color isolation.
- Garry/Godzilla: factor stays constant after repeated loads; original restore is exact; custom initial
  scales, Basic mode, nonuniform XYZ, four physics/collider modes, kittens, staging and animated subparts.
- Ring+Rocky+Pebbles on same celestial, world reload and renderer restart: dependency cleanup intact.
- Animations during save: paused/running/queued, phase jitter, warp, zero delta, no offline catch-up.
- Missing target/template/asset, changed cloth topology, no free viewport slot: retained informative records.
- Active native load failure/corrupt extension/disk failure: no silent overwrite or success claim.
- F2 hidden HUD, paused simulation, solver work in flight, GPU recreation, unload with pending restore.
- Unsupported/newer feature schema and partial restore followed by save: opaque records survive.

Managed round-trip and integration-fixture tests can establish schema, identity, ordering, ownership and
failure semantics. Only an actual KSA run establishes physics, Vulkan materials, camera leases and audio
correctness; compilation alone cannot substantiate complete save support.
