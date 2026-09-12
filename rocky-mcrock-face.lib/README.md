# Rocky McRock Face library

Swaps native planetary-ring LOD meshes, diffuse/normal/PBR maps, band textures and optional
rock size/density/draw-distance/thickness. `RingAssetCatalog` discovers usable game assets;
`RingMeshFactory` creates ring-compatible mesh copies; `RingSwapController` snapshots
original references and applies/restores overrides. `RockyMcRockFaceSubmod` provides the
existing panel. See [host usage](../rocky-mcrock-face/README.md) for controls.

## Native saves

KSA saves retain detached selections from successful controller Apply operations, not
subsequent unapplied form changes. Exact body IDs and asset IDs are resolved after any
Bloomin' Onion rings are recreated. On load, old swaps restore their original references
before Bloom resets, and old baseline dictionaries are cleared. Runtime resource handles
are reconstructed through normal renderer rebuilds. Missing assets/bodies or renderer
failures produce recoverable Saves warnings; no similar mesh/body is substituted.

Per-LOD arrays are explicitly copied into save DTOs because `RingSelection.LodMeshIds` is
get-only. Removed/replaced ring references are not exported as active overlays. Native
rendering and repeated reset/rebuild acceptance remain required. See
[integration scope](../scope/rings.md) and [world save checks](../world-saves.tests/README.md).
