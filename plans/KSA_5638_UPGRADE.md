# KSA 5638 upgrade — impact, remediation and acceptance

## Verdict and inputs

The update breaks compilation in two places and silently disables or corrupts several features that
still compile. All compile breaks and every runtime finding are fixed in the working tree
(uncommitted), and the **parts-now** mod was removed at the user's request. **Static and managed
verification only: no live KSA pass was run.**

| Input | Verified value |
|---|---|
| CURRENT | `../ksa-game-assemblies/current`, build **2026.10.19.5638**, metadata date 2026-10-20 |
| PREVIOUS (on disk) | `../ksa-game-assemblies_prev/current`, build **2026.10.10.5554** |
| Recorded baseline | **5482** (`scope/` header). The honest span is **5482 → 5638**, reviewed through `ksa-game-assemblies` git history (`a2f724f` 5482, `47703f3` 5541, `935a32b` 5554, `edda844` 5638) |
| Authoritative sources | The supplied `decomp` and `Content` trees, not the in-repo historical decomp |
| Build DLLs | Explicit `KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/` (trailing separator); macOS default resolves the same tree |
| Native installation | None; the supplied Windows x64 `KSA.dll` cannot execute on this ARM64 host |
| Dependencies | .NET SDK 10.0.402; StarMap.API 0.3.6, Lib.Harmony 2.4.2, Tomlyn 0.19.0 unchanged. StarMap's loader must be its 5638 build (`78816da`, Brutal reference update) |

## Method

