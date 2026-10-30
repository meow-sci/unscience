# KSA 5707 upgrade — impact, remediation and acceptance

## Verdict and inputs

KSA 5707 breaks compilation in two places and silently breaks four features that still compile
(Iron Man save load and copies, Sphinx on low-end GPUs, Pebbles' new pre-pass) plus one DOH naming
edge case and one Pebbles lighting regression. All eight were fixed with the user's approval.
**Static and managed verification only: no live KSA pass was run by the coding agents.**

| Input | Verified value |
|---|---|
| CURRENT | `../ksa-game-assemblies/current`, build **2026.10.25.5707**, metadata date 2026-10-29, revisions 5639–5703 |
| PREVIOUS | `../ksa-game-assemblies_prev/current`, build **2026.10.19.5638** — also the recorded `scope/` baseline, so the span is one hop |
| Changelog coverage | Every revision 5639–5703 is logged in CURRENT's `version.json` (65 entries); **no unlogged gap** |
| Authoritative sources | The supplied `decomp` and `Content` trees (191 C# files changed, 64 added, 4 removed — the RenderCore interfaces that moved to `Brutal.VulkanApi.Abstractions`; 53 Content files changed, 6 added (shaders), none removed; no Content `Id` removed, 7 added) |
| Build DLLs | `KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/` (trailing separator); same DLL file list as 5638 |
| Native installation | None on this ARM64 host |
| Dependencies | StarMap.API 0.3.6, Lib.Harmony 2.4.2, Tomlyn 0.19.0 unchanged. Brutal assembly versions stay 1.0.0.0 (rev 5652 bumped only package versions); StarMap HEAD compiles to byte-identical IL against both trees — **no StarMap rebuild needed** |

## Method

1. Changelog scan (65 revisions) and a full source/Content diff 5638 → 5707.
2. Whole-solution build against CURRENT as the alarm; root errors fixed in a throwaway worktree first to
   expose masked errors (none: only `iron-man.tests` had a second-order error).
3. Eight read-only area audits (Iron Man; parts/editor/resources; vehicle physics/kittens; part render/GPU;
   characters/materials/plumes/parachutes/decals/audio; clutter/planet/statics; celestials/rings/cameras/
   viewports; shell/UI/saves/StarMap), each diffing every Harmony target (signature, body, IL anchors),
   string reflection, shader anchor, descriptor set, byte layout and asset id it owns. Key claims were
   re-verified against the decomp by the coordinator before acting.
4. Approved compile fixes, then approved runtime fixes with tests and the lockstep documentation pass.

## Changelog — revisions that touch the suite

| Revisions | Change / suite exposure |
|---|---|
| 5643 | **Editor + Part memory rework**: symmetry ghost previews, `PartCloner` copies, `Connector[]`, lazy `Connections`/`SubtreeModules`, all constructors chained through one private constructor — Iron Man (broken), dont-stifle-me, blinky, its-so-shiny, PartRenderFilter (ghost compose path) |
| 5640 | Pipelines needing more than 8 descriptor sets are skipped (static objects use 10) — sphinx (broken on low-end GPUs) |
| 5645 | Experimental HDR: `HdrPresentPass`, `GetCompositeTarget(viewport)`; scene/offscreen format unchanged (R16G16B16A16) — thug-life, graffiti clear; StarMap restart popup drift (upstream) |
| 5646 | Vehicles placed by colliders; StrutA/B/C gain colliders — godzilla, garrys-torch (live) |
| 5652 | Brutal packages + `SimpleVkTexture.VkImage/VkImageView` rename; RenderCore descriptor/image interfaces moved to `Brutal.VulkanApi.Abstractions` — graffiti, pebbles, thug-life (compile) |
| 5654–5658, 5676–5682, 5689–5690 | Compute tessellation, terrain and clutter in the depth-normal pre-pass, AO receiving, erosion, `UsesCustomNormals` — pebbles (broken), graffiti terrain decals, sphinx terrain height (live) |
| 5663 | Clouds in secondary viewports (`ViewportOptionFlags.RenderClouds`, per-slot cloud targets) — hot-pursuit (VRAM, live), ring rebuild cost |
| 5641, 5644, 5647–5649, 5672–5675, 5684–5700 | Aurora/magnetospheres, Io/Ganymede/Triton/Pluto moved to the shared library (ids unchanged), Pluto `LockedTo` + 113° tilt, space weather — kiwis-marbles (cosmetic drift), bloomin-onion |
| 5669, 5688, 5694, 5700 | Kitten radiation dose, sickness, death (`KillLethallyDosedCrew` inside `ApplyVehicleSolvers`), roster columns, step limiting in `GetJobSimStep` — doh (naming edge case), The Tick (new non-force kill path), PhysicsFrameHook (clamp honored) |
| 5701, 5703 | Gas storage in tanks (rated pressure) — eternal-flame refills gas to rated-pressure mass (same as stock), blinky `CanHold` gas rule |
| 5642, 5653, 5671, 5702 | Clipboard paste fix, Debug menu toggles, MMU leg sign, Windows monitor list — no impact |

## Compile breaks and fixes

1. **`SimpleVkTexture.Image/ImageView` → `VkImage/VkImageView`** (rev 5652): one line each in
   `graffiti.lib/DecalTextures.cs:157`, `pebbles.lib/Assets/GlbTextures.cs:87`, `thug-life.lib/ThugLifeTextureFactory.cs:24`.
2. **`Part.Connectors` `List<Connector>` → `Connector[]`** (rev 5643): Iron Man grows/removes anchors by
   array replacement (`IronManConnectors.cs:49,78`), `Length` in `IronManConnectorData.cs:31` and
   `IronManEvaSettings.Saves.cs:24,37,39`; the `iron-man.tests` fixture models the array and 5707's
   lazily allocated, shared-empty `Connections` list.

These failures masked 16 dependent projects (Unscience host, sphinx, world-saves.tests, legacy hosts).

## Runtime findings and fixes

| # | Mod | Finding (class) | Fix |
|---|---|---|---|
| 1 | iron-man | **Save load bypasses the patched constructor** (semantic drift): `RegenerateFromPartInstance` → `Part(PartInstance, Part)` now chains to a private 10-argument constructor, skipping the public 4-argument one Iron Man patched. Marked kittens kept the `iron-man:v1:` marker as their id, lost anchors, and connected anchors threw `IndexOutOfRangeException` during connection regeneration | `IronManConnectorPatches.cs:14-30` patches the private constructor (`__0` name, `__1` template, `__2` instance); the 4-argument patch is removed (both would decode twice) |
| 2 | iron-man | **`PartCloner` copies drop anchors and crash** (new path): the copy constructor rebuilds template connectors only, then reconnects by the source's connector index | Postfix on `Part(Part source, Part? parent)` → `IronManConnectors.CopyOwned` (`IronManConnectors.cs:58`) re-adds owned anchors in order |
| 3 | sphinx | **Draw with no pipeline bound** on devices without 10 descriptor sets (new reachability, rev 5640): stock now skips null pipelines; the `AfterColor` postfix did not | `SphinxPatches.cs:43-49` binds `VkPipeline pipeline` and returns when it is null |
| 4 | pebbles | **New depth-normal pre-pass not redirected** (missed new caller, rev 5677): Pebbles-owned material ids were looked up in the stock buffer → wrong cutout silhouettes/normals and AO; possible out-of-range index (unconfirmed) | Dependency-free `pebbles.lib/Runtime/ClutterMaterialBindings.cs` now lists five methods incl. `CreateDepthNormalPrePassRenderer`; each still has exactly one binding |
| 5 | doh | **Custom name can bind a roster kitten** (new gating, rev 5669): EVA kittens share the same-named roster entry for dose, sickness and death (KIA) | `KittenSpawner.Naming.cs:30` also rejects names in `Universe.KittenRoster` (case-insensitive); batch numbering skips them |
| 6 | pebbles | **`UsesCustomNormals` dropped** (new flag, rev 5689): owned bodies regained the over-dark foliage AO | Optional `MaterialRecipe.UsesCustomNormals` (`PebblesRecipe.cs:68,73`), captured (`ClutterCapture.cs:73`), inherited from the stock material by `SourceId` when absent (`ClutterGraph.cs:26-27,135`) |

### Semantic drift needing no code (documented, live-check)

- **Radiation death** is a new kill path outside The Tick's force-based protection; IVA crew of protected
  vessels can die while the vessel survives. Existing disposed-vehicle guards (torch, dent-wizard,
  godzilla, doh) cover kittens removed before the shared handoff.
- **`Universe.GetJobSimStep`** clamps steps to radiation thresholds; `PhysicsFrameHook` passes the original
  result through. `DeserializeSave` does not reset the game's `_stepLimitTime` (at most one short step).
- **`Vehicle.UpdatePerFrameData()`** now credits radiation dose; extra calls by weld/launch/spawn code see
  zero elapsed dose time.
- **Editor ghost previews** compose through `PartTree.ComposeRenderData` without `EnsureBuilt`; the shared
  render filter and IvaForceRender apply consistently; godzilla does not scale ghosts (cosmetic).
- **Hot Pursuit** feeds now render clouds (~140 MB VRAM per 2048² feed); cloud targets are per slot,
  not per lease. Cloud shadows, plumes and exhaust are still main-view only.
- **Pluto** gained a 113° tilt and tidal lock to Charon: Charon-relative weld offsets saved on 5638 point
  differently in world space (payload unchanged); welding Charon leaves Pluto's spin unlocked.
- **Gas tanks**: eternal-flame refills to rated-pressure mass (same as the stock refill).
- **Terrain**: CPU height (`GetTerrainHeightFromDirCcf`) and GPU tessellation changed — 5638 Sphinx statics
  may float/sink slightly on eroded terrain (existing snap-to-ground fixes it); graffiti terrain decals
  need a visual check.
- **Raytraced IVA** remains unfiltered by PartRenderFilter; rev 5670 makes it work on AMD, so more players see it.
- **Upstream (StarMap):** `ConfirmRestart` copies the game's render loop without the new
  `HdrPresent.Record(…, hasScene:false)`; with HDR enabled its popup may render black (unconfirmed).

## Verified unchanged

StarMap hooks (`OnDrawUiFrame`, `OnDrawUiViewports`, `DrawProgramMenusHook`, `ModLibrary`/`Mod`); HotkeyGuard
`GameSettings.OnKeyAll` (body identical); HiddenUiFrameHook; MenuBarPatch; PhysicsFrameHook's seven seams;
native save types (`UncompressedSave`, `SaveName`, `UniverseData`, `PartInstance`, …) and skip rules;
providers and `KSAColor.Xkcd`; PartRenderFilter, Vehicle Paint and IvaForceRender bodies/fields; static,
dynamic and glass `PerInstanceData`; paint bits 11–31; `MaterialData`; every mesh shader; avatar chain and
all kitten-animation fields; visor IL anchor (ilspycmd); doh render bridge and 512-material capacity;
pyro; free-fallin descriptor sets; graffiti resolve seam and offscreen format; thug-life pass and
formats; glass `_fovRadians`; camera controllers; zippo; ring reflection; kiwis-marbles orbit seams;
Kitchen Sink and The Tick `DetectStructuralFailure` anchors; dont-stifle-me's five editor targets;
eternal-flame refill path; Godzilla cull anchor and `UpdateCollisionGeometry`; every hard-coded asset id
(no Content `Id` removed).

## Verification results

- Full non-incremental solution build against CURRENT 5707: **0 warnings, 0 errors**.
- **All 18 managed suites pass** from fresh Debug builds: byo-music, camera-saves, dent-wizard, doh,
  garrys-torch, godzilla, iron-man, iron-man-flight, iron-man-mode, kitchen-sink, ksa-upgrade, pebbles,
  pyro, save-yourself, saves, sphinx, the-tick and world-saves.
- New coverage: `iron-man.tests` models 5707 constructor chaining (load, 4-argument and copy constructors
  all reach the private one) with load and copy checks; `doh.tests` roster-name checks; `pebbles.tests`
  `UsesCustomNormals` legacy/explicit/round-trip/sidecar checks; `ksa-upgrade.tests/Ksa5707Checks.cs`
  (with the new `kitchen-sink.tests/KsaAssemblyProbe.Members.cs` partial) proves on the real KSA.dll that
  every `Part` constructor reaches the private seam, that the methods binding the clutter material buffer
  equal `ClutterMaterialBindings.Methods`, and that `WriteCommandsColor` skips null pipelines. Against the
  5638 DLL these checks fail, as intended.
- `scripts/check-dist.py` passes: one Unscience mod, 31 feature assemblies, no proprietary KSA DLL.
- **Runner gotcha:** each test project also holds stale `bin/Release` output from the 5638 work; run
  `bin/Debug/net10.0/<name>.dll` (or `dotnet run`) explicitly. An early run in this upgrade picked the
  Release binaries by accident; the results above are from the fresh Debug builds only.

Build command:

```sh
KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/ \
UNSCIENCE_DIST_DIR=<scratchpad>/dist DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
dotnet build ksa-mod-experiments.slnx --no-incremental -nr:false -p:UseSharedCompilation=false
```

## Saves

No save IDs or record versions changed. Pebbles' recipe gains one optional field (absent → inherit the
stock material, so 5638-era records load unchanged). Iron Man's anchor marker format is unchanged; only
the constructor that decodes it moved. KSA itself now saves kitten radiation dose (`KittenRosterEntryData`)
and derives space-weather state from seeds and time — both intentionally excluded from the sidecar.

## Native acceptance (pending)

- Iron Man: load a world save and a blueprint with connected anchors (order, fuel flow, character);
  Copy Tree / duplicate an unconfigured kitten with anchors; edit nodes while a part is grabbed.
- Sphinx on a device (or forced failure) without 10 descriptor sets: statics skipped, no validation error.
- Pebbles-owned body with alpha clutter and trees, AO on: silhouettes and foliage AO match stock.
- DOH: spawn with a rostered kitten's name (refused) and a batch whose prefix collides (skipped).
- Radiation: The Tick-registered kitten at 4 Sv dies; welded/launched kitten killed mid-operation is
  removed cleanly; save/load around a radiation step limit during warp.
- Godzilla and Garry's Torch on strut-heavy vessels (new colliders) and near launch spawns.
- Hot Pursuit: four 2048² feeds with clouds (VRAM, frame time, resize hitch); ring apply with feeds open.
- Graffiti terrain decals with compute/hardware tessellation and on grass; HDR on/off.
- Eternal Flame with a gas tank and a fuel-line-only tank; DSM per-axis scale under radial symmetry.
- All open 5638 native checks remain open (see [KSA_5638_UPGRADE](KSA_5638_UPGRADE.md) and [ISSUES](../ISSUES.md)).
