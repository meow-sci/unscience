# Ground clutter: Pebbles

## KSA 5901 → 5949 verification (2026-11-25)

Checked statically against `2026.11.25.5949` (revs 5902–5948) from `2026.11.16.5901`. `pebbles.lib` builds
and `pebbles.tests` passes, with negative controls. Nothing has run in game. Line numbers are 5949.

- **Viewport release takes a slot (5919/5920, compile fix).**
  - `ClutterEcotypeRenderData.ReleaseViewport(IViewport)` became `ReleaseViewport(int slot)`.
  - `ClutterResidency` now resolves `GroundClutterRenderer.ResolveViewportSlot` behind the existing
    `HasViewportSlot` guard, the same sequence as native `OnViewportUnregistered` (`GroundClutterRenderer.cs:478-497`).
- **Per-planet resources are now allocated lazily (5919/5920).**
  - `GroundClutterGpuResources` (new; `ClutterEcotypeRenderData.Resources`) now holds every per-ecotype
    buffer, mapped memory and descriptor pool. `ClutterCubeCellGrid` is CPU-only.
  - The large per-planet buffers are created by `AllocatePlanetResources` and freed by `DisposePlanetResources`,
    both guarded by `HasPlanetResources` (`ClutterEcotypeRenderData.cs:378-449`). They are OutCcfPositions,
    CellObjectCounts, LodFade, the terrain `CurvatureBuffer` and six view-selection buffers per view type.
  - Native allocates them in `AccumulateViewportCells` (`GroundClutterRenderer.cs:1356-1365`). That is the first
    clutter consumer of the frame, `Program.cs:5172`, after the `ExecuteNextClothSolvers` swap point at `:2588`.
    Native frees them in `DisposeUnusedPlanetResources` (`:1474-1487`), but only for arrays in `PlanetEcotypeRenderData`.
  - Pebbles builds the way native does and never allocates these itself.
  - `ClutterResidency.ReleaseDetached` now also calls `DisposePlanetResources()`, after every cell is released.
    Retained originals and replaced bundles therefore no longer hold per-planet VRAM while detached.
  - A restored array is reallocated by the renderer before it is used. All later disposal, native or Pebbles
    (`DestroyFrameResources` → `DisposePlanetBuffers`, `:437-449, 1497`), checks the same flag, so nothing is
    freed twice.
  - `ClutterEcotypePhysicalData` has no lazy path. It allocates its terrain curvature in `BuildTerrainData`
    (`ClutterEcotypePhysicalData.cs:1004-1010`) and frees it in `Dispose`.
- **Retirement re-audit (5919).**
  - `ClutterRetirement` now walks `GroundClutterGpuResources`; its constructor is captured as well.
  - `GroundClutterTerrainData.Dispose` no longer frees `CurvatureBuffer`, so retirement cleans that buffer first.
  - The borrowed `Bvh` field (owned by the planet mesh) is not walked.
  - `_depthNormalPrePassBlendState` is now a static `NativeList`, which the instance-only walk ignores.
  - The `MappedMemory[][]` set is unchanged and now lives on the new type.
- **Collider-less clutter needs the planet-mesh structure (5903).**
  - A type with collision `None` snaps to the planet mesh (`ClutterCubeCellGrid.cs:102`).
  - The game now builds the mesh BVH/TLAS only when some stock ecotype on that mesh collection snaps to the
    mesh (`PlanetMeshLibrary.cs:70-76, 118-151`).
  - It covers only a patch: billboard snap step plus the stock snapping reach, `GenerationRange` plus
    `GetCellWidth(2πR, separation)·√2` (`CubeMesh.cs:370-385`). Rays that miss place nothing (`Generate.comp:404-440`).
  - Without the structure, `GroundClutterGpuResources.BuildFrameResources` throws "Celestial mesh missing
    BVH/TLAS" (`:377-379, 858-863`).
  - Stock data: Luna (collection `Default`) and Mars (`MarsScale`) are all-collidable and have no structure.
    Earth's patch is sized for Grass (80 m).
  - **Pebbles refuses** an enabled type with collision `None` when the body has no structure, or when its reach
    exceeds the patch (`Models/SnapToMeshPolicy.cs`, native facts in `Runtime/ClutterSnapToMesh.cs`).
  - The check runs in `QueueApply`, for immediate UI feedback, and again in `Apply` before any allocation.
    The `InvalidOperationException` names the body and type and asks for custom colliders. It reaches the
    Apply status or the save-replay warning with no preparation fault and nothing to retire.
  - **Disabled types** keep their stock collision type and install no colliders. Forcing `None` would throw
    on Luna/Mars. Native `PopulateColliderPrimitives` only warns about collider-less objects (`:270-297`), and
    a zero biome mask rejects every candidate (`GenerateCollision.comp:320-327`). Smooth-normal orientation
    becomes surface-normal for them, because native `ToParameters` rejects it for collidable types.
- **Unchanged:**
  - reflection names (`_renderPassInfo :214`, `_planetClutterMaxBoundingRadius :342`, `_groundClutterRendererCreated :339`);
  - the five material-binding methods, one call each (`:561, 683, 731, 782, 832`);
  - the hooks and the `Solid.frag` marker (once, `:321`) and material layout;
  - the depth-normal pipeline. It now targets a 2-attachment `MultiRenderTarget` (5915/5937), but it is built
    natively and only the buffer getter is redirected.
- **Star-system switching (5916).** `SwapSystem` → `PlanetRenderer.Dispose` → the existing
  `GroundClutterRenderer.Dispose` prefix restores the originals before native disposal. The next `Update` sees
  the new system and drops the suspended records.
- **Native acceptance:**
  - On Luna or Mars, a collider-less replacement is refused with a message, and one with colliders applies.
  - On Earth, a collider-less Tree replacement is refused; Grass applies.
  - A disabled type on Luna applies.
  - VRAM drops after Apply while the originals are detached, and restored clutter reappears.
  - Switch systems with an override applied, then load a save from the other system.

## KSA 5801 → 5901 verification (2026-11-20)