1. Changelog scan of all three logged windows and a full source/Content diff of 5482 → 5638
   (819 KSA C# files changed: 99 added, 4 removed; 211 Content files).
2. Whole-solution build against CURRENT as the alarm; each root error was fixed in a throwaway worktree
   to expose masked errors before touching the working tree.
3. Eight read-only area audits (shell/saves/UI, vehicle physics, Iron Man, celestials/camera/rings,
   render/decals, characters/materials, parts/editor/assets, plumes/clutter/parachutes), each diffing
   every Harmony target (signature, body, IL anchors), string reflection, shader anchor, descriptor set,
   byte layout and asset ID it owns.
4. Compile fixes (approved), then six isolated coding passes for every confirmed finding, merged and
   rebuilt together.

## Changelog and unlogged span

Logged: **5483–5540** (5541 window), **5542–5553** (5554 window), **5603–5637** (5638 window).
**Revisions 5555–5602 appear in no `version.json`.** The Helm control frame, the two-argument
`DetectStructuralFailure` and the kitten-impact branch landed there and were found only by the source diff.

| Revisions | Relevant change / suite exposure |
|---|---|
| 5485, 5486, 5518, 5637 | Follow-relative map/portrait/IVA cameras; portrait camera controls — camera-controller-override, doh |
| 5487, 5488, 5523, 5524 | `PlumbedCore` engine base, ThermalCore, antimatter/magnetic-trap parts — blinky |
| 5493–5499, 5511–5515, 5531, 5533 | **Multiple stars** (`OrbitingStar : IOrbiter`, barycentres, TwoBody orbits) — kiwis-marbles |
| 5502 | Per-view instance-buffer slices, kitten/parachute shadow-cascade culling — render filter (clear), garrys-torch scale (live) |
| 5505–5553 (various) | Plume-trail rewrite — pyro unaffected |
| 5530, 5534, 5546 | **Descriptor-set renumbering**, texture library slot, secondary-viewport atmosphere/terrain, bindless clamp — free-fallin, graffiti, hot-pursuit, Engine Emissive |
| 5539, 5549–5552 | DOP853 long warp steps, TVC LQI — no mod change |
| 5542 | CoreCommandA/Structural/Fairing re-import with convex hulls and `_ColMesh_` collision atlases — godzilla, capsule glass (live) |
| 5544 | Thruster cache recomputes on propellant availability — helps Eternal Flame relight |
| 5555–5602 (unlogged) | `Ctrl` → `Helm` frames + `Tnm`; `DetectStructuralFailure(…, SimStep)` + `DetectKittenImpact` |
| 5607, 5486 | Boarding/despawn via `Universe.DestroyVehicle` (portrait/emitter release) — doh |
| 5612 | Lone bubbles switch to the planet-fixed frame — may end Garry's Torch error spam |
| 5618 | **Brutal open source: assemblies renamed** — every project |
| 5629, 5634 | Clutter in multiple viewports, per-viewport residency — pebbles |
| 5630 | **Release build optimized** — inlining risk for small Harmony targets |
| 5632, 5633 | Vehicles with missing parts skipped on load — save preflight |

## Compile breaks and fixes

1. **Brutal assembly renames (rev 5618).** `Brutal.Core.Common` → `Brutal.Core`;
   `Brutal.Core.{Numerics,Strings,Memory,Collections}` → `Brutal.{…}`; namespaces unchanged. Every csproj
   HintPath is `Exists()`-guarded, so references silently vanished and produced 349 CS0246/CS0234
   errors. 73 csproj files updated; no assembly-qualified strings exist in code.
2. **`Vehicle.Ctrl2Body` → `Helm2Body`** (unlogged). `IronManControlFramePatches` and the flight fixture
   retargeted. Behaviourally sufficient: `FlightComputer` sets `Helm2Asmb = nav.Helm2Body` and derives
   `Tnm2Asmb` (thrust-aligned) and `Ctrl2Asmb` (`ActiveControlFrame`) from it each tick.

## Runtime findings and fixes

| Mod | Finding (class) | Fix |
|---|---|---|
| free-fallin | Pre-5530 `SET_TEXTURE 1`/`SET_MATERIAL 2` injected into the shared `Model_Skinned.vert`; material SSBO bound at the CSM UBO slot for **every skinned pipeline** (descriptor-layout) | New `CanopyShaderSource` parses the defines from live `ModelPbr.frag`, cross-checks `ModelNormal`/`ModelTranslucent`, fails closed (no patch, stock shaders untouched). glslc + spirv-cross proof: `MaterialBuffer (1,0)`, textures set 5. `FreeFallinShaderChecks` (19) |
| the-tick, kitchen-sink | `DetectStructuralFailure(VehicleUpdateState, SimStep)`: 1-arg lookup threw, both inert (reflection-miss); fixtures mirrored the old body | Lookups fixed; fixtures mirror 5638; `KsaAssemblyProbe` reads real KSA.dll metadata to assert signature and anchor counts |
| the-tick, kitchen-sink | New kitten-only `DetectKittenImpact` returns before the G-load check (new-gating) | Kitchen Sink: second guarded anchor masks the impact result for protected kittens (kept 5482 survival). The Tick: postfix already discards it — now deliberate and tested |
| native saves | Preflight refused saves that 5638 loads by skipping missing-part/character vehicles (semantic-drift) | Missing part/character/parent now warn via `NativeSaveHooks.LoadWarning`; structural checks KSA still cannot survive remain; reset join is `JobSystems.WaitAll()` |
| kiwis-marbles | Stars are now `IOrbiter`; restore searched only `Celestial`s, dropping star-target welds | Resolve by exact id among the offered orbiters; ambiguous ids unresolved; surface helper supports stars; reverse-cycle welds refused |
| doh | Despawn bypassed `Universe.DestroyVehicle` (portrait camera/emitter release new in 5486/5607) | `DestroyVehicle(kitten, CrewDisposition.Boarded)` (no roster change); also joins the nearest-orbit worker |
| iron-man | Stock `CopyFrom` never copies `Helm2Asmb`, so the thrust marker is ~90° off for the rocket helm | `IronManNavballPatches` postfix on private `UpdateNavBallMarkers` for enabled kittens only |
| iron-man | RCS invalidation wrote `Zero` (Identity frame), so the new `IsCtrl2BodyCurrent` gate skipped recompute after disable | Writes `default` (all-zero frame) so recompute is forced |
| pebbles | Per-viewport residency (5629) leaked on swapped-out arrays; nested `MappedMemory[][]` retired out of order | Release every registered viewport on outgoing arrays; generalised retirement unmapping |
| blinky | Magnetic-trap tanks could be paired with pixel engines; `is Combustor` diagnostics | `FuelAnchorFinder` keeps tanks where `Tank.HoldableSubsetOf(mix) != null`; checks test `PlumbedCore` |
| camera-controller-override | Absolute ECL offsets quantise at interstellar distances | Follow-relative offsets through `Camera.PositionCce`; absolute fallback when unfollowed |
| hot-pursuit | Mirrored `NearbyCelestial` never cleared on release | `HotPursuitCelestialState.Clear` on release/lease loss; mirror kept (same body rule as stock) |
| ksa-abstractions | Interior gate moved to `RendersInterior()` (crew portraits) | `IvaForceRender` aligned with stock; render fixtures model `RendersInterior()` |
| parts-now | Stale schema blocklist (`ExplosionVolumeTemplate`, `ExplosionVolumeLook`) and `_` mesh skip | **Mod removed** (user decision). Legacy `parts-now` records retained/reported (`saves.tests/RetiredFeatureTests.cs`, 16 checks) |

## Verified unchanged

StarMap targets; `GameSettings.OnKeyAll`; `HiddenUiFrameHook`; `MenuBarPatch`; the seven
`PhysicsFrameHook` seams and `ExecuteNextVehicleSolvers` (profiler call only); `Vehicle.Teleport` chain;
eternal-flame refill path (5544 improves relight); godzilla cull/transpiler anchors; avatar reflection
chain (`_renderable` → `_characterAvatar` → value-type `Core` → `Scale`); kitten-animations fields;
doh render bridge; `MaterialData` (AlbedoColor @16, stride 80) and both `PerInstanceData` layouts; paint
bits 11–31 free; Vehicle Paint seams and anchors; visor IL anchor; Engine Emissive (LUT now bindless
`TEMPERATURE_LUT_SAMPLER`); thug-life `RenderMainPass`/UnlitMesh; graffiti resolve seam and set 0
(`SET_TEXTURE 2` satisfies the new `TextureSet.glsl` `#error`); pyro; zippo (`ColorRgb`); glass
`_fovRadians`; IVA camera unlock; bloomin/rocky ring reflection; byo-music/FMOD; skittles ImGui style;
dont-stifle-me gizmo; every hard-coded asset id (924 code literals diffed against both Content trees).

## Verification results

**Final whole-solution build: 0 warnings, 0 errors** against CURRENT's DLLs (serial, non-incremental).
**All 18 managed suites pass:** byo-music, camera-saves (12), dent-wizard, doh (34), garrys-torch,
godzilla, iron-man, iron-man-flight (incl. helm/navball and RCS gate checks), iron-man-mode,
kitchen-sink (108 G-load, real KSA.dll probe), ksa-upgrade (incl. free-fallin shader layout,
`RendersInterior` filters), pebbles (retirement shape, viewport residency), pyro, save-yourself, saves
(skipped-vehicle loads, retired features), sphinx, the-tick (108, real KSA.dll probe), world-saves
(star-target weld). No fixture executes native KSA code or GPU rendering.

Build command:

```sh
KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/ \
UNSCIENCE_DIST_DIR=<scratchpad>/dist DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
dotnet build ksa-mod-experiments.slnx --no-incremental -m:1 -nr:false -p:UseSharedCompilation=false
```

## Native acceptance (pending)

- Optimized KSA.dll: menu item, hotkey blocking while typing, IVA seat head visibility, Iron Man
  frame — after many frames and after a mid-session feature toggle/reapply.
- Free Fallin with Vulkan validation: kittens, eyes, canopies; console shows the derived sets.
- The Tick / Kitchen Sink: protected vessel ram/dive; protected EVA kitten dropped >20 m/s onto ground
  and water survives; unprotected dies; save/reload.
- Saves with a missing part mod and with a missing character: KSA alert, Unscience warning, roster.
- Kiwi's Marbles: weld a planet to Alpha Centauri A / Proxima, save, load, unweld.
- DOH: despawn a kitten that is controlled, followed by main and portrait cameras, targeted, emitting.
- Iron Man: navball thrust marker on the rocket nose (incl. gimbal trim); manual RCS rotation right
  after switching to EVA; attitude hold and burns with armed/firing engines (Tnm frame).
- Pebbles: open/close/move secondary viewports during an override, restore, no "cell pool exhausted".
- Camera sequences near another star; Hot Pursuit feed close/reopen at a different body.
- Crew portraits: pixel/shiny parts hidden, capsule glass; graffiti with radiation belts/grid on.
- Godzilla on convex-hull parts (collisions, fuel hoses); capsule window alignment after re-import.
- Blinky on a vessel with a magnetic trap. Garry's Torch: check whether 5612 ended the error spam.
- All standing 5482 native checks remain open (see [ISSUES](../ISSUES.md)).
