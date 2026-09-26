# Unscience — Full Game-Integration Scope

This folder maps the suite's game types, Harmony targets, reflection, shaders, assets and lifecycle
contracts. Update the owning area and master index in the same change as an integration edit.

## Version baseline

- **Cataloged against:** KSA **2026.9.22.5482**, metadata date **2026-09-24**.
- **Diffed from:** **2026.9.10.5438**, which matches the previous recorded baseline.
- **Authoritative inputs:** sibling `ksa-game-assemblies/current/{dll,decomp,Content}` and
  `ksa-game-assemblies_prev/current/{dll,decomp,Content}`. The in-repo `decomp/ksa` is historical.
- **Build references:** explicitly set `KSAFolder` to CURRENT's `dll/` (macOS; no separate native
  install). Distribution was redirected during all checks.
- **Changelog coverage:** 43 entries, revisions **5439–5481**; the window ends at **5482** without a
  separate 5482 entry. No intermediate baseline gap. Source/Content comparison covers the complete pair:
  133 changed/25 added/1 removed KSA C# files; 33 changed/3 added Content files. Only `KSA.dll` and
  `Planet.Render.Core.dll` changed.
- **Verification:** whole-solution compilation (74 projects, 0 warnings) and 15 managed suites, plus
  eight area audits of reflection, Harmony signatures/bodies/IL anchors, render/GPU, shader/asset and
  byte layouts. Evidence and the native acceptance matrix are in [KSA_5482_UPGRADE](../plans/KSA_5482_UPGRADE.md).
  This is a **static/managed baseline; live game validation remains pending**.
