# Sphinx — body-fixed imported GLB statics

Baseline: KSA **2026.9.7.5402**; source paths below are relative to
`../ksa-game-assemblies/current/decomp`, shaders to its sibling `Content` directory.
Implementation: `sphinx.lib`; hosts: Unscience (distributed), Sphinx (development only).

## Lifecycle and ownership

`SphinxSubmod` implements `ISubmod`; the Unscience shell calls Initialize/Update/RenderContent/
RenderFloatingWindows/Dispose and its existing hidden-HUD fallback continues Update. Hosts apply
HotkeyGuard and `SphinxPatches.Apply/Remove` through their normal Harmony lifecycle. Private GPU
creation/replacement/removal runs from queued Update actions outside UI rendering. Render hooks
receive live transform edits on the next Update, with no Apply button. Texture selection and UV
widgets capture per-entry changes separately; a failed texture edit leaves transforms usable.
UV scale/offset is session state, preserved by selection and duplication, with an identity reset.
Render hooks work with the HUD hidden. Entries are removed when their exact Celestial is no longer
returned by CelestialProvider; unload retires entries before releasing their GLB textures/cache.

No celestial/static-object registry mutation, new shader or shared mesh allocation.
Placements are session-only, have optional automatic box/mesh colliders and do not submit to the shadow-caster passes.
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

## Physics / collider contract

`CollisionGeometry` recognizes a closed axis-aligned bounds box only when all six faces have two
unique triangles sharing their face diagonal; duplicate reverse faces are accepted. Other meshes
retain their triangles. Auto falls back to a bounds box above 100,000 source triangles with a visible
warning; explicit Mesh rejects it. Fitted box and Off are manual overrides. Degenerate mesh triangles
are skipped; triangles are duplicated with reverse winding because Bepu mesh surfaces are one-sided.
Alpha textures never change collision topology. Per-entry geometry is limited to a 20 km diagonal,
100 km local center offset and the suite to 500,000 source collision triangles.

`StaticCollider` builds `BepuPhysics.Collidables.Box` or `Mesh` directly through
`ConstraintSim.UnlockShapes()` / `ShapesUnlock.{Shapes,BufferPool}`, `Shapes.Add`, `BufferPool.Take`,
`Mesh(Buffer<Triangle>,Vector3,BufferPool)`, and `Shapes.RemoveAndDispose`. The game-provided
`MeshColliderTemplate.CreateShapeInto` is unsupported at this baseline and is deliberately bypassed.
Mesh triangles bake the same grounded local XYZ transform as rendering, relative to a local center;
boxes use scaled dimensions, transformed center and decomposed rotation. `GroundPlacement.FrameCcf`
is shared with rendering to preserve Y-up/slope basis. Bubble translation subtracts double-precision
`BubbleOrigin.PositionBub` before conversion to Bepu float coordinates.

| Hook / direct seam | Use and update requirement |
|---|---|
| `ConstraintSim.BeginStaticObjectPass()` postfix | Refresh our handles once per substep before narrow phase; read `HandleToState`, `VehicleUpdateState.Origin/GetReadOnlyStates`, `ReadOnlyPhysicsStates.Kinematic.PositionPhys`. Require matching `BubbleOrigin.Parent` and `BubbleFrame.Ccf`; include statics within 2 km + shape radius of any bubble vehicle. |
| `ConstraintSim.UpdateSimForSnappedOrigin(VehicleUpdateState)` postfix | Refresh poses after origin shifts; inspect ordering when the bubble reframes. |
| `ConstraintSim.TryResetForPool()` / `Dispose()` prefixes | Remove all owned statics before the game clears/reuses handles or nulls Simulation. Shape ownership stays with entries. |
| `ConstraintSim.IsGroundSurfaceFor(VehicleUpdateState,StaticHandle)` postfix | Recognize only Sphinx-owned handles in that exact simulation; stock results remain true. Required for ground friction, EVA contact normals and terrain-contact bookkeeping. Recheck callers/inlining on updates. |
| **String/IL watchlist:** internal `KSA.NarrowPhaseCallbacks.AllowContactGeneration(int,CollidableReference,CollidableReference,ref float)` and its `Sim` field | Replace exactly one call to `BepuHandles.IsGroundSurface(StaticHandle)` with a helper that also checks the callback's own simulation. Validate the one-call invariant; preserve stock filtering and Pebbles' early return. Without this hook Sphinx statics never generate contacts. |
| `ConstraintSim.Simulation`, `Simulation.Statics.{Add,ApplyDescription,Remove}`, `StaticDescription`, `RigidPose`, `StaticReference.{Pose,Shape}`, `StaticHandle`, `TypedIndex` | Per-simulation handles reference entry-owned global shapes. Default awakening wakes bodies when supporting statics are moved/removed. No shape changes from solver threads. |
| `JobSystems.{VehicleSolver,ClothSolvers}.Wait()` | Before main-thread queued edits, body pruning, disposal or unpatch. All bubble handles detach before freeing shapes. No mutation during narrow-phase callbacks. |

