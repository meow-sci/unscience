# Unscience — Full Game-Integration Scope

This folder is the **authoritative reference for how the unscience mod suite plugs into the
Kitten Space Agency (KSA) game**. Its purpose is singular: when KSA ships an update, this is the
first place to look to decide *what might break and where*. Every unscience feature is mapped to the
exact game types, methods, fields, Harmony patch targets, shaders and assets it depends on, with the
decompiled-source path for each so the dependency can be re-checked against any future build.

> Keep this in sync. Any change to an unscience feature's game integration **MUST** update the
> relevant file here. See [`../AGENTS.md`](../AGENTS.md) → "scope/ maintenance" for the binding rule.

---

## Version baseline

- **Cataloged against:** KSA build **`2026.9.7.5402`** (2026-09-02) — decomp at
  `…/ksa-game-assemblies/current/decomp`, assets at `…/ksa-game-assemblies/current/Content`.
  On macOS `Directory.Build.props` tier 2 resolves `KSAFolder` to
  `../ksa-game-assemblies/current/dll/`, so **`dotnet build` compiled against exactly this build** —
  there is no separate install to reconcile and no `KSAFolder` trap.
- **Diffed from:** KSA build **`2026.8.22.5348`** — the previously verified baseline, and also what is
  on disk as `ksa-game-assemblies_prev`. **Baseline == OLD**, a single hop.
- ⚠ **The changelog is NOT complete for this span.** NEW's `version.json` covers only `5400 → 5402`
  (one commit, rev 5401 — the thumbnail "data stride" fix). **Revisions 5349–5400 are in no
  `version.json` on disk**, so this pass was driven by the source diff (197 `KSA/*.cs` changed, 66
  added, 2 removed; 20 Content files) rather than a changelog.
- **How each touchpoint was verified:** (1) `dotnet build ksa-mod-experiments.slnx --no-incremental`
  against the `5402` reference DLLs — **52/52 projects, 0 warnings, 0 errors** after three compile
  breaks were fixed (`KSA.Viewport` → `IViewport`, `Cursor.InputRay` → `GetEgoRay`,
  `VolumetricExhaustRenderer.AddInstance` air-state args); (2) re-grep of the **entire**
  string-reflection watchlist plus a signature + body diff of **every** Harmony patch target across
  both trees, with a field-vs-property check on each reflected member; (3) byte-layout diff of
  `PerInstanceData`/`MaterialData`/`ExhaustInstance`, `diff -rq` + `cmp` of `Content/Core/Shaders`,
  and an id check of every referenced asset; (4) a read of every changed game file the area tables
  cite, for gating/semantic drift — the substitute for the missing changelog.
- ⚠ **A green build is a small fraction of the risk here.** The behavioral findings below **cannot be
  cleared statically** — see *Current status* for what still needs a live in-game pass.
- The repo's own `decomp/ksa` copy is **older still** (June 12) and is not authoritative — always diff
  against the `ksa-game-assemblies` git tags.
- Build is cross-platform since 2026-08-22 (see [`../plans/KSA_5261_UPGRADE.md`](../plans/KSA_5261_UPGRADE.md) §7).

When a new game version arrives, bump this baseline and re-run the workflow below.

---

## How to use this on a game update

1. **Rebuild first.** `dotnet build` against the updated install surfaces all *typed* breaks
   immediately (renamed/removed public members, changed signatures used via `nameof`/direct calls).
   Many integration points are typed and will fail loudly here.
2. **Diff the string/reflection touchpoints.** Compile-clean ≠ safe. Open
   [`game-integration-surface.md`](game-integration-surface.md) → *String-based reflection watchlist*
   and re-grep each entry in the new decomp. These (private fields, string method names, Harmony
   overload param arrays) fail **silently at runtime**, not at compile. The same section lists the
   🔶 **standing invariants** — facts about the game that no grep can check, chiefly
   `StateBitFlag` bits 11..31 (humble-arteest) and **`[StarMapAllModsLoaded]` firing before
   `ModLibrary.Bind()`** (parts-now).
3. **Scan the changelog for behavioral hits.** Read the new `version.json` commit list and match it
   against the per-area "Update-risk findings" sections — some changes (control gating, editor tag
   schema, particle/shader reworks) break behavior without moving a symbol.