- Dated area tables retain historical build/line citations; their 5482 verification sections and the
  upgrade report record the 5438 → 5482 deltas.

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
| [`saves.md`](saves.md) | Native scene save/load extension across all bundled features | Ordered cleanup/replay, deferred world replacement, native save directory sidecars and stable part references |
| [`00-architecture-and-abstractions.md`](00-architecture-and-abstractions.md) | unscience supermod shell (`Mod.cs`/`Patcher.cs`/`MenuBarPatch`/`UnscienceState`) + `ksa-abstractions.lib` | StarMap lifecycle map, consolidated-Harmony cross-ref, `HotkeyGuard`, `IvaForceRender`, providers |
| [`vehicle-physics.md`](vehicle-physics.md) | dent-wizard, eternal-flame, garrys-torch, godzilla, i-feel-seen | `Universe.ExecuteNextVehicleSolvers` (Eternal Flame fuel + electricity refills since 5482), `Battery.Refill`, `Vehicle.Teleport` (+ `PhysicsFrameHook.JoinOrbitReaders`), KittenEva reflection, Godzilla's `IsLargeEnoughToRender` cull redirect; **Godzilla separates visual, collider and nominal physics size; garrys-torch preserves actuator results; default-off source collisions use scoped Bepu shape suppression** |
| [`celestial-and-lights.md`](celestial-and-lights.md) | kiwis-marbles, zippo | `Celestial.SetOrbit`, `IParentBody.Children`/`UpdatePerFrameDataTree`, `Universe.ExecuteNextVehicleSolvers` prefix (kiwis-marbles sim-step timing, fixed 2026-08-23), `IOrbiter`, `LightModule`/`LightSwitch`; Zippo Disco's per-instance templates, cone angles and `KeyframeAnimationModule.TimeGoal` ownership |
| [`camera.md`](camera.md) | camera-controller-override, glass, hot-pursuit | `OrbitController/FlyController/FixedController.OnFrame`, `Camera._fovRadians`; four public secondary-viewport leases under the sealed 8-slot registry; part-raycast camera mounts; Hot Pursuit nearby-celestial sync and stock secondary-render omissions |
| [`pixel-grids-and-render.md`](pixel-grids-and-render.md) | blinky, its-so-shiny, thug-life | shared `PartRenderFilter` on `PartTreeRenderData.Compose*` (replaced the removed `*Module.UpdateRenderData` prefixes @5482), `PartTree.CreateFromNewPartTree`, `RocketCore.FeedConnectors` (blinky ignition), `SuperMeshRenderSystem.RenderMainPass`, UnlitMesh shaders |
| [`character-and-materials.md`](character-and-materials.md) | doh, humble-arteest, kitten-animations | `KittenRenderable.UpdateRenderData` visor draw gate (Humble Arteest), `GpuMaterialSystem.BigBuffer`, `KittenEva`/`EVADoor` (**doh @5402**: spawn/despawn now `JobSystems.VehicleSolver.Wait()` before touching the shapes registry), `PerInstanceData` `StateBitFlag` free-bit paint written through private `PartTreeRenderData.WriteState/WriteDynamicState` (5482) + `ShaderModuleUtils.FromFile` shader patch; visor `Draw(ViewHandle)`; **kitten-animations** — filterable selection of any live EVA kitten by `Vehicle.Id`, Harmony prefix on `AnimatedRenderable.UpdateAnimation`, 17 private `KittenRenderable` animation fields, and a mod-owned `CatExpressionAnim` |
| [`part-editor-and-robotics.md`](part-editor-and-robotics.md) | parts-now, dont-stifle-me | parts-now's `ModLibrary` reflection + `DeviceMeshInterleaved.Shared` headroom invariant; **dont-stifle-me** scale patches on `VehicleEditor.ScaleBoundsFor` / `UpdateSelectedScale` / `QuantizeScale`, plus configurable editor-limit patches on `DrawParachuteSection` / `Parachute.SetDiameter` (2–1000 m diameter). **flexo removed @5348** — compiled clean, but the robotics approach never worked and will not be reattempted; `OrbitLinePass` remains unowned; `PartModelRenderer.UpdateRenderData` is now used by Iron Man |
| [`iron-man.md`](iron-man.md) | iron-man | EVA/Iron Man mode selector with upright editor support in both modes, private connectors/save reconstruction, conditional worker `IsKitten` routing, rocket controls/gauges/RCS, active-only upright surface teleports, early equipment / late avatar rendering; **default EVA, native acceptance pending** |
| [`exhaust-plumes.md`](exhaust-plumes.md) | pyro (including runtime On/Off cycles) | `Vehicle.AddVolumetricExhaustInstances` postfix, `VolumetricExhaustRenderer.AddInstance`, `VolumetricExhaustInstance` (+ private `_shaderData`), internal `VolumetricExhaustTemplate.References`, `PlumeData`/`ExhaustInstance` layout drift (new @5348) |
| [`decals.md`](decals.md) | graffiti | `RenderTarget.ResolveAttachments` postfix (GridPass-window projected-decal pass), `GlobalShaderBindings` + `BindlessTextureLibrary` descriptor sets, runtime GLSL vs `Common/*.glsl` headers, `Part.RayCastEgo` + live `Parachute.ClothPositionsFront` triangle picking + `Cursor.GetEgoRay`, CPU terrain march; **no string reflection** (new @5348; canopy picking added @5402) |
| [`parachutes.md`](parachutes.md) | free-fallin | `ChuteRenderable.Draw`, `Utils.SetShaderFromMod`, and `ShaderModuleUtils.FromFile` prefixes; private `_renderable` + protected `AnimatedRenderable.MaterialIndices`; runtime `MaterialData` and PNG/PBR uploads; material-gated bind-pose projection through `Model{,_Skinned}.vert` / `ModelPbr.frag`; stock canopy assets (new @5402) |
| [`ground-clutter.md`](ground-clutter.md) | pebbles; [GLB materials](ground-clutter-glb-materials.md) | Per-body native clutter graphs, private materials, `ExecuteNextClothSolvers` transactions, collider/physics invalidation, shared copied GLB discovery, uploads and independent Workshop preview; shared Harmony ownership |
| [`statics.md`](statics.md) | sphinx | Body-fixed GLB placement; live transforms and UV scale/offset, private vertex replacement, native static-renderer postfixes, shared GLB/PNG imports, terrain picking and automatic box/mesh colliders with per-bubble contact/lifetime hooks |
| [`rings.md`](rings.md) | rocky-mcrock-face, bloomin-onion | planetary-ring mesh/texture swap (rocky) and **runtime ring definition on any celestial** (bloomin-onion) via the public `PlanetaryRingsReference` data tree + `Program.RebuildRenderer()`; **no Harmony patches**; `ModLibrary.AllMeshes`/`AllFiles` reflection, `MeshReference.<HostPrimitives>k__BackingField`, ctor-baking invariant in `PlanetaryRingsRenderData`; bloomin-onion adds `PlanetTransparenciesRenderer._anyRings` (load-bearing), `TextureReference.<TextureAsset>k__BackingField` (painted textures) and a cosmetic `DistantSphereRenderer._material` ring-shadow sync (moved from `_data` @5482); ring data also drives atmosphere ring shadows since 5470 |
| [`ui-customization.md`](ui-customization.md) | skittles, kitchen-sink | `ImGui` style surface, editor refresh/IVA rendering, and selected-vehicle G-load protection via `PhysicsBubble.DetectStructuralFailure`; Flexo diagnostics removed |
| [`audio.md`](audio.md) | byo-music | Shared sound imports, FMOD stream/channel ownership, vessel-relative 3D playback and repeat/gaps |