Per-simulation maps live in a concurrent registry; each bubble mutates only its own map outside its
Bepu workers. Filtering reads stable handle sets. Main-thread entry edits wait for solver completion.
Transform/collider replacement is prepared before retiring the old shape; texture changes leave it
alone. Visibility-off, removal, clear, retired bodies and unload detach handles immediately after the
wait. Pooled simulations discard handles without freeing entry-owned shapes. Origin/range changes
refresh poses without allocations in the global shape registry. No native runtime acceptance is
implied by these typed API checks.

## GPU / shader contract

Shaders: `Core/Shaders/Mesh/StaticObject.vert`, `StaticObject.frag`,
`StaticObjectNormalIndirect.frag`; game shader ids `StaticObjectVert`,
`StaticObjectFrag`, `StaticObjectPrePassIndirectFrag` (resolved by stock renderer, not by Sphinx).

- Native vertex input: interleaved **32 bytes**, position float3 at **0**, normal float3 at **12**,
  UV float2 at **24**. Backface culling is counterclockwise; glTF double-sided backfaces reverse
  winding and normals. Shader inverse-transpose handles positive nonuniform scale.
  Live UV mapping writes `original UV * scale + offset` at that same byte offset for every
  primitive/backface; positions and normals stay unchanged. The native shader samples all maps
  with these coordinates (including PNG alpha and GLB normal/PBR), using the Repeat sampler.
  There is no new shader, descriptor layout, reflected member or Harmony target.
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
- UV-only edits retain managed original vertex arrays and upload a complete replacement set of
  private vertex buffers through AssetUploadSubmission, with transfer-to-vertex-input barriers.
  After submit+wait and owning-device WaitIdle, swap all buffers and retire the previous set.
  Failure before the swap leaves the visible mesh unchanged. Textures, indices, descriptors and
  frame matrices are reused; PNG replacement still rebuilds the resource with the current mapping.
  Recheck upload cancellation, old-buffer retirement and native vertex ABI on game updates.

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
scale/rotation/offset grounding cases plus invalid and overflowing inputs. UV mapping tests cover
scale/offset ordering, matching backfaces, 200 repeated edits/resets, unchanged source vertices and
geometry, invalid/overflow rejection and the vertex ABI. Pebbles managed tests cover importer and
material conversion and shared catalog behavior. Source inspection confirms the three hook bodies,
shader bindings, matrix convention and allocator ownership above.

Managed collider checks cover closed boxes, open/dented/nonfinite/overlapping faces, reversed
backfaces, degenerate triangles, limits and 200 collider/render transform matches.

**Native collision acceptance remains required:** walk an EVA kitten on/inside imported geometry;
land a vessel on a roof; pass through mesh doorways/arches; compare fitted-box filling; nonuniform
scale/rotation/slope/offset alignment; disable/hide/show, move while occupied, duplicate, remove,
clear, failed/oversized edits and retry; multiple same/different-body bubbles, origin snaps, pool
return/reuse, pause/warp/F2, system change, unload and stable shape/handle counts. Check stock
terrain/launchpad and Pebbles collision coexistence and awake/sleep ground-contact behavior.

**Native rendering acceptance remains required:** textured multi-material GLB on flat/sloped/polar terrain;
click misses/range/Escape and UI capture; large XYZ-scaled/rotated meshes; opaque/masked/double-sided
materials; PNG replacement failure retaining original; duplicate/hide/remove/re-import; moving
and resizing existing statics continuously; live independent U/V scale and positive/negative
offsets on embedded/PNG multi-material meshes; identity reset; duplicate mapping isolation;
invalid texture retry with transforms still usable; repeated UV drags without resource growth;
moving camera/body rotation/pause/warp/F2; secondary viewports; graphics pipeline rebuild; body/system
change and unload with frames in flight. Verify no phantom alpha depth, wrong texture sampling,
stock static corruption or retained freed descriptors. No GPU/runtime claim follows from compilation.
