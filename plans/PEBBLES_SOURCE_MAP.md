# Pebbles: ground-clutter source map

**Historical source investigation, 2026-09-05; KSA 2026.9.7.5402.**
Retained as evidence for the Pebbles backport. Proposal language below describes the original
investigation; shipped behavior and current ownership are documented in
[ground-clutter scope](../scope/ground-clutter.md) and [Pebbles README](../pebbles.lib/README.md).

**Later builds:**
- **KSA 5919** moved every per-ecotype GPU buffer, mapped memory and descriptor pool cited below as
  `ClutterCubeCellGrid` into `GroundClutterGpuResources`. That class is reached through
  `ClutterEcotypeRenderData.Resources`, and its large per-planet buffers are allocated lazily.
- **KSA 5903** builds the snap-to-mesh acceleration structure only where stock clutter snaps to the mesh,
  over that clutter's reach. The rendered-mesh snapping path below is therefore no longer available to every
  noncollidable override.
- See the scope's 5949 section.

`D/` = [current decompilation](../../ksa-game-assemblies/current/decomp/); `C/` = [current Content](../../ksa-game-assemblies/current/Content/). In tables, filenames without a namespace directory are in `D/KSA/`. Source line numbers are this snapshot's decompiled-file lines. Paths resolve from the repository's sibling game-reference checkout; they are not distributed game assets.

## A. Reference loading and shared assets

| Source | What it establishes |
|---|---|
| `AstronomicalTemplate.cs:53–54,151–161` | A body owns a nullable `GroundClutterReference`; load resolves its biome aliases against that body's biome table. |
| `GroundClutterReference.cs:8–44` | Ecotype list and forwarding of data load, biome processing and asset resolution. |
| `ClutterEcotypeReference.cs:12–59` | Name, placement, variant list, derived material list and collision mode. `ToParameters` requires distribution texture, conditionally requires type texture, and chooses `SnapToMesh = !Collideable`. |
| `ClutterEcotypeReference.cs:165–185` | At least one object; validation rejects anisotropic scale in collidable groups. |
| `ClutterEcotypeReference.cs:188–239` | Object ID references become shared registered templates; material inventory is populated from resolved LOD references and deduplicated by hash. |
| `ClutterObjectTemplate.cs:13–44,48–59,73–85,130–163` | Atlas ID, five LODs, collider list, substances, cached mesh atlas, shared ID resolution and derived mass/volume. Scaled mass is base mass × scale³. |
| `GroundClutterLodReference.cs:13–29,40–48,51–121` | LOD is a mesh group, plus ordered materials. Resolve caches `Meshes`; private `BuildMaterialIndirection` sorts distinct primitive material IDs and maps them to the listed materials. Resolve returns early once meshes exist. |
| `ModLibrary.cs:88–90,674–681,993–1000,1852` | Global clutter object/game-data collections, register/get and application of separate substance game data. |
| `PbrMaterialReference.cs:10–29,38–48,66–101` | Shared material references and inherited channels. Registration/loading is unsuitable as a general-purpose runtime cloning API. |
| `GroundClutterMaterialReference.cs:13–64,127–186,189–203` | Actual clutter flags/channels; `_isReference` and cached `ColorPipelineFlags` must be consistent on private clones. |
| `BiomesReference.cs:12–26,58–90` | ID/control cubemaps, blend exponent and body-local alias mapping. |
| `GroundClutterPlacementReference.cs:136–154` | Comma-separated alias list becomes one uint mask; empty string means every bit enabled. |

Fresh detached graph construction needs an explicit adapter for private reference/cached fields. Do not call `OnDataLoad` against a fake mod simply to initialize caches: it registers shared assets and may turn a colliding clone back into a reference. Do not assume `Resolve()` repairs a prepopulated LOD. Either build indirection with a validated adapter or use a scoped resolution mechanism with complete private mesh/material identities. Public source types do not imply all useful operations are public.

### Content/import evidence

