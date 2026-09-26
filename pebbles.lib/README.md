# Pebbles

Pebbles is an independent bundled feature library in the Unscience supermod. It authors per-celestial ground clutter replacements and collision recipes. It has no standalone StarMap entry and references no other feature library.

## Usage and behavior

1. Pick a **Mesh** or use **Import GLB…**. Import selects the complete scene automatically; choose an individual imported mesh if needed. The mesh recipe can be prepared before selecting a planet.
2. Set the uniform **Scale**. **Preview and set up colliders** opens the textured preview with that scale intact. Add fitted box, sphere, capsule or cylinder shapes and adjust their positions, rotations and dimensions. Changing mesh scale resizes existing collider dimensions and offsets together. **Use colliders** enables/disables the authored shapes.
3. Choose a **Planet**, then check one or more clutter target types, or **All clutter types**.
4. Press **Apply to planet**. Every variant and all five LODs of those types receive the mesh, materials, scale and colliders. Other types keep their current settings. Restore an applied type or the entire planet under **Applied clutter** in the same panel.

The form does not expose placement, LOD, resource-budget or material-channel tuning. Native placement locations and LOD distances are retained. Selected types use a fixed instance multiplier of one, so the authored mesh/collider size matches the preview. Custom colliders automatically enable native primitive-list collision; smooth-normal orientation becomes surface-normal orientation when collision requires it. Mesh selection clears old colliders; scale is retained. Registry meshes use a neutral material; imported GLBs automatically use their own supported materials, including base-color textures as diffuse with source colors preserved, through both import and the regular mesh picker.

The collider editor retains orbit/pan/zoom, framing, grounding, fitted primitives, numeric and handle editing, duplicate/mirror/delete, snapping and undo/redo. It no longer exposes separate LOD meshes or texture channels. **Done** keeps the detached recipe; **Cancel** discards the edit. Finish the editor before changing the main mesh or applying. Neither editing nor Done changes a planet.

Planet and clutter-type identities are exact. Refresh discovers targets without applying anything; missing types stay unresolved and changed target signatures block Apply until refreshed. Authoring selections, unapplied collider edits and loaded GPU/CPU caches are session-only; applied overrides travel with native KSA saves and copied GLB files persist in the shared library. Main's existing window/header visibility persistence still applies; no workspace or Live State abstraction is required.


## Loading your own GLB

Use **Import GLB…** to browse for a self-contained GLB, or expand **GLB file path** to paste an absolute path and press **Load file**. The file is copied into `My Games/Kitten Space Agency/.unscience/glbs` before loading. Import selects the complete scene and automatically assigns embedded base-color/diffuse, normal, PBR and opacity maps where supported. Choosing a different imported scene/mesh also refreshes its materials automatically; there is no separate texture-assignment step.

Import supports self-contained **GLB 2.0**, static triangle primitives, indexed or non-indexed geometry, float positions/normals and float or normalized unsigned byte/short UV streams. Missing normals are generated. The main texture selects its UV set (including secondary sets); its KHR_texture_transform offset, rotation and scale are baked into the imported UVs. Missing required UV data remains an error. Scene hierarchy transforms and mirrored instances are baked; animation is not played, and individual meshes use raw local geometry. Imported meshes share the scale and collider controls.

Core metallic/roughness materials support embedded PNG/JPEG images, base-color factors, normal scale, AO/roughness/metallic factors, opaque or alpha-mask coverage and double-sided rendering. Material slots from different imported files remain distinct. Common Blender material extensions (specular, IOR, clearcoat, sheen, anisotropy, iridescence, transmission, volume, dispersion, unlit and emissive strength) fall back to core base-color/PBR materials, including when marked required. Import reports omitted effects; glass becomes solid, unlit becomes normally lit and emissive glow is omitted. Existing base-color textures and factors remain intact. Older KHR_materials_pbrSpecularGlossiness materials retain their diffuse image/factor, with nonmetallic roughness approximated from scalar glossiness; specular/detail textures are omitted. Different UV sets/transforms or unsupported encodings on optional normal/AO/metallic-roughness maps skip only that detail map with a warning. Alpha blending becomes a 50% alpha cutout; clamp/mirrored wrapping uses repeat with a warning. WebP/KTX2 texture extensions use an embedded PNG/JPEG fallback when provided. A main texture containing only WebP/KTX2 still requires a decoder that Pebbles does not yet include; the error names the extension. Draco/meshopt geometry compression, skinning, morph targets, unknown required extensions, unsupported main-material/mapping extensions and external/data image URIs remain unsupported. Vertex colors, authored tangents and per-texture filter preferences are not reproduced; this is a conversion to the native clutter material model. See [GLB material conversion](../scope/ground-clutter-glb-materials.md).

