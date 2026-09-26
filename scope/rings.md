# Planetary rings — rocky-mcrock-face

## KSA 5482 (5438 → 5482) verification

Verified against `2026.9.22.5482` (NEW) vs `2026.9.10.5438` (OLD) with both supplied decomp/Content
trees. Managed/static only: the full solution build passes (no ring-specific managed test exists). No
native KSA run was possible.
Evidence: [KSA_5482_UPGRADE](../plans/KSA_5482_UPGRADE.md).

- **Bloomin Onion distant-sphere ring shadow: silent reflection miss, fixed.** Rev 5457 moved every
  material and ring field out of the push-constant struct `DistantSphereData`. That struct now holds only
  `WorldMatrix` and `EmissiveStrength`. The fields live in the new public struct `DistantSphereMaterialData`
  (`UseRingShadows :27`, `RingInnerRadius :33`, `RingOuterRadius :35`, `RingTextureId :25`,
  `SamplerClampId :23`, `RingNormal :9`), held in the new private field `DistantSphereRenderer._material`
  (`:29`).
  - The constructor bakes `SamplerClampId` at `:98` and the ring fields at `:110-118`.
  - Every `Render` recomputes `RingNormal` from the live reference (`:156-161`) and uploads `_material`
    to a per-frame UBO (`:162`).
  - The old `GetField("UseRingShadows")?.SetValue` on `_data` silently did nothing. A removed painted
    ring could therefore leave `UseRingShadows = 1` sampling a pruned bindless handle.
  - Fix: `bloomin-onion.lib/RingRendererRebuilder.cs:26,87-110` uses the typed
    `AccessTools.FieldRefAccess<DistantSphereRenderer, DistantSphereMaterialData>("_material")`. A rename
    now fails loudly; the error is logged and caught. The fix writes `UseRingShadows`, the radii and
    `RingTextureId`, which is set to 0 when the body has no rings, matching native ringless construction.
    `SamplerClampId` is no longer written.
  - The shader gates sampling on `material.useRingShadows` (`DistantSphere.frag:123`). The callers are
    unchanged (`RingDefinitionController.cs:97,113,133,194`).
- **Atmosphere ring shadows (rev 5470): a third consumer of ring data, read live.**
  `AtmosphereRenderer.FillPlanetAtmosphereData` (`:833`) reads `body.BodyTemplate.RingsReference` at
  `:851`: the band `Texture.Get().BindlessHandle`, `InnerRadius`, `OuterRadius` and
  `PlanetaryRingsRenderer.ComputeRingNormal`.
  - It runs per viewport per frame for every `AtmosphericBody`, from `BeginFrame` (`:358-373`) and
    `PrepareAtmosphereData` (`:793-801`).
  - `AtmosphereData` grew from 128 to 256 bytes, with new ring fields in `AtmosphereDataUbo.glsl:24-30`.
  - Sampling goes through the new shared `Common/RingShadows.glsl`, which is also included by
    `Planet/Planet.frag:88`, `DistantSphere.frag:11` and `Atmosphere/Atmosphere.comp:9`.
  - This data is not baked at construction, so both mods' edits reach the atmosphere on the next frame
    without a rebuild.
  - Saturn is an `AtmosphericBody` with rings. A Rocky band `Texture` swap therefore also changes Saturn's
    volumetric ring shadow. The shadow uses band `.a`, so an opaque picked texture gives a solid dark
    band, consistent with the existing surface shadow.
  - Bloomin rings on atmospheric bodies such as Earth now cast atmospheric shadows as soon as the template
    is assigned.
  - New hard invariant: a `RingsReference` on an `AtmosphericBody` must have a non-null `Texture` and radii,
    otherwise `BeginFrame` throws a NullReferenceException every frame. Bloomin always sets `Texture`
    (`RingReferenceBuilder.cs:74`). Rocky only replaces a band that is non-null (`RingSwapController.cs:137,174`).
  - The band handle is baked into a per-frame UBO, so Bloomin must keep its restore → WaitIdle rebuild →
    prune order.
- **Unchanged.**
  - `KSA.Rendering.Rings.Rendering/` is byte-identical, so the ctor-baking keystone (Rocky narrative #1)
    still holds.
  - The `PlanetTransparenciesRenderer` diff is cloud-upscaling renames, a new `Render(...)` parameter and
    return value, barrier helpers and volumetric-trail passes. The reflected fields `_ringsRenderer :41`,
    `_ringRendererCreated :47` and `_anyRings :69` are unchanged, as are `PopulatePlanets :151` (identical
    body) and the `RebuildFrameResources :352` gate (create branch `:382-385`).
  - `Program._planetTransparenciesRenderer :177`. `RebuildRenderer :5021` is the 5438 body plus one
    `SetCloudRenderer` line, and it still WaitIdles (`:5028`) before `RebuildFrameResources` (`:5042`).
  - `AtmosphereRenderer.AssignPlanetSlots` (`:308`) still keys on `AtmosphericBody`.
  - Every reference and asset class touched by Rocky or Bloomin is byte-identical, as are
    `PlanetRenderer`, `AstronomicalTemplate` and `GpuTextureSystem`. `SerializedCollection` only adds
    `Deregister`; `GetList` is at `:62`.
  - `GameSettings.ShowRings/ShowRingMeshes` (`:3261,3272`) have identical bodies. `StaticCelestial._distantRenderer`
    is still at `:8`.
- **Pre-existing, unchanged:** Rocky does not sync the distant-sphere band texture after a swap, so the
  far sphere keeps the stock band.
- **Live checks pending (native):**
  - Bloomin: add a ring to a ringless body, then zoom out to the distant sphere; the band shadow should
    appear, and clear again on remove.
  - Bloomin: a painted ring on Earth; the atmosphere shadow should appear on add and be clean on remove.
  - Bloomin: repeated apply/remove/prune, with no stale band in the atmosphere or on the far sphere.
  - Rocky: a band swap on Saturn; the atmosphere shadow should follow the band alpha, and restore should
    return the stock look.

## Verification — 5402 → 5438 (historical)

Both ring mods retain their catalog fields, mesh/texture backing fields and private renderer gates.
PlanetTransparenciesRenderer, StaticCelestial and DistantSphereRenderer are byte-identical; ring
renderer/data diffs only rename numeric temporaries. MeshReference retains HostPrimitives and adds
mesh-deformation bounds independently of ring strip conversion; its Z-radius fix is inherited.
No code migration required. Painted textures, ringless-system creation, rebuild/LOD/shadow behavior
and save/load still require native acceptance.

See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md). Older tables retain their dated line citations unless marked @5482.

