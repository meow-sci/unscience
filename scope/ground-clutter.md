# Ground clutter: Pebbles

Current owner: `pebbles.lib`. Runtime code is in `Runtime/` and asset/geometry ownership in `Assets/`; main authoring and Workshop UI belong to the same feature. Reference baseline: KSA **2026.9.7.5402**, sibling `ksa-game-assemblies/current`. Compilation and offline shader validation do not establish native GPU or gameplay behavior.

## Simple authoring behavior

The main form owns one detached replacement recipe and exact planet/type selections. Apply copies mesh/materials, transform and collider geometry to every variant/LOD of checked ecotypes while preserving target signatures, slot identities, native LOD thresholds and unchecked ecotypes. Selected ecotypes use MinScale/MaxScale = one, retaining the authored preview size without an extra random placement multiplier. Enabled custom colliders select PrimitiveList automatically; SurfaceNormalSmooth falls back to SurfaceNormal for native collision compatibility. Uniform authoring scale updates collider offsets around the mesh origin, dimensions and hull scale once; runtime continues consuming already-transformed collider coordinates. No new Harmony targets, reflection lookups or binary layouts are introduced.

## Runtime behavior and ownership

Pebbles queues per-celestial recipe application and restoration. The prefix of `Universe.ExecuteNextClothSolvers` runs after the prior frame's solver completion/application and before the next cloth and vehicle work is scheduled. It waits vehicle/cloth jobs and the graphics device, verifies the vehicle task's sync window, constructs a private reference graph/material table/placement/render/physical bundle, drains old collision exclusions, clears matching physics-bubble statics, and replaces the three arrays for the exact celestial hash. Visible geometry and collider proxies have independent bounds; the physics constructor receives the maximum of visual and proxy reach both per object and per ecotype. `_planetClutterMaxBoundingRadius` remains the visual radius used for shadow-frustum extension.

The original body template, placement/render/physical arrays and shadow bound remain retained for exact body restore. Both `Celestial.BodyTemplate` and `Astronomical.bodyTemplate` are rebound to a shallow private template whose clutter graph is wholly private. Shared mesh primitives, materials, texture references, and collider templates are never edited. Keep-original colliders are rebuilt privately; stock convex-hull behavior retains the native first-primitive rule. Custom hulls combine the selected mesh's primitives. Hull points, shapes and offsets belong to Pebbles; native physical data owns the registered scaled Bepu shapes.

Meshes are copied into private CPU `MeshAsset`s, including transformed positions/normals, UVs, and a uniformly uint source index stream to avoid the mixed-index atlas staging defect. Mesh and collider Euler rotations match native `QuaternionEx.CreateFromXyzRadians` (XYZ; row-vector matrices Rx * Ry * Rz). Positive object transforms are supported; reflected scales are rejected. Imported named registry glTF meshes are CPU-only, cached by `ClutterAssets`, and not registered globally. External GLB sources use a separate bounded managed decoder and private `MeshAsset` float3/float2 streams with uint indices. Default-scene node transforms, including mirrored winding, are baked before the object transform. Their texture conversion and upload are documented in [GLB imports](ground-clutter-glb-materials.md). Bound game textures are borrowed. The native renderer builds/uploads the private atlas.

Each private LOD receives explicit material indirection and private material references. External GLB slots are grouped by exact source identity and source-local material index, sorted ordinally independent of import order; stock material-index grouping remains unchanged. Scene and individual-mesh selections from one source share its slots. Pebbles routes only its own native material-call sites to its private `GroundClutterGpuMaterial` buffer and hash/index map. Construction context is thread-local; frame-resource rebuild context is keyed by the exact owned render object. Global material buffers and stock shader references are not overwritten. An explicit transfer-to-fragment-read buffer barrier follows material upload.

`SourceColors` adapts the current `ClutterSolidFrag` source while building the owned color pipeline. Bit 31 of the private material flags removes terrain-color modulation; bit 30 records an sRGB texture format so already-linear hardware sampling is not decoded a second time. The source marker must occur exactly once. Native include callbacks and original, NUL-terminated source path are preserved. Other stock shader variants and depth/shadow paths remain native. Native lighting, PBR response and shadows still apply.

