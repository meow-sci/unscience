# Scene save implementation coverage and native acceptance

Implementation baseline: `feature/saves`, KSA 5402 reference assemblies, September 2026.
Current compatibility: **5482** (`2026.9.22.5482`, upgraded from 5438), including Kitchen Sink
G-load records and Dent Wizard. The 5482 fixes outside the separately tracked ground-clutter work
change no save ID, version or payload. Against the 5482 reference assemblies, the checkout builds
**74 solution projects** with no warnings or errors, and all **15 managed suites** pass on macOS.
The preceding Kitchen Sink reconciliation passed **71 projects and 14 managed suites** on Windows;
Kitchen Sink contributes 54 damage/lifecycle checks and 26 real-adapter save/restore checks.
Both version-1 records remain unchanged. See [reconciliation evidence](KSA_5438_RECONCILIATION.md).
This report describes the implemented adapters and their limits. The earlier
[state inventory](saves-state-inventory.md) is a research assessment of desired coverage,
not the final implementation specification. See [SAVES.md](SAVES.md) for architecture and
[native lifecycle research](saves-game-lifecycle.md) for the game-side evidence.

**Native acceptance is pending.** Managed builds and fixture tests cannot establish that
Vulkan rendering, FMOD playback, native physics, cloth, or actual KSA loading behaves correctly.
The original implementation checks ran on macOS ARM; the 5438 reconciliation ran managed checks
on Windows with a matching installed game, without launching it. The 5482 upgrade ran managed
checks on macOS ARM64 only; the supplied KSA.dll is Windows x64, so the game was not launched.
Run the checklist below in KSA before describing scene restoration as accepted.

The combined Dent Wizard / Kitchen Sink checkout is covered by the
[Dent Wizard integration record](DENT_WIZARD_UPSTREAM_INTEGRATION.md). Historical Windows and
feature/saves test totals below remain attributed to their original runs.

## What a save represents

Use ordinary KSA Save / Load. The Unscience host writes a versioned `unscience.json` alongside
the native `universe.xml`, bound to that file's SHA-256. The native save owns vehicles, kittens,
full-part geometry, native module records, camera pose and game time. The sidecar supplies
Unscience recipes, target ownership, original baselines and the continuation state listed below.
The integration is wired by [UnscienceSaves](../unscience/UnscienceSaves.cs) for the
[29 registered submods](../unscience/Mod.cs). There are **31 feature records**: Pyro separates
shared template edits from plume instances, and Kitchen Sink keeps G-load registrations in a
separate backward-compatible record alongside its original IVA boolean. Standalone mod entrypoints do not acquire
this host workflow merely because their shared library exposes a participant.

This is durable scene setup restoration, not a complete simulation checkpoint. Native objects
are reconstructed and external rendering/audio resources are recreated or rebound. Offline time
does not advance saved Unscience animations. Physical contacts, solver internals, cloth deformation,
audio sample position and every transient UI operation are not serialized by these adapters.
Some animated setups resume their sampled phase; others deliberately stop, as specified below.

Whole-world replacement is deferred to the early physics-frame boundary. Old native jobs join,
including KSA 5482's `JobSystems.NearestOrbitAndPerformanceWorker` (renamed from `ConcurrentWorkers`).
`PrepareFrame` re-queues the nearest-orbit job just before the handoff, so the explicit join is
still required. Old feature ownership and pending mutations are cleared, KSA reconstructs its world, and adapters
restore in dependency order before new simulation dispatch. This avoids destroying resources after
the current ImGui frame has already referenced them. The extra frame and all native lifecycle
assumptions still require the in-game checks below.

## Implemented coverage, all 29 submods

Each source link is the adapter defining the current capture/reset/restore policy. “Restores”
below describes implemented behavior, not an assertion that native acceptance has passed.

