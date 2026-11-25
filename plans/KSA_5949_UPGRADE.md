# KSA 5949 upgrade — impact, remediation and acceptance

## Verdict and inputs

Upgraded the suite to the supplied **2026.11.25.5949** build. One compile break (Pebbles) and two silent
startup/runtime regressions are fixed: Iron Man no longer installed at all (5939 serializer signature),
and Unscience scene saves were lost on the new runtime star-system switch (5916). Pebbles now refuses
collider-less replacements the 5903 planet mesh cannot place (user decision) and its native-resource
retirement follows 5919's GPU-resource split. Blinky's feed diagnostics follow the new gas-generator
rocket rule. One pre-existing bug found by the sweep (BYO Music channel group id) is fixed by user
decision. Verification is **static and managed**: no 5949 game installation was used on this macOS
ARM64 host.

| Input | Value |
|---|---|
| CURRENT / NEW | `../ksa-game-assemblies/current`, build **2026.11.25.5949**, metadata date **2026-11-25**, `fromRevision=5901`, `toRevision=5949` |
| PREVIOUS / OLD | `../ksa-game-assemblies_prev/current`, build **2026.11.16.5901**; matches the prior recorded scope baseline (single hop) |
| Changelog | 47 entries, every revision **5902–5948**; **5949 is unlogged** |
| Sources | Supplied CURRENT/PREVIOUS `decomp/` and `Content/` trees. In-repo `decomp/ksa` is historical and was not used |
| Source delta | After normalizing line endings: 261 decomp files changed, 94 added, 1 removed (≈ +15.7k/−5.0k lines); 253 of the changed files are `KSA.*` |
| Content delta | 96 files changed, 18 added, 5 removed, 2 renamed — mostly shaders (cloud ambient/sun transmittance, normal+roughness pre-pass, plume trails, terrain mesh) plus destinations/knowledge tiers, heat shields and engine-cycle rocket XML |
| Changed DLLs | KSA, Planet.Core, Planet.Render.Core, Brutal.Vulkan, Brutal.Vulkan.Abstractions, BepuPhysics, BepuUtilities. Brutal.ImGui is byte-identical |
| Build DLLs | Plain `dotnet build ksa-mod-experiments.slnx`; `Directory.Build.props` resolves `KSAFolder` to the adjacent `../ksa-game-assemblies/current/dll/` with no override |
| Dependencies | StarMap.API, Lib.Harmony and Tomlyn unchanged |

## Changelog priorities

| Revisions | Coupling reviewed |
|---|---|
| 5939 | Batched symmetric detach; **incremental part-tree render data** (`NotePartAppended`/`NotePartsRemoved`, per-part transform refresh); editor-history snapshots reuse unchanged `TransformReference`s via a new optional `EditorSnapshot.PreviousTransforms` parameter |
| 5916 | **Runtime star-system switching**: different-system saves queue a swap (`SaveSystemSwap`), `Program.SwapSystem` tears down vehicles/renderers/planet meshes and rebuilds via `Universe.CreateSystem`; contained-system saves load directly (`LoadIntoRunning`); multiplayer join uses the same swap |
| 5925, 5936 | Engine cycles (gas generator / pressure fed); `Rocket` became abstract with `GasGeneratorRocket`/`PressureFedRocket`/`SolidRocket`/`UnmodeledFeedRocket`/`AntimatterRamRocket`; `Rocket.Core` field removed |
| 5902, 5903, 5907, 5919–5922 | Per-viewport terrain UV offsets; planet-mesh acceleration structures only for stock snap-to-mesh clutter, sized to its reach; implicit planet mesh; **lazily allocated per-planet clutter GPU resources** (`GroundClutterGpuResources`, `ReleaseViewport(int)`) |
| 5904, 5915, 5923, 5926, 5937, 5940, 5948 | Vulkan abstractions preview; `MultiRenderTarget` pre-pass with separate normal and roughness targets; cloud ambient moved to `CloudLighting.glsl`; sun transmittance tile (global set binding 2) after the main colour target lost alpha |
| 5909, 5910, 5918, 5928–5930, 5941 | Shadow cascade count; texture streaming; plume trail/explosion shader refactors; exhaust/ring pipeline-rebuild fixes |
| 5931, 5934, 5911 | Fuel-line tank load fix; velocity-verlet gravitation phase + environment recompute; Bepu 2.5.0-beta.33 (readonly structs, friction fix) |
| 5946 | Load empties seats whose kitten is not in the roster, is seated elsewhere or is on EVA (`Universe.ClearInvalidSeats`) |
| 5912, 5938, 5942, 5943 | Destinations, knowledge tiers, arrival service and visit log (saved per system), Life Support mission legs |
| 5905, 5906, 5908, 5913, 5914, 5917, 5924, 5927, 5932, 5933, 5935, 5944, 5945, 5947 | Editor/UI fixes (MASS markers), thermo/atmosphere tables, new launch site, heat shields/Thermal category, scenario crew — no mod coupling found |