4. **Re-check shaders & per-instance layout.** Runtime-recompiled GLSL and per-instance data hacks
   (humble-arteest) break when the game's shader sources change even though the C#
   compiles. Includes verifying `PerInstanceData.StateBitFlag` bits 11..31 are still unused by the
   game. See [`game-integration-surface.md`](game-integration-surface.md) → *Shaders & assets*.
5. **Record deltas + update these docs**, then capture the fix work in a `plans/` document (see the
   current one: [`../plans/KSA_5348_UPGRADE.md`](../plans/KSA_5348_UPGRADE.md)).

---

## Distribution

Only Unscience is shipped. Legacy standalone hosts remain compile-checked projects but no longer
deploy/publish; their lifecycle maps below are development references. Feature-library project
boundaries and the Unscience lifecycle are unchanged by this packaging refactor.

## The integration model (how unscience attaches to KSA)

- **StarMap is the loader seam, not the game.** `unscience/Mod.cs` is the single `[StarMapMod]` entry.
  StarMap.API Harmony-patches the game's render loop (`Program.OnDrawUiFrame` / `OnDrawUiViewports` /
  `OnFrame`) and dispatches to attributed methods (`[StarMapBeforeGui]`, `[StarMapAfterGui]`, …). The
  suite primarily rides those hooks. Garry's Torch also transpiles `Program.PrepareFrame` at the
  result-application/next-snapshot handoff (see vehicle physics). The two GUI
  hooks' targets are skipped by the game while the HUD is hidden (F2 → `Program.DrawUI == false`), so
  `ksa-abstractions.lib/HiddenUiFrameHook` prefixes the always-called `Program.OnDrawUiConsole` and
  replays the shell's non-UI per-frame work only in that state (see
  [`00-architecture-and-abstractions.md`](00-architecture-and-abstractions.md)).
- **One consolidated Harmony instance.** `unscience/Patcher.cs` owns a single
  `Harmony("MeowSci.Unscience")`; each feature lib exposes `Apply(Harmony)`/`Remove(Harmony)` and the
  supermod applies them all onto that instance. `HotkeyGuard` is applied first.
- **`ISubmod` aggregation.** 29 feature libs implement `ISubmod` (`Name`/`Initialize`/`Update`/
  `RenderContent`/`RenderFloatingWindows`/`Dispose`); the same classes power each feature's standalone
  mod too.
- **`ksa-abstractions.lib` is the game-facing seam.** Cross-cutting game access is funneled through a
  handful of static helpers there, so a game update's blast radius is concentrated in one library.
- **Integration-point taxonomy** used throughout these docs: *Harmony patch* (prefix/postfix), *Reflection*
  (`AccessTools`/`System.Reflection`, especially string-named private members), *Direct API* (typed,
  compile-checked), *Render-pass/GPU* (render-system patches, shaders, Vulkan, per-instance byte
  offsets), *Asset* (templates/shaders/characters/sounds by id/path), *Lifecycle* (StarMap/ISubmod).

---

## Contents

