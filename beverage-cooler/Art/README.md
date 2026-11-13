# Beverage Cooler art

`BeverageCooler.blend` is the portable editable source, with original geometry, packed PBR
textures, an isolated preview scene and a keyed lid hinge. Nothing from Core is embedded.
`Red-closed.png`, `Red-open.png`, `Stainless-closed.png` and `Stainless-open.png` show both exterior
finishes and the native gold-foil interior. The references supplied for this task guided the proportions and
hardware; no pixels or meshes from those photographs are included.

The cooler has a rounded enamel or stainless shell, black protective base, hollow insulated
foil-lined tub, recessed lid panel, two metal side handles with black grips, front grip and latch,
bottle opener, rear hinges and a drain plug. Nominal body dimensions are 65 × 40 cm;
the closed lid reaches 46.3 cm above its mounting base. Hardware slightly exceeds the body bounds.

The game export is deliberately low polygon. Rounded body corners use two segments per quadrant;
hardware uses six-sided cylinders, four-sided tubes and one-segment bevels only on the front grip.
Tiny screws, chain links and hidden duplicate insulated walls are omitted. Smooth shading and
small PBR maps carry surface detail. The lid, hollow tub and recognizable handles remain.

| Asset cost | Original | Optimized |
|---|---:|---:|
| Visible triangles per cooler | 9,260 | 710 |
| Picking triangles per cooler | 9,260 | 112 |
| Texture dimensions (nine shared maps) | 512 × 512 | 128 × 128 |

The six visible meshes contain 24/356/82 triangles (BodyShell/BodyTrim/BodyFoil) and 82/108/58
(LidShell/LidTrim/LidFoil). Their exported vertices total 1,360, including UV/normal splits.
The picking meshes use 76/36
triangles and 192 exported vertices combined. Native picking tests candidate triangles linearly;
these proxies avoid copying hardware tessellation into the mouse-picking path. Small double-sided
extent planes include protruding handles, opener and hinges in native picking-derived bounds.
They are separate from the unchanged five-box body and one-box animated lid physics colliders.

The original frame-drop report is not a measured attribution. KSA shares textures across cooler
instances and batches render instances by model. Compare the original and optimized builds in game
with the same craft, camera and hover state; Blender inspection cannot establish an FPS gain.

Rebuild in Blender's Python Console or through Blender MCP, supplying the actual script path:

```python
from pathlib import Path
art = Path('/absolute/path/to/beverage-cooler/Art')
for name in ('create_cooler', 'create_animation', 'preview_cooler'):
    p = art / (name + '.py')
    exec(compile(p.read_text(), str(p), 'exec'), {'__name__': '__main__', '__file__': str(p)})
```

`create_cooler.py` rebuilds only its named collection, preserving unrelated Blender content.
`cooler_textures.py` produces deterministic 128px diffuse/normal/ORM maps: red enamel, brushed
stainless and a shared trim atlas. The trim atlas retains four U strips for plastic, rubber, chrome
and dark hardware. Color maps are sRGB; normal and ORM maps are linear, with AO/Roughness/Metallic
in R/G/B. The script exports six visible one-primitive meshes with normals, UVs and tangents,
and separate simple body/lid picking proxies from `cooler_picking.py`. GLB materials are omitted; native XML binds the
external maps; BodyFoil/LidFoil use the game's `CoreSurfacesB_Material` instead. Foil uses the same
wrinkled atlas patch as the chair, U=.495–.605/V=.39–.49, with per-face projection at .16 UV/metre.
No Core textures are embedded or distributed. Only runtime GLB/PNG/XML files ship; source and
previews stay in the repository. Export is restricted to selected meshes in the active scene.

Run `python3 scripts/check-cooler-assets.py` from the repository root after rebuilding. It reads
the actual GLB indices/attributes and PNG dimensions, enforcing at most 1,000 visible and 120 picking
triangles and 128px texture edges. One-sided surface rays inspect winding and normal agreement at
the cavity floor, inside walls, lid center and perimeter. Distribution validation runs these checks.

The first underside profile had backward-facing cap/rim triangles and stopped short of the outer
lid shell. KSA culled those faces; Blender's default two-sided materials concealed the mistake.
The corrected foil profile shares its outer loop with the shell and explicitly faces down toward
the viewer. The body liner shares its outer rim with the body shell too. Open profile winding is
authored deliberately and is not globally recalculated after joining. All cooler preview materials
enable backface culling, matching the native one-sided surface contract.

Native XML PartModel keys must be globally unique per render definition: KSA's process-wide model
cache uses that Key alone. Reusing generic Shell/Trim keys aliases body/lid meshes and exterior
finishes even when this GLB's mesh names and XML references are correct. The twelve cooler render
components use distinct namespaced keys; placement keys retain their separate animation/save role.

Authoring coordinates are metres, +Z up, −Y front. Export bakes `(x,y,z)` to KSA `(-y,-x,-z)`
without exporter Y-up conversion and leaves node transforms at identity. Body vertices use the
base origin. Lid vertices are relative to the authoring hinge `(0,.19,.409)`, which becomes
KSA `(-.19,0,-.409)`. The native XML places both `Lid` and `LidCollider` at that hinge.

`create_animation.py` writes the separate native animation GLB, independently runnable in Python.
The first clip is `Actuation`: the animated `Hinge` node turns +100° about KSA Y over 1.2 seconds.
Unanimated children named exactly `Lid` and `LidCollider` inherit its world matrix. KSA's importer
skips animated nodes when binding placed subparts, so retain that hierarchy. The source Blender
hinge uses −100° about authoring X at frames 1–37 (30 fps); this is equivalent to the runtime track.

`preview_cooler.py` makes an isolated studio, renders four one-sided previews and writes a compressed
source library with packed original maps and both exterior materials. The source contains procedural
gold as a portable foil proxy. Set `KSA_CHAIR_PREVIEW_TEXTURES` to decoded transient Core maps (as
described in the chair's Art README), or call `previews(directory)`, for native foil previews.
Those maps are removed afterward and are never packed into the source or exported GLB.
The source animation is for authoring previews; runtime uses the separate native keyframe asset.

Native 5801 CPU checks import and sample the actual animation GLB, validate the variant family
and supply capacity, and round-trip native save records. Blender renders verify geometry/material
appearance. In-game lighting, thumbnails, collision contacts, variant switching and repeated
scene load still need [native acceptance](../../plans/saves-acceptance.md#beverage-cooler).