Rev 5875 compacts per-viewport clutter state from eight `ShaderSlot` entries to five clutter slots
owned only by Main/Secondary viewports. `ClutterEcotypeRenderData.ReleaseViewport`/`ReleaseCell`
now call `GroundClutterRenderer.ResolveViewportSlot`, which throws for any other registered viewport
(`GroundClutterRenderer.cs:401-446`); `ViewportRegistry.Views` always includes the part-thumbnail and
two portrait viewports. `ClutterResidency.ReleaseDetached` released every registered viewport, so
**every Apply and save-load replay failed** after the first ecotype. **Fixed:** release only viewports
for which `GroundClutterRenderer.HasViewportSlot` is true, mirroring native `OnViewportUnregistered`.
The residency fixture models the compacted slot (unmapped viewports throw) and registers thumbnail and
portrait viewports; removing the guard reproduces the native exception.

Checked unchanged: Pebbles' reflected fields/methods, `BuildMaterialIndirection(string)`, material
buffer, push constants and instance layout; `Solid.frag` marker (once) with cloud shadows removed and
sets renumbered by native code; 5885 host-memory buffers are planet BVH only; the 8 → 5 arrays are
covered by generic retirement walking. Native Apply/Restore/replay with secondary viewports, thumbnails
and portraits remains pending. Saved displaced-object positions can sit off the 5849–5857/5874 terrain.

See [KSA 5901 upgrade review](../plans/KSA_5901_UPGRADE.md).

## KSA 5707 → 5801 verification (2026-11-08)

`ClutterEcotypeReference.Key`, biome/collider keys and resolved material/mesh/texture lists replace
old Name/Alias/Id/Get seams. `ClutterGraph.cs` fills `MeshRefs`/`MaterialRefs` plus the resolved readonly
lists and invokes `GroundClutterLodReference.BuildMaterialIndirection(string objectId)` (formerly no
arguments). The five material-buffer redirect methods still match the real DLL.

Capture/replay now preserves optional `PlacementRecipe.Curvature` (domain, texel spacing, curve) and
`MaterialRecipe.TerrainFadeStart/End`. Absent legacy fields inherit current stock settings. UsesCurvature
is derived from the restored curve. Recipe IDs/versions and native clutter-grid key values remain stable.
`ClutterRetirement` includes `GroundClutterTerrainData` and curvature LUT bindless slots after interrupted
builds. `ClutterGeometry.CopyNative` explicitly allocates/copies native stride lists; successful lists
transfer to MeshAsset ownership. Workshop VertexInput is disposed. Managed recipe validation/round-trip
checks pass; runtime graph construction, GPU retirement and curvature/collision parity need native use.

See [KSA 5801 upgrade review](../plans/KSA_5801_UPGRADE.md) for evidence, validation boundaries and native acceptance.

## KSA 5707 verification (5638 → 5707, 2026-10-29)

Verified statically against `2026.10.25.5707` (revs 5639–5703) from `2026.10.19.5638`. This is
static/managed only. The full solution builds with 0 warnings and 0 errors, and `pebbles.tests` passes.
`ksa-upgrade.tests/Ksa5707Checks.cs` checks metadata in the real 5707 KSA.dll: it passes on 5707 and
fails on 5638. Nothing has run in game. Line numbers below are 5707.

Fixes:

- **Compile.** `SimpleVkTexture.Image`/`ImageView` became `VkImage`/`VkImageView` (`RenderCore/SimpleVkTexture.cs:75,77`),
  fixed at `pebbles.lib/Assets/GlbTextures.cs:87`. This one break also masked `sphinx.lib`.
- **Depth-normal pre-pass material redirect (new, revs 5677/5678).**
  - The new private `ClutterEcotypeRenderData.CreateDepthNormalPrePassRenderer` (`:668`, called from
    `CreatePipelinesFromMaterials` `:425`) binds `GroundClutterRenderer.MaterialBuffer` at binding 1 (`:735`).
  - The pass runs every frame in the main viewport: `PrePassRenderer.cs:287` → `PlanetRenderer.RenderClutterDepthNormalPrePass`
    (`:2064`) → `GroundClutterRenderer.RenderGroundClutterDepthNormal` (`:1987`), which iterates `PlanetEcotypeRenderData`.
  - Without the patch, Pebbles-owned ecotypes looked up their private material IDs in the vanilla buffer
    (`SolidDepth.frag:68`). Opacity-mapped clutter therefore wrote wrong cutouts and normals into the
    ambient-occlusion (AO) pre-pass.
  - The target list now lives in the dependency-free `Runtime/ClutterMaterialBindings.cs`, five methods
    including the new one, and `ClutterHooks.cs:37` iterates it. Each method still has exactly one
    `MaterialBuffer` getter or `GetMaterialIndex` call.
  - `Ksa5707Checks` proves that the set of `ClutterEcotypeRenderData` methods calling
    `get_MaterialBuffer`/`GetMaterialIndex` in the real KSA.dll equals that list. A future binding method
    therefore fails a managed check.
  - The ecotype `RebuildFrameResources` (`:1323`) recompiles and recreates pipelines without calling
    `SetBuffer` again, so the redirected buffer persists. The colour pass still depth-tests (Equal)
    against the redirected `DepthOnlyPrePass`.
- **`UsesCustomNormals` (new, rev 5689).**
  - `GroundClutterMaterialReference.UsesCustomNormals` is `PipelineFlags` 0x200 (`:25,61,141,190`).
    Its `USES_CUSTOM_NORMALS` macro is used only by the depth-normal pass, which then infers geometric
    normals for AO (`SolidDepth.frag:122`). 16 stock `EarthTreesAssets.xml` materials set it.
  - The new nullable `MaterialRecipe.UsesCustomNormals` (`Models/PebblesRecipe.cs:68,73`) is captured at
    `ClutterCapture.cs:73` and applied at `ClutterGraph.cs:26-27,135` through a shared `StockFlags` helper.
    A null value inherits the stock material by `SourceId`, like `KeepBackfaceNormals`. Without it,
    Pebbles-owned trees would get the over-darkened foliage AO that rev 5689 fixed.
  - It is a CPU pipeline key, not a GPU material flag, so flags bits 31/30 are unaffected.
  - The GLB importer does not read the bundler's new `usesCustomNormals` extra (`KSA.GlbImport/ClutterAssetBundler.cs:20,367`),
    so imports resolve to false. This is not implemented.