| Submod | Captured setup and restoration | Continuation, exclusions and dependencies |
|---|---|---|
| [Blinky](../blinky.lib/BlinkySubmod.Persistence.cs) | Named grid ownership, exact existing A/B engine-cell addresses, rows/columns, sparse active mask, render flag, scroll pixels/speed/offset, pending grid deletion. Rebinds native-created parts and repairs fuel feeds. | Never spawns a second grid. Preserves native ignition/module state; scrolling resumes its offset. Pending deletion restarts its delay against new references. Arbitrary unfinished build/editor input is not captured. Missing/duplicate cells reject that grid with a warning. |
| [Bloomin' Onion](../bloomin-onion.lib/BloominOnionSubmod.Saves.cs) | Applied body-to-ring definitions, including procedural band configuration, stripes and asset selections. Resets owned rings then reapplies definitions before Rocky overlays. | Applied definitions only; unsaved editor drafts and preset-library files are separate. Referenced meshes/textures and normal controller restrictions still apply. |
| [BYO Music](../byo-music.lib/ByoMusicSubmod.Saves.cs) | Unfinished sounds' vehicle targets, filenames, repeat, gap, volume and range. | Every restored sound is paused. Resume starts at the beginning; sample position, current gap progress and former paused/playing distinction are not restored. Finished sounds are omitted. Sound files remain required. |
| [Camera Controller Override](../camera-controller-override.lib/CameraControllerOverrideSubmod.Saves.cs) | Ordered typed keyframe recipes, parallel groups, pending group recipes, return-to-start duration/easing/powers. | Restores stopped. Current running segment, elapsed time and its captured start pose are not resumed. Native camera pose loads independently. Pressing Play starts the reconstructed sequence through its normal behavior. |
| [Dent Wizard](../dent-wizard.lib/DentWizardSubmod.cs) | KSA saves the launched vessel's resulting orbit/physical state; v1 `dent-wizard` is an empty lifecycle record. Reset cancels automatic mode, armed and pending shots. Mass labels are read live from native vessel state and are not sidecar data. | Source selection and speed are input for the next one-shot action, not an applied registration or ongoing force; they reset to unselected/5 m/s. Launches never replay on load. Kitchen Sink independently saves any source/target G-load protection. |
| [DOH](../doh.lib/DohSubmod.Persistence.cs) | Registry of native-restored spawned kittens, character IDs, cloned tint and named material colors, plus spawn offset/count/character/color options. | Rebinds kittens instead of spawning duplicates; registry ownership supports later Despawn. Missing kittens or changed material identities warn. Character assets must be installed. Pending spawn actions are not replayed. |
| [Don't Stifle Me](../dont-stifle-me.lib/DontStifleMeSubmod.Saves.cs) | Editor scale enable/snap flags and extended-value policy. | Existing part geometry/configuration remains native-owned. Does not serialize an editor undo stack or uncommitted operation. |
| [Eternal Flame](../eternal-flame.lib/EternalFlameSubmod.Saves.cs) | Monitored vehicles, fuel/electricity switches and refill interval. | Monitoring resumes; interval timing restarts, without offline catch-up. Native save owns the current resource quantities. |
| [Free Fallin'](../free-fallin.lib/FreeFallinSubmod.Saves.cs) | Applied global canopy material settings and effective albedo. | Recreates appearance, not a cloth simulation checkpoint. Applied state only; external textures remain dependencies. |
| [Garry's Torch](../garrys-torch.lib/GarrysTorchSubmod.Persistence.cs) | Source/target/optional target-part, pose, XYZ factor, lock/collision/enabled flags; original full-part and avatar scale baselines; active and queued weld animation recipes. | Rebinds ownership while keeping native effective full-part scale, including attachments not yet scaled by an idle weld. Separate avatar correction is restored. Animation start/elapsed/easing and queued steps continue. Unweld uses the original baseline; topology changes, ownership conflicts and dependency cycles can prevent restoration. |
| [Glass](../glass.lib/GlassSubmod.Saves.cs) | Field-of-view override enable state and degrees. | Native camera selection/pose remains native-owned. Preset library is not copied. |
| [Godzilla](../godzilla.lib/GodzillaSubmod.Persistence.cs) | Vessel Smart/Basic factors, physics/collider channels, global channels, original full/subpart scale, original full-part positions/pivot/avatar size and effective child scales. | Reconstructs scale ownership from original snapshots so repeated loads do not intentionally compound scale. Restore returns to those originals. Missing/changed topology and another scale owner are reported instead of guessing. |
| [Graffiti](../graffiti.lib/GraffitiSubmod.Saves.cs) | Terrain, part and canopy decal anchors, image names, projection geometry, cloth triangle/barycentric address, visibility, brightness/alpha. | Placement mode stops. Missing PNGs or unavailable canopy can leave a dormant entry with warnings; missing part/body cannot be rebound. Cloth deformation itself is not captured. Images and compatible canopy topology remain required. |
| [Hot Pursuit](../hot-pursuit.lib/HotPursuitSubmod.Saves.cs) | Mounted part, surface basis, transform, FOV, dimensions, visibility and whether its viewport was open. | Placement mode stops. Attempts to reopen saved viewports; slot exhaustion keeps the camera setup and reports that its viewport needs reopening. No native viewport/texture handles persist. |
| [Humble Arteest](../humble-arteest.lib/HumbleArteestSubmod.Persistence.cs) | Paint activation/blend, global/template/per-part colors, named kitten material colors, visor hiding, global/per-module engine emissive settings. | Restores effective owned appearance and recreates relevant shader/material state. Requires compatible material names and module layouts; missing targets/uploads warn. Unapplied editor input is not generally captured. |
| [I Feel Seen](../i-feel-seen.lib/IFeelSeenSubmod.Saves.cs) | Tracked vehicle identities and each force-visibility flag. | Rebuilds tracking on native-restored vehicles. Deleted/missing vehicles warn. |
| [Iron Man](../iron-man.lib/IronManSubmod.Saves.cs) | Configured kittens, enabled mode, original EVA/control settings needed to disable the mode, current flight-computer preferences. | Mode restores with engines **disarmed**. Existing connectors are configured/reused through the normal code path. Runtime thrust/burn continuation is not promised; the player deliberately arms engines again. |
| [It's So Shiny](../its-so-shiny.lib/ItsSoShinySubmod.Persistence.cs) | Existing host/light cell addresses, grid ownership/appearance, sparse mask, render flag, scroll recipe/offset and pending deletion. | Rebinds native cells without spawning or forcing ignition/switch state; scrolling resumes. Pending deletion restarts its delay. Shared light-template appearance retains the feature's existing shared-template semantics, with Zippo's template ledger restoring originals. |
| [Kitchen Sink](../kitchen-sink.lib/KitchenSinkSubmod.Saves.cs) | Global IVA force-render flag plus G-load-protected vehicle IDs in separate version-1 records. | Clears old references before reconstruction and rebinds exact IDs; missing/ambiguous targets warn and retain state. Legacy boolean-only saves remain valid; picker/filter are transient. |
| [Kitten Animations](../kitten-animations.lib/KittenAnimationsSubmod.Persistence.cs) | Selected kitten; forced clip by source/label, active/paused state and native clip phase; driver controls/global tuning; expression settings and latched expression clip identity. | Forced looping clips and frozen poses resume, and latched expressions return at their held weight. Unlatched one-shot expressions stay stopped; intermediate expression easing is not checkpointed. Missing/ambiguous clip identity or unavailable native phase fields warn/fail the block. |
| [Kiwi's Marbles](../kiwis-marbles.lib/KiwisMarblesSubmod.Persistence.cs) | Celestial source/vehicle-or-celestial target/offset and original orbital parent, epoch and state vectors. | Restores ordered welds and their future Unweld baseline. Cycles through both target dependencies and actual parent ancestry are rejected. This is a weld/orbit recipe, not all possible arbitrary celestial-system mutations. |
| [Parts Now](../parts-now.lib/PartsNowSubmod.Saves.cs) | Runtime mod IDs and declared part-template IDs as dependency records. | Does **not** install, embed, unload or automatically replay arbitrary runtime mods. Required native templates/characters are preflighted before world destruction; install/enable missing dependencies before retrying. |
| [Pebbles](../pebbles.lib/PebblesSubmod.Saves.cs) | Applied per-body clutter recipes, including their imported mesh identities/settings (`pebbles`); removed/displaced clutter on non-stock-spacing grids (`pebbles.clutter-state`, v1, order 61, new @5482). | Recreates applied fields and releases old import ownership. Stock-spacing removed/displaced clutter is saved natively by KSA 5482; Pebbles keeps those native entries stock-grid and restores override grids after recipes (native state wins). Workshop previews and unapplied recipe edits are not captured; pending unsynced hits are lost as natively. A missing record (vanilla/older saves) restores recipes and stock state only. Imported GLB assets remain required; changed content hashes reject the saved selection. |
| [Pyro](../pyro.lib/PyroSubmod.Persistence.cs) | `pyro.templates`: edited shared plume templates. `pyro`: anchored plume settings, enabled flags and on/off cycling configuration, current phase and remaining interval. | Recreates plume instances after template edits, with cycling resumed without offline catch-up. Does not embed referenced assets or native renderer state; preset catalog remains separate. |
| [Rocky McRockFace](../rocky-mcrock-face.lib/RockyMcRockFaceSubmod.Saves.cs) | Applied ring overlays still owning their target ring: LOD mesh/material/band selections and field overrides. | Reapplies after Bloomin' Onion. Superseded overlays and unapplied selection drafts are omitted. Referenced assets must remain available. |
| [Skittles](../skittles.lib/SkittlesSubmod.Saves.cs) | Effective ImGui theme colors and style values. | A scene can restore its theme; native saves without a sidecar reset to the captured startup-theme baseline. Global theme library/default preference and window layout autosave are separate. |
| [Sphinx](../sphinx.lib/SphinxSubmod.Saves.cs) | Applied body-local statics, GLB/PNG identity, anchor position/normal, scale/rotation/offset, UV mapping, visibility/alignment and collision mode. | Recreates render resources and colliders under existing limits (32 statics and aggregate vertex budget). Pending placement actions are cleared. Assets are not bundled; collision behavior requires native verification. |
| [Thug Life](../thug-life.lib/ThugLifeSubmod.Persistence.cs) | Anchored sunglasses quads, local position/rotation, size and visibility. | An entrance slide saves its current pose and stays stopped after load. Its remaining animation is not replayed. |
| [Zippo](../zippo.lib/ZippoSubmod.Persistence.cs) | Edited shared light component originals/current values; per-part default color baselines; Disco draft and active recipes, paused state, phase/seed, switch and actuator originals; active/queued light transitions. | Disco and queued transitions continue from saved phase/elapsed values. Restores actuator ownership using current module ordinals; conflicts/layout changes warn. Shared-template edits retain their original shared scope. GPU light/material objects and live queue keys are never serialized. |

## Recovery and portability limits

Kitchen Sink persistence follow-up (2026-09-13): `kitchen-sink.tests` passes 26 additional
real-adapter/coordinator checks covering G-load registrations, alongside its 54 damage checks.
The shared `saves.tests` suite and full solution build also pass. Native cart/save/load acceptance
remains open; the earlier baseline suite run below is not a claim of a new full-suite rerun.

- Copy the **whole native save directory**, including its matching sidecar. PNG, GLB, audio,
  character and runtime part-mod libraries remain external dependencies. Saves do not bundle their
  bytes or guarantee identical output if those files or installed game assets change.
- PNG and audio references use filenames without content fingerprints. Replacing a same-name file
  changes the media restored by the save; there is no saved hash to detect that substitution. Pebbles/
  Sphinx GLB identities do carry a content hash: restore resolves the filename inside the installed
  shared GLB library and rejects changed content rather than silently choosing a new mesh version.
- Normal unresolved targets produce feature warnings; references do not fall back to the controlled
  vehicle or a similarly named part. Structural addresses assume compatible native reconstruction.
  They are not a general migration mechanism for edited `universe.xml` files or changed part trees.
- Native template/character/system preflight can reject a load before destroying the current world.
  It is not exhaustive native validation and cannot guarantee rollback after any later native fault.
- Missing, corrupt, oversized, hash-mismatched or unsupported whole-document sidecars are ignored
  with diagnostics while native loading proceeds. Old scene setup is cleared. Their raw bytes are
  **not** retained in memory for a later save; preserve the original directory before overwriting it.
- Unknown feature IDs/versions and invalid or partially failed known feature records are retained
  as original payloads for future saves. Any warning during a feature's restore retains that entire
  feature record, even when some entries restored successfully. A “restored records” count therefore
  does not mean every object was restored without warnings.
- While a known feature's original record is retained, subsequent saves keep that record instead of
  current edits to that feature. Fix dependencies and reload the original save to retry. Alternatively,
  **Use current setup for future saves** discards retained records for supported feature versions so
  later saves capture the current partial setup. It does not repair missing objects, retry restoration,
  edit the original disk save immediately, or discard unknown/future-version records.
- Capture failures can retain the most recent successful record for that feature in the current
  world. With no earlier record, the feature cannot be recovered from that failed capture; status and
  saved warnings identify the failure. After an incomplete destructive native load, preserving incoming
  feature records is data retention, not restoration of the previous running universe.
- Sidecar replacement uses a flushed temporary file, and the host writes it only when KSA's
  `UncompressedSave.Write()` returns true. Since KSA 5482 (rev 5453) the overwrite deletes and
  recreates the save folder inside `Write()` through `SaveDirectory.TryReplace`. This happens after
  state capture and before the native files are written. If the delete fails after 3 IO retries, the
  path is outside the saves root, or the native metadata/universe write fails, `Write()` returns
  false instead of throwing. The old folder can then survive intact or partly deleted. The host
  writes no sidecar and reports `KSA could not write save '<id>'; Unscience state was not written.`
  A process interruption or sidecar write failure can still leave a native-only or incomplete save.
  There is no atomic transaction across both files and no automatic backup/version history.
- Since KSA 5482 (rev 5441) an unreadable `universe.xml` is logged by KSA, and `Load` returns
  before reconstruction. The host reports
  `Load failed: KSA could not read save '<id>'; the current scene was left unchanged.`
  Nothing is reset, and the next successful load clears the error.
- Arbitrary console/reflection edits, other mods' state, historical undo stacks, most unapplied UI
  drafts, pending unapplied jobs, file-selection dialogs and placement previews are outside these
  explicit adapters. Exceptions include captured weld/light queues, camera keyframes/pending groups
  and grid pending-deletion intent; their specific policies are listed above. Existing
  global presets/import catalogs and toolbox layout autosave remain independent.

The recovery behavior above is defined by
[SceneSaveCoordinator](../ksa-abstractions.lib/Persistence/SceneSaveCoordinator.cs),
[SaveStorage](../ksa-abstractions.lib/Persistence/SaveStorage.cs), and the host status UI.

## Managed verification and its boundary

The repository contains targeted executable checks for
[storage/coordinator/hooks/part identity](../saves.tests/README.md),
[Garry scale and animation continuation](../garrys-torch.tests/README.md),
[Godzilla baseline preservation](../godzilla.tests/README.md),
[camera recipe reconstruction](../camera-saves.tests/README.md),
[world recipe validation](../world-saves.tests/README.md),
[Iron Man flight settings](../iron-man-flight.tests/README.md), and
[Pyro cycle phase](../pyro.tests/README.md). The scale/animation checks include nonzero numeric
round trips and repeated native-style reconstruction, rather than only serializability.
Fixture checks substitute native seams; they do not execute real graphics/audio/physics ownership.
Final full-solution build passed with zero warnings and errors. All 12 managed executables passed:
`saves.tests`, `world-saves.tests`, `camera-saves.tests`, `garrys-torch.tests`, `godzilla.tests`,
`iron-man-flight.tests`, `iron-man.tests`, `iron-man-mode.tests`, `pyro.tests`, `byo-music.tests`,
`pebbles.tests`, and `sphinx.tests`. The build used
`dotnet build -m:1 -p:UNSCIENCE_DIST_DIR=/private/tmp/unscience-saves-dist`; generated output
was isolated from the installed game. These results do not mark any native item below complete.

KSA 5482 upgrade (September 2026): the full solution again builds with no warnings or errors, and
all 15 managed suites pass against the 5482 reference assemblies. `saves.tests` now mirrors the 5482
native shapes: `Write()` returns `bool`, `Load()` returns on an unreadable file, and the worker join
is `NearestOrbitAndPerformanceWorker`. It adds *native write returning false reports failure
without sidecar* and *unreadable save reports failure and only clears load context*, and updates the
callback-isolation check. The reset-ordering traces now expect `join nearest-orbit-and-performance`.
These are managed fixture results only; the three 5482 items in the checklist below remain open
natively.

## Native acceptance checklist

Record KSA build, Unscience commit, OS/architecture, installed mods/assets, pass/fail and observed
warnings for each run. Use throwaway save copies for failure injection. Keep an untouched source
save and screenshots/counts/reference values for comparison. All boxes begin unchecked.

### Transaction and lifecycle

- [ ] Create a scene with multiple modified features, save A, edit them, then load A through normal
  KSA UI. Sidecar exists, status has expected records, and reconstructed setup matches saved values.
- [ ] Save a different setup B; run A → B → A and repeated A reloads, then exit/restart KSA and load A.
  Confirm process-global templates and render flags do not leak from the previous scene.
- [ ] Load a native-only save and create/load another system. Confirm old ownership, controllers,
  template edits and queued world mutations are cleared; startup theme baseline returns.
- [ ] Test save overwrite, distinct save names, load from the console, and load while the toolbox is
  hidden with F2. Verify normal gameplay resumes and no current-frame texture reference is invalidated.
- [ ] Attempt editor-refused load, invalid system, malformed native data and a missing required
  part/character. Confirm refusal before destructive reset where preflight applies, with useful status.
- [ ] Queue feature changes and a world load in the same frame, including grid deletion and Sphinx
  placement. Confirm stale actions cannot affect the new world; explicitly saved grid deletion still runs.
- [ ] Inspect game logs and renderer validation output after repeated transitions. No disposed object,
  Vulkan resource, cloth/orbit worker, FMOD channel or secondary viewport use-after-release should appear.

### Identity, geometry and ownership

- [ ] Launch a protected EVA or vessel with Dent Wizard, save and reload; confirm native vessel
  motion and Kitchen Sink protection restore, while Dent Wizard has automatic mode off, no armed/pending shot and
  its next-shot form resets. Repeat with a vanilla save/new system; no protection or shot leaks.

- [ ] Use duplicate display names and repeated identical part templates, including subparts and debris.
  Paint/anchors/grid cells resolve to their exact intended targets rather than the first matching part.
- [ ] Save Smart and Basic Godzilla modes with nonuniform XYZ factors and all physics/collider channel
  combinations. Reload repeatedly; compare full/subpart geometry and collisions, then Restore to the
  original custom geometry and original nondefault kitten size.
- [ ] Save Garry chains with part targets, disabled welds, collision toggles and a newly added attachment
  on an idle scaled weld. Reload repeatedly; verify exact effective geometry, ongoing following and
  Unweld originals. Exercise Godzilla/Garry ownership conflicts and missing members without compounding.
- [ ] Save celestial→celestial and celestial→vehicle weld chains. Verify loaded following and Unweld's
  original orbit. Inject cycles and ancestor hazards into a matching test payload; expect diagnostics,
  no recursive refresh failure and no accidental parent cycle.
- [ ] Save multiple Blinky/Shiny grids, sparse masks, scrolling and pending deletion. Check native part
  counts do not grow, fuel/power connections work, render suppression/cache exclusions still work despite
  native lost part names, and stopping/deleting one grid leaves the others intact.
- [ ] Save DOH clones with distinct materials and an uncolored clone. Check roster/vehicle counts remain
  unchanged, unique tint remains unique, material selection still works and Despawn acts on restored clones.
- [ ] Save enabled Iron Man mode with customized EVA/flight preferences. Confirm connectors are not
  duplicated, engines load disarmed, manual arming works, and Disable restores original control settings.
- [ ] Verify Eternal Flame monitoring, I Feel Seen visibility, IVA rendering, editor flags and Glass FOV
  resume their saved policies, including disabled/false values and missing-target diagnostics.

### Playback and appearance

- [ ] Save Garry and Zippo transitions partway through easing with several queued steps. Compare post-load
  continuation to an uninterrupted run through queue promotion; confirm elapsed offline time has no effect.
- [ ] Save paused/running Disco lights with randomized color/movement and actuator control. Compare phase,
  switches and colors, then Stop and check original light/actuator values. Test an ownership conflict.
- [ ] Save forced looping kitten motion, frozen pose and latched expression. Verify phase/pose, held facial
  variant and tuning after reload. Verify an unlatched one-shot expression stays stopped.
- [ ] Save camera sequences including parallel groups and unfinished group recipes. They must restore
  stopped and remain editable/playable; verify native camera pose and return-to-start behavior.
- [ ] Save audio during a track and repeat gap. Reload must be silent/paused; Resume starts at the beginning
  with correct volume/range/repeat/gap. Finished sounds must not resurrect.
- [ ] Save Pyro shared template changes and anchored running/disabled cyclic plumes. Check template-before-
  instance replay, enabled state and remaining on/off phase. A → native-only must restore original templates.
- [ ] Save global/template/part paint, kitten material colors, visor state, engine glow and Shiny/Zippo
  shared light edits together. Verify both intended sharing and original baseline cleanup across A → B → A.
- [ ] Save Thug Life during an entrance slide; confirm its sampled pose restores without retriggering it.

### Rendering, world content and assets

- [ ] Save Bloomin' Onion rings with procedural bands and Rocky overlays on the same body. Test reset and
  reapplication order, original ring restoration, renderer rebuild, rings-disabled settings and missing assets.
- [ ] Save Pebbles applied GLB recipes and Sphinx statics with UVs, visibility and each collision mode.
- [ ] Pebbles (5482): save/load with removed and displaced clutter at the same and at a changed spacing;
  confirm no new holes in stock clutter, override removals persist and in-flight objects keep velocity.
  Confirm placement/collision behavior after restart and repeated loads; no duplicate instances or growth
  in imported/render resource ownership should accumulate across a bounded repeated-load test.
- [ ] Save terrain/part/canopy Graffiti and custom Free Fallin' materials. Test packed/deployed canopies,
  unavailable canopy anchors, texture rescan and repeated frame transitions without invalid GPU references.
- [ ] Save multiple mounted cameras with open/closed viewports. Verify camera mounting and render quality;
  exhaust secondary viewport slots and confirm recoverable closed-camera setup with an actionable warning.
- [ ] Copy a complete save to a compatible second installation with matching assets, then repeat with
  missing PNG, GLB, audio, character and part-mod dependencies. Verify the documented distinction between
  preflight refusal and partial scene restoration; reinstall dependencies and reload the untouched original.
- [ ] Replace a PNG/audio file with different content under the same filename and confirm the documented
  changed-media behavior. Replace a GLB under its saved filename and confirm hash-mismatch rejection.

### Recovery and failure reporting

- [ ] Use copies with missing/truncated/duplicate-property/oversized/future-schema JSON and a sidecar from
  another `universe.xml`. Confirm native-only fallback and visible diagnostics; no old feature state leaks.
- [ ] Use unknown feature IDs, future feature versions, invalid known payloads and one missing entry among
  valid entries. Save again and verify retained original payloads, including partially restored feature blocks.
- [ ] Edit a partially restored feature and save. Confirm retained originals still win with a warning;
  select Use current setup for future saves, save another slot, and confirm supported feature edits now win
  while the untouched original slot and unknown/future records remain preserved as documented.
- [ ] Inject native write failure, sidecar write failure and post-reset native load failure. Confirm status
  does not claim full success, no unmatched capture writes to another slot, later loads have no stale
  transaction context, and recovery limits are clear rather than implying automatic world rollback.
- [ ] **KSA 5482 failed save.** Overwrite an existing save while `SaveDirectory.TryReplace` cannot
  replace its folder. On Windows, keep a file in the save folder open. Confirm that KSA reports the
  failure, that no new `unscience.json` is written into the surviving old folder, and that the old
  sidecar still matches its `universe.xml`. The toolbox must show
  `KSA could not write save '<id>'; Unscience state was not written.`, and a later normal save
  must succeed with a sidecar. Managed
  coverage: `saves.tests` write-returns-false check. Native: pending.
- [ ] **KSA 5482 unreadable save.** Corrupt `universe.xml` in a copied save and load it. The current
  scene and its Unscience setups must be unchanged, with no reset. The toolbox must show
  `Load failed: KSA could not read save '<id>'; the current scene was left unchanged.`, and the next
  successful load must clear it. Managed coverage: `saves.tests` unreadable-save and callback-isolation checks.
  Native: pending.
- [ ] **KSA 5482 reset join.** Start a deferred load, from the UI and from the console, while the
  nearest-orbit job is busy. For example, hover a flight-plan patch at high warp with several
  vessels. Expect no `ObjectDisposedException`, orbit-worker fault or hang, and cleanup must begin
  only after `JobSystems.NearestOrbitAndPerformanceWorker` has joined. Managed coverage:
  `saves.tests` load/new-system traces (`join nearest-orbit-and-performance`) and the join-failure
  abort check. Native: pending.