- `C/Core/GroundClutter/GenericRockAssets.xml`: one host-only atlas, one rock material, seven five-LOD objects with authored convex-hull primitives. Luna and Mars refer to these same IDs.
- `C/Core/GroundClutter/EarthTreesAssets.xml`: one host-only atlas, seventeen materials, fifteen object templates; trunk, leaves and distant cards are independent material slots.
- `C/Core/GroundClutter/GrassAssets.xml:4–42`: host-only atlas, five texture channels, foliage-specific flags and five mesh LODs; no colliders.
- `C/Core/GroundClutter/_GameData.xml` and `_Materials.xml`: separate object substance/volume assignments and solid substance densities. These are not texture-material definitions despite `_Materials.xml`'s name.
- `D/KSA.GlbImport/ClutterAssetBundler.cs:16–64,67–151`: asset-authoring path reads named object roots, `<Object>_Lod<N>` children, materials and collider markers. It repeats the last provided LOD to fill five slots and validates unique mesh names/material membership. This is authoring tooling, not a required runtime loader.
- `D/KSA/MeshReference.cs:76–113`: imports all primitives from one named glTF mesh, normals/UVs, primitive material IDs and bounds. `createDeviceMesh:false` avoids the unnecessary ring/part GPU-binding path. It does not evaluate a character's current pose or compose arbitrary scene-node hierarchies for Pebbles.

## B. Candidate placement and identity

### Grid allocation

`CubeCellGrid.cs:448–456` defines, with planet radius `R`, object separation `s` and effective generation range `g`:

```text
gridResolution = ceil((2πR / 4) / (16s))
kernelSize     = ceil(g / (16s))
cellCapacity   = (2 × kernelSize + 1)²
candidateCapacity = 256 × cellCapacity
```

The render constructor expands authored range by half the diagonal returned by its `GetCellWidth(MeanRadius, separation)` calculation (`ClutterEcotypeRenderData.cs:200–204`). The helper's first parameter is named circumference (`CubeCellGrid.cs:440–445`), so do not silently substitute a corrected circumference and call it identical native allocation. Budget calculations must reproduce the observed constructor or conservatively bound it, not just use the author's range. `ClutterCubeCellGrid.cs:269` allocates for candidate capacity. Separation changes grid resolution/identity; range changes coverage/capacity. The layout is a six-face cube sphere, with a moving active neighborhood, not every cell of the whole planet allocated at once.

Cell identity includes face/X/Y; instance identity adds the subcell (0–255), ecotype index and celestial hash. GPU compacted output positions are not stable IDs. `BubbleClutterStatics.cs:15–60` shows the CPU identity contracts.

### Generation sequence

The render path is `GroundClutterRenderer.OnFrame` → `UpdateEcotypeClutter` → unload/generate; `GenerateGroundClutter` dispatches candidates, terrain processing and finalize. See `GroundClutterRenderer.cs:1069–1386` and the shader family at `C/Core/Shaders/Planet/GroundClutter/`.

The physical generator makes the shared placement algorithm especially explicit:

| Step | Evidence in `GenerateCollision.comp` | Behavior |
|---|---|---|
| Candidate coordinate | 237–284 | 16×16 subcells, cell X/Y and local X/Y hash, hardcoded positional jitter. Hash does not include body, ecotype or face. |
| Distribution | 290–295 | Texture red compared to random X. |
| Reserved areas | 301–306 | Reject inside uploaded launchpad zones. |
| Biome membership | 309–330 | Sample four biome IDs/weights, exponentiate by body blend strength, normalize and sum enabled weights; compare with the same random X. |
| Terrain probes | 334–350 | Generate directions around candidate for height/normal evaluation. |
| Scale | 353–359 | Random Y selects one of sixteen scale entries. |
| Object variant | 361–372 | Random Z or type-distribution texture plus jitter selects object-list index. |
| Orientation randomness | 375 | Carries random W to finalization. |

Shared random components mean density factors are correlated; do not describe distribution × biome × slope × altitude as independent probabilities whose product predicts exact density.