New native behaviour that needs no code, because Pebbles builds its pipelines through the native `Create*` methods:

- Rev 5676 removed the clutter pre-pass A2C colour target (`ClutterPrePass(context, depthFormat, samples)`,
  `Rebuild(samples)`). A2C now writes `gl_SampleMask` from a `SAMPLE_COUNT` specialization constant
  (`SolidDepth.frag:35-37,117-118`).
- Rev 5678 added AO receiving: `Solid.frag` `SET_AO 8` (`:16,66,380`), plus a seventh external set in
  `CreateColorRenderer` (`:592`).
- Rev 5660 added viewport/scissor dynamic states.
- `Generate.comp:267,275-276` and `GenerateCollision.comp:214,222-223` changed their gradient scaling
  identically, so render and collision placement still agree.
- Partial retirement:
  - The new `_opaqueDepthNormalPrePassRenderers` (`List<SimpleGraphicsPipeline>`, `:118`) is covered by
    the generic collection rule and disposed by native `Dispose`.
  - `_depthBlendState` was renamed `_depthNormalPrePassBlendState` (`:156`). It is a `PtrOwner` and, as
    before, is not retired.
  - The descriptor pool now holds `4 × materials` sets (`:367`).

Verified unchanged:

- **Hooks.**
  - `Universe.ExecuteNextClothSolvers(double,SimStep)`: `:2137`; its body and the `Program.cs:2422` call site are identical.
  - `GroundClutterRenderer`: `RebuildFrameResources(DeviceEx)` `:2083`, `SerializeSave(List<…>)` `:2194`, `Dispose` `:2216`.
    Its other changes are additive.
  - `ShaderReference.CompileVariantWithCustomOptions` (`:128`): the file is byte-identical.
- **Source-colour injection.** The `Solid.frag` marker occurs once (`:327`). Its identifiers are present:
  `textureSampler` `:64`, `inUv` `:47`, `materialData` `:277`.
- **Material layout.** `Common/GroundClutterCommon.glsl` and the C# `GroundClutterGpuMaterial` (6×uint)
  are identical. No shader reads flags bits 30/31.
- **Reflection.** `_renderPassInfo` `:195`, `_planetClutterMaxBoundingRadius` `:317`,
  `PlanetRenderer._groundClutterRendererCreated` `:340`, `Universe._vehicleUpdateTask` `:44`,
  `StagingPool._commandBufferIndex`/`_submitted` `:29,31`, physical `_primitiveShapes`/`_compoundShapes` `:90,92`.
  `Celestial.BodyTemplate` is still an auto-property, and `Astronomical.bodyTemplate` is unchanged.
- **`SyncGroundClutter`.** Its body is identical, so the `DrainExclusions` mirror holds.
- **Ownership classes.** `ClutterEcotypePhysicalData`, `ClutterCubeCellGrid`, `ClutterViewResources` and
  `SimpleVkMeshAtlas` are byte-identical; `GroundClutterPlacementData` only has the rename.
- **Other unchanged types.** `GroundClutterLodReference`, `MeshReference`, `ModLibrary`, `TextureReference`,
  `TextureLoader` and `ShaderModuleUtils` are unchanged. Residency changed only in log line numbers.
  `AstronomicalTemplate` adds `AuroraReference`, which the shallow template clone shares.

Native acceptance still required:

- On a Pebbles-owned body in the main viewport with AO on, check the AO and normals around opacity-mapped
  foliage. There should be no rectangles or halos.
- Captured and legacy tree recipes should match stock foliage AO (`UsesCustomNormals`).
- Change the MSAA sample count with overridden ecotypes. The depth-normal pipelines should rebuild with the
  new specialization constant.
- Check source-colour materials under the new AO receive.

## Previous verification — 5482 → 5638

Verified statically against `2026.10.19.5638` (`ksa-game-assemblies` `edda844`, revs 5483–5638) from
`2026.9.22.5482` (`a2f724f`). The full solution builds and the `pebbles.tests` checks pass. Native
acceptance is open.

Rev 5629 added per-viewport cell residency. Each `ClutterEcotypeRenderData` now owns:

- eight per-slot viewport `CubeCellGrid`s (`ClutterEcotypeRenderData.cs:60,293-297`);
- `_viewportCellData` (`:62`), a `(cell, owner)` → `CellResidence` map holding a viewport mask and a
  pooled buffer slot;
- pending release, allocation and regeneration lists, and per-slot fade blocks.

`GroundClutterRenderer` tracks the body seen by each shader slot (`_viewportCelestials`, `:221`). It
releases a viewport only on the arrays currently stored in `PlanetEcotypeRenderData`:

- `OnViewportUnregistered` (`:413-428`) calls `ReleaseViewport` then `ResolvePendingReleases`;
- `AccumulateViewportCells` (`:1185-1265`) releases on a body change (`:1203-1211`) or when out of
  range (`:1245-1248`), then calls `ResolveCellResidency` for the active bodies (`:1256-1262`).

Pebbles changes:

- **Residency leak — fixed.** Apply swapped `PlanetEcotypeRenderData[body]` without releasing the
  outgoing arrays. Retained originals therefore kept mask bits and pooled cell slots for viewports that
  closed or changed body during an override, and leaked them after restore. The new
  `ClutterResidency.ReleaseDetached` runs from Apply, after `ClearStatics` and before the swap. For
  every viewport in `ViewportRegistry.Views` (`ViewportRegistry.cs:48`) it calls the renderer's own
  sequence:
  - `ReleaseViewport` (`ClutterEcotypeRenderData.cs:974`) and `ResolvePendingReleases` (`:993`);
  - then it clears `GenerationViewport` (`:90`), which the renderer consumes and clears each frame
    (`GroundClutterRenderer.cs:1464-1465`).

  Hidden secondary viewports are covered too: they are registered but not passed to
  `AccumulateViewportCells` (`PlanetRenderer.cs:2069-2082`). This applies to retained originals and to
  replaced override bundles. Restore needs no release: the outgoing override arrays are retired, and the
  originals were released when they were detached. `_viewportCelestials` is unchanged, so reattached
  arrays see empty viewport grids. `EvaluateCells` then re-evaluates fully because the loaded-cell count
  is 0 (`ClutterEcotypeRenderData.cs:940`). All calls are public; no new reflection.