| Area file | Covers | Highlights / highest-risk seams |
|---|---|---|
| [`game-integration-surface.md`](game-integration-surface.md) | **Master cross-reference index** — every game type/member touched, merged across mods | Start here for "does the game still have X?"; includes the string-reflection watchlist + shader/asset table |
| [`00-architecture-and-abstractions.md`](00-architecture-and-abstractions.md) | unscience supermod shell (`Mod.cs`/`Patcher.cs`/`MenuBarPatch`/`UnscienceState`) + `ksa-abstractions.lib` | StarMap lifecycle map, consolidated-Harmony cross-ref, `HotkeyGuard`, `IvaForceRender`, providers |
| [`vehicle-physics.md`](vehicle-physics.md) | eternal-flame, garrys-torch, godzilla, i-feel-seen | `Universe.ExecuteNextVehicleSolvers`, `Battery.Refill`, `Vehicle.Teleport`, KittenEva reflection; **garrys-torch PrepareFrame handoff preserves actuator results before teleport** |
| [`celestial-and-lights.md`](celestial-and-lights.md) | kiwis-marbles, zippo | `Celestial.SetOrbit`, `IParentBody.Children`/`UpdatePerFrameDataTree`, `Universe.ExecuteNextVehicleSolvers` prefix (kiwis-marbles sim-step timing, fixed 2026-08-23), `IOrbiter`, `LightModule`/`LightSwitch`; Zippo Disco's per-instance templates, cone angles and `KeyframeAnimationModule.TimeGoal` ownership |
| [`camera.md`](camera.md) | camera-controller-override, glass, hot-pursuit | `OrbitController/FlyController/FixedController.OnFrame`, `Camera._fovRadians`; four public secondary-viewport leases under the sealed 8-slot registry; part-raycast camera mounts; Hot Pursuit nearby-celestial sync and stock secondary-render omissions |
| [`pixel-grids-and-render.md`](pixel-grids-and-render.md) | blinky, its-so-shiny, thug-life | three `*Module.UpdateRenderData` patches, `PartTree.CreateFromNewPartTree`, `RocketCore.FeedConnectors` (blinky ignition), `SuperMeshRenderSystem.RenderMainPass`, UnlitMesh shaders |
| [`character-and-materials.md`](character-and-materials.md) | doh, humble-arteest, kitten-animations | `GpuMaterialSystem.BigBuffer`, `KittenEva`/`EVADoor` (**doh @5402**: spawn/despawn now `JobSystems.VehicleSolver.Wait()` before touching the shapes registry), `PerInstanceData` `StateBitFlag` free-bit paint + `ShaderModuleUtils.FromFile` shader patch; **kitten-animations** — filterable selection of any live EVA kitten by `Vehicle.Id`, Harmony prefix on `AnimatedRenderable.UpdateAnimation`, 17 private `KittenRenderable` animation fields, and a mod-owned `CatExpressionAnim` |
| [`part-editor-and-robotics.md`](part-editor-and-robotics.md) | parts-now, dont-stifle-me | parts-now's `ModLibrary` reflection + `DeviceMeshInterleaved.Shared` headroom invariant; **dont-stifle-me** scale patches on `VehicleEditor.ScaleBoundsFor` / `UpdateSelectedScale` / `QuantizeScale`, plus configurable editor-limit patches on `DrawParachuteSection` / `Parachute.SetDiameter` (2–1000 m diameter). **flexo removed @5348** — compiled clean, but the robotics approach never worked and will not be reattempted; `PartModelRenderer.UpdateRenderData` and `OrbitLinePass` are now unowned |
| [`exhaust-plumes.md`](exhaust-plumes.md) | pyro | `Vehicle.AddVolumetricExhaustInstances` postfix, `VolumetricExhaustRenderer.AddInstance`, `VolumetricExhaustInstance` (+ private `_shaderData`), internal `VolumetricExhaustTemplate.References`, `PlumeData`/`ExhaustInstance` layout drift (new @5348) |
| [`decals.md`](decals.md) | graffiti | `RenderTarget.ResolveAttachments` postfix (GridPass-window projected-decal pass), `GlobalShaderBindings` + `BindlessTextureLibrary` descriptor sets, runtime GLSL vs `Common/*.glsl` headers, `Part.RayCastEgo` + live `Parachute.ClothPositionsFront` triangle picking + `Cursor.GetEgoRay`, CPU terrain march; **no string reflection** (new @5348; canopy picking added @5402) |
| [`parachutes.md`](parachutes.md) | free-fallin | `ChuteRenderable.Draw`, `Utils.SetShaderFromMod`, and `ShaderModuleUtils.FromFile` prefixes; private `_renderable` + protected `AnimatedRenderable.MaterialIndices`; runtime `MaterialData` and PNG/PBR uploads; material-gated bind-pose projection through `Model{,_Skinned}.vert` / `ModelPbr.frag`; stock canopy assets (new @5402) |
| [`ground-clutter.md`](ground-clutter.md) | pebbles; [GLB materials](ground-clutter-glb-materials.md) | Per-body native clutter graphs, private materials, `ExecuteNextClothSolvers` transactions, collider/physics invalidation, GLB uploads and independent Workshop preview; shared Harmony ownership |
| [`rings.md`](rings.md) | rocky-mcrock-face, bloomin-onion | planetary-ring mesh/texture swap (rocky) and **runtime ring definition on any celestial** (bloomin-onion) via the public `PlanetaryRingsReference` data tree + `Program.RebuildRenderer()`; **no Harmony patches**; `ModLibrary.AllMeshes`/`AllFiles` reflection, `MeshReference.<HostPrimitives>k__BackingField`, ctor-baking invariant in `PlanetaryRingsRenderData`; bloomin-onion adds `PlanetTransparenciesRenderer._anyRings` (load-bearing), `TextureReference.<TextureAsset>k__BackingField` (painted textures) and a cosmetic `DistantSphereRenderer._data` sync (new @5348) |
| [`ui-customization.md`](ui-customization.md) | skittles, kitchen-sink | `ImGui` style surface, `ReinitializeDerivedValues` + IvaForceRender |
| [`audio.md`](audio.md) | byo-music | Shared sound imports, FMOD stream/channel ownership, vessel-relative 3D playback and repeat/gaps |