Recipe identity includes ecotype name, ordered object IDs, LOD mesh IDs/primitive counts and material IDs. Variant count/order and five LOD slots remain stable. Runtime requires nonempty geometry for every LOD. Maximum 51 object slots, candidate and repeated-vertex budgets, uniform XYZ placement scale for collidable ecotypes, valid positive installed-collider mass, resolved biome aliases/assets, and native parameter conversion are checked before commitment. Biome controls edit an ecotype's native 32-bit eligibility mask; duplicating/splitting ecotypes for biome-specific replacement is not implemented because native candidate selection is not a disjoint partition.

Exclusions are remembered per live body, ecotype name, and exact separation value, with immutable object slot identity. Every transition first drains old pending hits, then copies exclusion words and merges them using bitwise AND. Matching grids receive queued render and physical mask uploads. Switching spacing retains masks for both grids, so returning to the original spacing does not resurrect previously removed original instances. Returning to an unrelated spacing does not reinterpret another grid's subcell keys. Body identity and radius are fixed for each record's lifetime. Native launchpad/decal/terrain suppression stays active; exclusions are transient game state, not persisted recipes.

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
- `ClutterEcotypeRenderData.RebuildFrameResources`: prefix/finalizer scopes private material/shader bindings.
- `ClutterEcotypeRenderData.SortMaterialIds`, `CreateColorRenderer`, `CreateDepthPrePassRenderer`, `CreateShadowDepthRenderer`: transpilers replace exactly one `GroundClutterRenderer.MaterialBuffer` getter or `GetMaterialIndex` call per method. Unexpected match counts fail patch activation.
- `ShaderReference.CompileVariantWithCustomOptions`: prefix substitutes only `ClutterSolidFrag` compiled inside the owned material context.
- Public constructors of `GroundClutterPlacementData`, `ClutterEcotypeRenderData`, `ClutterEcotypePhysicalData`, `ClutterCubeCellGrid`, `ClutterViewResources`, `RenderCore.Mesh.SimpleVkMeshAtlas`: prefixes retain partial owned objects only inside construction context for failure cleanup.

## Reflection and binary dependencies

- `Celestial.<BodyTemplate>k__BackingField`, `Astronomical.bodyTemplate`; `object.MemberwiseClone`.
- `GroundClutterRenderer._renderPassInfo`, `_planetClutterMaxBoundingRadius`; public `PlanetPlacementData`, `PlanetEcotypeRenderData`, `PlanetPhysicalData` and `ExcludeInstance`.
- `PlanetRenderer._groundClutterRendererCreated`: distinguishes a live renderer from the nonnull disposed object retained when clutter is disabled.
- `Universe._vehicleUpdateTask`, `VehicleUpdateTask.SyncWindowBubbles`; bubble `Parent`, `ConstraintSim`, `GroundClutterStatics.Clear`, `PopulatePendingExclusions`, `RemoveExcludedClutterInstance`.
- `GroundClutterPlacementData._exclusionCache`; eight uint exclusion words per native cell; `GroundClutterRenderer.ExclusionData.AllIncluded`.
- `GroundClutterLodReference.BuildMaterialIndirection`; private setters of `MeshReference.HostPrimitives` and `PrimitiveMaterialIds`.
- `ModLibrary.AllMeshes`, `AllFiles`, `AllGltfs`; `SerializedCollection<T>.GetList`; glTF model/named mesh indexes and `MeshReference.Load(..., createDeviceMesh: false)`.
- `StagingPool._submitted`, `_commandBufferIndex`: discard the outer transaction's unsubmitted command buffers after preparation failure before pool disposal.
- `ClutterEcotypePhysicalData._compoundShapes`, `_primitiveShapes`: reachable partial-shape retirement; `ConstraintSim.UnlockShapes`, Bepu shape ownership/removal.
- Partial retirement inspects direct public/nonpublic instance fields only on the six captured native ownership classes. It recognizes `BufferEx`, `BufferPartitionInfo`, mapped memories, descriptor pools/layouts, samplers/image views, `SimpleVkTexture`, `SimpleComputePipeline`, `SimpleGraphicsPipeline`, and their collections. Physical `MeshAtlas`/`PlacementData` references are borrowed and skipped. Resource field layout changes require re-audit.
- `GroundClutterGpuMaterial` native layout, texture bindless IDs, flags bits 31/30 reserved by Pebbles, and the shader's `materialData.flags`, `diffuseTextureId`, `globalTextures`, `textureSampler`, `inUv`, diffuse conversion and terrain modulation statement.
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
`PreviewSubmission` owns command pool/buffer/fence submission and completion;
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