- **Retirement ordering — fixed.** Five `ClutterCubeCellGrid` fields are now nested:
  - `_cellAnchorEgoMemory`, `_viewportCellSlotMemory`, `_generateUboMemory` and `GenerateCellDataMemory`
    are `MappedMemory[][]` (`ClutterCubeCellGrid.cs:284-304`; all `MappedMemory[]` in 5482);
  - the matching buffers are `BufferEx[][]`.

  The partial-retirement pre-unmap pass matched only `MappedMemory`/`MappedMemory[]`, so failure-path
  cleanup could dispose buffers before unmapping. `FieldShapes.Holds` now matches mapped memory at any
  array, nullable, sequence or dictionary-value depth. `ClutterEcotypePhysicalData` maps remain
  `MappedMemory[]` (`:138-144`).
- **VRAM (document only).** The shared cell pool is now sized for eight viewports:
  `MaxCellSlots = NumCells × 8` and `MaxInstanceCount = MaxCellSlots × 256` (`ClutterCubeCellGrid.cs:318-320`;
  5482 used `NumCells × 256`). There are also per-slot × frames-in-flight anchor, slot-list and
  generate buffers (`:388-440`). Each Pebbles bundle and the retained originals therefore hold about
  8× the 5482 per-ecotype instance storage (`ObjectData` 32 B). `RecipeValidation`/`CandidateBudget`
  do not count it.
- **Unchanged contracts (re-verified).** These still hold:
  - each transpiler target has exactly one match: `SortMaterialIds` → `GetMaterialIndex`;
    `CreateColorRenderer`, `CreateDepthPrePassRenderer` and `CreateShadowDepthRenderer` → `MaterialBuffer`;
  - reflection names: `_renderPassInfo` (`GroundClutterRenderer.cs:195`), `_planetClutterMaxBoundingRadius`
    (`:317`), `PlanetRenderer._groundClutterRendererCreated` (`:316`), `Universe._vehicleUpdateTask`
    (`:44`), physical `_primitiveShapes`/`_compoundShapes` (`:90,92`), and `StagingPool._commandBufferIndex`/`_submitted`
    (`Brutal.VulkanApi.Abstractions/StagingPool.cs:29,31`);
  - hook signatures: `RebuildFrameResources(DeviceEx)` (`:2034`), `SerializeSave(List<…>)` (`:2145`),
    `Dispose` (`:2167`), ecotype `RebuildFrameResources(DeviceEx)` (`:1250`), and
    `CompileVariantWithCustomOptions(CompileOptions)` (`ShaderReference.cs:128`);
  - `Universe.SyncGroundClutter` (`:2156-2192`) is logic-identical to the `DrainExclusions` mirror;
  - the `Solid.frag` source-color marker occurs once (`:324`). The injected identifiers still exist:
    `textureSampler` `:63`, `inUv` `:46`, `materialData` `:274`, and
    `GroundClutterMaterialData.flags` (`Common/GroundClutterCommon.glsl:507`).

  The clutter shaders churned heavily (15 files), but Pebbles uses native pipelines/shaders except
  the one-line color injection.

Native acceptance still required:

- apply an override, then open, close and move secondary viewports, including to another body or out
  of range;
- restore and reapply, and confirm that clutter reloads in every viewport without "Ground clutter
  cell pool exhausted" errors;
- cause a deliberate preparation failure, and check that it unmaps before buffer release;
- check VRAM with several overridden ecotypes;
- complete the 5482 list below.

## Previous verification — 5438 → 5482

Verified statically against `2026.9.22.5482` (revs 5439–5481) using both supplied source/Content trees; managed checks pass, native acceptance is open.
Rev 5447 turned clutter hits into displacement: `BubbleClutterStatics` queues `ClutterRemoval` (key, `ClutterExclusionType`, `GroundClutterPlacementData.DisplacedObject`) and adds a live dynamic Bepu body; `Universe.SyncGroundClutter` (Universe.cs:2064-2098) records it with `AddDisplacedObject` / `RemoveDisplacedObject`. Rev 5447/5459 made KSA save live `PlanetPlacementData` natively (`GroundClutterRenderer.SerializeSave`/`DeserializeSave`, GroundClutterRenderer.cs:1827-1895; CelestialSystem.cs:641/778; `Universe.DeserializeSave` resets clutter at :2371). Pebbles changes:

- **Compile fix.** `DrainExclusions` consumes `List<ClutterRemoval>`, forwards `removal.Type` to `ExcludeInstance(…, ClutterExclusionType)` (the type is inert natively) and mirrors the native displaced-record bookkeeping, so `ClearStatics` → `BubbleClutterStatics.Clear` settles records that exist (no "settle … no record" errors, no vanished objects).
- **Physics parity.** `ClutterEcotypePhysicalData.PopulateUnitChildMasses` weights compound children by `ColliderTemplate.VolumeCubicMetres` (virtual, base 0). Private `OwnedHull` now overrides it with the ported `ConvexHullColliderTemplate.ComputeHullVolume` (Bepu face fans, `HullMath`) and recentres points on their bounds centre before `ConvexHullHelper.CreateShape`, as stock does; `ShapeOffsetCollider` stays the true centroid. Every keep-original rock compound (2–27 hulls) gets stock masses/COM instead of the "no collider volume" warning. `ClutterObjectTemplate.AngularDamping` (0.5 on stock trees) is copied per slot.
- **Tree lighting.** `GroundClutterMaterialReference.KeepBackfaceNormals` (rev 5473; `PipelineFlags` 0x100 → `KEEP_BACKFACE_NORMALS`, Solid.frag `#if DOUBLE_SIDED && !KEEP_BACKFACE_NORMALS`) is captured into `MaterialRecipe.KeepBackfaceNormals` and set on private materials. The recipe field is nullable: recipes saved before it (and GLB imports) inherit the stock material with the same `SourceId`, else false.
- **Displaced carry-over.** Each live record's `ClutterGridMemory` remembers masks **and displaced records** per grid (ecotype + exact separation); see runtime behavior below.
- **Native saves.** New postfix `GroundClutterRenderer.SerializeSave(List<ClutterEcotypeSaveData>)` rewrites the entries of override ecotypes whose grid differs from stock (or that are disabled) with the remembered stock-grid state, because native entries are keyed only by `CelestialId`/`EcotypeId` (`GridResolution` always 0) and reload into the stock placement. Override-grid state travels in the new `pebbles.clutter-state` sidecar record.
- **Reflection removed.** `GroundClutterPlacementData._exclusionCache` is no longer read or written: snapshots use public `SerializeSave(ClutterEcotypeSaveData)`, writes use `GetExclusionData`/`ExcludeCell`/`RemoveDisplacedObject`/`DeserializeDisplacedObject`.
- **Asset drift (no code change).** EarthTreesAssets.xml changed TreeType1–15 LOD 5 cards (`_Lod4_Cards` → `_Lod3_Cards`), so Earth `Tree`/`SmallTree`/`Shrub` signatures changed: 5438 recipes containing them fail explicitly ("changed since capture; recapture"), surfaced as save warnings. No migration is implemented.
- **Automatic native behavior.** Pebbles bundles use native constructors, so the 2048-slot displaced render tail, `DISPLACED_INSTANCES` pipelines, rest-delta buffers, `PackedColor`/`UnitInertia` and baked AO (5477) / BRDF roughness fix (5472) apply automatically. The tail adds about 2048 instance slots of VRAM per ecotype per bundle that `RecipeValidation`/`CandidateBudget` do not count.

Native acceptance still required: knock rocks/trees loose on a Pebbles body, then apply/restore/apply during motion (no settle errors, nothing vanishes, loose objects keep falling); trees settle (damping); rocks tumble without hull-volume warnings; save/load at same and changed separation (no new holes in stock, override removals persist); 5438 tree recipes warn; tree backface lighting and GLB material look under the new AO/BRDF.

## Previous verification — 5402 → 5438

Pebbles preview uses renamed `VkIndexType.UInt32` with unchanged index width/value. The 19 ground-clutter shaders and five XML graphs are byte-identical. All constructor/material/transpiler/reflection contracts remain; StagingPool still retains `_submitted` and `_commandBufferIndex`, so cancellation discards only unsubmitted work despite new fence recycling. Segmented physics retains the drained cloth/vehicle edit window; native apply/restore, faults and bubble merge/split acceptance remain open.

Verified against `2026.9.10.5438` using both supplied source/Content trees.
See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md).
Older catalog tables below retain their explicitly cited build/line numbers; this section records the current delta.

Current owner: `pebbles.lib`. Runtime code is in `Runtime/` and asset/geometry ownership in `Assets/`; main authoring and Workshop UI belong to the same feature. Reference baseline: KSA **2026.9.7.5402**, sibling `ksa-game-assemblies/current`. Compilation and offline shader validation do not establish native GPU or gameplay behavior.

## Simple authoring behavior

The main form owns one detached replacement recipe and exact planet/type selections. Apply copies mesh/materials, transform and collider geometry to every variant/LOD of checked ecotypes while preserving target signatures, slot identities, native LOD thresholds and unchecked ecotypes. Selected ecotypes use MinScale/MaxScale = one, retaining the authored preview size without an extra random placement multiplier. Enabled custom colliders select PrimitiveList automatically; SurfaceNormalSmooth falls back to SurfaceNormal for native collision compatibility. Without colliders a type snaps to the planet mesh, which KSA 5903+ supports only within the stock snapping reach on bodies whose stock clutter snaps to the mesh; elsewhere Apply refuses it and asks for colliders (see the 5949 section). Disabled types keep their stock collision type and install no colliders. Uniform authoring scale updates collider offsets around the mesh origin, dimensions and hull scale once; runtime continues consuming already-transformed collider coordinates. No new Harmony targets, reflection lookups or binary layouts are introduced.

## Runtime behavior and ownership

Pebbles queues per-celestial recipe application and restoration. The prefix of `Universe.ExecuteNextClothSolvers` runs after the prior frame's solver completion/application and before the next cloth and vehicle work is scheduled. It waits vehicle/cloth jobs and the graphics device, verifies the vehicle task's sync window, constructs a private reference graph/material table/placement/render/physical bundle, drains old collision exclusions, clears matching physics-bubble statics, and replaces the three arrays for the exact celestial hash. Visible geometry and collider proxies have independent bounds; the physics constructor receives the maximum of visual and proxy reach both per object and per ecotype. `_planetClutterMaxBoundingRadius` remains the visual radius used for shadow-frustum extension.

The original body template, placement/render/physical arrays and shadow bound remain retained for exact body restore. Both `Celestial.BodyTemplate` and `Astronomical.bodyTemplate` are rebound to a shallow private template whose clutter graph is wholly private. Shared mesh primitives, materials, texture references, and collider templates are never edited. Keep-original colliders are rebuilt privately; stock convex-hull behavior retains the native first-primitive rule. Custom hulls combine the selected mesh's primitives. Hull points, shapes and offsets belong to Pebbles; native physical data owns the registered scaled Bepu shapes.

Meshes are copied into private CPU `MeshAsset`s, including transformed positions/normals, UVs, and a uniformly uint source index stream to avoid the mixed-index atlas staging defect. Mesh and collider Euler rotations match native `QuaternionEx.CreateFromXyzRadians` (XYZ; row-vector matrices Rx * Ry * Rz). Positive object transforms are supported; reflected scales are rejected. Imported named registry glTF meshes are CPU-only, cached by `ClutterAssets`, and not registered globally. External GLB sources use a separate bounded managed decoder and private `MeshAsset` float3/float2 streams with uint indices. Default-scene node transforms, including mirrored winding, are baked before the object transform. Their texture conversion and upload are documented in [GLB imports](ground-clutter-glb-materials.md). Bound game textures are borrowed. The native renderer builds/uploads the private atlas.