The procedural modifier passes between generation and finalization use the body's existing terrain system. Do not alter terrain definitions or assume a diffuse texture determines geometry. Render generation has a rendered-mesh snapping path for noncollidable clutter and a procedural terrain path for collidable clutter (`ClutterEcotypeReference.ToParameters`, `ClutterCubeCellGrid` constructor and renderer dispatch).

`FinalizeGenerateCollision.comp:119–235` derives the normal from five height samples, chooses orientation, samples the altitude LUT and applies the slope mask. The corresponding render finalizer also handles snap-to-mesh barycentric information and smooth normals. Both must remain in agreement for collidable settings.

- Up and SurfaceNormal apply a yaw selected from min/max rotation.
- SurfaceNormalAndGradient aligns downhill. Its native branch does **not apply the yaw limits**, despite a comment describing jitter. See physical finalizer `200–215` and render finalizer `394` onward. Hide/disable yaw edits for this mode unless a shader extension implements them consistently.
- SurfaceNormalSmooth is explicitly rejected for collidable ecotypes (`ClutterEcotypeReference.cs:53–55`). It is not a supported collision mode to expose experimentally by bypassing validation.
- Scale is sixteen linearly interpolated XYZ values (`GroundClutterPlacementReference.cs:68–75`). Physics caches sixteen uniform scales from X only; native validation rejects unequal XYZ.
- Slope uses a power of the clamped normal/radial dot product (or 1 for nonpositive strength), followed by the shader's bias/contrast transform and saturation. See render finalizer `491–500` and collision finalizer `223–235`. This is not a degrees-based slope cutoff.
- LUT uses 1×1024 `R8UNorm`, samples the Hermite spline over body approximate min/max terrain heights and clamps density to [0,1] (`GroundClutterPlacementData.cs:69–113`). Preserve keys and both tangents in recipes; show finite altitude resolution.

### Exclusions

`GroundClutterPlacementData.cs:18,37–60` stores eight uint inclusion words per cell, clearing one bit when an instance is destroyed. This is a private in-memory dictionary; no serialization or public include/reset path was found. Both render and physical generation consume it.

`GroundClutterPlacementData.cs:126–153` takes the first four qualifying launchpad landmarks. Each spherical zone uses `(FootprintRadius + 50m) / MeanRadius`; nonpositive footprints are ignored. These four vec4s are a shader layout limit, not a user-set collection size.

The current call path found for `GroundClutterRenderer.ExcludeInstance` is breakage propagation from `Universe.SyncGroundClutter`. No automatic Graffiti/decal-driven clutter-erasure path was found in the reviewed ground-clutter and decal/terrain sources. Terrain modification can affect placement heights/masks without being an instance removal API.

## C. Rendering and LODs

### Construction and cached state

`GroundClutterRenderer.cs:357–389` builds three aligned per-celestial arrays: placement, render and physical. Physical data receives the render mesh atlas and object radii. `_planetClutterMaxBoundingRadius` is also derived here, and is used in shadow culling. Updating only one array or only the authored mesh list is incomplete.

`ClutterEcotypeRenderData.cs:128–261`:

1. Enforces five times the variant count ≤ 256.
2. Builds object/LOD → primitive ranges for every mesh and every primitive.
3. Copies all referenced host primitives into `SimpleVkMeshAtlas`.
4. Computes each object's maximum radius across all LOD primitives and caches five screen-size thresholds.
5. Creates the render cube grid and per-ecotype resources.

`GroundClutterLodReference.BuildMaterialIndirection` associates each sorted distinct source atlas material ID with the ordered list of LOD materials. A mesh with several primitives using the same source material has one such mapping, not one material entry per primitive. Clutter render sorting subsequently groups primitive draws by pipeline/material; updates must preserve that indirection.

### Arbitrary mesh compatibility

