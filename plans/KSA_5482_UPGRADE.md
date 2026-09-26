# KSA 5482 upgrade — impact, remediation and acceptance

## Verdict and inputs

The update breaks compilation (one renamed scheduler masking 16 errors in 8 projects) and silently
disables several features that still compile. All compile breaks and every approved runtime finding
are fixed in the working tree (uncommitted). **Static and managed verification only: no live KSA pass
was run.**

| Input | Verified value |
|---|---|
| CURRENT | `../ksa-game-assemblies/current`, build **2026.9.22.5482**, metadata date 2026-09-24 |
| PREVIOUS | `../ksa-game-assemblies_prev/current`, build **2026.9.10.5438**, metadata date 2026-09-15 |
| Recorded baseline | **5438**, equal to PREVIOUS; no missing intermediate span |
| Authoritative sources | Both supplied `decomp` and `Content` trees, not the in-repo historical decomp |
| Build DLLs | Explicit `KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/`; this macOS checkout's default resolves the same tree |
| Native installation | None on this machine; the supplied Windows x64 `KSA.dll` cannot execute in the ARM64 test runner |
| Distribution | `UNSCIENCE_DIST_DIR=<scratchpad>/dist`, keeping checks out of live mod folders |
| Dependencies | .NET SDK 10.0.100; StarMap.API 0.3.6, Lib.Harmony 2.4.2, Tomlyn 0.19.0 unchanged. Only `KSA.dll` and `Planet.Render.Core.dll` differ; Brutal/Bepu assemblies are identical |

## Method

1. Changelog scan (43 entries) and full source/Content diff of both trees.
2. Whole-solution build against CURRENT as the alarm; the root error was fixed in a throwaway worktree
   to expose the masked errors before touching the working tree.
3. Eight read-only area audits (render data, shell/input/saves, vehicle physics, orbits/camera/rings,
   characters/materials/shaders, parts/editor/grids, Iron Man, plumes/clutter/statics/audio), each
   diffing every Harmony target (signature, body, IL anchors), string reflection, shader anchor, byte
   layout and asset ID it owns in both trees.
4. Compile fixes (pre-approved), then runtime fixes after explicit approval of each group.
5. New managed regressions for the new render-data seams; full rebuild and all managed suites.

## Verification results

**Final whole-solution build passed: 74 projects, 0 warnings, 0 errors** against CURRENT's DLLs
(serial, non-incremental, 1m15s). **All 15 managed test executables passed**: `garrys-torch.tests`,
`kitchen-sink.tests`, `pebbles.tests`, `godzilla.tests`, `byo-music.tests`, `pyro.tests`, `sphinx.tests`,
`iron-man.tests`, `iron-man-flight.tests`, `iron-man-mode.tests`, `saves.tests`, `world-saves.tests`,
`camera-saves.tests`, `ksa-upgrade.tests`, `dent-wizard.tests`.

The first build failed with a single CS0117 (`JobSystems.ConcurrentWorkers`) in `ksa-abstractions.lib`,
which every feature depends on. With only that rename applied in a throwaway worktree, 16 errors remained
in blinky, its-so-shiny, humble-arteest (×2), doh, dont-stifle-me, iron-man and pebbles; the shell,
sphinx, six hosts and `world-saves.tests` were blocked behind them and compiled clean once unblocked.
The reference path was verified with its trailing separator.

Build command (serial; the default parallel build stalls in this sandbox):

```sh
KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/ \
UNSCIENCE_DIST_DIR=<scratchpad>/dist DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
dotnet build ksa-mod-experiments.slnx --no-incremental -m:1 -nr:false -p:UseSharedCompilation=false
```