Bundled in the unscience supermod (26): blinky, bloomin-onion, byo-music, camera-controller-override, doh,
dont-stifle-me, eternal-flame, free-fallin, garrys-torch, glass, godzilla, graffiti, hot-pursuit, humble-arteest,
i-feel-seen, its-so-shiny, kitchen-sink, kitten-animations, kiwis-marbles, parts-now, pebbles, pyro,
rocky-mcrock-face, skittles, thug-life, zippo. (jplrepo is a development reference and is not loaded by the supermod.)

---

## Current status against `5402` (summary)

Full detail lives in [`game-integration-surface.md`](game-integration-surface.md) §6; the remediation
record is in [`../plans/KSA_5402_UPGRADE.md`](../plans/KSA_5402_UPGRADE.md). The 5348→5402 span
(54 revisions, one logged) is dominated by a **viewport registry rework** (`Viewport` class → the
`IViewport`/`IGameViewport` interfaces, `Index` → `ShaderSlot`), **parachutes** with a cloth solver,
**part structural failure / debris**, an **exhaust plume deformation** rework, and a **light-switch
consolidation**. Blast radius on unscience: **three compile breaks (fixed), four behavioral watch
items, one game-side regression.**

**Build-blocking — fixed this pass:**
- **`KSA.Viewport` removed** → six one-line `IViewport` retypes in ksa-abstractions (`IvaForceRender`),
  dont-stifle-me, i-feel-seen, parts-now and graffiti (`Index` → `ShaderSlot`).
- **`Cursor.InputRay` removed** → graffiti uses `Cursor.GetEgoRay(Program.MainViewport)`; the ray is
  now same-frame rather than one frame stale.
- **`VolumetricExhaustRenderer.AddInstance` gained `airVelocity`/`airDensity`** → pyro computes them
  the way `Vehicle.AddVolumetricExhaustInstances` does.

**Pebbles backport:** bundled through main's `ISubmod`/shared Harmony lifecycle, with session-owned authoring and applied controls in its existing panel. Managed checks and compilation cover the port; native apply/restore, collision, preview and unload still need an in-game smoke pass. See [ground clutter](ground-clutter.md).

**Zippo Disco backport:** the abandoned new-UX branch's complete party-light engine now runs through
main's existing standalone and Unscience `ISubmod` lifecycle. It supports vehicle-wide or per-light
color, moving-assembly actuation and spotlight-spread cycles with independent timing, pause/status
controls, exact runtime ownership, conflict handling and stop/disappearance/unload restoration. No
Harmony target, shader or asset dependency was added. Native color isolation, actuation, cone spread,
craft destruction and unload restoration still need an in-game smoke pass. See [celestial and lights](celestial-and-lights.md).

**Garry's Torch actuator fix:** a guarded `Program.PrepareFrame` transpiler now welds after completed
module states commit and before the next cloth/vehicle/orbit snapshots, using `SimStep.PreviousTime`.
Managed Harmony timing checks cover the old discarded-result regression, pause/warp and unload;
native actuation, weld chains and HUD-hidden behavior still need a live pass. See [vehicle physics](vehicle-physics.md).

**Behavioral — compile-clean, needs a live pass before any code change:**
- **hot-pursuit** — nearby-celestial synchronization now prevents the 5402 secondary distant-sphere
  artifact after each mounted pose; the stock secondary renderer still omits particle and volumetric
  exhaust passes, so engine plumes are not expected. Live checks remain for scaled/robotic parts,
  target destruction/debris handoff, terrain clamping, Glass coexistence, slot contention, and
  close/reopen lease behavior.
- **pyro (and the game) — refraction is dead in 5402.** Nothing sets `_hasRefractionInstances` any
  more, so pyro's Refraction slider is inert. Game-side; confirm on a stock engine.