## Confirmed findings and fixes

All findings are **new in NEW relative to OLD** unless marked pre-existing. Evidence paths are relative
to each tree's `current/decomp/KSA/` unless marked otherwise.

| Consumer / production code | Class and evidence in OLD → NEW | Resolution |
|---|---|---|
| Pebbles: `pebbles.lib/Runtime/ClutterResidency.cs` | **Compile break / signature (5919)**: OLD `ClutterEcotypeRenderData.ReleaseViewport(IViewport)` resolved the slot itself; NEW `ReleaseViewport(int slot)` (`ClutterEcotypeRenderData.cs:1143`), with the caller resolving it (`GroundClutterRenderer.OnViewportUnregistered :478-497`) | Resolve the slot behind the existing `HasViewportSlot` guard, exactly like native. Fixture mirrors the slot API |
| Iron Man: `iron-man.lib/IronManConnectorPatches.cs` | **Signature / reflection miss (5939)**: OLD internal `Part.GetReferenceWithChildren(ref uint, PartInstance, bool)` (`Part.cs:2427`); NEW adds `EditorSnapshot.PreviousTransforms? previousTransforms = null` (`Part.cs:2442`; type `EditorSnapshot.cs:14`, callers `PartTree.cs:514`, `Part.cs:2497`). The exact-type `AccessTools.Method` returned null, `IronManPatches.Apply` rolled back **all** Iron Man patches, and saved anchor connections would again be dropped by native `DropStaleConnectors`. Build and fixture tests stayed green (`nameof` binds the public overload; the fixture faked the old shape) | `FindSerialize` resolves the four-parameter overload via `AccessTools.Inner(typeof(EditorSnapshot), "PreviousTransforms")`. The postfix is unchanged: 5939 still builds a new `PartInstance` per node and only reuses `TransformReference`s, which the postfix never touches. Fixture updated; `Ksa5949Checks` pins the real overload and its callers |
| Unscience saves: `ksa-abstractions.lib/Persistence/*` | **Ownership-lifetime / new gating (5916)**: see [Cross-system loads](#cross-system-loads) | See that section |
| Pebbles: `ClutterGraph.cs`, new `Models/SnapToMeshPolicy.cs`, `Runtime/ClutterSnapToMesh.cs`, `ClutterController.cs` | **Semantic drift / new gating (5903)**: OLD built a whole-mesh BVH/TLAS for every body with clutter (`PlanetMeshLibrary.cs:66-71`); NEW only when the **stock** clutter has a snap-to-mesh (non-collideable) ecotype, over a patch sized to its reach (`PlanetMeshLibrary.cs:70-76,118-151`, `CubeMesh.Generate :370-385`), and the GPU build throws without it (`GroundClutterGpuResources.cs:377-379,858-863`). Pebbles' collider-less replacements are snap-to-mesh, so Apply threw on Luna/Mars and Earth trees were cut to ≈80 m; disabled ecotypes (forced to `None`) threw too | **User decision: refuse.** Before any allocation (UI queue, Apply and save replay) an enabled collider-less type is refused with a message naming the body/type when the mesh structure is missing or its reach exceeds the patch, mirroring the native gate and patch formula. Disabled types keep their stock collision type and install no colliders (native only warns; `BiomeMask=0` places nothing) |
| Pebbles: `ClutterRetirement.cs`, `ClutterHooks.cs` | **Ownership-lifetime (5919)**: per-ecotype buffers, maps and descriptor pools moved from `ClutterCubeCellGrid` to the new `GroundClutterGpuResources` (`ClutterEcotypeRenderData.cs:48,332`); `GroundClutterTerrainData.Dispose` no longer frees `CurvatureBuffer` (moved to `DisposePlanetResources :117-120`). An interrupted Pebbles build leaked them | Retirement walks `GroundClutterGpuResources` (its borrowed `Bvh` is not walked) and cleans the terrain curvature buffer; the constructor tracker includes the new type |
| Pebbles: `ClutterResidency.ReleaseDetached` | **VRAM (5919/5920)**: native frees per-planet buffers only for arrays stored in `PlanetEcotypeRenderData` (`DisposeUnusedPlanetResources :1474-1487`), so detached originals kept theirs for the whole override | **User decision:** detached arrays call `DisposePlanetResources()` after releasing every cell (guarded by `HasPlanetResources`; native re-allocates in `AccumulateViewportCells` before use) |
| Blinky: `blinky.lib/RocketFeedCheck.cs`, `LcdGridBuilder.VerifyPropellantFeeds`, `BlinkySubmod` diagnose | **Semantic drift (5925/5936)**: OLD liquid engines had independent Engine and GasGenerator rockets (either could fire); NEW one `GasGeneratorRocket` with `[Combustor, GasGeneratorCore]` whose `UpdatePropellantAvailability` requires both (`GasGeneratorRocket.cs:30-44,174-179`). The "starved gas-generator core is harmless" message became wrong | Diagnostics count a pixel/controller as fed only when one rocket has tank-fed cores and every one reaches a tank. Feed repair/ignition unchanged (both cores share one connector and propellant in stock) |
| BYO Music: `byo-music.lib/VesselSound.cs` | **Asset id (pre-existing, not 5949)**: `ModLibrary.Get<ChannelGroupReference>("Sfx")` never resolved — `Sfx` is only the code-created FMOD group; its `ModLibrary` reference is `SfxChannel` (`Content/Core/Sounds.xml:46`, attached in `AudioBindings.cs:39-41`); `Get` throws (`ModLibrary.cs:1149-1153`), so every vessel sound failed to start | **User decision:** use `SfxChannel` |

## Cross-system loads

**Native change (5916).** OLD `UncompressedSave.Load` always called `Universe.DeserializeSave`
(`UncompressedSave.cs:80`). NEW calls `SaveSystemSwap.RequestLoad(Id, SystemId, ApplyLoaded)`
(`UncompressedSave.cs:87-88`; `ApplyLoaded` = `DeserializeSave` + `Program.OnGameLoaded`, `:106-109`), which
decides by `SaveSystemSwap.PlanLoad` (`SaveSystemSwap.cs:100-120`): **Load** (same system) and
**LoadIntoRunning** (all saved bodies exist in the running system) apply at once; **Swap** queues
`_pending` and returns; **NotInstalled** alerts. The queued swap runs from `SaveSystemSwap.OnFrame`
(`Program.OnFrame :2669`, after `ImGui.Render`) → `Program.SwapSystem` (`Program.cs:1905-1985`: worker join,
GPU idle, `DestroyAllVehicles`/`DestroyAllPhysicsBubbles`, renderer/planet-mesh/streamed-texture disposal,
`Universe.CreateSystem`, `BuildSystemRenderers` behind `Loading` frames) → the stored `ApplyLoaded`.
Multiplayer joins reach the same swap through `RequestJoin`.

**What broke.** (1) `NativeSavePreflight` rejected every save whose system id differed from the running
one, so contained-system loads that native performs failed with an unfixable message. (2) For a swap,
the replayed `Load` returned with the switch only queued: `FinishedLoading` reported a false "KSA could
not read save" and discarded the prepared `unscience.json`; `SwapSystem` then destroyed the world with
no Unscience reset (it calls `CreateSystem`, not the hooked `LoadSystem`), and our own `DeserializeSave`
prefix deferred the swapped-in load to the next physics frame, so `Program.OnGameLoaded` ran before the
world existed and the eventual load restored zero records — a re-save then wrote empty state.

**Fix (user decision: carry state across).** `NativeSaveHooks` (+ new `NativeSaveHooks.SystemSwap.cs`):

- Preflight accepts exactly when `SaveSystemSwap.PlanLoad(id)` is `Load` or `LoadIntoRunning`.
- A `RequestLoad` prefix wraps `apply` so the later `ApplyLoaded` is matched to its save; a postfix records
  KSA's decision. A queued switch keeps the file transaction (prepared sidecar, retained records) open
  instead of reporting failure; NotInstalled names the missing system.
- A `Program.SwapSystem` prefix runs the existing joined reset (`JobSystems.WaitAll`, clear pending,
  `Resetting`) before any native teardown, honouring `ISaveParticipant.ResetState`; join switches reset
  the same way. `WontFit` (restart into the save's system) drops the continuation; a throwing swap ends
  the transaction with a failure.
- The `DeserializeSave` inside `ApplyLoaded` runs immediately (native order, before `OnGameLoaded`) with a
  worker join but no second reset, then restores the sidecar exactly once; the transaction ends when
  `ApplyLoaded` returns. A marker cleared at the next `BeforePhysics` keeps any later load from skipping
  its reset. Any other load started while a switch is queued ends the queued one explicitly.
- `SceneSaveCoordinator.Capture` keeps the previous scene's retained records if a save (e.g. Save Yourself)
  is captured while a load waits for the switch.

Native timing is kept deliberately: running the swap from Unscience's PrepareFrame handoff would move
`SwapSystem` and its nested `Loading` frames away from where KSA runs them. At native timing, draw data
built earlier in that frame is replaced by `Loading.OnFrame` before anything is submitted. `Program.OnFrame`
itself (transpiled by You Didn't See Anything) and the tiny `SaveSystemSwap.OnFrame` are not patched. The
same reset-before-swap also prevents Bloomin Onion's ring shadow bake from pointing at freed band textures
in a swapped-in system (render-fx review F2). Details: [saves](../scope/saves.md),
[lifecycle](saves-game-lifecycle.md#runtime-star-system-switching-ksa-5916-verified-against-5949).

## Decisions recorded, not changed

| Interaction | Evidence | Options |
|---|---|---|
| Pre-5949 saves where a DOH-spawned kitten that never entered the roster later boarded a vessel | NEW `Universe.ClearInvalidSeats` (`Universe.cs:344-411`, absent in OLD) runs in `CreateSystem` and `DeserializeSave` (`:340,:2617`) before any sidecar restore and empties seats whose kitten is not in the roster, already seated, or on EVA. DOH only backfills roster entries for kittens still on EVA | Accept (first 5949 load empties those seats; current DOH spawns enter the roster and are unaffected), or add a pre-load roster backfill for seated DOH kittens |
| Iron Man rocket-mode kitten incapacitated with Auto engaged (carried from 5901) | Unchanged by 5949 | Unchanged |

## Surfaces checked without another confirmed code break

- **Reflection and Harmony:** 1,033 production identifier literals and `___` injectors resolve in both
  trees; 147 `(type, member)` lookups (141 identical declarations; the rest are the Iron Man finding and
  helper artefacts checked by hand); 116 Harmony target bodies diffed (103 identical, none lost callers
  or became abstract; the 13 changed bodies are the findings above plus harmless log/debug-name or
  additive changes); all 14 transpiler anchors keep their OLD counts. The whole §4 watchlist is intact.
- **5939 incremental render data:** `Compose`/`ComposeDynamic`/`ComposeGlass` are textually identical
  and the batch fields unchanged (shared `PartRenderFilter`, capsule glass). Vehicle Paint's
  `WriteState`/`WriteDynamicState`/`EnsureBuilt` keep their signatures; appended parts go through the
  patched writers and removal compaction copies `StateBitFlags`, so paint bits 13–30 survive. Transform
  writers (Godzilla, Garry's Torch, Dont Stifle Me, Beverage Cooler lid) use public setters that the
  per-part refresh observes. No mod hooks the batched detach path.
- **Rendering:** no mod builds a pipeline against the pre-pass (now two colour attachments) or reads the
  main colour alpha; Graffiti borrows the native global layout (new binding 2 is opt-in GLSL), and its
  render region and include chain are textually identical. Thug Life, Sphinx and Free Fallin targets,
  shaders and IDs are unchanged (Free Fallin only reads `ModelNormal.frag`'s unchanged set defines).
  `MaterialData`, every `PerInstanceData`, `InstanceData`, `StaticObjectModel`, `PBRPushConstants` and the
  `Ubo*` structs are byte-identical; every mod-edited shader is byte-identical and all 17 anchors match.
- **Plumes and rings:** Pyro's structs, postfix/prefix targets and exhaust assets are unchanged; the ring
  reference tree and reflected fields (`_anyRings`, `_ringsRenderer`, `_material`, backing fields) are
  intact; `RebuildRenderer` is identical. 5941 fixes the shader-stage leak both ring mods trigger.
- **Vehicle/physics/crew:** no Harmony patch targets a removed or devirtualized rocket method; Iron Man's
  RCS transpiler and 5901 `InvalidateCache` seams are intact; `PhysicsBubble.DetectStructuralFailure`,
  `PrepareFrame` and `ExecuteNextVehicleSolvers` are identical; Bepu structs became `readonly`
  (binary-compatible). Wing Walker riders are seated natively and are not evicted by 5946.
- **UI/input:** `HotkeyGuard`, StarMap entry points, `MenuBarPatch`, the DrawUI gate counts, GaugeCanvas
  and CrewClock are unchanged; camera controllers, `GameViewport` and `ViewportRegistry` are identical.
- **Assets/schema:** Content IDs 5,545 → 5,599 (7 removed, all volumetric-trail compute shaders); no
  removed ID is referenced. Every production ID and mod XML reference resolves; the schema classes behind
  mod XML are byte-identical.
- **Terrain:** `Celestial.GetTerrainHeightFromDirCcf` and its callees are unchanged (5907 changed only GPU
  vertex fetch), so Sphinx, DOH placement, Graffiti, Dent Wizard and Hot Pursuit terrain reads hold.

## Save compatibility

Sidecar IDs, versions and payloads are unchanged. Iron Man writes the same anchor envelope again now
that its patches install. A Pebbles recipe the 5903 mesh cannot place is reported as a replay warning
and the body keeps stock clutter; the saved recipe is retained. Cross-system saves: see above. New
native save fields (destinations, visit log, `ArrivedAt`, `FirstVisits`, body knowledge) are native XML.

## Validation and native acceptance

Commands (repository root, no environment overrides for compilation):

```sh
dotnet build ksa-mod-experiments.slnx --no-incremental
# Execute every *.tests/bin/Debug/net10.0/<app>.dll (runtimeconfig-backed) and you-didnt-see-anything.patch-tests.
KSA_DLL_DIR=../ksa-game-assemblies_prev/current/dll dotnet ksa-upgrade.tests/bin/Debug/net10.0/<app>.dll   # negative control
python3 tools/check_runtime_bootstrap.py --entry-assembly unscience/bin/Debug/net10.0/MeowSci.Unscience.dll
UNSCIENCE_DIST_DIR=<scratch>/dist/ dotnet build ksa-mod-experiments.slnx   # CI-style output location only
python3 scripts/check-dist.py <scratch>/dist
```

Final results on 2026-11-25:

| Check | Result |
|---|---|
| Initial plain build against 5949 (adjacent `../ksa-game-assemblies/current/dll/`) | 1 error: `pebbles.lib/Runtime/ClutterResidency.cs` CS1503 (`IViewport` → `int`); then the pebbles.tests fixture |
| Clean `--no-incremental` whole-solution build after fixes | **PASS — 91 projects, 0 warnings, 0 errors** |
| Managed executables | **23/23 PASS**, no SKIP/WARN lines. New/updated: `Ksa5949Checks` and two `Ksa5901Checks` seams (ksa-upgrade); 9 `SystemSwapChecks` + 27 native-lifecycle checks (saves); `SnapToMeshChecks`, slot/VRAM residency and 5919 retirement shapes (pebbles); 4-param serializer fixture (iron-man) |
| PREVIOUS-DLL negative control | **PASS**: `KSA_DLL_DIR=<PREVIOUS>/dll` passes the 5901 checks and fails the first 5949 check |
| Production-code negative controls | **PASS**: saves without the 5916 seams lose the transaction; Pebbles without the VRAM release/order, with disabled types snapping or without rounding tolerance fails exactly the new checks |
| Wing Walker native XML harness | **NOT RUN to completion** (unchanged limitation): needs game-shipped `MemoryPack.Core 1.21.4` etc., absent from `current/dll/` |
| Runtime bootstrap / built entry module | **19/19 PASS**, initializer present |
| CI-style distribution build + validator | **PASS**: one Unscience mod, 34 explicit feature assemblies, cooler/chair asset budgets |
| `git diff --check` (+ new files) | **PASS** |
| Native game/editor/Vulkan acceptance | **NOT RUN**: no 5949 installation |

Why the first plain build was not enough: Iron Man's break is a string/overload lookup and the saves
break is runtime ordering — both compiled and passed their fixtures. They were found by the
reflection/Harmony sweep and the lifecycle diff, and are now pinned against the real DLL.

Native acceptance still required:

- **Iron Man:** the unscience window lists Iron Man as applied; anchors survive editor undo/redo, Copy
  Tree, save/reload, world and blueprint load, and loading a 5901 save.
- **System switching:** a Sol save while running Sol + Interstellar loads in place; Earth-and-Moon ↔ Sol
  loads restore feature state once and survive a re-save; no Vulkan errors or stale overlays with Hot
  Pursuit cameras, decals, statics, clutter, rings, sounds and welds active; modded shared ring templates
  do not show in the new system; an auto save in the frame before a switch keeps retained records; an
  uninstalled-system save names the system; a multiplayer join to another system resets first; a
  `WontFit` restart through StarMap (`-load <save>`) restores the sidecar; You Didn't See Anything (F2,
  Detached Only) during the runtime loading screen.
- **Pebbles:** collider-less replacements on Luna/Mars and Earth trees show the refusal and apply once
  colliders are added; Earth Grass applies; a disabled type on Luna applies (one native warning per
  object); VRAM drops after Apply and restored clutter reappears; switching systems with an override.
- **5939 render data:** Vehicle Paint, Blinky/Shiny pixels and capsule glass through editor attach,
  symmetric detach, undo/redo and dock/undock; Godzilla/weld scaling redraws every part.
- **Blinky:** pixel ignition/brightness and Diagnose output on A3 (gas-generator) grids.
- **BYO Music:** an imported file plays on a vessel and follows the SFX volume slider.
- **Physics/render (no code change):** Bepu friction fix with DOH placement and Pebbles colliders; Sphinx
  statics with SSAO and the split normal/roughness targets; Free Fallin full canopy; Graffiti native
  shader compile; 5927 editor centre-of-mass markers under F2 hiding.
- All earlier pending native acceptance (5901 and before) and Purr2D2 coexistence checks remain open.
