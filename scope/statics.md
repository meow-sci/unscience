# Sphinx — body-fixed imported GLB statics

Baseline: KSA **2026.9.7.5402**; source paths below are relative to
`../ksa-game-assemblies/current/decomp`, shaders to its sibling `Content` directory.
Implementation: `sphinx.lib`; hosts: Unscience (distributed), Sphinx (development only).

## Lifecycle and ownership

`SphinxSubmod` implements `ISubmod`; the Unscience shell calls Initialize/Update/RenderContent/
RenderFloatingWindows/Dispose and its existing hidden-HUD fallback continues Update. Hosts apply
HotkeyGuard and `SphinxPatches.Apply/Remove` through their normal Harmony lifecycle. Private GPU
creation/replacement/removal runs from queued Update actions outside UI rendering. Render hooks
work with the HUD hidden. Entries are removed when their exact Celestial is no longer returned by
CelestialProvider; unload retires entries before releasing their GLB textures/cache.

No celestial/static-object registry mutation, physics patch, new shader or shared mesh allocation.
Placements are session-only, have no collider and do not submit to the shadow-caster passes.
They receive native lighting/shadows, are occluded by depth and write native opaque depth/normals.

## Harmony and typed game surface

| Integration | Current source / use | Update requirement |
|---|---|---|
| `StaticObjectRenderer.UpdateRenderData(IViewport,int)` postfix | `KSA/StaticObjectRenderer.cs:275`; Sphinx prepares camera-relative matrix slices | Must run before recording each viewport for the same resource frame. |
| **Private string lookup** `StaticObjectRenderer.WriteCommandsColor(CommandBuffer,IViewport,int,VkPipeline,StaticObjectModel.DrawBucket)` postfix | `:306`; hooks opaque/blended calls while stock pipeline and globals remain bound | Exact overload and parameter names `commandBuffer`, `viewport`, `frameIndex`, `bucket` are Harmony dependencies. Nonterrain buckets must always bind full state when NearbyCelestial exists, even with zero stock draws. |
| `StaticObjectRenderer.WriteCommandsPrePass(CommandBuffer,IViewport,int)` postfix | `:367`; opaque Sphinx normal/depth draw | Must bind native prepass and globals before return. Do not emit alpha primitives: stock normal shader ignores alpha. |
| `StaticObjectRenderer.{PerDrawDataDescriptorSetLayout,PerInstanceDataDescriptorSetLayout,PipelineLayout,PrePassPipelineLayout}` | `KSA/StaticObjectRenderer.cs`; private descriptor allocation/binds | Set layouts and lifetime must stay compatible. Current Rebuild replaces pipelines while retaining these layouts and the native sampler. |
| `StaticObjectModel.PerDrawData` / `DrawBucket` | `KSA/StaticObjectModel.cs:16,28` | Six int fields and Opaque/Blended semantics; skip OpaqueTerrain entirely. |
| `Program.GetRenderer`, `Renderer.{Device,Allocator,Graphics,MaxFramesInFlight,FrameCount}` | `KSA/Program.cs`, `Core/Renderer.cs` | Private buffers, uploads, disposal; frame count must remain unchanged from prepare through command recording, then advance on submit. Owner-device mismatch hides the model until replaced. |
| `IViewport.{ShaderSlot,GetCamera}`, `ViewportRegistry.MAX_VIEWPORTS`, `Program.{MainViewport,GetMainCamera,EditorFlag}` | `KSA/IViewport.cs`, `ViewportRegistry.cs`, `Program.cs` | Actual viewport/frame limits determine separate matrix slices; flight views only, exact camera NearbyCelestial identity. |
| `Camera.{NearbyCelestial,GetPositionEgo}`, `Cursor.GetEgoRay(IViewport)` | `KSA/Camera.cs`, `Cursor.cs` | Main-view cursor ray and body/vessel ego positions, no old InputRay API. |
| `Celestial.{MeanRadius,GetTerrainHeightFromDirCcf,GetCce2Ccf,GetCcf2Cce,Id}` | `KSA/Celestial.cs` | Accurate height + mean radius produces surface radius; body-fixed CCF rotates to CCE then adds camera-relative body origin. Sampled slope uses ±1m tangents. |
| `Vehicle.Parent`, shared VehicleProvider/CelestialProvider, `Universe.CurrentSystem` | `KSA/Vehicle.cs`, shared abstractions | Beside-vessel placement uses its Celestial parent; prevent queued placement into retired bodies. |
| `MeshReference.{PrimitiveMaterialIds,PrimitiveCount,HostPrimitives}`, host vertex/index spans, `MeshAttribute.{Position,Normal,Uv0}` | game RenderCore mesh surface; Pebbles GLB adapter | uint triangle indices, normalized normals, UV0 and baked scene transforms. Layout checks reject incomplete streams. Material recipes map sorted material slots; double-sided vertices/backfaces are duplicated. |
| `TextureReference.{BindlessHandle,EmptyWhite,EmptyNormal}` + Pebbles `ClutterAssets` | texture/material resolution | GLB/PBR conversions and fallback semantics are shared with Pebbles; explicit PNG override preserves native normal/PBR maps. |