- **garrys-torch vs part failure.** Overlapping welded vehicles can now shed debris or be destroyed.
  `WeldEngine.UpdateWeld` gained a disposed guard so the aftermath unwelds cleanly instead of throwing,
  but nothing stops the game destroying a welded craft — that still needs eyes on it.
- **garrys-torch XYZ scale.** Weld state, UI, presets, animation, and the public API now carry independent
  X/Y/Z factors. Normal vehicles write the existing `Part.Scale : double3`; KittenEva's scalar-only
  character path is corrected by a narrow postfix on `KittenRenderable.ModelToBodyMatrix`. Legacy
  scalar TOML/API inputs migrate uniformly. Live-check unequal axes and identity restore on unweld.
- **graffiti terrain decals** — the accurate terrain-height path now derives from `MeanRadius`.
- **IvaForceRender** — `PartModel.AddInstance` now early-returns for viewports without
  `RenderPartModels`; a postfix still runs after that, so the postfix now mirrors the gate (and reads
  IVA mode off its own viewport). Dormant either way; wants a look in the editor with Force IVA on.
- **dont-stifle-me parachute limits** — the new **jpl said no clamps** control patches the 5402
  parachute editor slider and setter clamp to accept 2–1000 m. It compiles against the current
  surface but needs a live editor pass, including a symmetry counterpart.
- **free-fallin parachute materials** — the new submod replaces canopy material slot zero at draw
  time, supporting stock tint, panel-tiled PNG replacement, cohesive full-canopy bind-pose
  projection, center compositing, and stock-scaled or uniform PBR. Its projection shader transforms
  compile to valid SPIR-V against 5402 but need a live orientation/deformation pass alongside
  restore and unload.
- **thug-life** — `RenderMainPass` now also runs per secondary viewport; the quad still has never had
  a live pass on any build since 5261.

**Verified clean against 5402:** the **entire string-reflection watchlist** (same kind and type),
**every Harmony target signature** apart
from the `IViewport` retype (all single overloads; `GameSettings.cs` byte-identical;
`ExecuteNextVehicleSolvers` body identical), **`PerInstanceData`/`MaterialData` byte-identical**,
`MeshIndirect.*`/`UnlitMesh.*` byte-identical, frames and telemetry types unchanged, no `Brutal*` drift.

**Kitten animation targeting:** the filterable picker now follows the controlled kitten by default or
pins the panel to any live EVA kitten by stable `Vehicle.Id`; target changes restore target-owned
processor state. It compiles clean against 5402; uncontrolled playback, target disappearance/re-EVA
and target switching with an active override still need an in-game pass.

**Carried forward (unchanged by this build):** kitten-animations forced clips/expressions and
parts-now load-time validation still want a live pass; humble-arteest Vehicle Paint remains dead by
design (4693). `___Transform`, zippo `"Color"`, and the "supermod never
wires `IvaForceRender`" notes were stale and are closed. pyro, graffiti, rocky-mcrock-face,
bloomin-onion and dont-stifle-me have still never been exercised in-game.

**What still needs a live pass:** F11 smoke; pyro plume bend in atmosphere + heat-haze check;
garrys-torch weld with crash-tolerance log watch; graffiti vehicle + terrain decal placement;
parts-now runtime part thumbnail (rev 5401); free-fallin stock tint / panel replacement / Full Canopy /
center decal /
uniform PBR / restore-unload; dont-stifle-me scale-then-attach plus 2 m / 1000 m
parachute edits with symmetry; kiwis-marbles weld near
a deployed chute; hot-pursuit placement/motion/lease contention + Glass independent FOV; the standing thug-life / humble-arteest / blinky /
its-so-shiny render checks. A green `dotnet build` and the managed Pebbles/Garry's Torch checks do not cover these native behaviors.

**Godzilla added:** Smart uniform layout-preserving vessel scaling and Basic raw XYZ scales, with
snapshot restoration and Garry's Torch scale ownership exclusion. The shared PrepareFrame hook
queues edits before welds. Managed snapshot and Harmony checks pass; native scale/collision/animation
and unload smoke tests remain. See [vehicle physics](vehicle-physics.md#godzilla-godzilla--godzillalib).

**BYO Music added to Unscience:** copied shared sound catalog, nonblocking FMOD playback following
vessels in the audio camera frame, live gain/range and repeat/gaps. Full solution and managed
catalog/scheduler checks pass. Native listening/decoding/unload remain a live check; see [audio](audio.md).