Each private LOD receives explicit material indirection and private material references. External GLB slots are grouped by exact source identity and source-local material index, sorted ordinally independent of import order; stock material-index grouping remains unchanged. Scene and individual-mesh selections from one source share its slots. Pebbles routes only its own native material-call sites to its private `GroundClutterGpuMaterial` buffer and hash/index map. Construction context is thread-local; frame-resource rebuild context is keyed by the exact owned render object. Global material buffers and stock shader references are not overwritten. An explicit transfer-to-fragment-read buffer barrier follows material upload.

`SourceColors` adapts the current `ClutterSolidFrag` source while building the owned color pipeline. Bit 31 of the private material flags removes terrain-color modulation; bit 30 records an sRGB texture format so already-linear hardware sampling is not decoded a second time. The source marker must occur exactly once. Native include callbacks and original, NUL-terminated source path are preserved. Other stock shader variants and depth/shadow paths remain native. Native lighting, PBR response and shadows still apply.

Recipe identity includes ecotype name, ordered object IDs, LOD mesh IDs/primitive counts and material IDs. Variant count/order and five LOD slots remain stable. Runtime requires nonempty geometry for every LOD. Maximum 51 object slots, candidate and repeated-vertex budgets, uniform XYZ placement scale for collidable ecotypes, valid positive installed-collider mass, resolved biome aliases/assets, and native parameter conversion are checked before commitment. Biome controls edit an ecotype's native 32-bit eligibility mask; duplicating/splitting ecotypes for biome-specific replacement is not implemented because native candidate selection is not a disjoint partition.

Removed and displaced clutter is remembered per live body in a `ClutterGridMemory` keyed by ecotype name and exact separation value, with immutable object slot identity. Every transition first drains old pending hits (recording displaced objects as native `SyncGroundClutter` does), snapshots the outgoing placement before `ClearStatics` (so in-flight objects keep their pose/velocity and stay loose), and replays remembered state into grids with the same key: masks merge with bitwise AND and receive queued render/physical uploads; the destination's displaced records are replaced by the remembered set through `DeserializeDisplacedObject` (loose unless settled), and invalid object/scale slots are skipped with a log. A placement Pebbles replayed into is authoritative for its displaced records (records it dropped were destroyed); a fresh or disabled (zero biome mask) placement only merges, so renderer recreation cannot erase remembered objects whose bits stay cleared. Every remembered displaced record keeps its slot bit cleared. Disabled ecotypes never receive displaced records. Switching spacing retains both grids' state, so returning to a spacing restores its removed and displaced objects; an unrelated spacing never reinterprets another grid's subcell keys. Body identity and radius are fixed for each record's lifetime. Native launchpad/decal/terrain suppression stays active.

Release queues restores into the same safe frame phase. Hiding the feature does not release it. `GroundClutterRenderer.Dispose` restores the original arrays before native disposal and suspends live recipes; a replacement renderer requeues them. Explicit feature unload waits CPU/GPU completion and restores immediately. Ownership comparisons reject overwriting arrays or templates replaced externally. GLB cache release waits until all body hooks/live/pending ownership has retired without faults, re-releases any intervening Workshop preview, and retires uploaded textures on their original device/bindless library before clearing source records. CPU-only imports need no renderer for disposal.

## Harmony targets

`PebblesSubmod` implements main's `ISubmod`; its controller owns per-body runtime records.
`unscience/Patcher.cs` applies these hooks to the shared `MeowSci.Unscience` Harmony owner
at startup and removes them at unload. `ClutterHooks.Remove` filters by both owner ID and
Pebbles patch declaring type; it must never unpatch all methods of the shared owner.
Hooks remain installed while idle, with pending/owned-state guards. The host's HotkeyGuard
covers all Pebbles text inputs. There is no workspace/live-state framework dependency.

Targets:

- `Universe.ExecuteNextClothSolvers`: prefix applies/restores pending transactions.
- `GroundClutterRenderer.RebuildFrameResources`: postfix rebuilds the retained original render pipelines alongside the active overrides so restoring after graphics settings changes is compatible.
- `GroundClutterRenderer.Dispose`: prefix restores native ownership before native destruction.
- `GroundClutterRenderer.SerializeSave(List<ClutterEcotypeSaveData>)` (KSA 5482): postfix rewrites entries of this renderer's override ecotypes whose grid differs from stock, or that are disabled, with the remembered stock-grid state; per-record failures clear those entries rather than leave override-grid cells.
- `ClutterEcotypeRenderData.RebuildFrameResources`: prefix/finalizer scopes private material/shader bindings.
- `ClutterEcotypeRenderData.SortMaterialIds`, `CreateColorRenderer`, `CreateDepthPrePassRenderer`, `CreateDepthNormalPrePassRenderer` (5707), `CreateShadowDepthRenderer`: the list lives in `Runtime/ClutterMaterialBindings.cs`. Transpilers replace exactly one `GroundClutterRenderer.MaterialBuffer` getter or `GetMaterialIndex` call per method, and unexpected match counts fail patch activation. `ksa-upgrade.tests` (`Ksa5707Checks`) requires that the list equals every binding method in the real KSA.dll.
- `ShaderReference.CompileVariantWithCustomOptions`: prefix substitutes only `ClutterSolidFrag` compiled inside the owned material context.
- Public constructors of `GroundClutterPlacementData`, `ClutterEcotypeRenderData`, `ClutterEcotypePhysicalData`, `ClutterCubeCellGrid`, (5919) `GroundClutterGpuResources`, `ClutterViewResources`, `RenderCore.Mesh.SimpleVkMeshAtlas`: prefixes retain partial owned objects only inside construction context for failure cleanup.

## Reflection and binary dependencies