## GPU / shader contract

Shaders: `Core/Shaders/Mesh/StaticObject.vert`, `StaticObject.frag`,
`StaticObjectNormalIndirect.frag`; game shader ids `StaticObjectVert`,
`StaticObjectFrag`, `StaticObjectPrePassIndirectFrag` (resolved by stock renderer, not by Sphinx).

- Native vertex input: interleaved **32 bytes**, position float3 at **0**, normal float3 at **12**,
  UV float2 at **24**. Backface culling is counterclockwise; glTF double-sided backfaces reverse
  winding and normals. Shader inverse-transpose handles positive nonuniform scale.
- Descriptor **set 2 binding 0** is a **24-byte** six-int storage record: diffuse 0, normal 4,
  PBR 8, emissive 12, TFI 16, alpha 20. **Set 2 binding 1** is a sampler. Sphinx asserts every
  offset and stride at runtime. Emissive/TFI indices are -1, retaining importer fallback behavior.
- Descriptor **set 3 binding 0** is a **64-byte mat4** storage record, using the same packed
  Brutal row-vector matrix convention as `StaticObject.UpdateRenderData`'s model-to-ego matrix.
  Each direct DrawIndexed uses firstInstance/vertexOffset/firstIndex zero; **gl_DrawID is zero**,
  so each primitive has its own one-record material descriptor. No indirect draw table is borrowed.
- Matrices occupy aligned slices of a private mapped HostVisible|HostCoherent buffer, one for each
  `ShaderSlot × MaxFramesInFlight`. Alignment comes from `KSA.Rendering.Utils.MinStorageBufferOffsetAlignment`.
  The prepared `Renderer.FrameCount` stamp prevents a newly created or stale slice from being drawn.
- Stock color command binds pipeline, viewport, global camera (set0), shared textures (set1),
  CSM (set4), terrain shadows (set5), viewport lights (set6), AO (set7), cloud shadows (set8),
  optional planet uniforms (set9), and PBR push constants. Sphinx borrows all that state, replacing
  only sets2/3 and vertex/index buffers. Recheck these **method bodies**, not just signatures.
- Private GLB/PNG images are allocated in the game's bindless texture library, also backing
  TextureSystem's descriptor set. GLB factors are baked by Pebbles. Shader expects gamma-encoded
  diffuse and AO/roughness/metallic channels; opacity is a separate alpha map. PNG overrides use
  a .5 cutoff and existing GLB UVs. Alpha primitives use the native blended pass, sorted by entry
  anchor distance, and skip the normal prepass. This is cutout approximation, not full glTF blending.
- Private device-local vertex/index/material uploads use **AssetUploadSubmission** (shared with
  Pebbles): private command pool/buffer/fence, transfer-to-vertex/index/fragment barriers, explicit
  submit+wait. Recording failure cancels rather than letting StagingPool.Dispose submit invalid work.
  `BufferEx`, `MappedMemory`, DescriptorPoolEx, sampler/descriptors, VkUtils staging and Vulkan
  command bindings are typed dependencies. Retirement waits for the owning device before freeing
  pool/mapping/buffers/sampler/PNG. GLB textures remain owned by ClutterAssets until its cache clears.

The feature deliberately avoids `DeviceMeshInterleaved.Shared`'s fixed allocation and SuperMesh's
linear shared pools. Do not replace private buffers with those pools without a capacity/reclamation
plan. Conservative waits can hitch but avoid freeing referenced native resources.

## Shared import boundary

`GlbLibrary` / `LibraryFileBrowser` and `PngLibrary` / `PngFileBrowser` use copied shared catalogs.
No uploads happen during directory discovery. Explicit placement imports a content-hashed GLB
snapshot through ClutterAssets, preserving existing placements when a file changes. Models retain
Pebbles' bounded GLB/texture conversions, unsupported-feature warnings and limits; see
[ground-clutter-glb-materials.md](ground-clutter-glb-materials.md).
`ImportedPngTexture` adds a public owning PNG adapter in pebbles.lib using that same bounded
RGBA decoder and `GlbTexture.Upload/Release`; it does not dispose borrowed stock textures.

## Validation

Full solution build: zero warnings/errors against 5402. Managed sphinx.tests exercises 200
scale/rotation/offset grounding cases plus invalid and overflowing inputs. Pebbles managed tests
cover importer/material conversion and shared catalog behavior. Source inspection confirms the
three hook bodies, shader bindings, matrix convention and allocator ownership above.

**Native acceptance remains required:** textured multi-material GLB on flat/sloped/polar terrain;
click misses/range/Escape and UI capture; large XYZ-scaled/rotated meshes; opaque/masked/double-sided
materials; PNG replacement failure retaining original; duplicate/hide/remove/re-import; moving
camera/body rotation/pause/warp/F2; secondary viewports; graphics pipeline rebuild; body/system
change and unload with frames in flight. Verify no phantom alpha depth, wrong texture sampling,
stock static corruption or retained freed descriptors. No GPU/runtime claim follows from compilation.
