# Unscience — Full Game-Integration Scope

**You Didn't See Anything** optionally retains ImGui windows during F2 hide UI, with detached
platform-window selection and stock-gauge exclusion, including optional purr2d2 detached HUD
submission/custom-input compatibility. Flags are scene-owned; native rendering/input
acceptance remains pending. [UI integration](ui-customization.md#you-didnt-see-anything-5801) ·
[implementation and acceptance](../plans/YOU_DIDNT_SEE_ANYTHING.md).

Kitten Animations retains per-kitten holds across camera/control switches and saves all targets by
exact vehicle ID; legacy records migrate. Ported from `feature/animation-stickyness` to KSA 5801;
41 ownership/adapter checks, shared save/upgrade suites and the full build pass. Native multi-kitten
rendering and save/load acceptance remain pending. See
[character/materials](character-and-materials.md#kitten-animations) and
[save ownership](saves.md#kitten-animation-holds).

**War Paint** uses a preset editor with live previews across named kitten assignments, multi-target
application, dirty-only saving and confirmed deletion/unpainting. Width-aware tables follow the
ImGui design conventions, with padded/enlarged dialogs, taller materials, live text validation and
explicit deselection. Partial catalogs retry as native assets become ready. Earlier colors migrate into named presets; `war-paint` v1/order85 adds
optional preset names while preserving effective scene colors. DOH carries names into assignments;
Humble retains part paint/emissive controls. War Paint owns the global Show visor glass checkbox
and draw gate; action feedback uses the native game logger. The existing Humble v1 visor field
remains the compatibility save owner. [Editor acceptance](../plans/WAR_PAINT_PRESET_EDITOR.md).
[Integration](character-and-materials.md#war-paint-consolidation-5801) · [acceptance](../plans/WAR_PAINT_CONSOLIDATION.md).

Humble Arteest's reported immediate white vessels on Enable remain **unresolved**. Three
independent audits and 64 offline shader compilations found no exterior cause; native compiler-path
A/B diagnostics are available. [Evidence](../plans/HUMBLE_ARTEEST_5801_PAINT_INVESTIGATION.md).

Runtime startup includes the [shared networking bootstrap](00-architecture-and-abstractions.md#runtime-networking-bootstrap) and 19 isolated load-context scenarios. The helper is compiled into the Unscience entry module; no game member or sidecar contract changes. Native Windows combined-mod startup acceptance remains pending.

This folder maps the suite's game types, Harmony targets, reflection, shaders, assets and lifecycle
contracts. Update the owning area and master index in the same change as an integration edit.

DOH **Spawn at...** adds viewport collider/terrain picking, a capsule-clearance preview, synchronized
creation and scoped native input ownership. Native poses/crew and the existing DOH record retain
save ownership; pending gestures cancel on reset. [Integration and native acceptance](doh-placement.md).

DOH spawns now enter the native kitten roster and start an EVA mission. Existing tracked EVA
kittens are repaired without replacing native history; roster/career data remain KSA-owned.
Spawn name collisions automatically advance above the highest matching native scene/roster suffix,
including after loads; no counter or additional scene state is saved.
[Native roster integration and acceptance](../plans/DOH_ROSTER_INTEGRATION.md).

## Version baseline

- **Cataloged against:** KSA **2026.11.10.5801**, metadata date **2026-11-07**.
- **Diffed from:** **2026.10.25.5707**, matching PREVIOUS and the prior recorded baseline.
  Supplied source/Content trees are authoritative; in-repo `decomp/ksa` remains historical.
- **Build references:** explicit CURRENT `dll/`; output redirected to a temporary distribution.
  No native game installation is available on this macOS ARM64 host.
- **Changelog coverage:** all 93 revisions **5708–5800** logged; **5801 unlogged**.
- **Verification:** whole solution builds with 0 warnings/errors; all 19 managed suites, real-DLL
  reflection/constructor/layout checks, live-source paint/canopy transforms, 19 isolated bootstrap
  cases and distribution validation. **Static/managed only; native acceptance pending.**
- **Evidence:** [5801 impact and remediation](../plans/KSA_5801_UPGRADE.md). Dated area tables retain
  historical citations; current dated sections supersede their compatibility status. Previous reports:
  [5707](../plans/KSA_5707_UPGRADE.md), [5638](../plans/KSA_5638_UPGRADE.md),
  [5482](../plans/KSA_5482_UPGRADE.md), [5438](../plans/KSA_5438_UPGRADE.md).

## How to use on a game update

1. Resolve CURRENT, PREVIOUS, recorded baseline and actual build DLL paths.
2. Build the whole solution; diff every reflection lookup and Harmony signature/body.
3. Compare changed subsystem behavior, including worker handoffs and ownership lifetimes.
4. Recheck shaders, assets, GPU offsets and free flag bits even when compilation succeeds.
5. Fix confirmed breaks, update area/master documentation, run managed checks, and record the
   live flight/editor checks that have not been performed. Follow the upgrade-ksa skill.

## Integration model and distribution

Only **Unscience** ships. Legacy hosts remain compile-checked development projects. StarMap.API
attributes dispatch the shell; one consolidated Harmony instance applies feature patches.
`ksa-abstractions.lib` owns shared helpers, hotkey protection, hidden-HUD callbacks and the
`Program.PrepareFrame` mutation handoff. Features also reference KSA directly, so every area matters.
Scene-save adapters extend native saves; imported files remain external dependencies.

## Contents

| Area file | Covers | Highlights / highest-risk seams |
|---|---|---|
| [`game-integration-surface.md`](game-integration-surface.md) | **Master cross-reference index** — every game type/member touched, merged across mods | Start here for "does the game still have X?"; includes the string-reflection watchlist + shader/asset table |
| [`doh-placement.md`](doh-placement.md) | DOH click-to-spawn | Main-viewport mouse/key prefixes, physical part/static rays, accurate terrain, borrowed Bepu shape queries, capsule/COM/motion and scene cancellation; native acceptance pending |
| [`wing-walker.md`](wing-walker.md) | wing-walker | Native surface-mounted crew parts, render submission, standing animation/origin hooks and stock chair assets; native acceptance pending |
| [`saves.md`](saves.md) | Native scene save/load extension across all bundled features; save-yourself timed auto saves | Ordered cleanup/replay, deferred world replacement, native save directory sidecars and stable part references; Save Yourself produces native saves via `GameSaveProvider` (`SaveName.TryAccept` → `UncompressedSave.Make`, no patches) |
| [`00-architecture-and-abstractions.md`](00-architecture-and-abstractions.md) | unscience supermod shell (`Mod.cs`/`Patcher.cs`/`MenuBarPatch`/`UnscienceState`) + `ksa-abstractions.lib` | StarMap lifecycle map, consolidated-Harmony cross-ref, `HotkeyGuard`, `IvaForceRender`, providers |
| [`vehicle-physics.md`](vehicle-physics.md) | dent-wizard, eternal-flame, garrys-torch, godzilla, i-feel-seen, the-tick | `Universe.ExecuteNextVehicleSolvers` (Eternal Flame fuel + electricity refills since 5482), `Battery.Refill`, `Vehicle.Teleport` (+ `PhysicsFrameHook.JoinOrbitReaders`), KittenEva reflection, Godzilla's `IsLargeEnoughToRender` cull redirect; **Godzilla separates visual, collider and nominal physics size; garrys-torch preserves actuator results; default-off source collisions use scoped Bepu shape suppression**; The Tick prefixes `Part.CrashTolerancePascals` and prefix/postfixes `PhysicsBubble.DetectStructuralFailure` for per-vessel indestructibility |
| [`celestial-and-lights.md`](celestial-and-lights.md) | kiwis-marbles, zippo | `Celestial.SetOrbit`, `IParentBody.Children`/`UpdatePerFrameDataTree`, `Universe.ExecuteNextVehicleSolvers` prefix (kiwis-marbles sim-step timing, fixed 2026-08-23), `IOrbiter`, `LightModule`/`LightSwitch`; Zippo Disco's per-instance templates, cone angles and `KeyframeAnimationModule.TimeGoal` ownership |
| [`camera.md`](camera.md) | camera-controller-override, glass, hot-pursuit, kitchen-sink IVA unlock | `OrbitController/FlyController/FixedController.OnFrame`, `Camera._fovRadians`; Kitchen Sink temporarily replaces `GameViewport.IvaController` and patches seat head visibility; four public secondary-viewport leases under the sealed 8-slot registry; part-raycast camera mounts |
| [`pixel-grids-and-render.md`](pixel-grids-and-render.md) | blinky, its-so-shiny, thug-life | shared `PartRenderFilter` on `PartTreeRenderData.Compose*` (replaced the removed `*Module.UpdateRenderData` prefixes @5482), `PartTree.CreateFromNewPartTree`, `RocketCore.FeedConnectors` (blinky ignition), `SuperMeshRenderSystem.RenderMainPass`, UnlitMesh shaders |
| [`character-and-materials.md`](character-and-materials.md) | doh, war-paint, humble-arteest, kitten-animations | DOH exact vehicle names / numbered batch prefixes (scene-saved); `KittenRenderable.UpdateRenderData` visor draw gate (War Paint), `GpuMaterialSystem.BigBuffer`, `KittenEva`/`EVADoor` (spawn joins `JobSystems.VehicleSolver`; Smite uses native destroy input before frame-list refresh), `PerInstanceData` `StateBitFlag` free-bit paint written through private `PartTreeRenderData.WriteState/WriteDynamicState` (5482) + `ShaderModuleUtils.FromFile` shader patch; visor `Draw(ViewHandle)`; **kitten-animations** — filterable selection of any live EVA kitten by `Vehicle.Id`, Harmony prefix on `AnimatedRenderable.UpdateAnimation`, 17 private `KittenRenderable` animation fields, and a mod-owned `CatExpressionAnim` |
| [`part-editor-and-robotics.md`](part-editor-and-robotics.md) | dont-stifle-me | **dont-stifle-me** scale patches on `VehicleEditor.ScaleBoundsFor` / `UpdateSelectedScale` / `QuantizeScale`, plus configurable editor-limit patches on `DrawParachuteSection` / `Parachute.SetDiameter` (2–1000 m diameter). **flexo removed @5348** — compiled clean, but the robotics approach never worked and will not be reattempted; `OrbitLinePass` remains unowned; `PartModelRenderer.UpdateRenderData` is now used by Iron Man. **parts-now removed @5638** (user decision; its `ModLibrary` reflection and `DeviceMeshInterleaved.Shared` headroom invariant are retired; legacy `parts-now` save records are retained by the coordinator); 5638 notes on the CoreCommandA/Structural/Fairing convex-hull re-import, `PlumbedCore` and the `Interstellar` tag |
| [`iron-man.md`](iron-man.md) | iron-man | EVA/Iron Man mode selector with upright editor support in both modes, private connectors/save reconstruction, conditional worker `IsKitten` routing, rocket controls/gauges/RCS, active-only upright surface teleports, early equipment / late avatar rendering; **default EVA, native acceptance pending** |
| [`exhaust-plumes.md`](exhaust-plumes.md) | pyro (including runtime On/Off cycles) | `Vehicle.AddVolumetricExhaustInstances` postfix, `VolumetricExhaustRenderer.AddInstance`, `VolumetricExhaustInstance` (+ private `_shaderData`), internal `VolumetricExhaustTemplate.References`, `PlumeData`/`ExhaustInstance` layout drift (new @5348) |
| [`decals.md`](decals.md) | graffiti | `RenderTarget.ResolveAttachments` postfix (GridPass-window projected-decal pass), `GlobalShaderBindings` + `BindlessTextureLibrary` descriptor sets, runtime GLSL vs `Common/*.glsl` headers, `Part.RayCastEgo` + live `Parachute.ClothPositionsFront` triangle picking + `Cursor.GetEgoRay`, CPU terrain march; **no string reflection** (new @5348; canopy picking added @5402) |
| [`parachutes.md`](parachutes.md) | free-fallin | `ChuteRenderable.Draw`, `Utils.SetShaderFromMod`, and `ShaderModuleUtils.FromFile` prefixes; private `_renderable` + protected `AnimatedRenderable.MaterialIndices`; runtime `MaterialData` and PNG/PBR uploads; material-gated bind-pose projection through `Model{,_Skinned}.vert` / `ModelPbr.frag`; stock canopy assets (new @5402) |
| [`ground-clutter.md`](ground-clutter.md) | pebbles; [GLB materials](ground-clutter-glb-materials.md) | Per-body native clutter graphs, private materials, `ExecuteNextClothSolvers` transactions, collider/physics invalidation, shared copied GLB discovery, uploads and independent Workshop preview; shared Harmony ownership |
| [`statics.md`](statics.md) | sphinx | Body-fixed GLB placement; live transforms and UV scale/offset, private vertex replacement, native static-renderer postfixes, shared GLB/PNG imports, terrain picking and automatic box/mesh colliders with per-bubble contact/lifetime hooks |
| [`rings.md`](rings.md) | rocky-mcrock-face, bloomin-onion | planetary-ring mesh/texture swap (rocky) and **runtime ring definition on any celestial** (bloomin-onion) via the public `PlanetaryRingsReference` data tree + `Program.RebuildRenderer()`; **no Harmony patches**; `ModLibrary.AllMeshes`/`AllFiles` reflection, `MeshReference.<HostPrimitives>k__BackingField`, ctor-baking invariant in `PlanetaryRingsRenderData`; bloomin-onion adds `PlanetTransparenciesRenderer._anyRings` (load-bearing), `TextureReference.<TextureAsset>k__BackingField` (painted textures) and a cosmetic `DistantSphereRenderer._material` ring-shadow sync (moved from `_data` @5482); ring data also drives atmosphere ring shadows since 5470 |
| [`ui-customization.md`](ui-customization.md) | skittles, kitchen-sink, you-didnt-see-anything | `ImGui` style surface, F2 window/platform filtering, stock gauge exclusion, editor refresh/IVA rendering, **experimental medium-capsule exterior-window reveal** via exact static-model filtering, and selected-vehicle G-load protection via `PhysicsBubble.DetectStructuralFailure`; Flexo diagnostics removed |
| [`audio.md`](audio.md) | byo-music | Shared sound imports, FMOD stream/channel ownership, vessel-relative 3D playback and repeat/gaps |

Bundled in the unscience supermod (33): blinky, bloomin-onion, byo-music, camera-controller-override, dent-wizard, doh,
dont-stifle-me, eternal-flame, free-fallin, garrys-torch, glass, godzilla, graffiti, hot-pursuit, humble-arteest,
i-feel-seen, iron-man, its-so-shiny, kitchen-sink, kitten-animations, kiwis-marbles, pebbles, pyro,
rocky-mcrock-face, save-yourself, skittles, sphinx, wing-walker, the-tick, thug-life, war-paint, you-didnt-see-anything, zippo. (jplrepo is a development reference and is not loaded by the supermod.)

---

## Current status against 5801

Wing Walker adds native crew parts, scoped boarding and rendering hooks. Prior checks passed; its
helmet and standing-pose fix passes focused and full builds, packaging and the native CPU probe; the
updated production fixture passes; the in-game visual retest remains pending. [Integration and risks](wing-walker.md) · [validation](../plans/WING_WALKER.md).
Standing Mount now uses custom brown leather foot straps with a GLB/picking atlas and PBR textures;
all legacy subpart slots retain their order for native saved-state matching.

DOH **Smite / Smite All** replace custom despawning with KSA's native kill input (Kill/Abandoned),
which runs before the render-list refresh; the former physics handoff ran too late. Private War Paint
materials release after native death, reusable presets remain, and unload leaves vehicles to KSA.
Default offset and scene payloads stay unchanged. [Evidence, checks and native acceptance](../plans/DOH_NATIVE_KILL_5801.md).
DOH's panel now uses a Name/Model/Smite table, equal-width spawn/refresh actions and full-width
Smite All; transient action feedback uses KSA's logger instead of panel status/count labels.
The earlier Unscience pre-renderer shutdown and ownership-aware Graffiti cleanup remain in place.

Migrated resolved data/key references in rings, clutter, Pyro, Disco and capsule filtering. Fixed
DOH's silent Get-reflection miss, native yellow/purple highlight collisions in Vehicle Paint, and
Iron Man's changed constructor/save carrier with legacy anchor migration. Pebbles preserves new
curvature/terrain blending and retires terrain resources; custom pipelines explicitly dispose native
staging data. Legacy BYO Music XML follows the new content schema. Sidecar IDs/versions are unchanged.
Native rendering, legacy saved craft, editor undo/variants and life-support behavior need acceptance.
See [5801 review](../plans/KSA_5801_UPGRADE.md).

## Earlier status (5707)

Two compile breaks: `SimpleVkTexture.ImageView` → `VkImageView` (graffiti, pebbles, thug-life) and
`Part.Connectors` → `Connector[]` (Iron Man). Silent breaks fixed: **Iron Man** anchors were lost (and
connected ones crashed) on save load because every `Part` constructor now chains through one private
constructor that its patch did not cover — it now patches that seam, and a copy-constructor postfix keeps
anchors on `PartCloner` copies; **Sphinx** drew with no pipeline bound when the 10-set static pipeline
fails to build (rev 5640) — now skipped like stock; **Pebbles** did not redirect the new depth-normal
pre-pass (wrong AO/silhouettes) and dropped the new `UsesCustomNormals` foliage flag (optional recipe
field, inherits stock); **DOH** custom names could bind a rostered kitten's radiation/KIA state — now
refused. New native-only drift: radiation death (outside The Tick), step-limited warp, collider-based
spawns and strut colliders, Hot Pursuit clouds/VRAM, Pluto's tilt/lock, terrain height under statics and
decals. No save ids or versions changed. Native acceptance pending; see [KSA_5707_UPGRADE](../plans/KSA_5707_UPGRADE.md).

## Earlier status (5638)

DOH now exposes the existing `Kitten_doh_` prefix as a scene-saved name field: exact single names,
numbered batch prefixes and blank automatic fallback. Collision checks reuse native `All.TryGet`;
no new game hook or save version is introduced. See [character controls](character-and-materials.md#doh)
and [save compatibility](saves.md#doh-kitten-and-material-slot-saves). Native UI/load acceptance is pending.

**October 25 startup follow-up:** the supplied Windows log and in-game report establish failed
weld updates and shared-hook installation. Managed reproduction found a call-wrapper transpiler
ordering defect; installation now precedes existing foreign wrappers and reports/cleans failed
attempts. A native original-build comparison removes all three hook failures and restores welding
when only purr2d2 is disabled; updated-build native coexistence with purr2d2 is still unverified.
The source audit below identifies the corresponding transformation. Full Canopy also accepts CRLF shader defines; startup reports
full exceptions and partial patch-group failures.
No save IDs or payloads change. See the [follow-up evidence](../plans/KSA_5638_UPGRADE.md#startup-follow-up-2026-10-25).

The supplied Purr2D2 source identifies its vehicle-dispatch timing wrapper as the hidden anchor.
Paired source tests cover frame/load hooks; companion Purr2D2 cleanup honors deferred loads and
non-character workers, while Iron Man owns rocket-mode input first. Native co-loaded acceptance remains open.

Two compile breaks: the Brutal assembly renames (every csproj HintPath) and `Vehicle.Ctrl2Body` →
`Helm2Body` (Iron Man; the flight computer now derives `Tnm`/`Ctrl` frames from the helm). Silent
breaks fixed: **free-fallin** Full Canopy injected pre-5530 descriptor-set numbers into the shared
skinned vertex shader (now derived from live `ModelPbr.frag`, fails closed); **The Tick** and **Kitchen
Sink G-load** missed `DetectStructuralFailure(VehicleUpdateState, SimStep)` and were inert (both now
also cover the new EVA-kitten hard-landing branch; `KsaAssemblyProbe` checks real signatures); save
**preflight** refused saves KSA now loads by skipping missing-part vehicles (now warns); **kiwis-marbles**
dropped star-target welds on load (`OrbitingStar : IOrbiter`); **doh** despawn now uses
`Universe.DestroyVehicle`; **Iron Man** navball thrust marker and RCS cache gate; **pebbles** per-viewport
clutter residency and nested `MappedMemory[][]` retirement; **blinky** skips tanks that cannot hold its
propellant (magnetic traps); **camera-controller-override** plays back relative to the follow target;
**Hot Pursuit** clears its celestial mirror on release. Crew portraits now render interiors
(`RendersInterior()`); filters still align. **parts-now removed** (legacy save records retained).
Live checks added: optimized-KSA inlining after mid-session reapply, convex-hull collider scaling,
secondary-viewport terrain/atmosphere. See [KSA_5638_UPGRADE](../plans/KSA_5638_UPGRADE.md).

## Earlier status (5482)

Kitchen Sink's **See Inside Capsule (Experimental)** uses exact stock medium-capsule window
model filtering and an independent shared IVA visibility requirement. Default-off, reversible,
scene-saved; 20 managed render/ownership + 17 save checks pass. Main-view glass appearance and
alignment remain native acceptance. [Feature/asset scope](ui-customization.md#kitchen-sink-capsule-glass-experiment-5541).

Kitchen Sink's **Unlock IVA Camera** addition was inspected against local **5541** sources:
IVA controller replacement with native free-flight delegation, exact-seat/body-pose sidecar
replay and a scoped head-visibility postfix. No ray tracing pipeline or camera enum changes.
Managed validation and native acceptance are tracked in [camera](camera.md#kitchen-sink-unlocked-iva-camera).
This feature audit does not constitute a full 5482 → 5541 compatibility audit.
The current-assembly build also required Graffiti's per-view/current-frame global descriptor
binding migration; see [decals](decals.md#ksa-5541-descriptor-migration). Its save payload is unchanged.

The first 5482 build failed on one renamed scheduler that masked 16 errors in 8 projects. Fixed:
the scheduler rename; nullable `Part.Tree` (doh's backpack spawn would have thrown on every spawn);
removed per-module render hooks, replaced by a shared `PartRenderFilter` (blinky, its-so-shiny) and
cached-state paint seams (humble-arteest); the visor's per-view `Draw(ViewHandle)`; Pebbles' typed clutter
removals plus displacement follow-ups (hull volume/damping/tree-lighting parity, displaced
carry-over, stock-grid native saves and a new `pebbles.clutter-state` record). Silent regressions also fixed: failed native saves no longer receive a sidecar;
Godzilla visual scaling reinstalls; IvaForceRender shows interiors in the editor again; Bloomin's
distant-sphere ring shadow writes the new material struct. Pre-existing bugs root-caused and
fixed: Eternal Flame burn refills, a Torch/Dent Wizard teleport race with the orbit-hover job, Blinky's
false feed warning and missing load-failure reports. doh has two more fixes. Its spawn target is
held as a `Vehicle` object, because a list index moved onto another vehicle when KSA swap-removed one.
Its tinted clones no longer exhaust the fixed 512-slot GPU material pool, which crashed the game in
the native `KittenEva` constructor. See [character-and-materials.md](character-and-materials.md#doh) #33-#37. See the [master index](game-integration-surface.md#5482-verification-summary)
and [upgrade report](../plans/KSA_5482_UPGRADE.md). Earlier 5438 migrations: [KSA_5438_UPGRADE](../plans/KSA_5438_UPGRADE.md).

Kitchen Sink replaces the defunct Flexo test panels and solver hook with a filtered vehicle
picker and scene-saved G-load protection table. Both hosts install a guarded structural-failure
transpiler; collisions, part damage, pressure damage and telemetry retain native behavior.
The combined Dent Wizard/upstream integration passes all **74 projects and 15 managed suites**,
including G-load isolation, save replay and protection preserved across launch. Its detector is unchanged from 5402; native cart/UI/load acceptance remains open.
See [UI/customization](ui-customization.md#kitchen-sink).

The Tick (added 2026-09-28 against 5482) makes exact registered vessels indestructible by forces:
a prefix on `Part.CrashTolerancePascals` reports `double.MaxValue` for parts owned by a protected
vehicle (shared `PartTemplate` XML is untouched), and a prefix/postfix pair on
`PhysicsBubble.DetectStructuralFailure` discards the G-load / dynamic-pressure destruction event while
keeping telemetry. Version-1 `the-tick` save record; `the-tick.tests` passes; native ram/dive/save
acceptance is open. See [vehicle physics](vehicle-physics.md#the-tick-per-vessel-indestructibility).

Save Yourself (added 2026-09-28 against 5482) writes a new native save every N wall-clock seconds
(5–300, default 30, off by default) named `<prefix>_YYYYMMDDTHHMMSS` through the new
`GameSaveProvider` (`SaveName.TryAccept` → `UncompressedSave.Make`, the terminal `save` path), so the
existing save hooks and sidecar run unchanged. No Harmony patches. Settings are a global
`.unscience/save-yourself.toml` preference, not scene data; `save-yourself.tests` passes; native acceptance is open. See
[master index](game-integration-surface.md#save-yourself-timed-auto-saves-2026-09-28-5482).

Unscience now extends native save/load with versioned scene-state sidecars and explicit feature
adapters. World replacement is scheduled before new solver work and UI drawing; cleanup restores
old ownership and replay rebinds native objects. Managed persistence/lifecycle checks accompany
full compilation; native round-trip/GPU acceptance remains open. See [saves](saves.md).

Standing native checks remain: blinky/shiny hiding (raytraced IVA is not filtered), paint/emissive with dents, editor IVA reveal, visor; plume controls and stock-engine isolation;
canopy choices/restoration; one decal pass with MSAA/bloom; weld/scaling collisions under segmented
physics; runtime thumbnails/clutter/statics; Iron Man modes; secondary cameras; audio; scene saves.
Fuel-during-burn and Torch error-spam now have root-caused fixes; neither is declared fixed until reproduced in game.
Vehicle Paint's old “dead since 4693” diagnosis is obsolete: it was rebuilt for 5018; this upgrade
fixes a different render-overload regression.

See [ISSUES](../ISSUES.md) for current triage and dated records; [the old gaps plan](../plans/FIX_CURRENT_GAPS_PLAN.md)
is historical and superseded by the current upgrade report.

Dent Wizard added against 5438: source picker includes EVA/debris and live mass in kg; one-shot/automatic click firing inherits
orbital/surface velocity through the shared physics handoff. Managed launch checks and solution
build pass; native launch/picking/collision acceptance is pending.
Combined merge evidence: [integration record](../plans/DENT_WIZARD_UPSTREAM_INTEGRATION.md). See [vehicle scope](vehicle-physics.md#dent-wizard).