Bundled in the unscience supermod (29): blinky, bloomin-onion, byo-music, camera-controller-override, dent-wizard, doh,
dont-stifle-me, eternal-flame, free-fallin, garrys-torch, glass, godzilla, graffiti, hot-pursuit, humble-arteest,
i-feel-seen, iron-man, its-so-shiny, kitchen-sink, kitten-animations, kiwis-marbles, parts-now, pebbles, pyro,
rocky-mcrock-face, skittles, sphinx, thug-life, zippo. (jplrepo is a development reference and is not loaded by the supermod.)

---

## Current status against 5482

The first 5482 build failed on one renamed scheduler that masked 16 errors in 8 projects. Fixed:
the scheduler rename; nullable `Part.Tree` (doh's backpack spawn would have thrown on every spawn);
removed per-module render hooks, replaced by a shared `PartRenderFilter` (blinky, its-so-shiny) and
cached-state paint seams (humble-arteest); the visor's per-view `Draw(ViewHandle)`; Pebbles' typed clutter
removals plus displacement follow-ups (hull volume/damping/tree-lighting parity, displaced
carry-over, stock-grid native saves and a new `pebbles.clutter-state` record). Silent regressions also fixed: failed native saves no longer receive a sidecar;
Godzilla visual scaling reinstalls; IvaForceRender shows interiors in the editor again; Bloomin's
distant-sphere ring shadow writes the new material struct. Pre-existing bugs root-caused and
fixed: Eternal Flame burn refills, a Torch/Dent Wizard teleport race with the orbit-hover job, Blinky's
false feed warning and missing load-failure reports. See the [master index](game-integration-surface.md#5482-verification-summary)
and [upgrade report](../plans/KSA_5482_UPGRADE.md). Earlier 5438 migrations: [KSA_5438_UPGRADE](../plans/KSA_5438_UPGRADE.md).

Kitchen Sink replaces the defunct Flexo test panels and solver hook with a filtered vehicle
picker and scene-saved G-load protection table. Both hosts install a guarded structural-failure
transpiler; collisions, part damage, pressure damage and telemetry retain native behavior.
The combined Dent Wizard/upstream integration passes all **74 projects and 15 managed suites**,
including G-load isolation, save replay and protection preserved across launch. Its detector is unchanged from 5402; native cart/UI/load acceptance remains open.
See [UI/customization](ui-customization.md#kitchen-sink).

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
