# rocky-mcrock-face — swap the meshes and textures of KSA's planetary rings

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

KSA renders Saturn's rings with a bespoke two-part system: a flat analytic/volumetric **band**
(pure compute, textured by `Rings.png`) plus an instanced **rock field** near the camera — a few
small `.glb` rock meshes (one per LOD, max 5) that GPU culling replicates across a 40×40×40 chunk
grid and orbit-scrolls in the shaders to fake billions of particles in ≤6 indirect draw calls.

Rocky McRock Face lets you swap what that system draws, at runtime, from an ImGui panel:

- **Per-LOD rock mesh** — pick *any* mesh loaded into the game, including every part/subpart mesh
  (fuel tanks, engines, kittens' chairs...) plus the meshes inside glTF-file assets — the kitten
  itself (`KittenGlb:*`), helmet, visor, and MMU. ~800 entries in a filterable dropdown.
- **Rock material textures** — diffuse, normal, and AoRoughMetal maps, from every bound game texture.
- **Ring band texture** — the 2D ring color/alpha strip (also drives the ring shadow on the planet).
- **Rock field settings** — rock size, density (objects/km³), draw distance, and field thickness.

Overrides are **session-only** by design: restarting the game brings the stock ring back, and
Restore Defaults reverts within a session.

## Using it

1. Load a save in a system with a ringed body (Saturn). The panel lists ringed bodies it finds.
2. Pick meshes/textures per slot — "(game default)" keeps the stock asset for that slot.
   "All LODs" sets every LOD row at once.
3. Press **Apply**. The renderer is rebuilt through the game's own settings path (brief hitch),
   and the ring field re-appears with your assets.
4. **Restore Defaults** puts everything back; **Rescan Assets** refreshes the mesh/texture catalog
   (e.g. after parts-now loads new bundles).

Notes:
- Make sure *Planetary Rings* and *Ring Meshes* are enabled in the game's graphics settings — the
  panel warns when they are off.
- A mesh that is not centered on its origin will visibly orbit around its own origin — sometimes
  that is the fun part.
- Only a mesh's first primitive is drawn (the ring renderer's own constraint); multi-material part
  meshes render with the single rock material you chose.
- Very high density × draw distance costs VRAM and GPU time (the instance buffers are sized from
  those values at rebuild).
- **Watch the triangle counts** the panel shows under the LOD rows. The stock rocks have a 5-tier
  decimation chain because thousands of instances draw at once; a full-poly part or kitten mesh on
  every LOD slot multiplies into tens of millions of triangles and tanks the frame rate. Put heavy
  meshes on LOD 0/1 only and keep LOD 2+ light (or game default). Converted meshes no longer
  selected anywhere are freed automatically after each Apply/Restore.
- Character meshes are skinned; they import in bind pose (a T-pose kitten statue).

## How it works

The whole ring definition is public XML-backed data on the celestial's template
(`AstronomicalTemplate.RingsReference` → `PlanetaryRingsReference` → `RingObjectsReference` →
`RingLodReference.MeshFileReference.Mesh`). `PlanetaryRingsRenderData` reads that tree once, at
construction, baking per-LOD index counts, the max bounding-sphere radius, and the material's
bindless texture handles into a UBO. So the mod:

1. **Mutates the reference tree** (all public fields — no reflection for the swap itself), keeping a
   snapshot of the original values per rings-reference for restore.
2. **Disposes the rings renderer, then rebuilds.** `Program.RebuildRenderer()` alone is not enough:
   when the rings renderer already exists, the game only rebuilds its frame resources
   (pipelines/images) — `PopulatePlanets`, the only place ring data is re-read from the reference
   tree, runs solely in the renderer's constructor. So the mod waits for the device, disposes the
   existing `PlanetaryRingsRenderer` (clearing the `_ringRendererCreated` flag), and then calls
   `Program.Instance.RebuildRenderer()` so the game's own `CreateRingsRenderer` branch reconstructs
   everything — including instance buffers resized for density/draw-distance changes.

**Mesh conversion:** the ring pipeline draws `MeshReference.DeviceMesh` — a per-attribute-stream
`SimpleVkMesh` the game only builds for meshes loaded `Simple` (the stock ring rocks). Part/subpart
meshes are atlas-loaded interleaved into a shared buffer, so their `DeviceMesh` is null — and their
flags must not be flipped in place (IVA raytracing asserts `Interleaved`). The mod therefore clones
such meshes into a private `Simple` `MeshReference` sharing the retained CPU-side `HostPrimitives`
(every loaded mesh keeps Position/Normal/Uv0 + a uint32 index buffer — exactly the ring vertex
layout) and uploads a `SimpleVkMesh` for primitive 0 on first use. Clones are cached for the mod's
lifetime and disposed only after defaults are restored and the renderer rebuilt.

**Asset catalog:** meshes and textures are enumerated from `ModLibrary.AllMeshes` / `AllFiles`
(internal static `SerializedCollection<T>` fields, resolved once by reflection — the same pattern
parts-now's `GameRegistry` uses). Textures are filtered to those with a valid bindless handle;
normal maps to `TexturePowerReference` entries; `*_VM` pick-mesh hulls are hidden.

## Public API (MeowSci.RockyMcRockFaceLib)

- `RockyMcRockFaceSubmod` — the `ISubmod` (also hosted standalone by this mod, F11 window)
- `RingSwapController` — `RefreshBodies()`, `Apply(body, selection, out message)`, `Restore(body)`,
  `RebuildRenderer(out message)`, `IsRingsRendererCreated()`, `Bodies`, `Catalog`, `MeshFactory`
- `RingAssetCatalog` — `Refresh()`, `MeshIds`, `TextureIds`, `NormalTextureIds`, `TryGet*`
- `RingMeshFactory` — `GetRingUsable(MeshReference, out error)` (clone/convert cache)
- `RingSelection` — the per-body override model (session-only; nothing is persisted)
- `RockyUi` — public form-table / param-grid / filtered-id-combo helpers (also used by bloomin-onion)

## Game integration scope

See [`../scope/rings.md`](../scope/rings.md) for the full touchpoint map (what breaks on a game
update). Highlights: no Harmony patches at all; two reflected ModLibrary registry fields, one
reflected auto-property backing field (`MeshReference.<HostPrimitives>k__BackingField`), and the
private fields `Program._planetTransparenciesRenderer` → `PlanetTransparenciesRenderer.{_ringsRenderer,
_ringRendererCreated}` used to dispose the rings renderer so the rebuild re-reads ring data.
Everything else is typed public API.
