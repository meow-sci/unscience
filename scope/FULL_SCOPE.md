# Unscience — Full Game-Integration Scope

This folder maps the suite's game types, Harmony targets, reflection, shaders, assets and lifecycle
contracts. Update the owning area and master index in the same change as an integration edit.

## Version baseline

- **Cataloged against:** KSA **2026.10.19.5638**, metadata date **2026-10-20**.
- **Diffed from:** the recorded baseline **2026.9.22.5482** (not the on-disk PREVIOUS 5554), using
  `ksa-game-assemblies` git history `a2f724f..edda844`; 5541 (`47703f3`) and 5554 (`935a32b`) only
  date changes. The in-repo `decomp/ksa` is historical.
- **Build references:** explicitly set `KSAFolder` to CURRENT's `dll/` (macOS; no separate native
  install). Distribution was redirected during all checks.
- **Changelog coverage:** revisions 5483–5540, 5542–5553 and 5603–5637 are logged; **5555–5602 appear in
  no `version.json`** (the Helm control frame, `DetectStructuralFailure(…, SimStep)` and other changes
  landed there). Source/Content comparison covers the full span: 819 KSA C# files (99 added, 4 removed),
  211 Content files. Brutal went open source (rev 5618) and renamed its assemblies
  (`Brutal.Core.Common` → `Brutal.Core`, `Brutal.Core.{Numerics,Strings,Memory,Collections,Logging,Maths}` →
  `Brutal.{…}`); `KSA.dll` is now built with optimizations (rev 5630).
- **Verification:** whole-solution compilation (0 warnings) and 18 managed suites, plus eight area audits
  of reflection, Harmony signatures/bodies/IL anchors, render/GPU, shader/descriptor/asset and byte
  layouts. Evidence and the native acceptance matrix are in [KSA_5638_UPGRADE](../plans/KSA_5638_UPGRADE.md).
  This is a **static/managed baseline; live game validation remains pending**.