The pipeline requires float3 position, float3 normal and float2 UV0 streams plus triangle indices (`ClutterEcotypeRenderData.cs:418–421,724–729`). The atlas does not synthesize missing attributes. Reject unsupported geometry or explicitly prepare owned missing streams before Apply. Recompute normals correctly under geometry transforms, including inverse-transpose behavior for nonuniform transforms and winding for any permitted reflections.

`D/RenderCore.Mesh/SimpleVkMeshAtlas.cs:96–134` calculates origin-centered radii from bounds, concatenates primitive streams, and chooses a common index width based on the largest primitive vertex count. It repeats referenced input geometry for every object/LOD occurrence. Identical mesh references do not imply geometry deduplication or negligible GPU cost.

**Index-width hazard:** its staging allocation uses source index width, then may expand ushort indices into uint destination entries (`224–248`). A mixed atlas containing small ushort meshes and a large mesh requiring uint indices can therefore underallocate staging storage for the expansion. Normalize all affected host index streams to the destination width on private copies before constructing an atlas. Validate index bounds/counts and vertex stream lengths. Treat this as a source-observed hazard requiring a focused native regression case; do not patch the global atlas class for all game users as an incidental Pebbles change.

CPU geometry must be borrowed immutably or owned explicitly. Never transform shared `HostPrimitives` in place. Importing a kitten/helmet/part mesh is a static mesh choice, not live skinning, animation, part modules or physical assembly creation. Offer origin/rotation/size preparation to make unusual assets sit sensibly on the ground.

### Per-frame render sequence

| Stage | Main source |
|---|---|
| Camera-following cell generation/unload, terrain-stream refresh | `GroundClutterRenderer.cs:1069–1386`; `Generate.comp`, `FinalizeGenerate.comp`, `DeGenerate.comp` |
| Transform preparation in camera/ego coordinates | `GroundClutterRenderer.cs:1387–1430`; `PrepareInstances.comp` |
| Camera and shadow culling / projected-size LOD choice | `GroundClutterRenderer.cs:1431–1516`; `CullInstances.comp` |
| Count prefix sums, instance reordering, indirect commands | `ClutterViewResources.cs:81–139,342–437`; `PrefixSum.comp`, `ReorderInstances.comp`, `BuildDrawCommands.comp` |
| Depth / color / sun shadow draws | `GroundClutterRenderer.cs:1517–1649`; `ClutterEcotypeRenderData` pipelines; `SolidDepth`, `Solid`, `SolidShadow` shaders |

Camera and SunShadow are two view **types**, not two independently supported cameras. Per-ecotype and culling resources are shared by these categories. Secondary viewport correctness needs separate sequencing tests.

The depth prepass establishes cutout geometry; the color pass uses depth comparison Equal and does not write depth (`ClutterEcotypeRenderData.cs:436–437`). A replacement must render the same geometry/opacity in depth, color and shadows. Existing LOD cast-shadow and material cast-shadow booleans combine. Camera LOD fading and shadow culling have different paths; test distant silhouettes, not only the nearest LOD.

## D. Material buffers, shaders and lifecycle

`GroundClutterRenderer.cs:188–192,511–563` owns a globally deduplicated material dictionary, private index lookup and one buffer. `ClutterEcotypeRenderData.cs:302,417,465,514` consumes global indexes and captures the buffer in descriptors. This is the precise sharing boundary that private templates alone do not solve.

The recommended construction-context/local-table interception is described in the plan. It exploits these captured descriptors, so drawing normally does not need a continually active context. Native `ClutterEcotypeRenderData.RebuildFrameResources:814–848` recompiles pipelines against retained descriptors; complete renderer replacement is a different event requiring reconciliation.

Material controls are grounded in `GroundClutterMaterialReference.cs:127–203` and the shader sources:

- Supported maps: diffuse, normal, packed R=AO/G=roughness/B=metallic, red-channel opacity and red-channel thickness.
- Compile-time flags: terrain sampling, opacity, double-sided, shadow receiving, upward normal bias, extra specular, distance fade dither and transmission. Cached flags must be recomputed before pipeline grouping after edits.
- Diffuse alpha changes gamma/terrain-color behavior (`Solid.frag:288–299`). Separate opacity is essential when adapting ordinary textures; source alpha cannot be reused blindly for both meanings.
- Normal mapping uses screen derivatives rather than an imported tangent stream (`Solid.frag:301` onward).
- Distance dither starts at 60% of range; blend distances, foliage-specular and transmission coefficients are shader constants.
- `AlphaCutoff`, inherited emissive/thin-film/alpha-map and normal power have no active scalar/channel upload in this path. Hardcoded depth/shadow opacity cutoff is 0.5. New controls need owned texture preparation or explicit shader changes.

`PlanetRenderer.cs:1204–1209,1722–1740` constructs, rebuilds or disposes clutter based on game graphics settings. `GroundClutterRenderer.Dispose:1743–1805` owns removal of render/placement/physical resources and its material buffer. Owned replacement resources inserted into native arrays must be accounted for in this teardown; the feature must not also dispose them independently without ownership transfer.

The existing UI (`GroundClutterRenderer.cs:1664–1733`) is evidence for update mechanics, not a complete API contract. Scalar edits upload LUT and ecotype info but only force render cells. Spacing/range edits rebuild only render data. Neither is a general coherent mesh/collider replacement operation.

## E. Collision generation, breakage and consumers

`ClutterEcotypePhysicalData.cs:222–236` retains its own reference, placement object, render atlas and radii. Its multi-vessel grid reach includes maximum scaled object radius plus half object separation; authored visual generation range is not a collision-range control.

For mismatched original-collider/replacement-visual policies, compute physical reach from actual collider extents and offsets at all permitted scales. Native use of visual bounds assumes coherent stock geometry; a tiny replacement with a retained large collider breaks that assumption. Keep the derived render/shadow radius and conservative physics radius separate.

The collision enum values are None, PrimitiveList and ConvexHullList (`23–28`). The current `PopulateColliderPrimitives` implementation branches on None; both non-None values use the object's authored collider list. Objects can form compounds. `239–315` prebuilds collider shape combinations for each object and sixteen scales. Supported templates include primitive shapes and convex hulls. There is no native Mesh enum that automatically converts render geometry; any generated replacement proxy is additional Pebbles work.

A private object clone with an empty collider list gets a native warning but zero collider entries (`260–307`); bubble installation adds no statics for it. This supports per-variant no-collider policy without a shader dispatch change. The ecotype still retains its collidable terrain placement mode and scale/orientation constraints. Whole-ecotype None has different placement consequences.

`710–744,927–1095` covers physical generation, height streaming invalidation, procedural evaluation, finalization and per-frame readback. Results are accepted only if their cell still maps to the same buffer ID. Pebbles additionally needs renderer/request generation tracking across its own replacement boundaries.

`InvalidateGeneratedClutter:896–908` clears physical grid/generated-cache state; it does not remove existing Bepu statics. `BubbleClutterStatics.cs:354–446` creates statics only for desired cells not already in its dictionary. Therefore a buffer update can leave old shapes, transforms and mass installed indefinitely at a stationary vehicle unless statics are cleared/rebuilt.

`BubbleClutterStatics.cs:292–328` aggregates impacts, compares peak kinetic energy with mass × `BreakEnergyPerKg` (global default 25), removes broken clutter and queues exclusion identity. It does not create free dynamic rock/tree debris. `Clear:479–507` removes statics and hit caches but does not clear pending exclusions; those old-index events must be drained before swapping identity structures.

`Universe.SyncGroundClutter:1865–1897` collects and propagates exclusions across bubbles, updates renderer/physics masks, removes corresponding instances and synchronizes statics. `PhysicsBubble.cs:308–321` gates normal installation on global `GetGroundClutterCollisions`; the source default is false (`GameSettings.cs:1017–1018`). Body-specific policies must respect/report this gate.

### Why ordinary GUI timing is insufficient