New managed coverage (`ksa-upgrade.tests`) links the production `PartRenderFilter`, `VehiclePaint` and
`VehiclePaintPatches` against a fixture `PartTreeRenderData` with private nested batches. It proves the
Harmony bindings (private nested parameters bound as `object`, `IList` field refs), range compaction
(order, dent alignment, other vehicles' entries, owners, fail-open) and paint invalidation. Updated
fixtures: `saves.tests` (bool write, caught read), `godzilla.tests` (5482 cull shape — it previously
mirrored 5438 and hid the break), `garrys-torch.tests`/`dent-wizard.tests` (orbit-reader join). No fixture
executes native KSA code, GPU rendering or Bepu.

## Changelog and complete source span

CURRENT logs **43 commits: 5439–5481** (fromRevision 5438 / toRevision 5482; no separate 5482 entry).
Source comparison covers both endpoints: **133 KSA C# files changed, 25 added, 1 removed**
(`BubbleStepJob.cs`); all decomp 150/35/6. Content: **33 changed, 3 added** (`Common/RingShadows.glsl`,
`GroundClutter/Common/ClutterSurfaceColor.glsl`, `VolumetricExhaust/ComposeWithClouds.glsl`), none removed.

| Revisions | Relevant change / suite exposure |
|---|---|
| 5439, 5462, 5463 | Apsis altitudes, orbit-hover tiebreak, non-vehicle targets saved; no consumed member changed |
| 5441, 5445, 5453 | Lazy save lists, **bool `Write()`**, in-place `SaveDirectory.TryReplace`, caught universe read — save hooks |
| 5442, 5466–5468, 5471 | Clear Debris, glint deregistration, contact-buffer pool, breakup bulk change, wake-on-depart / camera hand-off |
| 5443/5444, 5480 | Frame queue/present wait; **`ConcurrentWorkers` → `NearestOrbitAndPerformanceWorker`**, frame-start wait |
| 5446, 5454, 5458, 5470 | Explosions/trails, deep exhaust compositing, **atmosphere ring shadows** — pyro (native), ring mods |
| 5447, 5448, 5459, 5473, 5477 | **Ground clutter displacement**, per-save clutter state, tree lighting, AO — Pebbles/Sphinx |
| 5449 | **Input binding refactor** (mouse buttons) — HotkeyGuard, Iron Man input |
| 5450–5452, 5455, 5460, 5461, 5465, 5476, 5479 | Rotation solver, frame/body selection, **physics islands**, eviction on worker, terrain sampling — physics mods |
| 5456 | **Part render data rewrite** (`PartTreeRenderData`; per-module hooks removed) — blinky, its-so-shiny, humble-arteest, IVA, Iron Man, Godzilla |
| 5457 | Distant-sphere heightmap; **ring fields moved to `DistantSphereMaterialData`** — bloomin-onion |
| 5464 | **Lazy `PartTree` derived data**, `Part.Tree` nullable, Split/Merge signatures — doh, iron-man, dont-stifle-me, blinky |
| 5469, 5472 | Galactic plane frame fix; **BRDF roughness fix** in part/fur/planet shaders — anchors intact |
| 5474 | **Mesh buckets per render pass + view handle** — visor, thug-life, iron-man, free-fallin, hot-pursuit |
| 5475 | Seven CoreFairingA nosecones/adapters became fuel tanks — blinky fuel anchors, eternal-flame, godzilla (native) |
| 5478 | Refill/deplete flag tank contents changed — eternal-flame |
| 5481 | 64-bit gauge digit rollers — Iron Man gauges (compatible) |

## Compile breaks and fixes

All new in CURRENT. Paths: game code under each tree's `current/decomp`.

1. **Scheduler rename — root error.** `JobSystems.ConcurrentWorkers` → `NearestOrbitAndPerformanceWorker`
   (`KSA/JobSystems.cs:12`), same single-runner scheduler. `NativeSaveHooks.ResetAtJoinedBoundary` and the
   `saves.tests` fixture. The join is still required: PrepareFrame re-queues the job (`Program.cs:2188-2192`)
   before our `GetJobSimStep` handoff (`:2207`); native `DeserializeSave` joins only orbit/vehicle/cloth.
2. **`Part.Tree` nullable; the constructor no longer builds a tree** (`Part.cs:662`; OLD ctor `:1440` removed;
   new `CreateOwnTree()` `:1456`). doh's backpack builder would have thrown on **every spawn** — it now calls
   `CreateOwnTree()` like `EVADoor.GetBackPackPart`. Iron Man unload skips treeless roots (aborted
   deserialization); Iron Man RCS and dont-stifle-me null-guard defensively.
3. **Per-module render hooks removed** (`PartModelModule/PartModelDynamicModule/PartModelGlassModule.UpdateRenderData`).
   Render data is cached per tree in `PartTreeRenderData`: `EnsureBuilt` rewrites batches when dirty, then
   `Compose` (static raster path bulk-appends one slot-ordered range per model **without `AddInstance`**),
   `ComposeDynamic` and `ComposeGlass` (per-slot `AddInstance`). Instance lists are cleared in
   `PartModelRenderer.ClearFrameData` because shadow culling re-reads them after upload.
   - New shared **`ksa-abstractions.lib/PartRenderFilter`**: owners register a `Func<Part,bool>`; one
     prefix/postfix pair per compose method records each batch's list start and compacts hidden slots out of
     that range (instance and dent lists together). It filters only when exactly `Count` entries were
     appended and dent lists align, and disables itself (logged) on any exception. One shared patch set is
     required: independent compactions would invalidate each other's ranges. Blinky and its-so-shiny keep
     their predicates and public `Apply/Remove`. **Gap:** raytraced IVA submissions are not filtered.
   - **Vehicle Paint** now postfixes the private `WriteState`/`WriteDynamicState` — the only writers of the
     cached `StateBitFlags` — and invalidates a tree's states from an `EnsureBuilt` prefix when
     `VehiclePaint.RenderStateVersion` changes. The old static `AddInstance` prefix had silently gone dead as
     well. Paint now reaches raster and raytraced IVA; thumbnails stay unpainted. `RequiredPatchCount` 5 → 4.
4. **Visor**: `StaticMeshRenderable.Draw()` → `Draw(ViewHandle)` (rev 5474). The transpiler matches
   `ldfld VisorMesh; ldloc view; call Draw(ViewHandle)` once (`KittenRenderable.cs:356,370`); the old
   `Draw()` lookup would also have returned null at runtime.
5. **Pebbles clutter removals** (rev 5447): `List<ClutterRemoval>` and `ExcludeInstance(…, ClutterExclusionType)`.
   `DrainExclusions` mirrors `Universe.SyncGroundClutter` (`:2064-2101`) including displaced-object records.

## Approved runtime fixes (silent 5482 regressions)

1. **Failed saves wrote the sidecar.** `UncompressedSave.Write()` now returns `false` instead of throwing
   (`UncompressedSave.cs:108-139`, new `SaveDirectory.cs`); the old folder can survive, so the postfix paired
   this session's feature state with the old universe. The sidecar is written only on success; a new
   `WriteFailed` callback discards the capture and sets a failure status.
2. **Unreadable saves were not reported.** `Load()` now catches the universe read and returns before
   `DeserializeSave` (`:61-79`). A file load that never reaches `DeserializeSave` is reported as a load failure.
3. **Godzilla visual scale did not install.** The cull moved into `Vehicle.IsLargeEnoughToRender(Camera)`
   (`Vehicle.cs:3707-3711`); the `UpdateRenderData` transpiler found no radius read, threw, and rolled back all
   five visual-scale patches. It now redirects that one call to a scaled-radius mirror.
4. **IvaForceRender editor interiors vanished.** `Compose` applies the internal gate itself (`:1300`). The
   reveal is now a `Compose` prefix/finalizer that clears `Template.Internal` for the call only; the flag has
   no other readers than `Compose` and `PartModel.AddInstance` (main-thread render).
5. **Bloomin distant-sphere ring shadow wrote nothing.** Rev 5457 moved ring fields into private
   `DistantSphereRenderer._material : DistantSphereMaterialData`; a removed painted ring could leave the sphere
   sampling a pruned texture. Now a typed field-ref write.

## Approved fixes for pre-existing bugs root-caused by the audit

1. **Eternal Flame burn refills** (ISSUES): fuel was refilled from the UI tick after the vehicle worker had
   snapshotted module state; its commit overwrote the refill (same in 5438). Fuel and electricity now refill
   in the existing `ExecuteNextVehicleSolvers` prefix, the window KSA's own refill command uses; rev 5478 flags
   refilled tanks so dry engines relight.
2. **Teleport vs orbit-hover race** (candidate for Torch error spam): the nearest-orbit job runs across our
   handoff and `Teleport` disposes the orbit points it reads. New `PhysicsFrameHook.JoinOrbitReaders()` joins
   once per frame, automatically before world changes/queued edits and explicitly before weld and launch
   teleports; idle frames never wait.
3. **Blinky false "no pixel can light" warning**: lazy derived data meant resource managers did not exist at
   the post-build check; it now calls `EnsureDerived(DerivedData.ResourceGroups)` first (5438 timing).

## Pebbles follow-ups

All three approved Pebbles groups are implemented (details: [ground clutter](../scope/ground-clutter.md)).

- **Physics parity:** the private convex hull overrides `ColliderTemplate.VolumeCubicMetres` (port of
  stock `ComputeHullVolume`, points recentred on their bounds like stock), so rev 5447's volume-weighted
  compound mass/centre of mass work and the "no collider volume" warnings stop; `ClutterGraph` copies the
  new `ClutterObjectTemplate.AngularDamping`, the only new template member (trees settle).
- **Tree lighting:** the new `KeepBackfaceNormals` material flag is captured and applied. Recipes gain a
  nullable field; absent values (older recipes, GLB imports) take the matching stock material's value.
  The existing `pebbles` record keeps its ID/version.
- **Displaced/saved clutter:** per body and grid (ecotype + exact separation) Pebbles remembers exclusion
  masks (merged with AND) and displaced-object records (snapshotted before statics are cleared, so objects
  in flight keep velocity) and replays them into matching grids across apply/restore. The private
  `_exclusionCache` reflection is gone. A new `GroundClutterRenderer.SerializeSave` postfix rewrites native
  entries for types applied at another spacing (or disabled) with the remembered stock-grid state — or
  clears them if that fails — so native saves no longer punch holes into stock clutter. Override-spacing
  grids go into a new sidecar record **`pebbles.clutter-state` (v1, order 61)**, restored after recipes;
  native state wins, missing records (vanilla/older saves) mean recipes + stock state only.
- Managed checks: hull volume on known shapes, legacy recipe defaults, grid-memory rules (merge, replace
  vs keep, spacing A→B→A, export/seed) and the new record through the production save path.
- **Not done:** migrating 5438 Earth-tree recipes (rev 5473 changed tree LOD ids, so those recipes fail
  with "changed since capture" as a save warning). Extra VRAM from the 2048-slot displaced tail per type
  is not counted in Pebbles' budgets.

## Every feature / shared area reviewed

| Feature / shared surface | Result |
|---|---|
| Unscience shell, StarMap hooks, menu bar | No own errors; `OnDrawUiFrame`/`OnFrame`/hidden-UI hooks and PrepareFrame's seven seams unchanged |
| HotkeyGuard | `GameSettings.OnKeyAll` intact for all keyboard input; user mouse-button bindings bypass it (not changed) |
| Saves | Rename, write result, load failure fixed; native clutter state now also saved by KSA (see Pebbles) |
| PartRenderFilter / blinky / its-so-shiny | New shared filter; blinky feed check forced; 5475 tank nosecones may become fuel anchors (live) |
| humble-arteest | Paint seams moved; visor fixed; Engine Emissive (dynamic sink) and Kitten Color unchanged |
| IvaForceRender / kitchen-sink | Editor reveal moved to `Compose`; G-load detector byte-identical; editor refresh now lazy (next frame) |
| doh | Backpack tree fixed; material bridge identical; per-view bucket capacity to check with mass spawns |
| Iron Man | Null-safe; `ClearBuckets(IViewport)` and render hooks compatible; late submissions are now dropped, so its early upload is required |
| Godzilla | Visual cull fixed; lazy derived data flushes before solvers |
| Eternal Flame | Refill timing fixed |
| Garry's Torch, Dent Wizard | Orbit-reader join; island stepping keeps one worker per simulation |
| i-feel-seen, kiwis-marbles, zippo, glass, camera-controller-override, hot-pursuit | Compatible; hot-pursuit leases covered by the 8 pre-registered views |
| thug-life | `RenderMainPass(IViewport, CommandBuffer)` compatible |
| free-fallin, graffiti, kitten-animations | Compatible; BRDF fix changes appearance only |
| rocky-mcrock-face, bloomin-onion | Distant-sphere sync fixed; atmosphere ring shadows follow ring edits live |
| parts-now, dont-stifle-me | Compatible (`Deregister` exists now, not adopted); null guard |
| pyro, sphinx, byo-music | Unchanged; sphinx compiles clean behind Pebbles |

## Known-broken baseline reconciliation

| Historical item | Status at 5482 |
|---|---|
| Camera `___Transform`, Zippo `Color` | Already retired; still retired |
| Vehicle Paint | 5438 overload fix superseded: seams moved to cached state writers |
| Missing shell `IvaForceRender.Patch` | Wired; mechanism replaced for 5482 |
| Eternal Flame fuel during burns | Root-caused and fixed; pending live reproduction |
| Torch error spam | Race closed; rev 5450 rotation-solver fix may also help; pending live logs |
| Pyro `_hasRefractionInstances` never set | Standing native defect, unchanged |
| Sphinx inlined contact-filter risk; statics removed under resting vessels | Unchanged; native acceptance |

## Not changed (recommendations, not approved/required)

- HotkeyGuard: optional prefix on `Program.DispatchKeyEvent` to block user mouse-button bindings while typing.
- Iron Man unload: call `EnsureDerived(DerivedData.All)` to keep rebuild failures inside its handler; fixture
  shapes (`PartTree? Tree`, frame-start instance clearing).
- Godzilla: `Vehicle.TakeOffRails()` after scaling a resting vessel (5465 skips terrain updates for sleepers).
- parts-now: adopting `SerializedCollection.Deregister` would reorder the editor browser; not adopted.

## Native acceptance still required

1. Load the built Unscience package; confirm every patch group logs applied (PartRenderFilter, VehiclePaint 4/4,
   visor, IvaForceRender, Godzilla visual scale), F11 toolbox and typing protection.
2. Blinky/its-so-shiny hiding and toggling in flight, editor and portrait views, both mods together; raytraced IVA gap.
3. Paint on static/dynamic/dented parts incl. live changes, save/load and raytraced IVA; Engine Emissive; visor.
4. Editor interiors with IvaForceRender; no duplicates in IVA.
5. Save over a locked folder (failure status, no sidecar in the old save); load a corrupt `universe.xml`.
6. Eternal Flame burn-to-dry and relight under warp; Torch welds and Dent Wizard shots while hovering orbit lines.
7. Godzilla visual-only/collider-only modes, culling, kittens; many-kitten doh spawn vs the per-view bucket cap.
8. Bloomin/Rocky ring edits with distant spheres and atmosphere shadows.
9. Pebbles: knock rocks/trees loose then apply/restore/apply; save/load at same and changed spacing (no new
   stock holes, override removals persist); vanilla load; tree lighting and settling.
10. The standing 5438 matrix (plumes, canopies, decals, secondary cameras, Iron Man, audio, saves).

Do not interpret a green build or managed fixture result as completion of this list.