- Dated area tables retain historical build/line citations; their 5638 sections and the upgrade report
  record the 5482 → 5638 deltas. Earlier reports: [5482](../plans/KSA_5482_UPGRADE.md), [5438](../plans/KSA_5438_UPGRADE.md).

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
| [`saves.md`](saves.md) | Native scene save/load extension across all bundled features; save-yourself timed auto saves | Ordered cleanup/replay, deferred world replacement, native save directory sidecars and stable part references; Save Yourself produces native saves via `GameSaveProvider` (`SaveName.TryAccept` → `UncompressedSave.Make`, no patches) |
| [`00-architecture-and-abstractions.md`](00-architecture-and-abstractions.md) | unscience supermod shell (`Mod.cs`/`Patcher.cs`/`MenuBarPatch`/`UnscienceState`) + `ksa-abstractions.lib` | StarMap lifecycle map, consolidated-Harmony cross-ref, `HotkeyGuard`, `IvaForceRender`, providers |
| [`vehicle-physics.md`](vehicle-physics.md) | dent-wizard, eternal-flame, garrys-torch, godzilla, i-feel-seen, the-tick | `Universe.ExecuteNextVehicleSolvers` (Eternal Flame fuel + electricity refills since 5482), `Battery.Refill`, `Vehicle.Teleport` (+ `PhysicsFrameHook.JoinOrbitReaders`), KittenEva reflection, Godzilla's `IsLargeEnoughToRender` cull redirect; **Godzilla separates visual, collider and nominal physics size; garrys-torch preserves actuator results; default-off source collisions use scoped Bepu shape suppression**; The Tick prefixes `Part.CrashTolerancePascals` and prefix/postfixes `PhysicsBubble.DetectStructuralFailure` for per-vessel indestructibility |
| [`celestial-and-lights.md`](celestial-and-lights.md) | kiwis-marbles, zippo | `Celestial.SetOrbit`, `IParentBody.Children`/`UpdatePerFrameDataTree`, `Universe.ExecuteNextVehicleSolvers` prefix (kiwis-marbles sim-step timing, fixed 2026-08-23), `IOrbiter`, `LightModule`/`LightSwitch`; Zippo Disco's per-instance templates, cone angles and `KeyframeAnimationModule.TimeGoal` ownership |
| [`camera.md`](camera.md) | camera-controller-override, glass, hot-pursuit, kitchen-sink IVA unlock | `OrbitController/FlyController/FixedController.OnFrame`, `Camera._fovRadians`; Kitchen Sink temporarily replaces `GameViewport.IvaController` and patches seat head visibility; four public secondary-viewport leases under the sealed 8-slot registry; part-raycast camera mounts |
| [`pixel-grids-and-render.md`](pixel-grids-and-render.md) | blinky, its-so-shiny, thug-life | shared `PartRenderFilter` on `PartTreeRenderData.Compose*` (replaced the removed `*Module.UpdateRenderData` prefixes @5482), `PartTree.CreateFromNewPartTree`, `RocketCore.FeedConnectors` (blinky ignition), `SuperMeshRenderSystem.RenderMainPass`, UnlitMesh shaders |
| [`character-and-materials.md`](character-and-materials.md) | doh, humble-arteest, kitten-animations | `KittenRenderable.UpdateRenderData` visor draw gate (Humble Arteest), `GpuMaterialSystem.BigBuffer`, `KittenEva`/`EVADoor` (**doh @5402**: spawn/despawn now `JobSystems.VehicleSolver.Wait()` before touching the shapes registry), `PerInstanceData` `StateBitFlag` free-bit paint written through private `PartTreeRenderData.WriteState/WriteDynamicState` (5482) + `ShaderModuleUtils.FromFile` shader patch; visor `Draw(ViewHandle)`; **kitten-animations** — filterable selection of any live EVA kitten by `Vehicle.Id`, Harmony prefix on `AnimatedRenderable.UpdateAnimation`, 17 private `KittenRenderable` animation fields, and a mod-owned `CatExpressionAnim` |
| [`part-editor-and-robotics.md`](part-editor-and-robotics.md) | dont-stifle-me | **dont-stifle-me** scale patches on `VehicleEditor.ScaleBoundsFor` / `UpdateSelectedScale` / `QuantizeScale`, plus configurable editor-limit patches on `DrawParachuteSection` / `Parachute.SetDiameter` (2–1000 m diameter). **flexo removed @5348** — compiled clean, but the robotics approach never worked and will not be reattempted; `OrbitLinePass` remains unowned; `PartModelRenderer.UpdateRenderData` is now used by Iron Man. **parts-now removed @5638** (user decision; its `ModLibrary` reflection and `DeviceMeshInterleaved.Shared` headroom invariant are retired; legacy `parts-now` save records are retained by the coordinator); 5638 notes on the CoreCommandA/Structural/Fairing convex-hull re-import, `PlumbedCore` and the `Interstellar` tag |
| [`iron-man.md`](iron-man.md) | iron-man | EVA/Iron Man mode selector with upright editor support in both modes, private connectors/save reconstruction, conditional worker `IsKitten` routing, rocket controls/gauges/RCS, active-only upright surface teleports, early equipment / late avatar rendering; **default EVA, native acceptance pending** |
| [`exhaust-plumes.md`](exhaust-plumes.md) | pyro (including runtime On/Off cycles) | `Vehicle.AddVolumetricExhaustInstances` postfix, `VolumetricExhaustRenderer.AddInstance`, `VolumetricExhaustInstance` (+ private `_shaderData`), internal `VolumetricExhaustTemplate.References`, `PlumeData`/`ExhaustInstance` layout drift (new @5348) |
| [`decals.md`](decals.md) | graffiti | `RenderTarget.ResolveAttachments` postfix (GridPass-window projected-decal pass), `GlobalShaderBindings` + `BindlessTextureLibrary` descriptor sets, runtime GLSL vs `Common/*.glsl` headers, `Part.RayCastEgo` + live `Parachute.ClothPositionsFront` triangle picking + `Cursor.GetEgoRay`, CPU terrain march; **no string reflection** (new @5348; canopy picking added @5402) |
| [`parachutes.md`](parachutes.md) | free-fallin | `ChuteRenderable.Draw`, `Utils.SetShaderFromMod`, and `ShaderModuleUtils.FromFile` prefixes; private `_renderable` + protected `AnimatedRenderable.MaterialIndices`; runtime `MaterialData` and PNG/PBR uploads; material-gated bind-pose projection through `Model{,_Skinned}.vert` / `ModelPbr.frag`; stock canopy assets (new @5402) |
| [`ground-clutter.md`](ground-clutter.md) | pebbles; [GLB materials](ground-clutter-glb-materials.md) | Per-body native clutter graphs, private materials, `ExecuteNextClothSolvers` transactions, collider/physics invalidation, shared copied GLB discovery, uploads and independent Workshop preview; shared Harmony ownership |
| [`statics.md`](statics.md) | sphinx | Body-fixed GLB placement; live transforms and UV scale/offset, private vertex replacement, native static-renderer postfixes, shared GLB/PNG imports, terrain picking and automatic box/mesh colliders with per-bubble contact/lifetime hooks |
| [`rings.md`](rings.md) | rocky-mcrock-face, bloomin-onion | planetary-ring mesh/texture swap (rocky) and **runtime ring definition on any celestial** (bloomin-onion) via the public `PlanetaryRingsReference` data tree + `Program.RebuildRenderer()`; **no Harmony patches**; `ModLibrary.AllMeshes`/`AllFiles` reflection, `MeshReference.<HostPrimitives>k__BackingField`, ctor-baking invariant in `PlanetaryRingsRenderData`; bloomin-onion adds `PlanetTransparenciesRenderer._anyRings` (load-bearing), `TextureReference.<TextureAsset>k__BackingField` (painted textures) and a cosmetic `DistantSphereRenderer._material` ring-shadow sync (moved from `_data` @5482); ring data also drives atmosphere ring shadows since 5470 |
| [`ui-customization.md`](ui-customization.md) | skittles, kitchen-sink | `ImGui` style surface, editor refresh/IVA rendering, **experimental medium-capsule exterior-window reveal** via exact static-model filtering, and selected-vehicle G-load protection via `PhysicsBubble.DetectStructuralFailure`; Flexo diagnostics removed |
| [`audio.md`](audio.md) | byo-music | Shared sound imports, FMOD stream/channel ownership, vessel-relative 3D playback and repeat/gaps |

Bundled in the unscience supermod (30): blinky, bloomin-onion, byo-music, camera-controller-override, dent-wizard, doh,
dont-stifle-me, eternal-flame, free-fallin, garrys-torch, glass, godzilla, graffiti, hot-pursuit, humble-arteest,
i-feel-seen, iron-man, its-so-shiny, kitchen-sink, kitten-animations, kiwis-marbles, pebbles, pyro,
rocky-mcrock-face, save-yourself, skittles, sphinx, the-tick, thug-life, zippo. (jplrepo is a development reference and is not loaded by the supermod.)

---

## Current status against 5638

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
