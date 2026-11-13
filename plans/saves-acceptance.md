# Scene save implementation coverage and native acceptance

## Beverage Cooler

All durable state is native: selected part variant/placement, Habitat meals/canisters and
fractional consumption, and KeyframeAnimation current/target times. No sidecar ID/version or
participant is added. Native 5801 prepass/deserializer/family validation and PartInstance XML
round trips cover both variants with different depleted stocks and partially opening/closing lids.
They do not replace a running-game acceptance pass. [Contracts](../scope/beverage-cooler.md).

The initial in-game report confirms actuation plays, but exposed a body mesh used as the lid and
both finishes red. Native PartModel cache aliasing caused both defects. The fix uses unique render
keys without changing saved part/module identities; restart KSA before retesting the update.

The low-poly update retains XML mechanics, the earlier six mesh IDs/eight render keys and durable
save/module identities. Runtime art has 710 visible/112 picking triangles and nine 128px maps.
Coarse open-tub/lid proxies omit exact microhardware/bevel geometry while preserving outer bounds.
`scripts/check-cooler-assets.py` enforces export budgets and bounds, also through package validation.
This measured workload reduction does not establish the cause of the reported FPS issue; native
frame-time profiling remains pending.

The underside correction adds two foil meshes/four stateless model keys, for eight meshes/twelve
render keys overall. All older IDs, Body/Lid/LidCollider slots and native gameplay/save keys remain.
The native Core material is referenced without copied textures. Front-facing ray coverage and a
closed visual lid seam are export-checked; native colliders/pressure behavior are unchanged.
Focused temporary fixtures reject flipped cap winding even with matching flipped normals, a
removed annular rim triangle and an incorrect native foil material binding. The twelve-model
native cache fixture preserves exact template/mesh/material identity and restores its test caches.

- [ ] Restart and load an older cooler scene. Look into the tub and below the open lid/bottom in
  both finishes: foil surfaces must be solid with no sky through the center or edge annulus. Check
  partial/closed lids, lighting/shadows and retained supplies/current/target after repeated A→B→A.

- [ ] Restart KSA and compare the same scene/camera, resolution, lighting and mod stack before/after
  the art change, with no cooler, one cooler and many coolers. Record CPU/GPU frame times and
  renderer memory where available; distinguish a measured improvement from an unproven cause.
- [ ] Load an existing craft with depleted supplies and a partial lid. Confirm stable native
  stock/state, animation and physical contacts. Select body/lid/cavity in both finishes; picking
  follows coarse proxies rather than every hardware/bevel detail. Check thumbnails, shadows and
  native bounds/culling at several distances and lid positions.

- [ ] Load a craft saved with the first cooler build. Confirm one body plus an actual thin lid,
  matching lid/body finish in both variants, retained supply depletion and partial lid state. Repeat
  variant switching after other coolers have rendered to exercise the populated global model cache.
- [ ] In the editor find Beverage Cooler under Unscience Supplies/Life Support/Structural. Cycle Red/Stainless
  Steel before placement, while grabbed and on a placed part; mass/supplies/connectors remain equal,
  appearance changes, and no VariantDrift or missing asset warning occurs. Copy/symmetry/undo/redo
  preserve native state without duplicated resources.
- [ ] Open/close and stop the native Actuate slider midway. Inspect lid picking/contact debug
  geometry: shell and shared collider rotate together about the rear hinge, the front rises, the
  body/cavity stay fixed, and open/closed/midway thumbnails and shadows remain sensible.
- [ ] Verify full storage is 12 meals+2 CO₂ scrubbers; consume or transfer different amounts from
  two coolers and observe native mass changes. Check a compatible habitat passage and a blocked
  surface attachment independently; blocked cabins must not gain access. No water/oxygen tank or
  occupied crew habitat is expected from this supplies-only part.
- [ ] With life support enabled, use a healthy EVA kitten with cargo space to retrieve/return
  supplies inside 2 m; repeat outside range and without cargo space. In-zone transfers require an
  able crew carrier. Verify native availability checks and remaining supplies/mass.
- [ ] Save A with both variants, unequal depleted stock and one partly opening/one partly closing
  lid. Reload twice, A→B→A, restart and reload: exact variants, placement, supplies/debts, current
  lid pose and continuation restore without refill, duplication or compounding transforms.
- [ ] Save/reopen blueprints with both variants and partial lids; record native blueprint resource
  policy. Load a vanilla save/new system and return to A; no previous scene state remains. Test a
  throwaway save with cooler assets missing, then reinstall and load the untouched original.

## DOH click-to-spawn

**Spawn at...** beside **Spawn Kitten(s)** creates one native-owned EVA at a validated surface.
The unchanged `doh` v1/order60 record saves existing controls/registry; native KSA saves pose/motion,
name/crew and War Paint saves paint. Hover/armed/pending input is intentionally transient and is
cancelled by the production adapter reset, including legacy/vanilla/new and repeated loads.
Range/gap/orientation are fixed policies, not additional user settings. Managed Bepu geometry,
production Harmony input and real-adapter cancellation checks pass alongside existing DOH tests.

- [ ] Save placed kittens on terrain and a moving/rotating vessel, with selected names/models/paint.
  Load repeatedly and A → B → A: exact native poses/motion/crew and paint, no duplicate vehicles/rows.
- [ ] Save while placement is armed and while a click is queued. Load that scene, a vanilla scene
  and a new system: no unexecuted spawn or retained input owner/preview; ordinary mouse input works.
- [ ] During placement remove/change the target part, change viewport/editor/map/HUD state or
  unload. No stale references or same-name reconstructed target may receive the queued action.
- [ ] Check ground slopes, curved/scaled parts, ceilings/edges/nearby kittens, launchpads/clutter,
  pause/warp and moving surfaces with native collider debug drawing. Blocked clicks keep the tool
  armed; clear clicks leave a 2 cm initial capsule gap, with expected native settling/locomotion.