Limits: 128 MiB file, 512 meshes, 4096 scene nodes, 2 million vertices/12 million indices/2048 primitives per selection, 4096 pixels per image dimension and 256 MiB retained CPU pixels per source. The import cache permits 16 file-content versions and 8 million retained vertices. Existing Apply budgets also count the copies repeated across variants and LODs: assigning a detailed model everywhere can exceed that budget.

Imported selections use absolute paths and SHA-256 file identities. Cached versions remain immutable snapshots if the source file changes; importing a changed file creates a distinct version. Applied recipes persist with native KSA saves; GLB contents stay in the copied shared library.

Import counts and per-planet restoration controls appear under **Applied clutter**. **Restore all and release resources** first retires planet overrides and the Workshop preview, then releases imported CPU/GPU resources before GUI rendering. Hiding the feature does not purge its cache. A newly selected mesh cancels a pending cache purge; failed native body retirement retains imports for safety.


## Existing Blender materials

Try exporting existing materials unchanged first. Pebbles tolerates the common appearance extensions listed above; a new material or texture is usually unnecessary. Texture transforms and the main texture’s UV selection work automatically; optional detail-map incompatibilities become warnings. Remaining unsupported extensions are named individually. This is intended for using downloaded GLBs directly, with a recognizable main texture rather than exact material parity.