- `Celestial.<BodyTemplate>k__BackingField`, `Astronomical.bodyTemplate`; `object.MemberwiseClone`.
- `GroundClutterRenderer._renderPassInfo`, `_planetClutterMaxBoundingRadius`; public `PlanetPlacementData`, `PlanetEcotypeRenderData`, `PlanetPhysicalData`, `ExcludeInstance(KeyHash, uint, Cell, uint, ClutterExclusionType)` and `MAX_UNIQUE_SCALES`.
- `PlanetRenderer._groundClutterRendererCreated`: distinguishes a live renderer from the nonnull disposed object retained when clutter is disabled.
- `Universe._vehicleUpdateTask`, `VehicleUpdateTask.SyncWindowBubbles`; bubble `Parent`, `ConstraintSim`, `GroundClutterStatics.Clear` (syncs then settles displaced bodies in the described placement), `PopulatePendingExclusions(List<BubbleClutterStatics.ClutterRemoval>)`, `RemoveExcludedClutterInstance`; `ClutterRemoval.Key/Type/Displaced`, `ClutterExclusionType.Displaced`, `ClutterInstanceKey(KeyHash, int, Cell, uint)`.
- Public `GroundClutterPlacementData.SerializeSave(ClutterEcotypeSaveData)`, `GetExclusionData`, `ExcludeCell`, `AddDisplacedObject`, `RemoveDisplacedObject`, `DeserializeDisplacedObject(key, record, loose)` and `DisplacedObject` fields; `ClutterEcotypeSaveData.Cell`/`DisplacedObject` (rest pose saved as `RestOffsetCcf`); eight uint exclusion words per native cell (`ExclusionData` InlineArray(8)); `GroundClutterRenderer.ExclusionData.AllIncluded`. No private placement fields are used.
- `ColliderTemplate.VolumeCubicMetres` (virtual, base 0) and `ShapeOffsetCollider` overridden by the private hull; Bepu `ConvexHull.FaceToVertexIndicesStart`, `GetVertexIndicesForFace`, `GetPoint`. `ClutterObjectTemplate.AngularDamping`; `GroundClutterMaterialReference.KeepBackfaceNormals` and (5707) `UsesCustomNormals`.
- `GroundClutterLodReference.BuildMaterialIndirection`; private setters of `MeshReference.HostPrimitives` and `PrimitiveMaterialIds`.
- `ModLibrary.AllMeshes`, `AllFiles`, `AllGltfs`; `SerializedCollection<T>.GetList`; glTF model/named mesh indexes and `MeshReference.Load(..., createDeviceMesh: false)`.
- `StagingPool._submitted`, `_commandBufferIndex`: discard the outer transaction's unsubmitted command buffers after preparation failure before pool disposal.
- `ClutterEcotypePhysicalData._compoundShapes`, `_primitiveShapes`: reachable partial-shape retirement; `ConstraintSim.UnlockShapes`, Bepu shape ownership/removal.
- Partial retirement inspects direct public/nonpublic instance fields only on the seven captured native ownership classes (5919 added `GroundClutterGpuResources`; its borrowed `Bvh` is not walked). `GroundClutterTerrainData` is disposed after its `CurvatureBuffer` is released, because 5919 moved that buffer out of `Dispose`. It recognizes `BufferEx`, `BufferPartitionInfo`, mapped memories, descriptor pools/layouts, samplers/image views, `SimpleVkTexture`, `SimpleComputePipeline`, `SimpleGraphicsPipeline`, and their collections. Fields holding `MappedMemory` at any nesting (`FieldShapes.Holds`; 5638 `MappedMemory[][]`) are unmapped before any other field is released. Physical `MeshAtlas`/`PlacementData` references are borrowed and skipped. Resource field layout changes require re-audit. The 5707 re-audit found only the new `_opaqueDepthNormalPrePassRenderers` pipeline list, which the collection rule covers. The 5949 re-audit moved the cell-grid GPU fields (unchanged shapes) to `GroundClutterGpuResources` and made `_depthNormalPrePassBlendState` static, so the instance-only walk never disposes it.
- KSA 5629+ residency surface (public, no reflection): `ViewportRegistry.Views`, `ClutterEcotypeRenderData.ReleaseViewport(int slot)` (5919; formerly `IViewport`), `ResolvePendingReleases()`, (5919) `DisposePlanetResources()`, `GenerationViewport`, and (5875) `GroundClutterRenderer.HasViewportSlot`/`ResolveViewportSlot` — residency is indexed by a clutter slot only Main/Secondary viewports own, not `IViewport.ShaderSlot`. The release sequence must keep mirroring `GroundClutterRenderer.OnViewportUnregistered`, including its `HasViewportSlot` guard; if residency moves to the renderer or gains other per-viewport state, `ClutterResidency` needs review.
- `GroundClutterGpuMaterial` native layout, texture bindless IDs, flags bits 31/30 reserved by Pebbles, and the shader's `materialData.flags`, `diffuseTextureId`, `globalTextures`, `textureSampler`, `inUv`, diffuse conversion and terrain modulation statement.
- KSA 5903+ snap-to-mesh surface (public, no reflection), read by `ClutterSnapToMesh` before any allocation: `CelestialTemplate.MeshCollection.Value.LastMesh`, `CubeMesh.TopLevelAccelerationStructure` (ray queries) / `Bvh` (fallback), `CubeMesh.ClutterPatchRadians`, `CubeMesh.GetMaxBillboardSnapStep()`, `Renderer.RayQueriesSupported`, `CubeCellGrid.GetCellWidth(2πR, separation)`. These mirror `GroundClutterGpuResources.BuildFrameResources`/`CreateRayQueriesGenerateWrites` and `PlanetMeshLibrary.GetEcotypeRangeMeters`; if the game changes how it decides or sizes the structure, `SnapToMeshPolicy` must follow.
- Native 256 candidates/cell, 16 physical scale buckets, five LODs, uint object/material indirection and transformed position/normal/UV layout; `CubeCellGrid.GetCellWidth` call convention follows the renderer's actual MeanRadius argument.

## Independent Workshop preview

`Preview/` owns a Vulkan dynamic-rendering target; it does not lease a stock viewport or
patch a camera. `Program.GetRenderer()` supplies the device/allocator/graphics queue;
`ShaderModuleUtils.FromString` compiles the embedded `Workshop.vert.glsl` and
`Workshop.frag.glsl`. Preview color is `R8G8B8A8UNorm`, depth `D32SFloat`, one sample.
`PreviewVertex` is 32 bytes (position 0, normal 12, UV 24); `PreviewPush` is 112 bytes
(matrix 0, camera 64, maps 80, options 96), checked at runtime. The private descriptor
layout has five combined image samplers at bindings 0–4. Native mesh streams must be
float3 position/normal, float2 UV; indices are uint or converted ushort.
`AssetUploadSubmission` owns command pool/buffer/fence submission and completion;
`PreviewTarget` transitions color between attachment-write and sampled-read and registers
its image through the ImGui texture API. Resize/replacement/release wait for GPU completion.
Camera math and collider gizmos remain managed, with no Bepu simulation in the editor.