Scope for **rocky-mcrock-face** (`rocky-mcrock-face.lib`, bundled in the unscience supermod), which
swaps the meshes and textures of KSA's planetary ring system (Saturn's rock field + 2D band) at
runtime. Written against KSA build **2026.8.22.5348**; re-verified against **2026.9.7.5402**, **2026.9.10.5438**
and **2026.9.22.5482** (see the top sections).

**Versions compared**
- Current: NEW = `2026.9.22.5482`, OLD = `2026.9.10.5438` (top section). Catalog baseline below:
- NEW = `2026.9.7.5402` — decomp root `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- OLD = `2026.8.22.5348` — decomp root `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`
- Line numbers below are NEW (5402) unless marked OLD or @5482; verified by grep/diff of both trees.

## How it hooks the game

**No Harmony patches.** The mod is a pure data-level swap: KSA's entire ring definition is public
XML-backed data (`AstronomicalTemplate.RingsReference` → `PlanetaryRingsReference` →
`RingObjectsReference` → `RingLodReference.MeshFileReference.Mesh`), and
`PlanetaryRingsRenderData` re-reads that tree from scratch every time the renderer is (re)built —
baking per-LOD index counts, the max bounding-sphere radius, and the material's bindless handles
into its UBO in the constructor. So the mod mutates the public reference tree (snapshotting
originals for restore) and then calls the public `Program.Instance.RebuildRenderer()` — the exact
path the game's own graphics settings use — to rebuild the ring render data with proper GPU sync.

The one genuinely non-public thing it does is **mesh conversion**: the ring pipeline draws
`MeshReference.DeviceMesh` (`SimpleVkMesh`, one vertex stream per attribute), which the game builds
only for `Simple` meshes. Part/subpart meshes are atlas-loaded `Interleaved` (DeviceMesh == null),
and flipping their flags in place would break IVA raytracing
(`KSA.Rendering.Raytracing/RaytracingRenderer.cs:1027` throws on a non-interleaved subpart). The
mod clones such meshes into a private `Simple` `MeshReference` that shares the retained CPU-side
`HostPrimitives` array (written via the auto-property backing field) and binds a `SimpleVkMesh` for
primitive 0.

## Update-risk narrative

1. **The ctor-baking contract is the design keystone.** The whole approach relies on
   `PlanetaryRingsRenderData`'s constructor reading `Lods[i].MeshFileReference.Get().Mesh`,
   `MaterialReference.{DiffuseReference,NormalReference,PBRMap}.Get().BindlessHandle`,
   `reference.Texture.Get().ImageView`, and Size/Density/RenderDistance/Thickness at build time
   (`KSA.Rendering.Rings.Rendering/PlanetaryRingsRenderData.cs:180-326` @5348). If a future build
   caches ring data elsewhere or stops rebuilding it in `RebuildFrameResources` →
   `PopulatePlanets`, Apply would silently stop having an effect (no crash).
2. **Only primitive 0 is drawn** (`RenderMeshes` uses `MeshLods[i].DeviceMesh`, i.e.
   `DevicePrimitives[0]` — `PlanetaryRingsRenderer.cs:597-603`). If the ring renderer ever starts
   drawing all primitives, converted clones (built with `PrimitiveCount = 1`) would need to bind
   every primitive.
3. **Vertex layout compatibility is an invariant, not a check.** The ring pipeline's
   `MakeMeshesVertexInput` is instance-uint + Position/Normal/Uv0 streams
   (`PlanetaryRingsRenderer.cs:880`), and `MeshReference.Load` imports exactly
   `Normals | UVs` with missing attributes default-filled (`MeshReference.cs:76-115`;
   `RenderCore.Gltf/GltfUtils.cs` fills absent streams). Any game change to either side breaks
   converted meshes with garbage rendering, not a compile error.
4. **RebuildRenderer alone does NOT rebuild ring data — the rings renderer must be disposed
   first.** `PlanetTransparenciesRenderer.RebuildFrameResources` (`:304`, branch `:330-337`) only calls
   `_ringsRenderer.RebuildFrameResources(...)` when `_ringRendererCreated` is true — that
   destroys/rebuilds pipelines and frame images but never re-runs `PopulatePlanets` (ctor-only,
   `PlanetaryRingsRenderer.cs:170`), so `PlanetaryRingsRenderData` (meshes, UBO, instances)
   survives untouched. Only the `else if (_anyRings) CreateRingsRenderer(...)` branch (`:334-337`)
   constructs a fresh renderer and re-reads the reference tree. The mod therefore: waits for the
   device (`Renderer.Device.WaitIdle()` — in-flight frames may reference ring GPU resources),
   calls the public `PlanetaryRingsRenderer.Dispose()` on the reflected instance, clears the
   private `_ringRendererCreated` flag, THEN calls `Program.RebuildRenderer(bool = false)`
   (`KSA/Program.cs:4913`, which also WaitIdles at `:4920`) so the game's own create branch
   rebuilds everything — including instance buffers resized for density/render-distance changes
   (`PopulatePlanets` runs before `CreateMeshRenderingResources` in the ctor). Called from the
   ImGui phase — the same phase the game's settings Apply uses. If this branching ever changes
   (e.g. `RebuildFrameResources` starts re-populating data itself), the dispose becomes redundant
   but harmless.
5. **The ControlTexture is deliberately NOT swappable.** `PlanetaryRingsRenderData.UpdateData`
   CPU-samples it every frame assuming ≥4 bytes/texel uncompressed RGBA
   (`SampleRingTexture`, `PlanetaryRingsRenderData.cs:100-118`); a compressed ktx2 there means
   garbage indexing. The band `Texture` is GPU-sampled only, so it is safe to swap.
6. **Restore-before-dispose ordering.** Converted clone meshes carry a finalizer that frees their
   GPU buffers (`MeshReference.~MeshReference`). The controller restores defaults and rebuilds the
   renderer **before** disposing clones, and caches clones for its lifetime so a GC can never free
   a mesh the renderer still references.
7. **Live (not ctor-baked) ring consumers (@5482).** The planet surface shadow (`PlanetRenderer`) and,
   since rev 5470, the volumetric atmosphere shadow (`AtmosphereRenderer.FillPlanetAtmosphereData`,
   `:833-862`) read `RingsReference.Texture`/radii every frame for atmospheric bodies. A band swap on
   Saturn changes both immediately, sampled by band alpha. The band `Texture` must never be set to null
   on an atmospheric body (the atmosphere dereferences it every frame); `RingSwapController.cs:137,174`
   only assigns non-null bands. Catalog textures are never freed, so no lifetime issue.

### 5348 → 5402 (2026-09-02)

- ✅ **No code change.** The entire `KSA.Rendering.Rings.Rendering/PlanetaryRingsRenderer.cs` diff
  (167 lines) is the game-wide `Viewport` → `IViewport` retype plus `inViewport.Index` →
  `inViewport.ShaderSlot` inside `RenderRingFarSide`/`RenderRingNearSideAndVolumetrics`/`UpdateMeshes`/
  `RenderMeshes` (`:347,373,485,571`). `PopulatePlanets` (`:324`), `ComputeRingNormal` (`:455`),
  `Dispose` (`:473`), the `MeshLods[i].DeviceMesh` draw (`:597`) and `MakeMeshesVertexInput` (`:880`)
  did not move. `PlanetaryRingsRenderData.cs` and `RingMeshesDistribution.cs` are **byte-identical**,
  so the ctor-baking contract (narrative #1) is untouched.
- ✅ `KSA/PlanetTransparenciesRenderer.cs` diff = `UpdateRingMeshes`/`RenderRingMeshes`/`Render` taking
  `IViewport` and one `ShaderSlot` rename; `_ringsRenderer :40`, `_ringRendererCreated :46`,
  `_anyRings :68`, `PopulatePlanets :150`, `RebuildFrameResources :304` (create branch `:330-337`) and
  `DisposeRingRenderer :354` are unchanged. `Program.RebuildRenderer` moved to `:4913` (was `:4742`);
  its body still `Device.WaitIdle()`s (`:4920`) before `_planetTransparenciesRenderer.RebuildFrameResources`
  (`:4934`); the only body changes are `MainViewport.Resize` → `((IViewportLifecycle)MainViewport).ApplyResize`,
  `_compositeRenderer[0]` → `[MainViewport.ShaderSlot]` and the CMAA2 loop iterating
  `ViewportRegistry.Views`. `Program._planetTransparenciesRenderer` is at `:176`, `Instance :453`,
  `GetRenderer :558`.
- ✅ Every reference/asset class the swap touches is byte-identical (`MeshReference`, `MeshFileReference`,
  `RingLodReference`, `RingObjectsReference`, `PbrMaterialReference`, `TextureReference`,
  `SerializedCollection`, `Gltf2Reference`, `FileReference`, `DeviceMeshInterleaved`), as is
  `GameSettings.cs`; `ModLibrary.AllFiles/AllGltfs/AllMeshes` are still internal static fields at
  `:68,76,80`. `RaytracingRenderer.cs:1027-1029` still throws on a non-interleaved subpart, so the
  clone-instead-of-flip design remains necessary. `RenderCore.Animation/Skeleton.cs` gained
  `CloneRig()` — irrelevant to the bind-pose glTF import (`MeshReference.Load` unchanged).
- ℹ️ Content diffs (`RayIntersections.glsl` cylinder fix, `ModelPbr.frag`/`ModelNormal.frag` two-sided
  normals, new `StaticObjectNormalIndirect.frag`, `ParachuteAssets.xml`) touch no ring shader or asset.
- Revisions 5349–5400 are unlogged (only rev 5401 has a changelog entry); the diff above is the evidence.
- **Live pass**: none required. If convenient, one Apply on Saturn to confirm the rebuild path still
  re-reads ring data after the `RebuildRenderer` body changes.

## Touchpoints

Decomp paths relative to `~/repos/meow-sci/ksa-game-assemblies/current/decomp` (NEW = 5402).

| # | Game member | Kind | Decomp path | Mod code ref | 5402 |
|---|---|---|---|---|---|
| 1 | `AstronomicalTemplate.RingsReference : PlanetaryRingsReference?` (public field, via `Celestial.BodyTemplate`) | direct API | `KSA/AstronomicalTemplate.cs:66`; `KSA/Celestial.cs:83` | `RingSwapController.RefreshBodies` | OK |
| 2 | `PlanetaryRingsReference.{Texture, ControlTexture : TextureReference, RingObjects : RingObjectsReference}` (public fields) | direct API | `KSA/PlanetaryRingsReference.cs:23,26,35` | `RingSwapController.{Apply,Restore,TakeSnapshot}` | OK |
| 3 | `RingObjectsReference.{Lods : List<RingLodReference>, MaterialReference : PbrMaterialReference, Size/Thickness/RenderDistance : DistanceReference, Density : DoubleReference, NumLods}` | direct API | `KSA/RingObjectsReference.cs` | `RingSwapController` | OK |
| 4 | `RingLodReference.{MinScreenSizePixels : float, MeshFileReference : MeshFileReference?}` | direct API | `KSA/RingLodReference.cs:8,11` | `RingSwapController`, submod UI (LOD labels) | OK |
| 5 | `MeshFileReference.{Get() : MeshFileReference, Mesh : MeshReference?}` — **`Mesh` is the swap slot** | direct API | `KSA/MeshFileReference.cs:15,28` | `RingSwapController.{Apply,Restore}` | OK |
| 6 | `PbrMaterialReference.{DiffuseReference : TextureReference?, NormalReference : TexturePowerReference?, PBRMap : TextureReference?}` (public fields) | direct API | `KSA/PbrMaterialReference.cs:10-17` | `RingSwapController` | OK |
| 7 | `MeshReference` — public `Id/Simple/Interleaved/PrimitiveCount/BoundingSphereRadius` fields, `HostPrimitives`/`DevicePrimitives` get-only props, `DeviceMesh => DevicePrimitives[0]`, `Bind(Renderer, StagingPool)`, `Dispose()` | direct API | `KSA/MeshReference.cs:17-58,120,145` | `RingMeshFactory`, `RingAssetCatalog` | OK — **multi-primitive shape is new @5348** (`DevicePrimitives[]` replaced the old single `DeviceMesh` field) |
| 8 | `MeshReference.<HostPrimitives>k__BackingField : MeshAsset[]` (auto-prop backing field) | **reflection (string)** | `KSA/MeshReference.cs:40` | `RingMeshFactory` static field lookup — null-checked; Apply fails with a UI error, never crashes | OK |
| 9 | `ModLibrary.AllMeshes : SerializedCollection<MeshReference>` / `ModLibrary.AllFiles : SerializedCollection<FileReference>` / `ModLibrary.AllGltfs : SerializedCollection<Gltf2Reference>` (internal static fields) | **reflection (string)** | `KSA/ModLibrary.cs:80,68,76` | `RingAssetCatalog.Collection<T>` — same pattern/names as parts-now `GameRegistry` (AllMeshes/AllFiles already on the watchlist) | OK |
| 9b | `Gltf2Reference.{Id, Source : FileReference?}` + `FileReference.ModPath` · `GltfUtility.LoadModel(string) : Gltf` + `Gltf.Meshes[].Name` (JSON-only parse for the catalog) · `GltfLoader(string)` ctor + `MeshReference.Load(GltfLoader, int mesh, createDeviceMesh: false)` (conversion — the exact import `MeshFileReference.DoLoad` runs for the stock ring rocks; skinned meshes import in bind pose) | direct API | `KSA/Gltf2Reference.cs:10`; `KSA/FileReference.cs:24`; `Brutal.GltfApi/GltfUtility.cs:38`, `GltfLoader.cs:23`; `KSA/MeshReference.cs:76` | `RingAssetCatalog.RefreshGltfMeshes`, `RingMeshFactory.GetRingUsableFromGltf` — makes character/MMU/helmet meshes (the glTF-file pipeline, never in `AllMeshes`) selectable | OK |
| 9c | `DeviceMeshInterleaved.IndexCount` (public field) · `SimpleVkMesh.IndexCount` | direct API | `KSA/DeviceMeshInterleaved.cs:117`; `RenderCore.Mesh/SimpleVkMesh.cs:26` | `RingAssetCatalog.GetMeshIndexCount`, `RingMeshFactory.GetConvertedIndexCount` — UI triangle-cost readout only | OK |
| 10 | `SerializedCollection<T>.GetList() : List<T>` (live list — copied before iteration) | direct API | `KSA/SerializedCollection.cs:62` @5482 | `RingAssetCatalog.Refresh` | OK (5482 only adds `Deregister`; the game deregisters runtime distant glints, which never appear in the mesh/file/gltf catalogs) |
| 11 | `TextureReference.{Id, BindlessHandle : int}` + `TexturePowerReference : TextureReference` | direct API | `KSA/TextureReference.cs:70`; `KSA/TexturePowerReference.cs` | `RingAssetCatalog` (handle==0 ⇒ excluded), `RingSwapController` | OK |
| 12 | `Program.{Instance : static Program, GetRenderer() : static Renderer, RebuildRenderer(bool = false)}` | direct API | `KSA/Program.cs:452,557,5021` @5482 | `RingSwapController.RebuildRenderer`, `RingMeshFactory` | OK — body retyped for `IViewport`/`ShaderSlot` (5402); @5482 adds one `_volumetricExhaustRenderer.SetCloudRenderer` line, WaitIdle (`:5028`) still precedes `RebuildFrameResources` (`:5042`) |
| 13 | `Program._planetTransparenciesRenderer` → `PlanetTransparenciesRenderer.{_ringsRenderer, _ringRendererCreated}` (private fields) + public `PlanetaryRingsRenderer.Dispose()` (typed) + `Renderer.Device.WaitIdle()` | **reflection (string)** + direct API | `KSA/Program.cs:177` @5482; `KSA/PlanetTransparenciesRenderer.cs:41,47` @5482 (dispose helper unchanged); `PlanetaryRingsRenderer.cs:473`; `Brutal.VulkanApi.Abstractions/DeviceExtensions.cs` | `RingSwapController.{IsRingsRendererCreated, DisposeRingsRendererForRecreation}` — the dispose-for-recreation step that makes the rebuild actually re-read ring data (narrative #4). A field rename degrades to a frame-resources-only rebuild: Apply hitches but changes nothing — **the original symptom**, so a silent break here is user-visible immediately | OK |
| 14 | `Renderer.Allocator.CreateStagingPool(renderer.GraphicsAndCompute, 1)` + `StagingPool` dispose = submit+wait; `SimpleVkMesh` built by `MeshReference.Bind` | direct API (render) | `Brutal.VulkanApi.Abstractions/StagingPoolExtensions.cs:5`, `StagingPool.cs:167`; `RenderCore.Mesh/SimpleVkMesh.cs:69` | `RingMeshFactory.GetRingUsable` | OK |
| 15 | `GameSettings.{ShowRings(), ShowRingMeshes()} : static bool` | direct API | `KSA/GameSettings.cs:3261,3272` @5482 | submod UI status hints | OK |
| 16 | `Universe.CurrentSystem.All.OfType<Celestial>()` | direct API | `KSA/Universe.cs:94`; `KSA/CelestialSystem.cs` | `RingSwapController.RefreshBodies` | OK |
| 17 | Consumer contract (not called, relied upon): `PlanetaryRingsRenderData` ctor bakes `LodProperties[i].Y = MeshLods[i].DeviceMesh.IndexCount`, `MeshCullingRadius = max BoundingSphereRadius`, `MeshDiffuseId/NormalId/PbrId = …BindlessHandle`; `PlanetaryRingsRenderer.{PopulatePlanets, RenderMeshes(Celestial, CommandBuffer, IViewport, int)}` | behavioral invariant | `KSA.Rendering.Rings.Rendering/PlanetaryRingsRenderData.cs:180-326` (byte-identical 5348↔5402); `PlanetaryRingsRenderer.cs:324,571-603` | design keystone — see narrative #1-#3 | OK (`RenderMeshes` param retyped `Viewport`→`IViewport`, `Index`→`ShaderSlot`; draw logic unchanged) |

Related but **not** integration points: `RingSelection` (mod-local, session-only state — overrides
are deliberately not persisted; a game restart is back to stock).

---

# Runtime ring definition — bloomin-onion

Scope for **bloomin-onion** (`bloomin-onion.lib`, bundled in the unscience supermod), which builds
brand-new `PlanetaryRingsReference` trees at runtime and puts them on any celestial. Written against
KSA build **2026.8.22.5348**, re-verified against **2026.9.7.5402** (same NEW/OLD roots as above), then
**5438** and **5482** (top sections; the 5482 distant-sphere `_material` fix and atmosphere consumer).
Shares rocky-mcrock-face's catalog + mesh conversion (touchpoints #7-#11
above apply unchanged; bloomin-onion's own rows are numbered B1+ below).

## How it hooks the game

**No Harmony patches.** Same data-level approach as rocky, one level up: instead of mutating an
existing tree it *constructs* one — every reference class the XML loader would produce
(`PlanetaryRingsReference`, `PlanetaryRingsVolumeReference`, `RingRaymarchingStepReference`,
`RingObjectsReference`, `RingLodReference`, `MeshFileReference`, `PbrMaterialReference`, the
`Distance/Radian/Double/BoolReference` value wrappers) — and assigns it to
`celestial.BodyTemplate.RingsReference` (public field; original snapshotted per template).

Because a body can *gain or lose* rings, the rebuild has one extra step over rocky's: the
transparencies renderer's body list (`_planetsWithTransparencies` + `_bodiesSortedBackToFront`,
rebuilt by its **public** `PopulatePlanets()`) and the private `_anyRings` flag that gates
`CreateRingsRenderer` in `RebuildFrameResources` are refreshed before `Program.RebuildRenderer()`.

Painted bands are runtime textures: a `TextureReference` subclass whose private-set `TextureAsset`
is seeded from a `GenericTexture` (`Brutal.TextureApi.Abstractions`) and then bound through the
game's own virtual `Bind` — so the ring renderer's `Texture.Get().ImageView` / `BindlessHandle` /
`ControlTexture.Get().TextureAsset.Texture.Data` reads all work unchanged.

## Update-risk narrative

1. **Reference-tree construction contract.** The builder must produce exactly what
   `PlanetaryRingsRenderData`'s ctor + `UpdateData` dereference: `Texture/ControlTexture.Get()`,
   `Volume.{MinThickness,MaxThickness,MinRenderDistance,MaxRenderDistance,Step.{Scale,MinSize,MaxSize},FadeToMeshes}`,
   `RingObjects.{Lods[i].MeshFileReference.Get().Mesh(.DeviceMesh/.BoundingSphereRadius),
   MaterialReference.{DiffuseReference,NormalReference,PBRMap}.Get().BindlessHandle,
   Size,Thickness,RenderDistance,Density,NumLods}`, `DetailScale`, `Inner/OuterRadius`,
   `Inclination`, `LongitudeOfAscendingNode`, `DefinitionFrame`
   (`PlanetaryRingsRenderData.cs:65-86,180-326` @5348). A new required field on any of these
   classes (left at its C# default) either NREs at rebuild (caught, reported, reference reverted)
   or renders wrong. `rings.IsValid()` is **not** usable as a gate: `DistanceReference.IsValid()`
   (`KSA/DistanceReference.cs:162`) demands `|value| > 100 km`, which the stock rock size / field
   thickness / draw distance / step sizes fail — the game never enforces it on rings.
   **@5482** the atmosphere pass also dereferences `Texture.Get().BindlessHandle`, `InnerRadius`,
   `OuterRadius` and the ring normal every frame for atmospheric bodies
   (`AtmosphereRenderer.FillPlanetAtmosphereData`, `:833-862`), so a built reference with a null
   `Texture` would throw every frame, not just at rebuild. The builder always sets it (`RingReferenceBuilder.cs:74`).
2. **`Get()` self-resolution.** Freshly constructed `MeshFileReference` / `TextureReference` /
   `PbrMaterialReference` return `this` from `Get()` because their private `_isReference` defaults
   to false (only `OnDataLoad` can flip it). If a future build makes `Get()` consult `ModLibrary`
   unconditionally, every built reference would throw at rebuild.
3. **Angle normalization mirrors `PlanetaryRingsReference.OnDataLoad`** (`MathEx.ToDeviationAngle`
   / `ToCompassAngle`, `KSA/PlanetaryRingsReference.cs:53-60`). Drift here is cosmetic (plane
   orientation), not a crash.
4. **Ecliptic frame dereferences `celestial.Parent.GetCce2Cci()`** in both
   `PlanetaryRingsRenderData` ctor and `PlanetaryRingsRenderer.ComputeRingNormal`; the builder
   refuses the ecliptic frame for a body without a parent.
5. **The control strip is CPU-sampled** (`SampleRingTexture`, 4 bytes/texel assumption) — painted
   control strips are RGBA8 by construction, and picked ones are filtered through
   `FormatDescriptor.{IsBlockCompressed, BlockSizeInBytes == 4}`.
6. **`_anyRings` is the on/off switch for the whole rings renderer.** If a build renames it, the
   `SetFieldValue` is a silent no-op: a system with no stock rings would never get a rings renderer
   (Apply "succeeds" but nothing renders) — immediately user-visible, never a crash. In a system with
   stock rings (Saturn) everything still works because `_anyRings` is already true.
7. **Painted textures' GPU lifetime** is tied to the renderer: freed only after a rebuild that no
   longer references them (`RingTextureFactory.PruneExcept`, controller `Dispose` order:
   restore → rebuild → dispose textures/clones). `GenericTexture` has no `Dispose`; its native
   8 KB buffer per strip is left to the GC finalizer (if any) — negligible.
8. **Distant-sphere ring shadow** (`DistantSphereRenderer._material.UseRingShadows/...` since 5482;
   `_data` before) is baked at `StaticCelestial` construction and re-uploaded every frame; the mod
   refreshes those struct fields through a typed `FieldRefAccess` on the private `_material` field,
   best-effort and guarded (logged on failure). Failure = the far-away sprite lacks/keeps a shadow band,
   or (if `UseRingShadows` stays set after a painted band is pruned) samples a freed bindless slot.
   At 5482 the old `_data` reflection had silently become a no-op (rev 5457); see the top section.

### 5348 → 5402 (2026-09-02)

- ✅ **No code change.** Everything the builder constructs is byte-identical this span
  (`PlanetaryRingsReference`, `PlanetaryRingsVolumeReference`, `RingRaymarchingStepReference`,
  `RingObjectsReference`, `RingLodReference`, `MeshFileReference`, `PbrMaterialReference`,
  `DistanceReference`, `RadianReference`, `DoubleReference`, `BoolReference`, `MathEx`,
  `AstronomicalTemplate`, `TextureReference`, `SerializedId`, `DistantSphereData`,
  `GpuTextureSystem`), so narratives #1–#3 and #5 stand unchanged. `Get()` self-resolution (#2) is
  unaffected — `MeshFileReference.Get()` is still `:28`.
- ✅ The three reflected private fields (`_ringsRenderer :40`, `_ringRendererCreated :46`,
  `_anyRings :68`) and the public `PopulatePlanets() : bool` (`:150`) did not move; the
  `RebuildFrameResources` gate (`!_ringRendererCreated && _anyRings`, `:330-337`) is unchanged (#6).
- ✅ `StaticCelestial._distantRenderer` (`:8`) and `DistantSphereRenderer._data` (`:24`, ring fields
  written at `:59-65`) are unchanged; `DistantSphereRenderer` only had `Render(...)` retyped to
  `IViewport`/`ShaderSlot`. `StaticCelestial.RenderSphereThisFrame` is now `new bool[8]` indexed by
  `ShaderSlot` (was `Program.ViewportCount`/`Index`) — not touched by the mod (#8).
- ✅ `Celestial.{Id, MeanRadius, BodyTemplate, Parent}` (`:104` via `Astronomical`, `:91`, `:83`, `:73`)
  unchanged; `Celestial.cs`'s own diff is UI (`IGameViewport`) plus terrain-modifier maths that now
  uses `MeanRadius` instead of `RenderData.SurfaceRadius` (`:825-857`) — unrelated (#4).
- ✅ `PlanetRenderer.cs:1985-1993` (per-frame ring-shadow read) and
  `AtmosphereRenderer.AssignPlanetSlots` (`:305-312`) are unchanged (B10).
- **Live pass**: none required for bloomin-onion specifically.

## Touchpoints (bloomin-onion)

| # | Game member | Kind | Decomp path | Mod code ref | 5402 |
|---|---|---|---|---|---|
| B1 | `PlanetaryRingsReference` (all public fields: `DefinitionFrame, Inclination, LongitudeOfAscendingNode, InnerRadius, OuterRadius, Texture, ControlTexture, DetailScale, Volume, RingObjects`; `IsValid()` deliberately unused — narrative #1) · `PlanetaryRingsVolumeReference.{MinThickness, MaxThickness, MinRenderDistance, MaxRenderDistance, Step, FadeToMeshes}` · `RingRaymarchingStepReference.{Scale, MinSize, MaxSize}` · `RingObjectsReference.{Name, Thickness, Size, RenderDistance, Density, Lods, MaterialReference}` · `RingLodReference.{MinScreenSizePixels, MeshFileReference}` · `MeshFileReference.Mesh` · `PbrMaterialReference.{DiffuseReference, NormalReference, PBRMap}` — **constructed**, not just mutated | direct API | `KSA/PlanetaryRingsReference.cs`; `KSA/PlanetaryRingsVolumeReference.cs`; `KSA/RingRaymarchingStepReference.cs`; `KSA/RingObjectsReference.cs`; `KSA/RingLodReference.cs`; `KSA/MeshFileReference.cs:15`; `KSA/PbrMaterialReference.cs:10-17` | `RingReferenceBuilder.Build`, `RingDefinitionSerializer.FromReference` | OK |
| B2 | `DistanceReference(double, DistanceUnit)` / `(double meters)` · `RadianReference(double radians)` + `.ToDegrees()` · `DoubleReference.FromValue` · `BoolReference(bool)` · `DistanceReference.{InMeters(), InKilometers()}` · `MathEx.{ToDeviationAngle, ToCompassAngle}(double)` · `OrbitDefinitionFrame` | direct API | `KSA/DistanceReference.cs:105-140`; `KSA/RadianReference.cs:23,66`; `KSA/DoubleReference.cs:44`; `KSA/BoolReference.cs:14`; `KSA/MathEx.cs:178,189`; `KSA/OrbitDefinitionFrame.cs` | `RingReferenceBuilder`, `RingDefinitionSerializer` | OK |
| B3 | `AstronomicalTemplate.RingsReference` (public field, **written**) via `Celestial.BodyTemplate : CelestialTemplate` · `Celestial.{Id, MeanRadius, Parent}` | direct API | `KSA/AstronomicalTemplate.cs:66`; `KSA/Celestial.cs:73,83,91` | `RingDefinitionController.{Apply, Remove, RestoreTemplate, HasStockRings}`, `RingReferenceBuilder.Validate` | OK |
| B4 | `PlanetTransparenciesRenderer.PopulatePlanets() : bool` (public) | direct API | `KSA/PlanetTransparenciesRenderer.cs:151` @5482 (body identical) | `RingRendererRebuilder.Rebuild` — refreshes `HasRings` per body + `_bodiesSortedBackToFront` sizing | OK |
| B5 | `Program._planetTransparenciesRenderer` → `PlanetTransparenciesRenderer.{_ringsRenderer, _ringRendererCreated, _anyRings}` (private fields) + public `PlanetaryRingsRenderer.Dispose()` + `Device.WaitIdle()` + `Program.RebuildRenderer()` | **reflection (string)** + direct API | `KSA/Program.cs:177,5021` @5482; `KSA/PlanetTransparenciesRenderer.cs:41,47,69,352-385` @5482 | `RingRendererRebuilder.{Rebuild, DisposeRingsRenderer, IsRingsRendererCreated}` — `_anyRings` is the only field new vs rocky (narrative #6) | OK |
| B6 | `TextureReference` subclassing: public `Category, Width, Height, Manifest, BindlessHandle, Bind(Renderer, StagingPool)` (virtual), `Dispose(Device)`, `SetHash()` · `TextureReference.<TextureAsset>k__BackingField` (private-set auto-prop) | direct API + **reflection (string)** | `KSA/TextureReference.cs:38-70` (fields), `:125` (`virtual Bind`), `:77` (`Dispose`); `KSA/SerializedId.cs:58` | `PaintedTextureReference.Create/Release` (`:19-20,50,60-61`) — null-checked; a miss disables Painted mode in the UI (`IsSupported`) with a clear message | OK |
| B7 | `RenderCore.TextureAsset(ITexture, string)` ctor · `Brutal.TextureApi.Abstractions.GenericTexture.Defaults.RGBA8UNorm(int2)` + `.Data` · `TextureFormatExtensions.Descriptor()` → `FormatDescriptor.{IsBlockCompressed, BlockSizeInBytes}` · `TextureAsset.Texture.Format` | direct API | `RenderCore/TextureAsset.cs:21`; `Brutal.TextureApi.Abstractions/GenericTexture.cs:78,122`; `FormatDescriptor.cs` | `PaintedTextureReference.Create`, `RingReferenceBuilder.IsCpuSampleable` | OK |
| B8 | `StaticCelestial._distantRenderer` (string, walked up the hierarchy) → `DistantSphereRenderer._material` (**private field, string via typed `AccessTools.FieldRefAccess<DistantSphereRenderer, DistantSphereMaterialData>`**; public struct `KSA.DistantSphereMaterialData`, public fields `UseRingShadows, RingInnerRadius, RingOuterRadius, RingTextureId` written; `SamplerClampId` and `RingNormal` left to native) | **reflection (string)**, cosmetic + GPU-handle hygiene | `KSA/StaticCelestial.cs:8`; `KSA/DistantSphereRenderer.cs:29` (field), `:98,110-118` (ctor bake), `:156-162` (per-frame normal + UBO upload); `KSA/DistantSphereMaterialData.cs:9-35` | `RingRendererRebuilder.SyncDistantSphereShadow` (`:26,87-110`) — typed ref access, try/catch + log | ⚠️ **fixed @5482** (rev 5457 moved the ring fields from `_data`/`DistantSphereData` into `_material`; the old `GetField(...)?.SetValue` silently no-op'd) |
| B9 | `GameSettings.{ShowRings(), ShowRingMeshes()}` · `CelestialProvider.GetAllCelestials()` (ksa-abstractions → `Universe.CurrentSystem.All`) | direct API | `KSA/GameSettings.cs:3261,3272` @5482; `KSA/Universe.cs:94` | submod UI / `BloominOnionSubmod.RefreshBodies` | OK |
| B10 | Consumer contract (relied upon): `PlanetTransparenciesRenderer.RebuildFrameResources` takes `CreateRingsRenderer` only when `!_ringRendererCreated && _anyRings`; `PlanetaryRingsRenderer.PopulatePlanets` iterates `Universe.CurrentSystem.All.OfType<Celestial>()` reading `BodyTemplate.RingsReference` at ctor; `PlanetRenderer` reads `RingsReference` per frame for the ring shadow (`PlanetRenderer.cs:1985-1993`); **@5482** `AtmosphereRenderer.FillPlanetAtmosphereData` reads `RingsReference` (band handle, radii, ring normal) per viewport per frame for every `AtmosphericBody` (atmospheric ring shadows, rev 5470) — requires non-null `Texture`; `AtmosphereRenderer.AssignPlanetSlots` keys on `AtmosphericBody` only (so a ring-only body joining `_planetsWithTransparencies` is harmless) | behavioral invariant | `KSA/PlanetTransparenciesRenderer.cs:352-385` @5482; `PlanetaryRingsRenderer.cs:324-346`; `KSA/PlanetRenderer.cs:1985-1993`; `KSA/AtmosphereRenderer.cs:308-318,358-373,793-801,833-862` @5482; `Content/Core/Shaders/Common/RingShadows.glsl` (new) | design keystone — narrative #1, #6 | OK — third consumer is live-read (no rebuild needed); painted-band lifetime still guarded by restore → WaitIdle rebuild → prune |

Related but **not** integration points: `RingDefinition` / `RingPresetStore` (mod-local model +
TOML under `.unscience/bloomin-onion-rings.toml`; body assignments deliberately session-only).

## Save/load adapters (feature/saves)

Both ring submods implement `ISaveParticipantSource`. Bloom stores applied body IDs and
complete detached `RingDefinition` recipes (including painted bands). Rocky adds a
controller-owned snapshot of each successfully applied `RingSelection`; capture excludes
overlays whose ring reference was replaced. This separates live values from unapplied UI
selections and explicitly copies its get-only per-LOD array.

Reset order is Rocky then Bloom: restore mutable ring overlays, then replace custom
`CelestialTemplate.RingsReference` values with their captured originals. Synchronous
controller reset methods clear reference/baseline dictionaries only after successful
rebuild. Replay order is Bloom then Rocky, rebuilding through the existing native ring
renderer APIs. Missing exact body/asset identities warn and retain source save payloads.
The adapters serialize no game reference tree, asset pointer or GPU handle.

Acceptance: ringless body, Saturn replacement, Bloom+Rocky overlay, remove/reapply, repeated
A→B→vanilla→A loads, changed template/asset IDs, hidden HUD and injected rebuild failure.
Renderer rebuilds must run outside an acquired frame and before current-frame ImGui image
references are emitted; see save lifecycle scope for the host's queued load boundary.
