# graffiti.lib

Core implementation for the standalone `graffiti` mod and the `unscience` umbrella mod. It places
save-aware projected PNG decals on vehicle art meshes, deployed parachute cloth, KittenEva
avatars, and celestial terrain.

## Main components

- `GraffitiSubmod` owns lifecycle, public placement/removal APIs, anchor resolution, and UI state.
- `DecalPicker` (plus `DecalPicker.Parachute`) casts the cursor ray against live parachute cloth triangles, vehicle part meshes,
  KittenEva bounding spheres, and finally the nearby body's CPU terrain surface.
- `DecalAnchors` recomposes part-local, cloth-barycentric, or geodetic anchors into ego-space
  projection boxes every frame.
- `DecalRenderer` and `DecalShaders` implement the post-resolve projected-decal Vulkan pass.
- `DecalTextures` manages Graffiti's GPU residency. PNG catalog/import behavior comes from the
  shared `ksa-abstractions.lib` `PngLibrary` and `PngFileBrowser`: every import is copied into
  `.unscience/pngs`, and the catalog is scanned at startup and via the Rescan button.
- `GraffitiPatches` installs the `RenderTarget.ResolveAttachments` postfix used by both hosts. On
  KSA 5438 it receives `inResolveDepth` and skips the early color-only resolve, preserving the
  final post-resolve window immediately before `GridPass`.

## Parachute behavior

KSA renders a canopy outside the normal `Part` view-mesh hierarchy. Graffiti therefore builds a
240-triangle CPU pick proxy from `Parachute.ClothPositionsFront` using the stock 8-ring × 16-spoke
topology. A hit stores its three cloth-node indices and barycentric coordinates. On later frames,
the anchor point and normal are rebuilt from those live nodes so the projection follows reefing,
inflation, and flutter. Canopy resolution uses the module runtime id with a parent-part id plus
canopy-index fallback for scene reloads.

The visible canopy is a bone-skinned GLB driven by the cloth nodes, not the literal proxy mesh.
Normal decal projection depth covers their small surface difference. See
[`../scope/decals.md`](../scope/decals.md) for all game integration dependencies and live-test risks.

## Build

Build the repository solution from the root:

```bash
dotnet build ksa-mod-experiments.slnx
```

## Native save persistence

KSA saves retain every placed decal's PNG name, dimensions, depth, rotation, opacity,
brightness and visibility. Terrain anchors retain exact body ID and geodetic position.
Vehicle/EVA anchors use a verified saved part-tree address; canopy anchors additionally
retain authored canopy index, cloth nodes, barycentric weights and clicked side. Runtime
part/module IDs, ego matrices and texture handles are reconstructed. Placement gestures
are cancelled when loading.

Loading clears old decals/GPU ownership synchronously. Missing targets/images report
Saves warnings; missing images and temporarily unavailable canopies can remain dormant.
The coordinator preserves the original feature payload after partial restoration. PNG
files are external shared-library dependencies, not copied into each save; rescan follows
current shared PNG contents. Native rendering/cloth acceptance remains required.