[Implementation evidence](DOH_CLICK_TO_SPAWN_RESEARCH.md#implementation) · [scope](../scope/doh-placement.md).

## Wing Walker

The chair art update uses a square foil frame, simplified worn webbing, coarse picking proxies and
three external 256px maps, with no embedded GLB images/materials. XML mechanics, all native part/
subpart/mesh/material IDs, render/module keys and five placement slots remain unchanged. No saved
state or migration is added. Export checks protect budgets, foil UVs and proxy bounds; visual and
frame-time behavior still need native acceptance.
The later stronger dingy/faded cloth pass changes existing PNG pixels only; verify its appearance
in native lighting while crew, resources, picking and physics retain the same behavior.

- [ ] Load an existing seated craft with distinct crew and depleted resources, then repeat A→B→A,
  native undo/redo and copy/symmetry. The new square-frame/worn-cloth appearance replaces the old art
  while crew/history/resources and duplicate-crew protection retain their existing behavior.
- [ ] Check native foil/cloth rendering, thumbnails, culling and coarse seat/back/arm/frame picking
  at several distances. Physical contacts keep the existing collider envelopes; Standing Mount is
  unchanged. Profile the same scene/settings with zero, one and many chairs before/after the update.

Editor crew duplication guard: 32 managed production-Harmony checks cover selection/pickup/gizmo
start, copy capture after reassignment, subtree ownership, native-history call order and repeated/
restored assignments. Real 5801 signature checks and empty-seat `IVASeat.GetSaveData` pass, as does
the full solution build. Native records and schema are unchanged.

- [ ] Assign distinct kittens, select/move a mount or its parent, change symmetry (2/4/8) and copy.
  Affected external seats and copies are empty; kittens are available to reassign exactly once.
  Stock seats and unrelated mounts retain their crew.
- [ ] Assign after final placement, then save/load repeatedly and A → B → A. Retain exact distinct
  crew names; edit after load and repeat symmetry/copy without duplicating occupants.
- [ ] Undo/redo selection unassignment and placement/copy edits, including a previously launched
  vehicle and unattached subtrees. Crew and roster restore together; redoing or copying again
  cannot create duplicate names. Hover/right-click Crew controls alone retain assignments.

Native parts, `IVASeatData`, roster, tanks and life support own all durable state; part template
selects posture. No feature sidecar is required, and appearance scope, weak animation markers and
diagnostics are transient. The appearance fix adds no settings. The beach chair and standing strap models change
XML visual references and collider data while preserving both part IDs, all five subpart
IDs/keys/order, the EvaDoor module and save format. The Capsules tag adds no scene state. The native parser check verifies that order.
The native XML fixture round-trips two `IVASeatData` crew names and both part IDs within
`PartInstance` data. It does not call `IVASeat.GetSaveData` or load a running scene. Repeated A–B–A
in-game save/load and resource/crew acceptance remain pending; see [validation](WING_WALKER.md).

## You Didn't See Anything

The new version-1 `you-didnt-see-anything` record stores enabled, detached-only and stock-gauge
exclusion flags as detached booleans. Reset/missing records disable the feature; replay assigns
the validated settings without serializing window pointers or F2 state. ImGui layout keeps its
existing ownership, and native context/input/patch state is transient.
Optional purr2d2 compatibility adds no saved fields. Its patch caches and mode markers are
transient; HUD detachment/positions/visibility retain purr2d2's preference ownership.

Managed feature persistence, production Harmony fixtures, shared `saves.tests`, upgrade checks,
full solution build and package validation pass against 5801. Native acceptance below remains open.

- [ ] Save different flag combinations and load A → B → A and the same save repeatedly. Confirm
  exact selection with F2 active and with detached windows open.
- [ ] Load a legacy/vanilla save and create a new system. Confirm disabled defaults and restored
  input/visibility; no old native pointers or suppression may leak.
- [ ] Exit/unload with F2 active and a filtered window focused. Confirm hook removal and input
  restoration before native ImGui teardown.
- [ ] With purr2d2 panels detached, load enabled/disabled/vanilla scenes during F2. Confirm saved
  retention flags apply, only detached panels survive, and old drags/capture do not leak across loads.

[Feature checks and native acceptance](YOU_DIDNT_SEE_ANYTHING.md).

## Kitten animation hold follow-up (5801 port)

Per-kitten drivers/expressions survive control/camera/selection changes. The existing v1 record saves
all targets with exact IDs and explicitly migrates legacy single-target records. Production lifecycle,
pose dispatch and real save-adapter JSON tests in `kitten-animations.tests` cover multi-target holds,
reset/reconstruction/repeated loads, missing/disposed/ambiguous targets, avatar replacement, phase
retention before first visible render, legacy payloads, invalid data and vanilla/unload cleanup.
Ported from `feature/animation-stickyness` commit `7f77908` onto the KSA **2026.11.10.5801** upgrade.
The current sources still select locomotion/condition state before the `AnimatedRenderable.UpdateAnimation`
pose hook; native animation culling and per-viewport/frame guards remain game-owned.
Validation: all **41** kitten ownership/adapter checks, `saves.tests`, `world-saves.tests` and
`ksa-upgrade.tests` pass, including real-5801-DLL phase-field/hook-order checks. Full solution
`dotnet build -m:1 -p:UseSharedCompilation=false -nodeReuse:false` passes with zero warnings/errors,
using explicit CURRENT `KSAFolder` and `/private/tmp/unscience-kitten-5801-dist` output.
Native rendering/culling and actual KSA save/load acceptance remain pending.

## DOH native roster integration

Every successful spawn now has a native roster record and active EVA mission. Crew health, dose,
condition, statistics, assignment, mission time and KIA history persist through KSA's universe.xml,
not an additional sidecar. DOH v1/order60 remains compatible; replay/live maintenance backfill old
unrostered tracked EVA kittens and preserve valid existing history without restarting missions.
Incompatible native records warn and retain the incoming sidecar instead of overwriting crew.
Failed spawns roll back only their own provisional roster row. See [integration checklist](DOH_ROSTER_INTEGRATION.md).

DOH collision recovery now advances occupied single names and numbered batches above the highest
matching native scene/roster suffix, including seated/retired/KIA reservations. Available exact names
stay unchanged. Numbering is derived after native reconstruction; no counter or additional sidecar
state is needed, and the saved name input remains unchanged (`doh` v1/order60). All 118 managed DOH
checks pass, including sparse/large suffixes, repeated real-adapter replay, new-scene reset and
post-load roster registration. Native spawning and actual KSA save/load acceptance remain pending.
The full solution builds against CURRENT 5801 with zero warnings/errors; `saves.tests`,
`world-saves.tests` and `ksa-upgrade.tests` also pass. Build output uses
`/private/tmp/unscience-doh-naming-dist`; this invocation uses `-p:NuGetAudit=false` because the
package vulnerability feeds are unreachable on this host. Compiler warnings remain errors.

- [ ] Spawn native and custom-character batches; verify exact names, EVA assignment, roster follow,
  health/dose/condition and mission/travel/speed statistics. Native crew must be unaffected.
- [ ] Save/load twice and A-B-A; verify one row/mission per kitten, no lost history and no duplicates.
  Load an older unrostered DOH save; verify repair without respawning vehicles.
- [ ] Board and EVA again, recover and Smite; retain the same roster identity, native seated/home
  state and KIA/cause history. Prune/unload must not erase native crew.
- [ ] Exercise a partial spawn failure, existing name, dead/retired/assigned conflict and a missing
  native target; preserve prior rows/history and retain unresolved sidecar data with diagnostics.

## War Paint consolidation

`war-paint` v1/order85 owns all active per-kitten paint recipes. It restores exact native identities,
private materials and original-binding ownership; detached slots can be reused after native reset.
Global presets live in `.unscience/war-paint-presets.json`, outside scenes. Existing DOH/Humble
records retain IDs/versions and migrate old effective colors; explicit new paints replay last.
New DOH saves include a preset name and detached spawn recipe. Active War Paint v1 records now
add optional `PresetName`; older nameless recipes import named presets, and missing global entries
recover from scene snapshots. Existing global recipes are not overwritten by scene previews.
UI editor/brush/filter/dialog state is transient and resets on scene replacement. Dialog padding,
minimum picker dimensions and live name/filter buffer refresh are presentation-only changes;
scene/global preset payloads and ownership are unchanged. Incremental discovery only extends the
draft/baseline with default role colors; it never writes the preset library automatically. None
deselection discards unsaved preview and clears transient editor state while preserving assignments.
RGBA picker edits use existing recipe fields. Native acceptance includes alpha-only changes from
both apply-all and per-material pickers surviving preset Save/restart and scene save/load.

Production service/preset/adapter checks cover independent edits, Unset ordering, detached snapshots,
restart persistence, portable material roles, legacy imports, repeated/vanilla loads and invalid or
unresolved record retention. Native acceptance: paint native and spawned kittens differently; save
presets and restart; spawn with the selected preset; preview all matching assignments, Save or
switch to discard; remove one kitten or confirm deletion of the preset and all its assignments;
board/delete; repeatedly save/load and confirm resource recovery and late-shutdown safety.
[Editor and save acceptance](WAR_PAINT_PRESET_EDITOR.md) · [original consolidation](WAR_PAINT_CONSOLIDATION.md).

Visor UI/draw ownership is now War Paint, while the existing `humble-arteest` v1 `HideVisor` field
continues to capture/reset/replay its global flag unchanged. Verify an older hidden-visor scene
loads with the new checkbox unchecked; new/native-only scenes default checked. Toggle it without
a paint preset, save/load twice and A-B-A, spawn while hidden, then unload and verify drawing.
Status messages are transient game logs and add no saved state. Managed production-Harmony checks
verify per-view visor-only gating, new kittens, default shown state and targeted unpatch/reset.

## 5801 despawn/shutdown follow-up

DOH panel layout/logging changes preserve `doh` v1 capture/reset/replay. The kitten table is derived
from the existing registry; removed status/count/hint labels and native log messages are transient.
Native UI acceptance: check Name/Model/blank action headers, red Smite buttons, full-width Smite All,
equal-width Spawn Kitten(s)/Spawn at... with the standard gap, and confirmations/errors in KSA's
log after loading.
Character choices load during initialization; the manual refresh button is removed without changing
saved character selection or the `doh` v1 record.

No save IDs/versions change. DOH transient Smite actions now use KSA's native kill-input queue,
before render-list refresh, retaining exact old objects rather than resolving same-name restored
kittens. Capture before dispatch includes the kitten; after native death it is omitted. Private
War Paint slots release post-death while reusable preset files remain. Native acceptance must repeat
the two-kitten/survivor case, controlled kitten, Smite All and save/load around dispatch.
[Native kill follow-up](DOH_NATIVE_KILL_5801.md). Explicit saved Offset
values stay unchanged; fresh/vanilla/missing-offset defaults are (1, 1, -2) m. Graffiti scene reset
shares ownership-aware cleanup with Dispose, retaining existing decal capture/replay. Shutdown callback
and device guards are transient lifecycle state. Production-adapter default/legacy/explicit-offset
checks and native-input stale-world cases pass. Native scene-load/exit/resource acceptance remains open:
[follow-up evidence](DOH_DESPAWN_5801_FOLLOWUP.md).

## KSA 5801 upgrade acceptance

The existing sidecar IDs/versions remain. Iron Man moves its unchanged v1 anchor payload from
removed native PartInstance.Id into a Components envelope, with legacy-attribute migration through
world/blueprint serializer hooks and restore at the private 11-argument constructor. Production
managed tests cover both getter serializers, repeated reconstruction and invalid/duplicate payloads.
Pebbles adds optional curvature/terrain fade state; legacy absence inherits stock. Paint saves RGB
unchanged and re-encodes GPU bits after load. Native rules/conditions/supplies/cargo stay KSA-owned.

All 18 managed suites pass against CURRENT; these do not prove native save reconstruction, GPU
retirement or in-game rendering. Native acceptance: legacy/new connected-anchor world + blueprint,
editor undo/redo, painted habitat highlights, repeated Pebbles replay/resource recovery, and scene
loads around crew deaths/boarding. See [5801 evidence](KSA_5801_UPGRADE.md).


## Runtime bootstrap follow-up

The default-networking bootstrap and diagnostic caches are process startup state, intentionally absent from scene saves. No persisted state or save lifecycle changes. Managed load-context regressions cover standalone/pair/triple ordering and late/pre-existing binding failures; native acceptance still requires a Windows combined-mod startup followed by ordinary save/load with the existing sidecars.

Implementation baseline: `feature/saves`, KSA 5402 reference assemblies, September 2026.
Current compatibility: **5482** (`2026.9.22.5482`, upgraded from 5438), including Kitchen Sink
G-load records and Dent Wizard. The 5482 fixes outside the separately tracked ground-clutter work
change no save ID, version or payload. Against the 5482 reference assemblies, the checkout builds
**74 solution projects** with no warnings or errors, and all **15 managed suites** pass on macOS.
The preceding Kitchen Sink reconciliation passed **71 projects and 14 managed suites** on Windows;
Kitchen Sink contributes 54 damage/lifecycle checks and 26 real-adapter save/restore checks.
Both version-1 records remain unchanged. See [reconciliation evidence](KSA_5438_RECONCILIATION.md).
Kitchen Sink now also registers `kitchen-sink-iva-camera` v1/order 190 (feature inspected against
local 5541): unlock state, base speed, exact seat and body-relative pose; native follow/mode
must match. The two earlier Kitchen Sink records retain their payloads. Production camera adapter,
controller ownership and shared identity resolvers are exercised by `kitchen-sink.tests`;
free-flight input/rendering/audio remain native acceptance work.
Kitchen Sink's capsule-glass experiment adds `kitchen-sink-capsule-glass` boolean v1/order 20,
independent of the existing three records. Its real adapter/coordinator passes 17 checks; the
production shared filter/IVA ownership passes 20 checks in `ksa-upgrade.tests`. Reset releases
its visibility requirement and replay reacquires through the normal toggle. Missing legacy records
leave it off; malformed/future/unavailable records produce diagnostics and are retained. Native
main-view glass/tint/depth and actual save-load rendering remain acceptance work.
The accompanying Graffiti 5541 descriptor binding migration changes only transient GPU binding;
its existing saved decal payload and reset/replay ownership remain unchanged.
IVA addition validation: `dotnet build -m:1 -p:UseSharedCompilation=false
-p:UNSCIENCE_DIST_DIR=/private/tmp/unscience-iva-dist` passed against local 5541 with zero
warnings/errors. `kitchen-sink.tests` passed 47 camera + 26 save + 54 G-load checks;
`saves.tests` passed, including 16 native-lifecycle fixtures, 24 coordinator/storage and 10
part-identity checks. Native rendered/input/audio acceptance remains pending.
Capsule experiment validation: full `dotnet build -m:1 -p:UseSharedCompilation=false
-p:SelectedDistModDir=/private/tmp/unscience-capsule-build/` passed against local 5541 with
zero warnings/errors. `kitchen-sink.tests`, `ksa-upgrade.tests` and `saves.tests` all passed;
no native game was launched.
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
[registered submods](../unscience/Mod.cs). Feature records include: Pyro separates
shared template edits from plume instances, Kitchen Sink keeps G-load registrations in a
separate backward-compatible record alongside its original IVA boolean, unlocked-camera record and capsule-glass boolean,
and The Tick adds `the-tick`. Save Yourself registers no record: its
prefix/toggle/interval are a global preference in `.unscience/save-yourself.toml`, and it only
*produces* native saves through the ordinary path. Standalone mod entrypoints do not acquire
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

DOH naming addition (October 25, 2026): additive `SpawnName` in the existing version-1 `doh` record
captures the editable exact name / batch prefix for both spawn buttons. Old records and vanilla/new
scenes use `Kitten_doh_`; blank input preserves automatic naming. Existing kitten names remain
native-owned. Current naming derives the next suffix from native scene/roster names without a counter.
Managed naming and real-adapter checks cover Unicode/blank round-trips, detached capture, repeated
and cross-scene loads, legacy/missing records and invalid text. `doh.tests` passes all 55 checks.
Full `dotnet build -m:1 -p:UseSharedCompilation=false
-p:UNSCIENCE_DIST_DIR=/private/tmp/unscience-doh-name-dist` passes against local KSA 5638 with zero
warnings/errors. Native UI/save-load acceptance is pending.

## Implemented feature coverage

Each source link is the adapter defining the current capture/reset/restore policy. “Restores”
below describes implemented behavior, not an assertion that native acceptance has passed.

| Submod | Captured setup and restoration | Continuation, exclusions and dependencies |
|---|---|---|
| [Blinky](../blinky.lib/BlinkySubmod.Persistence.cs) | Named grid ownership, exact existing A/B engine-cell addresses, rows/columns, sparse active mask, render flag, scroll pixels/speed/offset, pending grid deletion. Rebinds native-created parts and repairs fuel feeds. | Never spawns a second grid. Preserves native ignition/module state; scrolling resumes its offset. Pending deletion restarts its delay against new references. Arbitrary unfinished build/editor input is not captured. Missing/duplicate cells reject that grid with a warning. |
| [Bloomin' Onion](../bloomin-onion.lib/BloominOnionSubmod.Saves.cs) | Applied body-to-ring definitions, including procedural band configuration, stripes and asset selections. Resets owned rings then reapplies definitions before Rocky overlays. | Applied definitions only; unsaved editor drafts and preset-library files are separate. Referenced meshes/textures and normal controller restrictions still apply. |
| [BYO Music](../byo-music.lib/ByoMusicSubmod.Saves.cs) | Unfinished sounds' vehicle targets, filenames, repeat, gap, volume and range. | Every restored sound is paused. Resume starts at the beginning; sample position, current gap progress and former paused/playing distinction are not restored. Finished sounds are omitted. Sound files remain required. |
| [Camera Controller Override](../camera-controller-override.lib/CameraControllerOverrideSubmod.Saves.cs) | Ordered typed keyframe recipes, parallel groups, pending group recipes, return-to-start duration/easing/powers. | Restores stopped. Current running segment, elapsed time and its captured start pose are not resumed. Native camera pose loads independently. Pressing Play starts the reconstructed sequence through its normal behavior. |
| [Dent Wizard](../dent-wizard.lib/DentWizardSubmod.cs) | KSA saves the launched vessel's resulting orbit/physical state; v1 `dent-wizard` is an empty lifecycle record. Reset cancels automatic mode, armed and pending shots. Mass labels are read live from native vessel state and are not sidecar data. | Source selection and speed are input for the next one-shot action, not an applied registration or ongoing force; they reset to unselected/5 m/s. Launches never replay on load. Kitchen Sink independently saves any source/target G-load protection. |
| [DOH](../doh.lib/DohSubmod.Persistence.cs) | Native-restored spawned kitten tracking; native roster integration/backfill; name/prefix/offset/count/character; selected War Paint preset and detached spawn recipe. Roster history remains KSA-owned. | Existing v1 tint/material/group records migrate into independent War Paint targets. Native kittens are rebound without respawning. Missing targets warn. Pending spawn/despawn actions are transient. |
| [Don't Stifle Me](../dont-stifle-me.lib/DontStifleMeSubmod.Saves.cs) | Editor scale enable/snap flags and extended-value policy. | Existing part geometry/configuration remains native-owned. Does not serialize an editor undo stack or uncommitted operation. |
| [Eternal Flame](../eternal-flame.lib/EternalFlameSubmod.Saves.cs) | Monitored vehicles, fuel/electricity switches and refill interval. | Monitoring resumes; interval timing restarts, without offline catch-up. Native save owns the current resource quantities. |
| [Free Fallin'](../free-fallin.lib/FreeFallinSubmod.Saves.cs) | Applied global canopy material settings and effective albedo. | Recreates appearance, not a cloth simulation checkpoint. Applied state only; external textures remain dependencies. |
| [Garry's Torch](../garrys-torch.lib/GarrysTorchSubmod.Persistence.cs) | Source/target/optional target-part, pose, XYZ factor, lock/collision/enabled flags; original full-part and avatar scale baselines; active and queued weld animation recipes. | Rebinds ownership while keeping native effective full-part scale, including attachments not yet scaled by an idle weld. Separate avatar correction is restored. Animation start/elapsed/easing and queued steps continue. Unweld uses the original baseline; topology changes, ownership conflicts and dependency cycles can prevent restoration. |
| [Glass](../glass.lib/GlassSubmod.Saves.cs) | Field-of-view override enable state and degrees. | Native camera selection/pose remains native-owned. Preset library is not copied. |
| [Godzilla](../godzilla.lib/GodzillaSubmod.Persistence.cs) | Vessel Smart/Basic factors, physics/collider channels, global channels, original full/subpart scale, original full-part positions/pivot/avatar size and effective child scales. | Reconstructs scale ownership from original snapshots so repeated loads do not intentionally compound scale. Restore returns to those originals. Missing/changed topology and another scale owner are reported instead of guessing. |
| [Graffiti](../graffiti.lib/GraffitiSubmod.Saves.cs) | Terrain, part and canopy decal anchors, image names, projection geometry, cloth triangle/barycentric address, visibility, brightness/alpha. | Placement mode stops. Missing PNGs or unavailable canopy can leave a dormant entry with warnings; missing part/body cannot be rebound. Cloth deformation itself is not captured. Images and compatible canopy topology remain required. |
| [Hot Pursuit](../hot-pursuit.lib/HotPursuitSubmod.Saves.cs) | Mounted part, surface basis, transform, FOV, dimensions, visibility and whether its viewport was open. | Placement mode stops. Attempts to reopen saved viewports; slot exhaustion keeps the camera setup and reports that its viewport needs reopening. No native viewport/texture handles persist. |
| [Humble Arteest](../humble-arteest.lib/HumbleArteestSubmod.Persistence.cs) | Part paint activation/blend and global/template/part colors; engine emissive overrides; compatibility v1 HideVisor flag for War Paint's checkbox. | Legacy v1 MaterialColors migrate to private War Paint ownership; unmatched assets warn. Part targets and module layouts use existing stable resolvers. |
| [War Paint](../war-paint.lib/WarPaintService.Persistence.cs) | Active kitten identity, character, preset name and effective tint/material recipes, including previews. Global presets remain outside scenes. | v1/order85 with additive optional preset name; recover named legacy/missing-library recipes; restore exact reconstructed targets after DOH/Humble migration; reset original bindings and safely reuse/retire exact owned slots. Missing/incompatible targets warn and retain records. |
| [I Feel Seen](../i-feel-seen.lib/IFeelSeenSubmod.Saves.cs) | Tracked vehicle identities and each force-visibility flag. | Rebuilds tracking on native-restored vehicles. Deleted/missing vehicles warn. |
| [Iron Man](../iron-man.lib/IronManSubmod.Saves.cs) | Configured kittens, enabled mode, original EVA/control settings needed to disable the mode, current flight-computer preferences. | Mode restores with engines **disarmed**. Existing connectors are configured/reused through the normal code path. Runtime thrust/burn continuation is not promised; the player deliberately arms engines again. Custom anchors use a native Components envelope since 5801 (legacy Id markers migrate); the unchanged v1 payload is decoded at the private 11-argument Part constructor before index reconstruction. |
| [It's So Shiny](../its-so-shiny.lib/ItsSoShinySubmod.Persistence.cs) | Existing host/light cell addresses, grid ownership/appearance, sparse mask, render flag, scroll recipe/offset and pending deletion. | Rebinds native cells without spawning or forcing ignition/switch state; scrolling resumes. Pending deletion restarts its delay. Shared light-template appearance retains the feature's existing shared-template semantics, with Zippo's template ledger restoring originals. |
| [Kitchen Sink](../kitchen-sink.lib/KitchenSinkSubmod.Saves.cs) | Independent IVA visibility, capsule-glass experiment toggle, G-load target IDs, unlocked IVA camera/speed/exact seat/body pose in four separate v1 records. | Releases original-world controller/input before reconstruction; exact vehicle/part/seat replay requires matching native camera mode/follow. Missing/ambiguous/invalid targets and unavailable patches report and retain state. Absent legacy camera record resets to locked/default speed. Capsule glass reset releases its global interior requirement, with exact static-model filtering re-enabled on replay; absent records leave it off. Picker, held input, render/template caches and smoothing are transient. |
| [Kitten Animations](../kitten-animations.lib/KittenAnimationsSubmod.Persistence.cs) | Every registered kitten by exact vessel ID; per-kitten forced clip/source/label, active/paused state/phase, driver controls and latched expression identity/settings; scene-level panel selection/global tuning. | All targets restore independently of control/camera; legacy single-target v1 fields explicitly migrate when the added Kittens array is absent. Forced clips/frozen poses resume, and latched expressions return at held weight. Unlatched one-shot expressions stay stopped; intermediate expression easing is not checkpointed. Missing/ambiguous clip identity or unavailable native phase fields warn/fail the block. |
| [Kiwi's Marbles](../kiwis-marbles.lib/KiwisMarblesSubmod.Persistence.cs) | Celestial source/vehicle-or-celestial target/offset and original orbital parent, epoch and state vectors. | Restores ordered welds and their future Unweld baseline. Non-vehicle targets resolve by exact id among the offered orbiters, so orbiting stars restore (5638). Cycles through both target dependencies and actual parent ancestry are rejected. This is a weld/orbit recipe, not all possible arbitrary celestial-system mutations. KSA 5707: Pluto is now tilted 113° (rev 5649), so 5638-era offsets for welds whose target is under Pluto point a different world direction; payload unchanged, no migration. |
| Parts Now (**removed @5638**) | Nothing; legacy sidecars may still hold a `parts-now` v1 dependency record. | No participant owns the id: the coordinator reports `parts-now: unsupported feature/version 1`, retains the record and writes it back unchanged; the native load and every other feature proceed. Pinned by `saves.tests` `RetiredFeatureTests`. |
| [Pebbles](../pebbles.lib/PebblesSubmod.Saves.cs) | Applied per-body clutter recipes, including their imported mesh identities/settings (`pebbles`); removed/displaced clutter on non-stock-spacing grids (`pebbles.clutter-state`, v1, order 61, new @5482). | Recreates applied fields and releases old import ownership. Stock-spacing removed/displaced clutter is saved natively by KSA 5482; Pebbles keeps those native entries stock-grid and restores override grids after recipes (native state wins). Workshop previews and unapplied recipe edits are not captured; pending unsynced hits are lost as natively. A missing record (vanilla/older saves) restores recipes and stock state only. Imported GLB assets remain required; changed content hashes reject the saved selection. KSA 5707 adds an optional recipe `UsesCustomNormals` flag; absent (older records) inherits the stock material's value, so record ids/versions are unchanged. |
| [Pyro](../pyro.lib/PyroSubmod.Persistence.cs) | `pyro.templates`: edited shared plume templates. `pyro`: anchored plume settings, enabled flags and on/off cycling configuration, current phase and remaining interval. | Recreates plume instances after template edits, with cycling resumed without offline catch-up. Does not embed referenced assets or native renderer state; preset catalog remains separate. |
| [Rocky McRockFace](../rocky-mcrock-face.lib/RockyMcRockFaceSubmod.Saves.cs) | Applied ring overlays still owning their target ring: LOD mesh/material/band selections and field overrides. | Reapplies after Bloomin' Onion. Superseded overlays and unapplied selection drafts are omitted. Referenced assets must remain available. |
| [Save Yourself](../save-yourself.lib/AutoSaveSettingsStore.cs) | Nothing. Prefix, enabled flag and interval are a global Unscience preference (`.unscience/save-yourself.toml`), written through on change. | Intentional exclusion: auto saving is a session preference, not scene state, so loads never change it and the countdown continues across loads. Countdown position, last-save name and counters are transient. The feature produces native saves via `GameSaveProvider` so every other adapter runs as for a manual save. |
| [Skittles](../skittles.lib/SkittlesSubmod.Saves.cs) | Effective ImGui theme colors and style values. | A scene can restore its theme; native saves without a sidecar reset to the captured startup-theme baseline. Global theme library/default preference and window layout autosave are separate. |
| [Sphinx](../sphinx.lib/SphinxSubmod.Saves.cs) | Applied body-local statics, GLB/PNG identity, anchor position/normal, scale/rotation/offset, UV mapping, visibility/alignment and collision mode. | Recreates render resources and colliders under existing limits (32 statics and aggregate vertex budget). Pending placement actions are cleared. Assets are not bundled; collision behavior requires native verification. |
| [Wing Walker](../wing-walker.lib/README.md) | No sidecar: native part/template, IVASeatData, roster, tanks and life support. | Template selects posture; transient rendering scope and diagnostics are not persisted. Native repeated A–B–A load and crew/resource checks pending. |
| [The Tick](../the-tick.lib/TheTickSubmod.Saves.cs) | Indestructible vehicle IDs in a version-1 `the-tick` record (order 20). | Clears old references before reconstruction and rebinds exact IDs through the shared resolver; missing/ambiguous targets warn and retain the record; unavailable patches fail visibly. Vanilla and record-less saves restore nothing; picker/filter are transient; patch installation is global. |
| [Thug Life](../thug-life.lib/ThugLifeSubmod.Persistence.cs) | Anchored sunglasses quads, local position/rotation, size and visibility. | An entrance slide saves its current pose and stays stopped after load. Its remaining animation is not replayed. |
| [Zippo](../zippo.lib/ZippoSubmod.Persistence.cs) | Edited shared light component originals/current values; per-part default color baselines; Disco draft and active recipes, paused state, phase/seed, switch and actuator originals; active/queued light transitions. | Disco and queued transitions continue from saved phase/elapsed values. Restores actuator ownership using current module ordinals; conflicts/layout changes warn. Shared-template edits retain their original shared scope. GPU light/material objects and live queue keys are never serialized. |

## Capsule glass native acceptance (pending)

5801 visibility follow-up removes the editor's automatic reveal. The existing `kitchen-sink` and
`kitchen-sink-capsule-glass` v1/order20 records and their reset/replay ownership remain unchanged.
Thirty production render/ownership checks and Kitchen Sink's adapter suite pass; full build is clean.
See [evidence](IVA_VISIBILITY_SWITCHES.md).

- Enter/leave the editor with both switches off: no forced cabin. Test each switch alone and both
  together; release the final switch while editing and confirm native visibility is restored.
- Repeat all four saved switch combinations across scene loads and editor entry; vanilla/legacy
  scenes with neither enabled keep native visibility. Native IVA/crew portraits still show cabins.

- Main exterior view of a stock medium/Gemini capsule: enable **See Inside Capsule (Experimental)**,
  inspect both windows/cabin from several angles; check depth/tint, culling, shadows, door/hull isolation.
- Toggle **Always Render IVA Interiors** and the experiment in both orders, including changing
  the independent preference while the experiment is on; disable/unload and verify restoration.
- Spawn another capsule while enabled; retain native IVA glass in IVA with ray tracing on/off.
  Verify Blinky/Shiny owners keep working. Secondary feeds may omit glass through native behavior.
- Save each switch combination, change switches, reload repeatedly and A-B-A; load a vanilla or
  older save and verify experiment off. Confirm global interior requirement clears and restarts
  correctly. Native crew submission and exterior lighting remain unchanged.

## Recovery and portability limits

Kitchen Sink persistence follow-up (2026-09-13): `kitchen-sink.tests` passes 26 additional
real-adapter/coordinator checks covering G-load registrations, alongside its damage checks (54 at 5482;
108 at 5638, including the kitten hard-landing branch and a real-KSA.dll signature probe).
The shared `saves.tests` suite and full solution build also pass. Native cart/save/load acceptance
remains open; the earlier baseline suite run below is not a claim of a new full-suite rerun.

- Copy the **whole native save directory**, including its matching sidecar. PNG, GLB, audio,
  character libraries and installed content mods (including folders the retired Parts Now
  submod installed) remain external dependencies. Saves do not bundle their
  bytes or guarantee identical output if those files or installed game assets change.
- PNG and audio references use filenames without content fingerprints. Replacing a same-name file
  changes the media restored by the save; there is no saved hash to detect that substitution. Pebbles/
  Sphinx GLB identities do carry a content hash: restore resolves the filename inside the installed
  shared GLB library and rejects changed content rather than silently choosing a new mesh version.
- Normal unresolved targets produce feature warnings; references do not fall back to the controlled
  vehicle or a similarly named part. Structural addresses assume compatible native reconstruction.
  They are not a general migration mechanism for edited `universe.xml` files or changed part trees.
- Native system/structure preflight can reject a load before destroying the current world. Since
  KSA 5638 a missing part template, character or parent body no longer refuses it: KSA skips that
  vehicle (with its own alert), Unscience warns once per vehicle, and features report unresolved
  targets while keeping their records. It is not exhaustive native validation and cannot guarantee
  rollback after any later native fault.
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
[Iron Man flight settings](../iron-man-flight.tests/README.md),
[Pyro cycle phase](../pyro.tests/README.md), and
[DOH kitten/material-set rebinding and slot release](../doh.tests/README.md). The scale/animation checks include nonzero numeric
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

DOH GPU material-pool fix (September 2026, KSA 5482): adds the optional per-kitten `MaterialGroup`
field to the unchanged version-1 `doh` record, and releases detached material sets. The new
`doh.tests` suite (25 checks) and the full solution build pass on macOS. These are managed results
only; the DOH checklist items below remain open in-game.

KSA 5638 upgrade (October 2026): no save IDs, versions or payloads changed. Native preflight now warns
for vehicles KSA skips (missing part/character/parent) instead of refusing the load; the reset join is
`JobSystems.WaitAll()`. Kiwi's Marbles star-target welds restore; DOH despawn uses the native teardown;
The Tick and Kitchen Sink G-load records restore again (their detector lookup was inert on 5638 until
fixed). Legacy `parts-now` records are retained and reported. Managed results: `saves.tests` (18
lifecycle checks incl. skipped-vehicle loads, plus 16 retired-feature checks), `doh.tests` (34),
`world-saves.tests` (star-target weld), `kitchen-sink.tests` (108 G-load), `the-tick.tests` (108); all
18 suites and the full build pass. See [KSA_5638_UPGRADE](KSA_5638_UPGRADE.md).

KSA 5707 upgrade (October 2026): no save IDs or versions changed. Iron Man's native anchor marker is
decoded at the private `Part` constructor (the 5638 target no longer saw loads, so marked kittens lost
anchors or failed to load) and re-created on `PartCloner` copies. Pebbles recipes gain an optional
`UsesCustomNormals` field that legacy records inherit from the stock material. KSA now saves kitten
radiation dose in its roster and derives space weather from seeds and time — both deliberately not
sidecar state. Radiation can destroy an EVA kitten before the frame handoff; existing unresolved-target
reporting covers it. Managed results: `iron-man.tests` (5707 load/copy constructor chain),
`pebbles.tests` (legacy/explicit/round-trip flag), `doh.tests` (58), `ksa-upgrade.tests` (real KSA.dll
constructor-chain probe); all 18 suites and the full build pass. See [KSA_5707_UPGRADE](KSA_5707_UPGRADE.md).

October 25 startup follow-up (5638): the Windows log reports the shared frame hook failing to
install, and the user confirmed welding did not work. Scene-save hooks require that same handoff
before they install. A production-Harmony call-wrapper regression reproduces the failure shape;
the handoff now orders its validation before existing foreign wrappers and cleans failed installs
without removing foreign patches. This changes installation, not the save boundary, participant
IDs, versions, payloads, stable identity resolution or replay policy. Applied canopy settings remain
scene-saved; CRLF parser support changes only shader activation. Patch reports, owner/order
diagnostics and shader caches are transient and are rebuilt at startup. A native original-build
comparison disables only purr2d2: the shared hook installs, all three patch errors disappear and
welding works, with purrTTY/gatOS still enabled. Updated-build coexistence with purr2d2 and actual
native scene-save behavior remain unverified; the exact original inner exception is unavailable.

The actual-source audit adds `Purr2D2.Interop`: real Purr2D2 `GameHooks`, `PhysicsFrameHook` and
`NativeSaveHooks` compose on native-independent fixtures. A last-priority Purr2D2 deserialize
prefix observes `__runOriginal`, so a deferred or rejected direct load no longer clears its current
session equipment/holds/requests before replay; actual cleanup joins native jobs. File-load
deferral remains at `UncompressedSave.Load`. Existing Purr2D2 post-load cleanup is idempotent.
Its settings/generated assets remain global, fragment results are native-saved, and its per-kitten
equipment/holds retain their documented session policy. Unscience feature records, versions,
payloads and reset/replay ordering are unchanged. Iron Man keyboard priority and the Purr2D2
non-character-worker guard are transient dispatch/ownership changes, not new saved state.

## Native acceptance checklist

Record KSA build, Unscience commit, OS/architecture, installed mods/assets, pass/fail and observed
warnings for each run. Use throwaway save copies for failure injection. Keep an untouched source
save and screenshots/counts/reference values for comparison. All boxes begin unchecked.

- [ ] **5638 startup follow-up:** with the same purr2d2/purrTTY/gatOS stack, confirm the shared
  `PrepareFrame handoff hook applied` line, no failed scene-save/Dent Wizard/weld groups, and a
  zero-failure startup summary. Create a weld and confirm actual motion, then normal save/overwrite
  produces a fresh `unscience.json`; reload twice and A→B→A, load a vanilla save and a new system,
  and verify old ownership/queued mutations clear. Also test Dent Wizard firing and Godzilla
  Apply/Restore. If startup still fails, record the complete inner exception and target patch-owner
  report. The original-build comparison without purr2d2 passed startup/welding; this item requires
  the updated build with purr2d2 enabled and actual scene-save acceptance.

### Transaction and lifecycle

- [ ] Create a scene with multiple modified features, save A, edit them, then load A through normal
  KSA UI. Sidecar exists, status has expected records, and reconstructed setup matches saved values.
- [ ] Save a different setup B; run A → B → A and repeated A reloads, then exit/restart KSA and load A.
  Confirm process-global templates and render flags do not leak from the previous scene.
- [ ] Load a native-only save and create/load another system. Confirm old ownership, controllers,
  template edits and queued world mutations are cleared; startup theme baseline returns.
- [ ] Test save overwrite, distinct save names, load from the console, and load while the toolbox is
  hidden with F2. Verify normal gameplay resumes and no current-frame texture reference is invalidated.
- [ ] Attempt editor-refused load, invalid system and malformed native data. Confirm refusal before
  destructive reset where preflight applies, with useful status.
- [ ] Load a save with a missing part mod and one with a missing character (5638): KSA alerts and skips
  the vehicle, Unscience shows one warning per vehicle, crew return to the roster, and feature records
  for that vehicle are kept across a re-save.
- [ ] Load while a radiation step limit is active during warp (5707): the first resumed step may be
  shortened once; no queued Unscience mutation or weld runs against the previous world.
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
- [ ] In DOH, spawn repeatedly with the prefilled name and a custom name. Occupied names must
  automatically advance above the highest matching suffix, including gaps and roster-only names.
  Repeat with a numbered single name, custom batch prefix and blank automatic `Kitten_doh_` names.
  Save/load twice and confirm continued numbering, unchanged input and native names, no duplicate
  kittens/crew and preserved KIA/retired history. A legacy/vanilla save resets the field to its default.
- [ ] Save a DOH non-unique tinted batch; reload and confirm the batch still shares one set ("Tint All" on
  one recolors all) while unique kittens stay independent. Watch the DOH window's GPU slot counter across
  repeated loads, a vanilla load plus a spawn, and Despawn All: usage must return to its baseline instead
  of growing.
- [ ] Save enabled Iron Man mode with customized EVA/flight preferences. Confirm connectors are not
  duplicated, engines load disarmed, manual arming works, and Disable restores original control settings.
- [ ] Iron Man (5707): save and load a world and a blueprint with connected custom anchors; the kitten
  keeps its name (no `iron-man:v1:` id), node order and fuel flow. Copy Tree an unconfigured kitten with
  anchors; the copy has the same anchors and no crash.
- [ ] Radiation (5707): save/load with an EVA kitten killed by radiation and with a retired kitten.
  Records for the removed kitten are reported unresolved, with no stale-reference errors; KSA's roster
  dose columns restore natively. DOH automatically advances a spawn name held by a rostered kitten.
- [ ] Verify Eternal Flame monitoring, I Feel Seen visibility, IVA rendering, editor flags and Glass FOV
  resume their saved policies, including disabled/false values and missing-target diagnostics.
- [ ] IVA unlock: save away from the second seat with a changed speed; reload twice and A-B-A across
  scenes; confirm exact pose/seat/speed, IVA lighting/audio, head visibility and Return to Seat.
  Vanilla/legacy loads and new scenes release movement/input; missing seats or vehicles report
  failure without redirecting to another vessel. Verify mode changes, UI focus and unload cleanup.

### Playback and appearance

- [ ] Save Garry and Zippo transitions partway through easing with several queued steps. Compare post-load
  continuation to an uninterrupted run through queue promotion; confirm elapsed offline time has no effect.
- [ ] Save paused/running Disco lights with randomized color/movement and actuator control. Compare phase,
  switches and colors, then Stop and check original light/actuator values. Test an ownership conflict.
- [ ] Hold distinct body clips/frozen poses and latched faces on two EVA kittens in follow-controlled
  mode. Switch between them and a rocket, move the camera away/back, hide the toolbox, then edit one
  through the explicit picker. Both holds persist; clearing/disabling one leaves the other intact.
- [ ] Save those two kittens while controlling a rocket; reload twice and A→B→A. Verify exact target
  IDs, phase/pose, held facial variant, per-kitten strength and global tuning. Re-save before either
  kitten becomes visible. Load a legacy single-target record and a vanilla/new scene; no stale holds
  remain. Missing/destroyed targets warn without redirecting; exact-ID return rebinds. Unlatched
  one-shot expressions stay stopped on load. Native culling need not animate an invisible body.
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
- [ ] Pebbles (5707): load a 5638-era recipe on a body with stock trees; foliage AO matches stock
  (`UsesCustomNormals` inherited) and the depth-normal pre-pass shows correct cutout silhouettes.
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
  structural preflight refusal, natively skipped vehicles and partial scene restoration; reinstall dependencies and reload the untouched original.
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