For procedural textures or unsupported mapping, save a separate export copy of the Blender file. Bake the material color into a new PNG using Cycles and a non-overlapping first UV map, with the new Image Texture node active in each material. For a diffuse color bake, enable Color and disable Direct/Indirect. After baking, use the saved image as Base Color on a simple Principled material in the export copy. Metallic/emissive or complex mixed shaders may need their intended color routed through emission and baked with Emit. Original source textures need not be edited. See the [Blender baking manual](https://docs.blender.org/manual/en/4.3/render/cycles/baking.html).

## Implementation ownership

- `Models/`: game-independent recipe schema, detached validation, bounded GLB container/geometry/scene decoding, exact file identities, material-slot ordering, primary texture mapping, testable CPU material conversion and pixel conversion; removed/displaced clutter grid state (`ClutterGridState`, sidecar validation) and its carry-over rules (`ClutterGridMemory`).
- `Assets/`: read-only registry discovery, private CPU geometry imports, native embedded-image decoding and lazily uploaded GLB textures.
- `Import/`: feature-owned file browser and session-only navigation/selection state.
- `Runtime/`: source capture, private ecotype/mesh/material graphs, resource preparation, per-body commit/restore (`ClutterController`, `ClutterLiveRecord`), exclusion/displaced-object carry-over and native-save rewriting (`ClutterController.Grids`, `ClutterGridNative`), private hull colliders and their volume math (`ClutterColliders`, `HullMath`), physics invalidation, feature-owned Harmony demand.
- `Preview/`: independent Vulkan color/depth target, geometry, material sampling and local camera; no stock thumbnail viewport, camera switch or Bepu simulation.
- `Workshop/`: detached state/history, local camera and gizmo math, collider editing and responsive editor UI.
- `PebblesSubmod*`: main authoring controls, per-planet applied-state controls and the standard `ISubmod` lifecycle. The submod owns its controller, recipe, import cache and editor; runtime resources are never serialized.

`unscience/Patcher.cs` applies the controller's Harmony hooks to main's consolidated Harmony instance, after the host's existing HotkeyGuard. Hooks stay installed until unload and do no clutter work without pending or applied state. Removal targets only Pebbles patch methods, preserving other submods' hooks. Apply/restore remain queued to `Universe.ExecuteNextClothSolvers`; `Update` handles scene changes, discovery and deferred preview/import retirement, including while hidden. The floating editor and browser use `RenderFloatingWindows`.

See [ground-clutter integration](../scope/ground-clutter.md) for native dependencies and the original [source investigation](../plans/PEBBLES_SOURCE_MAP.md) for design evidence.

## Verification

Run `dotnet run --project pebbles.tests/pebbles.tests.csproj` from the repository root. Managed checks cover detached copying/serialization, placement and collider constraints, camera/gizmo math, undo history, GLB geometry/container validation, transform baking, exact file identities, material-slot isolation, pure pixel conversion, hull volume/recentring math, the backface-normals recipe default, grid memory carry-over rules and the `pebbles.clutter-state` record. Compilation verifies typed APIs against KSA 2026.9.22.5482. Native acceptance must cover private material descriptors, shadows, GPU retirement, stationary-cell collision refresh, exclusions, same-body replacement/restoration, scene changes, and Luna/Mars isolation. The preview uses conservative synchronization and may hitch while changing a large mesh or resizing; native rendering and gameplay are not established by managed checks. GLB acceptance additionally needs actual PNG/JPEG decode/upload, transformed atlases and secondary UVs, skipped detail maps, masks/blend cutouts, wrap approximations, alternate-image fallbacks, multi-material scenes, file changes, preview/live sharing and cache release.

Bound source textures must remain loaded while their recipes are applied or previewed. Native construction failure can leave allocations hidden in game-local variables; reachable resources are retired once and failures are reported, but a renderer/game restart may be required. See the [runtime failure limits](../scope/ground-clutter.md#failure-handling-and-verification-limits).

## Shared GLB library

Imports from the browser, pasted path and `ClutterAssets.ImportGlb` all use the shared
`GlbLibrary.Files` copied catalog. The PNG/sound `LibraryFileBrowser` now supplies folder navigation,
filtering, quick links and refresh; Pebbles no longer owns a separate browser implementation.
Duplicate names get a numbered suffix and originals may be moved/deleted after import. The 128 MiB
limit is checked before copying.

Every mesh picker, including Workshop's hull-source picker, lists **filename · GLB library** choices
from this directory. The catalog refreshes every two seconds or via **Refresh GLB library**. Scanning
lists names only: it does not decode files, consume the 16-source cache, or upload GPU resources.
Selecting a file loads its complete scene and freezes the exact path/hash identity before assigning
it to a recipe. Imported scene/individual-mesh choices then become available as before, with supported
embedded materials assigned automatically. Changing a shared file does not replace already-loaded
content or live planet overrides; select its library entry explicitly to load the new version.

`ClutterAssets.ResolveSelection(id)` is the public freeze step for lazy library choices. Consumers
must call it before storing recipes; `ResolveMesh` rejects unfrozen library ids. `GlbLibrary` and
`LibraryFileBrowser` are reusable by Sphinx or any future GLB tool, while Pebbles' geometry/material
loader remains a deliberate library dependency. `ReleaseAll` frees runtime import caches after
borrower retirement; it never deletes the shared files.

`pebbles.tests` now also checks copied GLB parsing after source deletion, managed recipe paths,
changed-file hashes, lazy selection identities and pre-copy limits. Full solution builds; native
preview, texture uploads, clutter retirement and actual file-picker UX retain their live-game checks.

## Shared rendering utilities

`AssetUploadSubmission` (formerly private `PreviewSubmission`) provides the same cancellable
private command buffer/fence upload path for Pebbles previews, GLB textures and Sphinx geometry.
`ImportedPngTexture` owns a common-library PNG color map and optional 0.5-cutoff opacity map using
the bounded GLB decoder/upload path, with device retirement before release. Sphinx borrows
`ClutterAssets` geometry/material conversion; each feature retains its own cache and ownership.

## Native save persistence

KSA saves retain each body's complete **applied** `PebblesRecipe`, including all ecotype
variants/LODs, transforms, material identities and collision recipes. The current form,
Workshop draft and pending unapplied commands are excluded. Before native reconstruction,
the save adapter cancels pending work, closes/releases the Workshop preview and restores
the original celestial templates and native clutter arrays synchronously. It applies saved
recipes synchronously at the host's safe restoration boundary, so missing renderers,
assets and native construction failures are reported rather than hidden in an async queue.

External GLBs resolve only within the current copied shared library, using the filename
from the saved identity and the unchanged content hash. Copy the same files when moving a
save to another computer; saved paths are not imported. Runtime caches and native resources
are rebuilt. Native cleanup faults retain ownership and block replay with a visible warning.

Knocked-over and removed clutter persists (KSA 5482 saves clutter natively). The native save
always holds each body's **stock-spacing** state, so the save stays correct without Pebbles.
When an applied type uses a different object separation, Pebbles writes the remembered stock
state into the native save and keeps its own spacing's removed/displaced objects in a separate
`pebbles.clutter-state` sidecar record, restored after the recipes. Older saves without that
record load their recipes and stock state only. A type applied at the stock spacing saves its
live state natively; if Pebbles is later absent, those objects appear with stock meshes.

## KSA 5482 compatibility

- Clutter hits now displace objects (dynamic bodies, settled records). Apply/restore drain them like the game does and carry displaced objects to any graph with the same spacing, so nothing vanishes and loose objects keep moving; other spacings keep their own state for later.
- Private convex hulls report their volume and are built around their bounds centre like stock hulls, so multi-hull rocks get stock mass distribution and centre of mass. Per-object `AngularDamping` (stock trees) is kept.
- Tree materials keep the new stock backface-normal lighting flag. Recipes saved before it inherit the stock material's value.
- Earth tree types changed their LOD 5 meshes, so 5438 recipes that include Earth trees must be recaptured (Apply/load reports "changed since capture").
- Needs in-game acceptance: displacement across apply/restore, save/load at same and changed spacing, tree lighting, and the extra per-type displaced-instance VRAM.

## KSA 5438 compatibility

The preview index binding uses KSA 5438’s renamed UInt32 enum member. Native clutter shaders, material layouts and reflection hooks remain compatible; live apply/restore, GPU retirement and collision acceptance are still required.
