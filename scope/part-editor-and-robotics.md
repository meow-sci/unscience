# Part Editor & Robotics — Game Integration Scope

## KSA 5707 verification (5638 → 5707, 2026-10-29)

Verified against `2026.10.25.5707` (NEW, revs 5639–5703) vs `2026.10.19.5638` (OLD) using both supplied
decomp/Content trees and the changelog. Static/managed only: the whole solution builds with 0 warnings
and 0 errors and every managed suite passes. No native KSA run was possible. No code change was needed
in this area (Iron Man's 5707 constructor and copy fixes are in [iron-man.md](iron-man.md)).

- **Don't Stifle Me targets are unchanged.** Each by-name target still has exactly one declaration,
  the same parameter names and an identical body:
  - `UpdateSelectedScale :4116` (call site `:3947`), `ScaleBoundsFor :4150`, `ForEachPartWithSymmetry :4159`,
    `QuantizeScale :4179`, `UpdateScaleGizmo :3896`, `DrawParachuteSection :2021`,
    `ForEachModuleWithSymmetry :1473`;
  - consts `:853/:855` (still 0.5 / 2.0); fields `Selected :584`, `ScaleGizmo :606`,
    `HighlightedGizmoSegmentIndex :612`, `GizmoGrabbed :614`, `CursorPositionScreen(LastFrame) :722/:724`;
  - the `ScaleGizmo` constructor (`:1281`) is identical, so the segment → axis mapping holds.

  `GenericGizmo`, `Parachute`, `ChuteTuning`, `ScaleFactors`, `PartTemplate`, `Camera` and `IViewport` are
  byte-identical. `Part.RefreshScale :1778` / `RefreshScaleAndReposition :1798` changed only for the
  `Connector[]` iteration; `PartTree.RefreshStaticMass :1142`; `Program.DrawProgramMenusHook :4271`
  (called at `:4258`); `DrawTransformMenu :6925` still writes per-axis `part.Scale` (`:6555`).
  `ScaleBoundsFor` still has two callers and `QuantizeScale` one.
- **Symmetry is now ghost previews while held, with real copies built at drop (rev 5643).** This is a
  semantic change that needs no code change.
  - While a part is held, its symmetry copies are render-only `GhostTransform`s. They re-compose the
    grabbed part's own tree at each transform (`VehicleEditor.cs:52-68,4335-4347`) and are not `Part`s.
    `Part.GrabPreview` and the old preview copies are gone.
  - On drop, `CreateRadialCopies` (`:4604`, called at `:4548`) builds the copies with
    `PartCloner.BeginBatch(...).CloneInto` (`PartCloner.cs:30-37`). OLD used
    `PartTree.Deserialize(GetReferenceWithChildren)` (OLD `:4701`).
  - The new `Part(Part source, Part? parent)` constructor (`Part.cs:1556-1563`) copies the full 3-axis
    `source.Scale`, so per-axis scales carry over to the copies. Module save data, such as a parachute's
    diameter, is copied through `GetSaveData` / `ApplySaveData` as before.
  - Clones join the tree through `PartTree.AdoptNewSubtree` (`PartTree.cs:1678`). `SymmetryLink` is still
    assigned (`VehicleEditor.cs:4775,5002`), so a per-axis drag of a placed part still reaches its
    siblings through the unchanged `ForEachPartWithSymmetry`.
- **Bounds update on gizmo release (rev 5643).** The stock release handler, which the mod does not patch,
  now calls `FullPart.UpdateBounds()` for every symmetry sub-part before the existing tank and derived-data
  refresh (`:3636-3650`; absent in OLD `:3548-3570`). A rotation reset now does the same (`:3496-3499`).
  Per-axis drags of sub-parts get the fix for free.
- **`EditorSnapMath` (new file).** Grab seating (`SeatFaceSnap` / `SeatStack` / `SeatFromSurface`) takes a
  `double3 Scale` and builds its matrix with `double4x4.CreateScale(scale)` (`EditorSnapMath.cs:16-18,32,42,118`),
  so a non-uniform scale feeds the snap math. No mod patches it.
- The other rev 5643 editor changes (staging, delta-V and fuel-flow recompute deferred until commit;
  faster drop/detach) do not touch the scale path. Quick-checklist items 1–4 still hold
  (`Universe.ExecuteNextVehicleSolvers :2149`, single overload).
- **Live checks pending (native).**
  - Drop a per-axis-scaled part under radial symmetry. Check the ghost shape, that the copies keep
    their non-uniform scale, and that later per-axis drags propagate to them.
  - Check face, stack and surface snap seating of a non-uniformly scaled part.
  - Carried over: the 2–1000 m parachute slider on symmetry copies; picking a heavily non-uniformly
    scaled part; Godzilla-scaled re-imported parts (collisions and hoses); capsule window alignment
    (`CoreCommandAAssets.xml` is byte-identical in 5707).

## KSA 5638 (5482 → 5638) verification

Verified against `2026.10.19.5638` (NEW) vs `2026.9.22.5482` (OLD) using both supplied decomp/Content
trees and the 5541/5554/5638 changelogs (revs 5483–5638). Managed/static only: the full solution
builds with 0 warnings and 0 errors (79 projects) and every managed suite passes. No native KSA run
was possible.

- **parts-now removed (user decision, not a compile break).** `parts-now/` and `parts-now.lib/` were
  deleted together with the solution entries, the `unscience` reference/registration and the
  `ksa-upgrade.tests` V8 checks. Its integration points are retired; see the stub section below.
  Legacy scene saves still load: their `parts-now` sidecar record has no owner, so the coordinator
  reports it, retains it and carries it forward unchanged (`saves.tests` `RetiredFeatureTests`).
- **Stock part re-import (rev 5542: "Added convex hull collider support to the GLB importer
  utility. Imported new versions of CoreCommandA, CoreStructuralA, CoreFairingA").**
  - Each `Core/Core{Command,Structural,Fairing}AAssets.xml:5` gains
    `<MeshAtlas Path="Meshes/…_CollisionAtlas.glb" HostOnly="true" />`. `MeshAtlasFileReference.DoLoad`
    registers each glTF mesh by name and loads a HostOnly atlas without a device upload
    (`meshReference.Load(gltfLoader, i, !HostOnly)`, `MeshAtlasFileReference.cs:47`), so the
    `_ColMesh_…` meshes carry `HostPrimitives` but no interleaved device mesh.
  - The authored `<Box>` collider lists were replaced by `<ConvexHull>` colliders naming those meshes
    (e.g. `_ColMesh_CoreCommandA_Prefab_MediumCapsuleVariantA_1`, `CoreCommandAAssets.xml:349-352`;
    one in CoreCommandA, one in CoreStructuralA, four in CoreFairingA). `ColliderTemplate.Create` now
    calls `Resolve()` first (`ColliderTemplate.cs:66-68`), binding `MeshColliderTemplate.Mesh`
    (`MeshColliderTemplate.cs:35`). `ConvexHullColliderTemplate.CreateShapeInto` builds a uniformly
    scaled copy of the base hull (`ConvexHullColliderTemplate.cs:102-107`); plain
    `MeshColliderTemplate.CreateShapeInto` still throws `NotSupportedException`.
  - **Godzilla collider scaling — live check.** `godzilla.lib/ColliderScaleState.cs:58` calls
    `ColliderModule.SetScale(in ScaleFactors)` (`ColliderModule.cs:48`), which swaps in
    `_template.CreateOwnedShape(scale.Scale)` (`ColliderTemplate.cs:55`). That path supports convex
    hulls, so the code is compatible, but collisions and landing of Godzilla-scaled capsules,
    decouplers and nosecones need a native pass.
  - **Capsule window alignment — live check.** kitchen-sink `CapsuleGlassExperiment` matches
    `CoreCommandA_Subpart_MediumCapsuleWindowA_Model` / `…WindowB_Model` exactly. Both ids still exist
    (`CoreCommandAAssets.xml:56,68`), but the re-imported capsule geometry needs an in-game check that
    the see-through windows still line up.
- **Fuel hoses in `PartTree.UpdateRenderData` (`PartTree.cs:1161`, signature unchanged).** It now
  calls `FuelLinks.UpdateConnectorPoses()` (`:1165`; `FuelLinkList.cs:923-941`) before
  `RenderData.EnsureBuilt`, re-aiming each fuel-port connector at its hose route on every render frame.
  Godzilla's prefix/finalizer on this method (`godzilla.lib/VisualScalePatches.cs:28-29,125-143`)
  rewrites only `matrixAsmb2Ego`, and the connector aim is computed in assembly space. **Live check:**
  hose rendering and port aim on a Godzilla-scaled vessel.
- **`DeviceMeshInterleaved` raytracing gate.** `Shared.Build` / `Rebuild` now test
  `Program.Instance.IsRaytracingActive` (`DeviceMeshInterleaved.cs:47,73`; set once at
  `Program.cs:869` as `IVARayTracing && RaytracingPipelinesSupported`) instead of
  `GameSettings.Current.Graphics.IVARayTracing`. After the parts-now removal no unscience code touches
  the shared interleaved buffer.
- **New `Interstellar` editor tag** (`Core/CoreEditorTagsGameData.xml:19`, `RootPartWhitelist` +
  `FaceSnapTargetWhitelist`). It is used by placeholder interstellar parts such as the magnetic trap
  `CoreFuelTankA_Prefab_MagneticTrapA` (`Core/CoreFuelTankAGameData.xml:764-785`). No mod in this area
  filters editor tags any more (parts-now's V7 is retired).
- **`PlumbedCore` (rev 5487).** Tank-fed propellant draw moved from `Combustor` to the new abstract
  `PlumbedCore : RocketCore` (`PlumbedCore.cs:7`: `Mix`/`DesiredMix`, `ResourceManager`, `FeedTanks`,
  `PlumbingCapability`, `ComputePropellantAvailable`). It is the base of `Combustor` (`Combustor.cs:3`),
  `ThermalCore` (rev 5488) and `AntimatterRamCore` (rev 5523). `is Combustor` still compiles. Blinky's
  feed checks now test `PlumbedCore`, and its fuel anchors skip tanks that cannot hold the pixel
  engine's propellant, such as magnetic traps (`Tank.HoldableSubsetOf`). See
  [pixel-grids-and-render.md](pixel-grids-and-render.md) rows 19, 23 and 24a.
- **Don't Stifle Me is clean.** Each by-name target still has exactly one declaration. Every body is
  identical or differs only by decompiler reformatting (inlined locals, folded `if (1 == 0)`):
  - `UpdateSelectedScale :4016`, `ScaleBoundsFor :4050`, `ForEachPartWithSymmetry :4059`,
    `QuantizeScale :4079`, `UpdateScaleGizmo :3796`, `DrawParachuteSection :1986`;
  - consts `:807/:809`; fields `Selected :555`, `ScaleGizmo :577`, `HighlightedGizmoSegmentIndex :583`,
    `GizmoGrabbed :585`, `CursorPositionScreen(LastFrame) :685/:687`.

  `GenericGizmo.cs` changed only by reformatting (`GetSegmentDataByViewport :275`); rev 5529's edge-on
  rotation-ring fix does not touch the scale gizmo. `Parachute.SetDiameter :406` is byte-identical and
  `ChuteTuning.cs` is unchanged. `Part.RefreshScale :1689` / `RefreshScaleAndReposition :1708` are
  reformat-only; `PartTree.RefreshStaticMass :1022` (→ `RecomputeStaticMass :1027`);
  `Program.DrawProgramMenusHook :4182` (called at `:4169`). The table rows below keep their @5482
  citations.
- **Live checks pending (native).** Godzilla-scaled re-imported parts (collisions and hoses); capsule
  window alignment; blinky on a vessel carrying a magnetic trap; and, carried over, Don't Stifle Me
  per-axis drag with symmetry siblings and the 2–1000 m parachute slider.

## KSA 5482 (5438 → 5482) verification (historical)

Verified against `2026.9.22.5482` (NEW) vs `2026.9.10.5438` (OLD) using both supplied decomp/Content
trees. Managed/static only: the full solution build passes, and `ksa-upgrade.tests` passes. No
native KSA run was possible.
Evidence: [KSA_5482_UPGRADE](../plans/KSA_5482_UPGRADE.md).

- **Don't Stifle Me: `Part.Tree` is now nullable (compile break, fixed).**
  - Revs 5456 and 5464 changed `Part.Tree` to `PartTree?` (`Part.cs:662`). They also removed the eager
    single-part tree that the `Part` constructor used to build (5438 `Part.cs:1440`); the replacement is
    `Part.CreateOwnTree()` (`:1456`).
  - The fix is `selected.Tree?.RefreshStaticMass()` at `dont-stifle-me.lib/PerAxisScaleDrag.cs:75`.
    Without it the build failed with CS8602, because warnings are treated as errors.
  - Behavior is unchanged. Every editor path that creates a selectable part gives it a tree immediately
    (`VehicleEditor.cs:2945-2946,6085-6086`; symmetry parts go through `AddChild`), and stock
    `UpdateSelectedScale` still dereferences the tree unguarded (`:4005`).
  - `PartTree.RefreshStaticMass()` is still public and still recomputes immediately (`PartTree.cs:1019`
    → private `RecomputeStaticMass :1024`). It does not clear the new lazy static-mass dirty bit, which is
    harmless: a later read recomputes it.
- **Don't Stifle Me targets are unchanged.** Each by-name target still has exactly one declaration, the
  same parameter names and a byte-identical body:
  - `UpdateSelectedScale :3973`, `ScaleBoundsFor :4009`, `ForEachPartWithSymmetry :4014`, `QuantizeScale :4039`;
  - `UpdateScaleGizmo :3746`, `DrawParachuteSection :1933`;
  - consts `:803/:805`; fields `Selected :551`, `ScaleGizmo :573`, `GizmoGrabbed :581`.

  `GenericGizmo.cs` and `ChuteTuning.cs` are byte-identical. `Parachute.SetDiameter` (`:395`) has an
  identical body. `Program.DrawProgramMenusHook` is at `:3961`, called from `:3948`.
- **Picking drift.** `Part.RayCastEgo` now rejects a ray early if it misses a bounding sphere
  (`Part.cs:2544-2549`). `RefreshScaleAndReposition` (`:1695`) now calls `UpdateBounds()`, so the bounds
  follow per-axis drags.
- *(The rev-5466 `SerializedCollection<T>.Deregister(T)` finding concerned parts-now only and was
  retired with it @5638.)*
- **Content, rev 5475.** Seven `CoreFairingA` parts are now fuel tanks: `InterstageBridge2W1WB`,
  `InterstageBridge3W2WB` and `NoseconeC/E/F/G/H`.
  - Each gained a `<Tank>`: either `CylindricalTank` or the new `<ConicalTank>` (`ConicalTankTemplate`).
  - Each gained a `BulkFluid` connector.
  - The two InterstageBridges lost their `<Decoupler>`.
  - Their editor tags are now "Fuel Tanks".

  No mod in this area references these parts (blinky's fuel-anchor handling of them is in
  [pixel-grids-and-render.md](pixel-grids-and-render.md)).
- **Live checks pending (native).**
  - Don't Stifle Me:
    - per-axis drag with symmetry siblings, confirming the mass refresh;
    - picking and selecting a heavily non-uniformly scaled part;
    - the 2–1000 m parachute slider.

## Verification — 5402 → 5438 (historical)

Don't Stifle Me scale/diameter targets retain their bodies; native scale/parachute editing remains an acceptance item. (This pass's Parts Now findings — V8 rejection of top-level `<Explosion>` / `<ExplosionVolume>`, registry/headroom/ordering checks and thumbnail descriptor bindings — were retired with parts-now @5638.)

Verified against `2026.9.10.5438` using both supplied source/Content trees.
See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md).
Older catalog tables below retain their explicitly cited build/line numbers unless marked @5482.

Permanent reference for how the **dont-stifle-me** (editor scale un-limiter) mod binds to the Kitten
Space Agency (KSA) game, so that future game updates that break it can be detected and root-caused
quickly. The retired **parts-now** and **flexo** surfaces are recorded as stubs below.

> With flexo gone this area has **no robotics implementation** — the title is kept for the game
> surface it maps. Historical findings live in git history and the dated upgrade plans.

- **Current verification:** `2026.10.19.5638` vs `2026.9.22.5482` (top section); 5482 vs 5438 below it. **Catalog baseline:** `2026.9.7.5402` (NEW), diffed from `2026.8.22.5348` (OLD). See
  [`FULL_SCOPE.md`](FULL_SCOPE.md) for the version block. Revisions 5349–5400 are **unlogged** in any
  changelog (only rev 5401 "Fixed crash for incorrect data stride for thumbnail rendering" is logged),
  so the decomp diff is the only evidence for this span.
- **Build status against 5638:** whole solution 79 projects, 0 warnings, 0 errors (parts-now removed).
  (5482: 74 projects after the dont-stifle-me `Part.Tree?` fix; 5402: 52/52 after the `Viewport` →
  `IViewport` fixes.)
- **Decomp (source of truth):** `~/repos/meow-sci/ksa-game-assemblies/current/decomp` (NEW, 5402) and
  `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp` (OLD, 5348); Content under the sibling
  `…/current/Content` folders.
- **Older sections below keep their original version pairs** (4680↔4750, 5018↔5117, 5117↔5261,
  5261↔5348) — each heading states its own span, so genuine regressions can be told apart from older
  drift. All decomp line numbers in the live table below were refreshed against **5402** (rows marked
  @5482 against 5482; the 5638 line map is in the top section).

Legend for *In NEW?*: ✅ present & signature-compatible · ⚠️ present but changed · ❌ removed/renamed.

---

## iron-man

The new EVA editor feature is mapped in [iron-man.md](iron-man.md), including instance connectors,
save-index reconstruction, body/root guards, character rendering and per-kitten flight activation.
The existing `KittenEva` object enters/exits the stock editor; no template XML is modified.

## dont-stifle-me

### Purpose
Undoes editor restrictions. The original feature targets two restrictions introduced in
`2026.8.22.5348`: the **0.5x–2.0x** top-level part
scale clamp (`VehicleEditor.MINIMUM_SCALE` / `MAXIMUM_SCALE`, surfaced through `ScaleBoundsFor`) and
**uniform-only** scale-gizmo drags (`UpdateSelectedScale` writes `new double3(s, s, s)`), and can
bypass the 0.25 m diameter **snapping** (`QuantizeScale`). Runtime toggles in `EditorScaleSettings`
(`Enabled`, `Snap`); patches are installed once and gate themselves per call. The extensible
`EditorLimitSettings.JplSaidNoClamps` toggle additionally changes the parachute editor diameter range
from each chute's authored bounds (20–50 m in current stock content) to **2–1000 m**.

### Unscience integration
- `DontStifleMeSubmod : ISubmod` (`dont-stifle-me.lib/DontStifleMeSubmod.cs`).
- `dont-stifle-me/Patcher.cs` applies `HotkeyGuard.Patch`, `EditorScalePatches.Apply(_harmony)` and
  `EditorValueLimitPatches.Apply(_harmony)`, then `MenuBarPatch.Apply(_harmony)`;
  `unscience/Patcher.cs` applies both patch groups under isolated `TryApply` / `TryRemove` calls (the
  supermod has its own menu entry).

### UI / hotkeys
- Standalone: **"Don't Stifle Me"** top-level menu (`Enabled`, `Snap scaling`, and **jpl said no
  clamps** checkboxes) drawn
  from a postfix on `Program.DrawProgramMenusHook` — the same hook unscience's `MenuBarPatch` uses.
  No window, no hotkey.

### Persistence
- None (in-memory toggles; scale controls default on, **jpl said no clamps** defaults off).

### Integration points

All four patch targets and both helper delegates are resolved by **name string** via
`AccessTools.Method` (`EditorScalePatches.cs:16-20`), so a rename fails at `Apply()` (logged +
red notice in the UI), not at compile time. Every one is also listed in
[`game-integration-surface.md`](game-integration-surface.md) §4.

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | Harmony (postfix, by-name) | `EditorScalePatches.cs:56,89` | `VehicleEditor.ScaleBoundsFor(Part)` **private static `(double Min, double Max)`** | `KSA/VehicleEditor.cs:4009` @5482 | ✅ | none (body identical 5402→5482; **single overload**) | Postfix rewrites `__result` to `(1e-6, +inf)`. Return type is `ValueTuple<double,double>` — a change to a struct/record breaks the `ref __result` binding at patch time. Consumers: `UpdateSelectedScale`, `QuantizeScale`. |
| 2 | Harmony (prefix, by-name) | `EditorScalePatches.cs:58,124` | `VehicleEditor.UpdateSelectedScale(ref readonly double4x4 matrixVehicleAsmb2Ego, IViewport inViewport)` **private void** | `KSA/VehicleEditor.cs:3973` @5482 | ✅ | ⚠️ **signature @5402** — `Viewport` → `IViewport` (param names unchanged; body line-identical to 5348; **single overload**, so the by-name `AccessTools.Method` cannot hit `AmbiguousMatchException`). Compile break fixed in `EditorScalePatches.cs:124` / `PerAxisScaleDrag.cs:28`. | Prefix param `ref double4x4 matrixVehicleAsmb2Ego` binds to the `ref readonly` original **by name** — parameter rename = patch failure. Returns `false` to skip stock. |
| 3 | Harmony (postfix, by-name) | `EditorScalePatches.cs:60,113` | `VehicleEditor.UpdateScaleGizmo(ref readonly double4x4, doubleQuat, IViewport, double)` **public void** | `KSA/VehicleEditor.cs:3746` @5482 | ✅ | ⚠️ signature @5402 (`Viewport` → `IViewport`; body identical; single overload) | Only `__instance` injected; reads `GizmoGrabbed` to end the per-axis drag session. Single overload assumed. |
| 4 | Harmony (prefix, by-name) **+** delegate | `EditorScalePatches.cs:48,52,62,103` | `VehicleEditor.QuantizeScale(Part, double rawScale) : private static double` | `KSA/VehicleEditor.cs:4039` @5482 | ✅ | none (body identical through 5482; single overload; still `0.25 / FindLargestDiameter()`) | Prefix: when `Snap` is off, `__result = Clamp(rawScale, ScaleBoundsFor(part))`, return false — param `rawScale` bound **by name**. Delegate (`MethodDelegate<Func<Part,double,double>>`) is what the per-axis drag calls, so it sees the prefix too. Snap-on path keeps the 0.25 m step (`SCALE_DIAMETER_INCREMENT_M` / `PartTemplate.FindLargestDiameter`). |
| 4b | Reflection (private static → delegate) | `EditorScalePatches.cs:53` | `VehicleEditor.ScaleBoundsFor(Part)` (same target as #1) | `KSA/VehicleEditor.cs:4009` @5482 | ✅ | none | `MethodDelegate<Func<Part,(double,double)>>` used by the snap-off prefix to clamp; goes through the patched body, so it returns the widened bounds. |
| 5 | Reflection (private static → delegate) | `EditorScalePatches.cs:50,54` | `VehicleEditor.ForEachPartWithSymmetry(Part, Action<Part>)` | `KSA/VehicleEditor.cs:4014` @5482 | ✅ | none (body identical through 5482; single overload — the new generic `ForEachModuleWithSymmetry<TModule>` at `:1363` is a different name) | `AccessTools.MethodDelegate<Action<Part,Action<Part>>>`; propagates the axis scale to symmetry siblings exactly like stock. |
| 6 | Typed API (editor state) | `PerAxisScaleDrag.cs:32-43` | `VehicleEditor.{Selected, HighlightedGizmoSegmentIndex, ScaleGizmo, CursorPositionScreen, CursorPositionScreenLastFrame, GizmoGrabbed}` (public fields) | `KSA/VehicleEditor.cs:551,579,573,681,683,581` | ✅ | none | Segment index → axis `0=X,1=Y,2=Z` is an **invariant of `ScaleGizmo`'s 3-segment construction** (`:1179`), not checkable by grep. |
| 7 | Typed API (math) | `PerAxisScaleDrag.cs:36-49` | `IViewport.GetCamera()`, `Camera.ScreenToEgoNearPlane(double2)`, `GenericGizmo.GetSegmentDataByViewport(IViewport)` / `PerSegmentData.Body2Cce`, `Part.PositionEgo(in double4x4)` | `KSA/IViewport.cs:51`, `KSA/Camera.cs:684`, `KSA/GenericGizmo.cs:277,176`, `KSA/Part.cs:1155` | ✅ | ⚠️ @5402: `KSA.Viewport` deleted → `IViewport`; `GetSegmentDataByViewport` now keys its lookup by `ViewportId` (`GenericGizmo.cs:206,279,298` use `inViewport.Id`) — transparent to the caller | Mirrors stock `UpdateSelectedScale` math line-for-line; **semantic drift** in the stock routine (e.g. a different depth heuristic) would leave per-axis drags feeling different from uniform ones without any symbol change. |
| 8 | Typed API (apply) | `PerAxisScaleDrag.cs:69-75` | `Part.Scale { get; set; }` (`double3`), `Part.RefreshScaleAndReposition()`, `Part.Tree` (**`PartTree?` @5482**), `PartTree.RefreshStaticMass()` | `KSA/Part.cs:819,1695,662`, `KSA/PartTree.cs:1019` @5482 | ✅ | ⚠️ @5482 `Part.Tree` nullable (rev 5456/5464; ctor no longer builds a tree) → call is `selected.Tree?.RefreshStaticMass()` (`:75`); `RefreshScale :1674` body untouched; `RefreshScaleAndReposition` now also refreshes bounds | 🔶 **Standing limitation:** `Part.RefreshScale` collapses `double3` to `new ScaleFactors(max axis)` for connectors, `IRescale` modules and mass. Non-uniform parts keep a non-uniform *mesh* but uniform connector offsets. If the game ever re-derives `Part.Scale` from `ScaleFactors` (uniformizes on load/refresh), per-axis scaling silently stops sticking. |
| 8a | Harmony (prefix, by-name) | `EditorValueLimitPatches.cs:29-35,73-83` | `VehicleEditor.DrawParachuteSection(Part, ReadOnlySpan<Parachute>) : private void` | `KSA/VehicleEditor.cs:1933` @5482 | ✅ | new dont-stifle-me consumer @5402; body identical @5482 | Before the stock diameter slider reads `parachute.Tuning.MinDiameterM` / `MaxDiameterM` (`:1977-1985`), expands every chute in the selected subtree to 2 / 1000 while the toggle is on. The prefix intentionally binds only `part`, avoiding a Harmony patch parameter for the byref-like `ReadOnlySpan`. |
| 8b | Harmony (prefix, typed signature) | `EditorValueLimitPatches.cs:31-37,85-92` | `Parachute.SetDiameter(float diameterM) : public void` | `KSA/Parachute.cs:395` @5482 | ✅ | new dont-stifle-me consumer @5402; body identical @5482 (Parachute only gained `DrawsUi`/`DrawCanopy(ViewHandle,…)`) | Expands all chute modules on the receiving part before the stock method calls `Tuning.ClampDiameter`; required because `VehicleEditor.ForEachModuleWithSymmetry` invokes `SetDiameter` on counterparts whose slider was not drawn. Original per-instance min/max pairs are tracked and restored on toggle-off/unload. |
| 8c | Typed API (runtime bounds) | `EditorValueLimitPatches.cs:63-70,94-106` | `Parachute.Tuning : ChuteTuning`; `ChuteTuning.{MinDiameterM, MaxDiameterM, DiameterM, ClampDiameter(float)}`; `Part.SubtreeModules` / `Part.Modules` | `KSA/Parachute.cs:140,369-378`; `KSA/ChuteTuning.cs:5,33-35,61`; `KSA/Part.cs` | ✅ | new dont-stifle-me consumer @5402 | Only the bounds are changed; stock `SetDiameter`, symmetry propagation, inert-mass refresh, save data, drag physics, cloth and rendering continue to consume `Tuning.DiameterM`. Disabling does not retroactively clamp a value already chosen, matching the scale feature's non-destructive toggle behavior. |

| 9 | Harmony (postfix, `nameof`) — standalone only | `dont-stifle-me/MenuBarPatch.cs:15,20` | `Program.DrawProgramMenusHook() : public void` (empty hook called inside the main menu bar) | `KSA/Program.cs:3961` (call site `:3948`) @5482 | ✅ | none | Same target as unscience's `MenuBarPatch`; draws `ImGui.BeginMenu("Don't Stifle Me")`. |

### Watch items
- `MINIMUM_SCALE`/`MAXIMUM_SCALE` are **consts inlined** into `ScaleBoundsFor`; if a later build
  inlines `ScaleBoundsFor` itself into its two callers, patch #1 has no target → clamp returns.
- The TRS debug menu (`DrawTransformMenu`, `:6836`) still writes `part.Scale` per-axis with no clamp;
  unaffected by, and independent of, this mod.
- `DrawParachuteSection` is private and resolved by name. A rename or overload addition disables the
  editor-limit patch group with a logged error and red UI warning; the scale patch group remains
  independently usable in unscience.

---

## flexo — **REMOVED @5348**

flexo (robotics — articulated hinge/rotor Parts on top of KSA's static Part system) was deleted from
the repo at baseline `2026.8.22.5348`. It was **not** a compile break: `flexo.lib` built clean against
5348. It was removed because the approach never worked in-game and will not be reattempted this way —
the hinge implementation depended on undocumented `Part` transform/bounds cache-invalidation semantics
(recorded below as **R-flexo-2**, the area's highest silent-breakage risk) and was the source of the
long-standing "flexo throws errors but works" entry in [`../ISSUES.md`](../ISSUES.md).

Removed in one pass: `flexo/` and `flexo.lib/`, both solution entries, the `unscience.csproj`
`ProjectReference`, and the supermod wiring in `unscience/Mod.cs` (`FlexoSubmod`) and
`unscience/Patcher.cs` (`FlexoPatches.Apply` / `.Remove`). No game part XML was ever written by flexo,
so nothing on disk needs migrating; its orphaned TOML under `~/.flexo/flexo_part_*.toml` can be deleted
by hand.

**Retired integration points** (no longer verify these on a game update):

- The former flexo `PartModelRenderer.UpdateRenderData` hook was retired. Iron Man now uses a distinct
  `PartModelRenderer.UpdateRenderData(IViewport,int)` prefix for EVA equipment upload timing; see [scope](iron-man.md).
- `OrbitLinePass.AddLineVertex` / `.AddLineEnd` — likewise unowned; flexo's editor scene was the last user.
- The whole hinge rotation surface: `Part.Asmb2ParentAsmb`, `Part.PositionParentAsmb`,
  `Part.BoundingBoxVehicleAsmb` / `ComputeBoundingBoxVehicleAsmb()`, `Part.TreeChildren`, `Part.SubParts`
  setter-touch cache invalidation, `Vehicle.UpdateAfterPartTreeModification()`.
- `PartTree.RecomputeStaticMass()` via `Traverse` (**R-flexo-3**, string-reflection watchlist entry — now
  off the list).
- Risk items **R-flexo-1** (by-name `ExecuteNextVehicleSolvers` prefix), **R-flexo-2** (private
  cache-invalidation contract), **R-flexo-3** and **R-flexo-4** (solver-phase tree mutation) are all closed.

**Still live elsewhere, keep verifying:**

- `Universe.ExecuteNextVehicleSolvers(double, SimStep)` — eternal-flame, kiwis-marbles and the unscience
  supermod prefix it (by `nameof`, not by string; kitchen-sink no longer does, repo grep @5482), see
  [`vehicle-physics.md`](vehicle-physics.md) and [`celestial-and-lights.md`](celestial-and-lights.md).
- `GenericGizmo` — **dont-stifle-me** uses `VehicleEditor.ScaleGizmo.GetSegmentDataByViewport(Viewport)`
  (see its section above).
- `PartTree.UpdateRenderData(...)`, `Part.Template.Id`, `Part.Connections` / `Connection.OtherPart` — all
  still reached by other mods; catalogued in their own sections.

Kept here only so a future reader who finds flexo in git history knows why it is gone. Its full
integration table, the 4680→4750 verification and the R-flexo-* findings live in git history and in
earlier upgrade plans.

---

## parts-now — **REMOVED @5638**

parts-now (runtime Part/SubPart loading: paste `<Assets>` XML into a new mod folder, or load /
reload / unload a mod folder without restarting the game) was deleted from the repo at baseline
`2026.10.19.5638` at the user's request. It was **not** a compile break.

Removed in one pass: `parts-now/` and `parts-now.lib/`, both solution entries, the `unscience.csproj`
`ProjectReference`, the supermod registration in `unscience/Mod.cs` (`PartsNowSubmod`), the "runtime
part mods" hint in the shell's save-details text (`unscience/UnscienceSaves.cs`), and the
`ksa-upgrade.tests` V8 bundle-validator checks (`BundleValidator{Checks,Fixture,Harness}.cs`; the
shared `Tank` / `FlowOrder<T>` / `ResourceManager` stubs moved to `PropellantFeedFixture.cs`).
parts-now patched no game method of its own; its standalone host only applied `HotkeyGuard`.

**What stays on disk.** Mod folders that parts-now installed are ordinary KSA content mods
(`mod.toml` + `<Assets>` XML under `ModLibrary.LocalModsFolderPath`, enabled in `manifest.toml`) and
keep loading at boot, so vessels built from those parts still load natively. The orphaned settings
file `<mods>/parts-now/parts-now.toml` can be deleted by hand.

**Saves.** The former `parts-now` participant (version 1, a `SavedDependency(ModId, PartIds)[]`
list used only for missing-template warnings; it never owned scene objects) has no replacement. A
legacy sidecar record takes the coordinator's unknown-feature path
(`ksa-abstractions.lib/Persistence/SceneSaveCoordinator.cs:76-81`): the load continues, the record is
reported as `parts-now: unsupported feature/version 1; retained for a compatible build.`, and it is
written back unchanged by later saves (`Capture`, `:37`). **Use current setup** only discards records
of registered participants, so it stays. `saves.tests` `RetiredFeatureTests` pins this.

**Retired integration points** (no longer verify these on a game update):

- Reflection: `ModLibrary.{AllMaterials, AllPartGameDataReferences, AllEditorTagDefinitions}`,
  `SerializedCollection<T>._collection` and `VehicleEditor._editorTagLookup` (parts-now's
  `AllParts` / `AllMeshes` / `AllFiles` lookups also go; those names stay live for other mods, below).
- Mesh headroom: writes to `DeviceMeshInterleaved.Shared.{RunningVertexBufferSize,
  RunningIndexBufferSize}`, reads of `VertexAllocation` / `IndexAllocation` / `IsBuilt`, the one-shot
  `Build()` / non-growing `Rebuild()` assumption and `MeshReference.DeviceMeshesInterleaved` sizes
  (U1, U2, U10). The `LoadAll`-before-`Bind` ordering is no longer a standing invariant here.
- Asset pipeline: `XmlHelper.Serializers[typeof(AssetBundle)]`, `AssetBundle.{OnDataLoad, Assets}`,
  `Mod.MakeUsing` / `Preload`, `ModLibrary.{Loaders, Binders, Find, Lookup, Manifest,
  LocalModsFolderPath, LocalManifestPath, MOD_TOML, CONTENT_FOLDER}`, `ModManifest.Save`, `ModEntry`,
  `ILoader.Load` / `IBinder.Bind`, `FileReference.Load` post-conditions, the duplicated
  `MeshAtlasFileReference.DoLoad` naming rule, `TextureReference.Dispose(Device)` and the
  `Loading.OnFrame` main-thread gate (U7, U8, U9).
- Part model and purge: `PartTemplate.{ApplyGameData, ResolveConsumerFeedPoints, Dispose,
  Thumbnail, EditorTagsStrings}`, `PartModel` / `PartModelGlass` / `PartModelDynamic`
  `.Get` / `Instances` / `InstancesRayTrace` pruning, `PartModel*Module.Template.RayTracers`,
  `ModuleBase.TemplateDataBase.Id` (U3, U5), `VehicleEditor.{ResetPartDiameterCache,
  UnattachedPartTrees, DynamicThumbnail}` and the unload safety gate.
- Thumbnails: `ThumbnailCreator`, `ThumbnailRenderer`, `ThumbnailRenderResources`, `ThumbnailPart`,
  `ThumbnailReference`, `ThumbnailDynamic`, `Program.ThumbnailViewport`, `Program.LinearClampedSampler`
  and `Camera.Unfollow(changeControl: false)` (U6, U12).
- Validation-only lookups: `SubstanceLibrary` reactions, `GrainGeometryLibrary`,
  `ModLibrary.Get<SoundBehavior>`, `BindlessTextureLibrary.{TextureCount, MaxTextures}`,
  `SerializedId.Mod` and the editor-tag rules (V7, U11).

**Still live elsewhere, keep verifying:** `ModLibrary.AllMeshes` / `AllFiles` / `AllGltfs`
(rocky-mcrock-face `RingAssetCatalog`, pebbles `ClutterAssets`), `ModLibrary.AllParts` (doh),
`KeyHash.Make` / `SubstanceLibrary` (doh), `VolumetricExhaustTemplate.Get` (pyro),
`Program.BindlessTextures` (graffiti), `Program.Editor` / `VehicleEditor.EditingSpace`
(`IvaForceRender`, kitchen-sink, humble-arteest), `PartModel.Instances` (`IvaForceRender`),
`IViewport.{GetCamera, Size, ShaderSlot}` and `Program.GetRenderer()` (several mods), and
`PartTree.UpdateRenderData` (Godzilla visual scale).

Kept here only so a future reader who finds parts-now in git history knows why it is gone. Its full
integration table, the U1–U12 findings and the per-build verification notes live in git history and
in the dated upgrade plans (`plans/KSA_5117_UPGRADE.md` … `plans/KSA_5482_UPGRADE.md`,
`plans/done/PARTS_NOW_PLAN.md`).

---

## Quick re-verification checklist (run on each new game build)

dont-stifle-me / shared part surface:

1. `Universe.ExecuteNextVehicleSolvers` still a **single overload** (`Universe.cs:2034` @5482) — eternal-flame, kiwis-marbles and the unscience supermod resolve it with `AccessTools.Method(typeof(Universe), nameof(…))` and **no param array**, so a second overload would make resolution ambiguous.
2. `PartTree.RefreshStaticMass()` still **public** (`PartTree.cs:1019` @5482; wraps private `RecomputeStaticMass :1024`) — dont-stifle-me calls it after per-axis drags (via `Part.Tree?`, nullable since 5482). No unscience code reflects on `RecomputeStaticMass` any more (the old kitchen-sink `Traverse` is gone; repo grep @5482).
3. `GenericGizmo` ctor / `PerSegmentData` / `Static.GenericGizmoRenderData`, and `VehicleEditor.ScaleGizmo.GetSegmentDataByViewport(IViewport)` (`GenericGizmo.cs:277`, keyed by `ViewportId` since 5402) (dont-stifle-me per-axis drag).
4. Editor scaling is **uniform and clamped 0.5×–2×** as of rev 5329 (was triaxial), and modules implement `IRescale.SetScale(in ScaleFactors)` — dont-stifle-me exists to undo exactly this, so re-check `MINIMUM_SCALE`/`MAXIMUM_SCALE`/`ScaleBoundsFor`/`UpdateSelectedScale`/`QuantizeScale` on every build, and that each of the five by-name `VehicleEditor` targets still has **exactly one** declaration (a second overload turns the by-name `AccessTools.Method` into `AmbiguousMatchException` at `Apply()`). `UpdateSelectedScale`/`UpdateScaleGizmo` take `IViewport` since 5402.

> The old **flexo** block was retired with flexo at 5348. `PartModelRenderer.UpdateRenderData(Viewport,int)`
> and `OrbitLinePass.AddLineVertex/AddLineEnd` are now **unowned** and need no re-verification;
> `PartTree.UpdateRenderData(ref readonly double4x4,bool,IViewport,int)` (`IViewport` since 5402) is still live — Godzilla's
> visual-scale prefix/finalizer (see [`vehicle-physics.md`](vehicle-physics.md)); the hinge rotation
> surface (`Part.Asmb2ParentAsmb`, `PositionParentAsmb`, `BoundingBoxVehicleAsmb`, `TreeChildren`,
> `SubParts`, `Vehicle.UpdateAfterPartTreeModification`) is gone from this repo.

> The parts-now block (items 8–19: U1–U12, reflection names, thumbnail and asset-pipeline surface)
> was retired with parts-now @5638; see its stub section above.

---

## Area summary — Update-risk findings (5261 → 5348)

- *(Two parts-now bullets — rev 5340 instantiating every part at load, and the `LoadAll` → `Bind`
  ordering invariant — were retired with parts-now @5638.)*
- ⚠️ **Editor scaling changed triaxial → uniform, clamped 0.5×–2×** (rev 5329), and many modules gained
  the new `IRescale` interface (`SetScale(in ScaleFactors)`). This is what dont-stifle-me undoes.
- ❌ **flexo removed** (after this pass). It was **not** a compile break — `flexo.lib` built clean
  against 5348 — but the robotics approach never worked in-game and will not be reattempted this way,
  so the mod, its `.lib` and all of its wiring were deleted. See the stub section above. Its 5348
  verification results are recorded in the two bullets below and need no re-checking, since nothing in
  the repo depends on them any more.
- ✅ *(now unowned)* `PartModelRenderer.UpdateRenderData(Viewport, int)` was still a single `static void`
  on `PartModelRenderer` at 5348, and the explicit `[Viewport, int]` overload array still resolved
  uniquely (the new 3-arg `(Viewport, int, ref readonly double4x4)` overload lives on other types).
  `GenericGizmo` and `OrbitLinePass` were unchanged. flexo was the last consumer of the
  `PartModelRenderer` hook and of `OrbitLinePass`; `GenericGizmo` lives on in dont-stifle-me.
- ✅ **`PartTree.RecomputeStaticMass` still present and still private**, so kitchen-sink's
  `Traverse.Method("RecomputeStaticMass")` still works. `PartTree` **gained a public
  `RefreshStaticMass()`** wrapper — an available simplification, not a fix.
- ✅ **`Part`'s API churned but missed both mods.** Rev 5329 removed `Part.Sequence`, `SetSequence(int)`,
  `ActivateInStage`, `DeactivateInStage` and `ScaleTotal`; neither flexo nor parts-now (both since
  removed) referenced any of them. `Part.Asmb2ParentAsmb`, `PositionParentAsmb` and `Scale` are
  unchanged.
- ✅ **`ModuleBase.Parent` became a property** (rev 5329, `IPartParent` split out of `Module`). Neither
  mod reflects on it.

---

## Area summary — Update-risk findings (5348 → 5402)

> Span `2026.8.22.5348` → `2026.9.7.5402`. Revisions **5349–5400 are unlogged** in any changelog; the only
> logged commit is rev 5401 "Fixed crash for incorrect data stride for thumbnail rendering", so the decomp
> diff (197 KSA files, 20 Content files) is the sole evidence for everything below.

- ⚠️ **`KSA.Viewport` was deleted and replaced by `IViewport` / `IGameViewport` / `ViewportBase` /
  `GameViewport` / `PartThumbnailViewport` / `ViewportRegistry`** (rev unknown). Two **compile breaks** in
  this area, both fixed: `dont-stifle-me.lib/EditorScalePatches.cs:124` + `PerAxisScaleDrag.cs:28`
  (`UpdateSelectedScale(ref readonly double4x4, IViewport)` prefix and its drag helper) and the
  since-removed parts-now thumbnail generator (`Program.ThumbnailViewport : IViewport`).
  Every by-name target keeps its **parameter names** and has **exactly one** declaration
  (`UpdateSelectedScale :3959`, `UpdateScaleGizmo :3732`, `ScaleBoundsFor :3995`, `ForEachPartWithSymmetry
  :4000`, `QuantizeScale :4025`), and every body is line-identical to 5348 — only the `Viewport` parameter
  type changed. `GenericGizmo.GetSegmentDataByViewport(IViewport)` (`:277`) now keys by `ViewportId`;
  `Program.ThumbnailViewport` (`Program.cs:497`) and `ThumbnailCreator.Viewport` (`:33`) became throwing
  properties; `IsOffscreen`/`ShouldRenderGizmos`/`EViewportLightMode`/`Program.Viewports`/`ViewportCount`
  are gone (the thumbnail viewport is now built with `ViewportOptionFlags.RenderPartModels`,
  `Program.cs:949`). Solution builds clean against 5402 (52/52 projects, 0 warnings, 0 errors).
- *(The rev-5401 thumbnail "data stride" finding concerned parts-now only and was retired with it @5638.)*
- ⚠️ **New gate in `PartModel/PartModelGlass/PartModelDynamic.AddInstance`** — early return unless
  `viewport.HasAny(ViewportOptionFlags.RenderPartModels)` (`PartModel.cs:410-413`); the IVA ray-trace branch
  now tests `viewport.HasAll(UseRaytracing) && viewport.Mode == IVA` instead of `== Program.MainViewport`.
  No effect here: every boot viewport carries the flag (`Program.cs:948-956`), and the kitchen-sink
  `IvaForceRender` postfix (now
  `ksa-abstractions.lib/IvaForceRender.cs:98`, already `IViewport`) behaves as before.
- ✅ **Parachutes + structural limits: additive schema; parachutes now consumed by dont-stifle-me.** `PartTemplate` gained
  `CrashTolerance` (`[XmlAttribute] double = NaN`, `:17-18`) and `<SubPartGroup>`/`SubPartGroups`
  (`:107-108`, merged in `ApplyGameData :327`); `Parachute.TemplateData` (`[XmlType("Parachute")]`,
  `Parachute.cs:12`, `ModuleList.cs:128`) is picked up by `XmlHelper`'s reflection-built overrides (file
  identical). New `PartStructuralLimits` / `PartFailure` /
  `PartContactLoad` types and `Part.{CrashTolerancePascals, InertMassKg, StructuralPart,
  IsAttachedInternal}` are not referenced by any unscience mod. Dont-stifle-me now consumes
  `Parachute`, `ChuteTuning`, `VehicleEditor.DrawParachuteSection`, and `Parachute.SetDiameter` only;
  it does not touch the cloth/physics implementation. Content: new `ParachuteAssets.xml`
  (GltfFile/PbrMaterial only, listed in `mod.toml`), four new radial parachute Parts + 14 SubParts in
  `CoreUtilityA*`, `CrashTolerance="3e6"` on `CorePropulsionA_Prefab_EngineA2..A6`, one new shader id in
  `DefaultAssets.xml` (`StaticObjectPrePassIndirectFrag`).
- ✅ **`CoreUtilityA_Prefab_ParachuteBayA` removed** (renamed `…ParachuteBayB` in `CoreUtilityAAssets.xml`
  / `…GameData.xml`). No unscience mod references it (repo-wide grep for `ParachuteBayA|CoreUtilityA_` is
  empty). All §5 ids used by this area's neighbours (`LightPart`, `CorePropulsionA_Prefab_EngineA2..A6`,
  `_connector3` BulkFluid/FeedsFrom wiring, `KittenBackPackPart`) still resolve.
- ⚠️ **Semantic drift with no symbol change (not reached by these mods, recorded for the next pass):**
  `Part.DisplayName` now prefers `Template.DisplayName` when it differs from the id (`Part.cs:1391`, was
  `DisplayName = Id`); `Part.ResetModuleProperties()` now nulls `LightSwitch` (`:1807`) and the
  `PartModel*Module` light-off flag reads the new `Part.IsLightSwitchedOff()` (`:1357`);
  `VehicleEditor.HandleConnectorConnections` only connects when `connector.CanConnect()` and prefers a
  coincident connector (`:5092-5097`); the editor's right-click gate reads `Program.InputViewport`
  (`:3586`, was `HoveredViewport`); `Part.CountEnabledSubtreeSequencedModules` was removed;
  `GizmosRenderer.MAX_GIZMO_INSTANCES` 131072 → 655360. `Part.RefreshScale`/`RefreshScaleAndReposition`
  /`Scale` and `ScaleFactors`/`IRescale` are untouched, so dont-stifle-me row 8's limitation stands as is.
- ✅ **Verified clean (kind + type unchanged, lines refreshed in the tables above):**
  `PartTree.RecomputeStaticMass` is still **private** (`:778`) so
  kitchen-sink's `Traverse` string works, and `PartTree.RefreshStaticMass` (`:773`) is still public;
  `Universe.ExecuteNextVehicleSolvers` is still a single overload with an identical body (`:1834`).
  (The parts-now byte-identity list and its U1–U12 re-check were retired with parts-now @5638.)
- 🔍 **Live check still owed** (compile-clean, but the 5349–5400 window is unlogged): per-axis-scale a
  part with dont-stifle-me, then attach it to a connector (exercises the new `CanConnect()` /
  coincident-connector path in `HandleConnectorConnections`).

## Scene save adapters

Don't Stifle Me records enabled/snap/extended editor limits and resets those settings before a new
scene, without modifying native saved geometry. Native save preflight verifies actually used
part/character templates before destruction. No new reflection target. See [saves](saves.md).

Parts Now's dependency-only `parts-now` participant was removed @5638. Legacy sidecar records with
that id are reported, retained and carried forward unchanged by the coordinator (see the parts-now
stub above and `saves.tests` `RetiredFeatureTests`).