`Program.PrepareFrame:2103–2108` waits/applies orbit, vehicle and cloth work. It then starts cloth at `2144`, followed by vehicle work at `2145`. `ChuteEnvironmentSnapshot.cs:136–161` captures up to eight nearby clutter collider shape entries for cloth. A prefix just before vehicle jobs is not sufficient to protect those earlier cloth readers.

Proposed commit candidate: `Universe.ExecuteNextClothSolvers(double, SimStep)` before new cloth snapshots/jobs, with the normal caller's previous waits established. Validate all call sites and lifecycle paths; `VehicleUpdateTask.SyncWindowBubbles:64–73` explicitly rejects use while running. Avoid treating a GPU `WaitIdle` as a CPU-job wait or vice versa.

Old Bepu shapes (`ClutterEcotypePhysicalData.Dispose:1320–1336`) can be removed only after bubble statics and cloth/vehicle references retire. GPU atlas/descriptors need separate submission completion. New render/placement/physical state, source-reference view and bounds must become coherent at commit; preparation and retirement may occupy different safe phases.

Physical construction itself is also a shared mutation: the constructor calls `PopulateColliderPrimitives`, which obtains `ConstraintSim.UnlockShapes` and allocates registry shapes. `ConstraintSim.cs:116–124` throws during vehicle stepping rather than waiting; it does not protect cloth consumers. This acquisition occurs before the collision-None early return. Perform construction and failed-construction shape cleanup only under verified solver-idle/shared-shape conditions, even while detached. Physical disposal also frees GPU buffers/mappings/descriptors (`ClutterEcotypePhysicalData.cs:1337–1367`), so both CPU and GPU completion are necessary. A graphics-safe phase alone is insufficient.

## F. Binary and shader contracts to add to scope when implemented

The following sizes/offsets are calculated from current C#/GLSL field declarations; verify with runtime size/offset assertions before uploads. Do not infer new usable fields from padding comments.

| Structure | Current contract |
|---|---|
| `GroundClutterGpuMaterial` | 24 bytes: five texture handles at offsets 0/4/8/12/16, flags at 20. |
| `ObjectData` | 64 bytes: position/altitude 0–15; quat XYZ/terrain primitive 16–31; scale/object/subcell/spare 32–47; color 48–63. |
| `CollisionData` | 64 bytes: CCF direction delta/altitude 0–15; quat XYZ/pad 16–31; scale/object/subcell/spare 32–47; reserved uint4 48–63. |
| `PlacementParams` | 400 bytes: 16 vec4 scales (256), four 16-byte parameter blocks (64), biome mask/reserved (16), four exclusion zones (64). |
| `TextureHandles` | 16 bytes; distribution and type handles plus padding. |
| `EcotypeData` | 432 bytes: placement 400, textures 16, object count/snap flag/min and max altitude 16. |
| `LodSplitsData` | Five floats per object; five-LOD indexing must match shaders. |
| `ExclusionData` | Eight uint words = 32 bytes, one bit per 256 subcells. |
| `ObjectPrimitiveData` | Two uints = 8 bytes; count and first primitive. |
| `ClutterInstancingPushConstData` / shadow push constants | Explicit 128-byte C# sizes; preserve offsets and shader expectations even where trailing space exists. |
| Vertex bindings | Instance ID, instance LOD fade, Position float3, Normal float3, UV0 float2. |
| Packed cull information | Current/previous LOD, object ID and shadow cascade mask bit packing in common GLSL. Altering variant capacity requires more than increasing one C# constant. |

Evidence: `ClutterCubeCellGrid.cs:29–181`; `ClutterEcotypePhysicalData.cs:54–73`; `GroundClutterRenderer.cs:90–163`; `C/Core/Shaders/Planet/GroundClutter/Common/GroundClutterCommon.glsl:191–291,364–372,420–435`.

## G. Proposed integration watchlist, not installed hooks