## Failure handling and verification limits

Preparation failures preserve the active graph. Completed resources use normal native disposal; interrupted construction uses once-only best-effort retirement of reachable owned fields so partial native initializers do not stop at their first null field. Cleanup errors retain the failed bundle and are exposed through controller `Faults`; they are not retried blindly because native disposal is not idempotent. Runtime records expose ecotype/material/repeated-vertex counts. The outer pool's partial commands are discarded; nested native pools own their submission/wait lifecycle.

This is not a claim of complete native allocation rollback: native constructors can allocate local buffers/textures/image views/compound children before publishing them to an object field. A native allocation failure in such a window may leave resources that Pebbles cannot reach, requiring renderer restart or game restart. Constructor capture and reachable cleanup do not solve that native ownership gap. Native draw, shadow, collision, bindless recycling, Vulkan resource failure and device-loss paths require in-game acceptance testing.

Acceptance must include stock capture/apply visual parity; multiple bodies sharing stock meshes/materials; native/source-color materials and sRGB formats; tiny visuals with large retained colliders; primitive and nondegenerate compound/hull collision; no-collision variants; all five LODs; spacing A→B→A and A→B→restore exclusions; queued release while hidden; graphics rebuild and renderer recreation; unload while solvers were previously scheduled; and deliberate preparation/retirement failures. Current source-color GLSL has been checked offline against the real game includes with both default and all optional material defines; native appearance remains unverified.

## Source evidence

The investigation and detailed native line map are retained in [the source map](../plans/PEBBLES_SOURCE_MAP.md). These explain the native system; the current implementation and limitations above govern shipped behavior.

## Shared GLB import/discovery

`ClutterAssets.ImportGlb` now copies through `ksa-abstractions.lib/GlbLibrary.Files` before the existing
`GlbImportLibrary.Import` path. All new `GlbIdentity` paths target `.unscience/glbs`. The browser and
pasted-path flows converge here. Main and Workshop hull mesh pickers include lazy catalog choices;
`ResolveSelection` imports/freezes their exact hash before recipe assignment. `ResolveMesh` rejects
unfrozen file choices. Catalog scans (every two seconds) list files without native allocation or JSON
load; explicit selection uses the unchanged game-facing geometry/material importer and borrower's
retirement rules. Imported versions remain stable if a shared file changes. Existing legacy content
ids still resolve through their recorded path/hash; no live overrides are rewritten by discovery.

Removed local UI surface `Import/GlbFileBrowser`; replaced with shared `LibraryFileBrowser` used by
PNG and sounds. `SharedFileLibrary` enforces GLB's existing 128 MiB maximum before copying. No new
Harmony patch, reflection lookup, game member, shader or GPU layout. Public library surface now
includes `RefreshSharedLibrary`, `ResolveSelection`, `RegistryDiscovered` and `MeshLabel`.
Managed parser/texture/Workshop tests plus copied-catalog/identity checks pass, and the full solution
builds. Native rendering/resource retirement retain existing live verification requirements.

## Save/load adapter (feature/saves)

`PebblesSubmod.Saves` registers `pebbles` (v1, order 60) for `ClutterController.Live` applied recipes. Its reset
closes/releases Workshop previews, cancels pending actions and uses synchronous
`ClutterController.ResetForSaveLoad` to restore original celestial templates and native
clutter arrays before native world replacement. Baseline/body caches are cleared.
`ApplyForSaveLoad` validates, joins physics/GPU work, then invokes the existing `Apply`
transaction directly so missing renderer/asset and native failures reach the save status.
No solver/render handles are serialized.

KSA 5482 saves live clutter state natively (see the 5482 section). Division of ownership:

- **Native save:** each body's *stock-grid* masks and displaced records. When an enabled override uses the stock spacing, its live state is valid for the stock grid and saved as-is (object/scale IDs are override slots; the slot count is identical, so a vanilla load only renders stock meshes in those slots). Otherwise the `SerializeSave` postfix substitutes the remembered stock-grid state.
- **`pebbles.clutter-state` (new separate v1 record, order 61, `ClutterBodyState[]`):** override grids whose key differs from every stock grid of that body, captured after syncing the live placement into memory. Validation bounds 256 bodies, 64 grids/body, 262,144 cells and 65,536 displaced records per grid, faces 0–5, subcells < 256, scale IDs < 16, finite 3/4-component vectors and unique keys. Restore runs after `pebbles` reapplied the recipes, seeds only unknown non-stock grids into the live record (native state wins) and replays matching live grids. A body without an applied recipe warns and the record is retained by the coordinator. Its reset is a no-op because the memory lives on records that the `pebbles` reset discards.
- The existing `pebbles` payload is unchanged except for the additive nullable `MaterialRecipe.KeepBackfaceNormals` and (5707) `MaterialRecipe.UsesCustomNormals`. When either is absent or null, it inherits the stock material by `SourceId`. The record id and version are unchanged, and legacy records load unchanged. Saves without the new record restore recipes and stock-grid state only.

`GlbImportLibrary.ResolveSource` now resolves saved imported identities by
`GlbIdentity.LibraryFileName` within `GlbLibrary.Files`, retaining SHA-256 verification.
Identity parsing accepts Unix, drive-absolute Windows and UNC paths as metadata for
portable saves; it never reads or copies those stored absolute locations. Exact imported
content and budgets remain unchanged. Test foreign library roots, changed/missing files,
body reuse, suspended renderer ownership and failed native retirement.

Scene replay regenerates the applied recipe, then native stock-grid state and sidecar override-grid
state are carried into it. In-flight Bepu bodies are not checkpointed beyond the native record's
pose/velocity; pending (not yet synced) removals are lost exactly as in native saves.