| Candidate | Purpose / gate |
|---|---|
| `GroundClutterRenderer.get_MaterialBuffer()` | Return an owned buffer only during a verified owned ecotype construction context. Validate inlining. |
| `GroundClutterRenderer.GetMaterialIndex(GroundClutterMaterialReference)` | Map that build's materials to local slots. |
| `Universe.ExecuteNextClothSolvers(double, SimStep)` prefix | Drain queued commits before new cloth readers; assert prior solver completion. |
| `PlanetRenderer.RebuildGroundClutterResources(DeviceEx, IRenderPassInfo)` observation | Reconcile graphics-toggle/recreation state; private target. |
| `GroundClutterRenderer.BuildFrameResources()` / `Dispose()` observation | Establish new renderer generations and relinquish/retire owned resources before teardown. Settle minimal hooks during proof. |
| `GroundClutterLodReference.BuildMaterialIndirection()` / `_atlasToLocalMaterial` | Narrow version-checked graph initialization adapter; prefer method invocation over duplicating opaque cache rules where possible. |
| Material reference resolution/cache flags, mesh `_isReference` if private imports need it | Build valid private resolved objects without registering or mutating global assets; validate actual field ownership/type. |
| `GroundClutterPlacementData._exclusionCache` | Compatible-generation transfer/snapshot when a new placement object is required. Reuse existing placement only when its retained reference/settings ownership remains valid. |
| `GroundClutterRenderer._planetClutterMaxBoundingRadius` | Update derived per-body culling bound with coherent swaps. |
| Native renderer maps, body reference view and owned ecotype arrays | Typed accesses where available; equality/generation checks before restore. |
| Bubble/static/exclusion APIs | Remove only matching body consumers, drain old pending exclusions, invalidate readbacks; do not use a global clear as a shortcut. |

If a candidate cannot be verified, fail that capability clearly. Do not fall back to globally mutating shared assets. Exact chosen patches/private members become active scope documentation only with their implementation.

## H. Rocky comparison and repository contracts

- `ksa-rings.lib/RingAssetCatalog.cs:78,105,135`: useful existing mesh/glTF/bound-texture discovery, with assumptions to revisit (for example normal selection restricted to `TexturePowerReference`).
- `ksa-rings.lib/RingMeshFactory.cs:10`: private primitive-zero GPU conversion solves a ring-specific problem. Pebbles should use host primitives and clutter's atlas instead.
- `rocky-mcrock-face.lib/RockyMcRockFaceSubmod.Workspace.cs:15`: explicit authoring bindings; body identity separate from recipe assets.
- `rocky-mcrock-face.lib/RockyMcRockFaceSubmod.Live.cs:14`: typed per-body live item and copy/restore operations.
- `rocky-mcrock-face.lib/RockyMcRockFaceSubmod.cs:78`: retaining owned resources across renderer use is a useful precedent. Pebbles additionally has physics and multiple material/pipeline consumers.
- Pebbles should report applied state only after success; do not duplicate any existing UI path that sets “applied” before rebuilding succeeds.
- Original newux workspace contract: no applying/allocating/disposal/collision resets from save/load, no feature-to-feature references, hidden updates continue, single host/guard. Main uses session-owned feature state; no workspace framework is backported.

## I. Limits of this investigation

This review covers the available managed reference graph, Core clutter assets, import path, generation/finalization shaders, instancing/LOD/material passes, physical readback/Bepu installation, breakage, exclusions, solver consumers and renderer lifecycle. It identifies a concrete implementation route and the relevant runtime-control surface. It does not establish native API correctness, actual GPU memory behavior, visual quality, thread timing under every game mode, external mod compatibility, or safe performance budgets.

The most consequential proof obligations are owned material binding, private graph resolution, CPU/GPU/cloth-safe transactional replacement, native teardown coordination, mixed index-width geometry, source-color texture encoding and exact biome routing if approved. These are implementation acceptance gates, not completed experiments. Source observations may also reflect decompiler artifacts; compare the next game update against this inventory and verify behaviors in the running game.
