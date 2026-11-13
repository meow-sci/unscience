# Wing Walker art

`BeachChair.blend` is the seated part's editable low-poly model: interlaced, used rainbow webbing,
square gold foil rails with mitered joints, two U-shaped folding legs and aged white plastic armrests.
The fabric has uneven sun bleaching, dusty rub marks, pale broken fibers and darkened selvedges;
slightly shifted strip UVs vary the wear between ribbons. Roughness and shallow woven normals carry
the detail without modeled fibers. The cloth uses eight length segments per ribbon instead of 100.
The stronger dingy pass adds broad grey-brown ingrained soil, markedly darker worn edges, heavier
desaturation and more abrasion streaks. Its contrast is designed to read at gameplay distance rather
than relying on fine weave noise; geometry and 256px map dimensions stay the same.

| Exported chair cost | Original | Square-frame replacement |
|---|---:|---:|
| Visible triangles | 12,100 | 1,028 |
| Frame triangles within that total | 2,236 | 156 |
| Picking triangles | 12,100 | 96 |
| Model GLB bytes | 2,063,596 | 84,200 |
| Own PBR maps | 3 × 512px | 3 × 256px |

The webbing/plastic mesh has 872 triangles; its frame has 156, with flat face normals. Combined
exported vertices total 1,456 including attribute splits. `chair_picking.py` builds a 96-triangle,
192-vertex proxy using broad seat/back/arm panels and sparse leg strips/bars; it retains the major
gaps between the legs and encloses the visible bounds. Fine weave gaps and bevels are not matched.
The native six physics boxes and crew/eye/connector setup retain ownership and dimensions.

`create_beach_chair.py` builds the geometry; `chair_material.py` produces three original 256px
diffuse/normal/ORM maps for the worn webbing and plastic. The GLB retains separate stable
`Unscience_WingWalker_BeachChair` and `_Frame` mesh names for the two native materials, plus `_VM`.
No materials or images are embedded: native XML binds the external maps and real Core foil.

Frame UVs select an interior wrinkled region of `CoreSurfacesB_Subpart_TrussFFoilA`, used by
`CoreStructuralA_Prefab_StrutOcto2HB`: GLB U=0.495–0.605, V=0.39–0.49. Each flat rail face uses an
aspect-correct narrow strip at roughly 0.171 UV units/metre, matching the sampled native foil scale.
Long faces do not stretch the whole patch over their width. The four faces use offset strips; miter
ends stay inside the same island. Blender V is flipped relative to GLB V. Native XML still references
`CoreSurfacesB_Material`; no Core texture payload or geometry is copied into the source or GLB.

`BeachChair-preview.png` renders actual native foil maps for the preview only. The portable source
contains procedural gold and only the three packed original webbing/plastic maps. The optional
native preview reconstructs the missing blue/Z channel of decoded BC5 normals. Native preview
maps are removed after rendering, never packed or exported.

The chair's ground plane is KSA Z=0.3821768. The existing seated eye point and model offset are
retained. A temporary installed kitten/seated-idle reference was used to check the seat, back and
armrest alignment, then removed from the source. Native 5801 render/physics acceptance is pending.
The rebuild script shares the PNG writer from `create_standing_mount.py`. From Blender:

```python
from pathlib import Path
art = Path('/absolute/path/to/wing-walker/Art')
for name in ('create_beach_chair', 'preview_beach_chair'):
    p = art / (name + '.py')
    exec(compile(p.read_text(), str(p), 'exec'), {'__name__': '__main__', '__file__': str(p)})
```

`preview_beach_chair.py` creates a separate studio, preserves unrelated scenes, saves the portable
source, then renders with procedural gold by default. To inspect native foil, set
`KSA_CHAIR_PREVIEW_TEXTURES` to a transient directory of decoded `CoreSurfacesB_{Diffuse,Normal,PBR}.png`
maps or call its `preview(directory)` function. The committed preview used those maps; do not add
their source files to this repository. `python3 scripts/check-chair-assets.py` checks exported cost,
frame normals, atlas UVs, proxy bounds and texture dimensions; distribution checks run it too.

## Standing Mount

`StandingMount.blend` is the editable source: two broad brown leather loops over the kitten's
boots, small flat attachment tabs, subtle grain and tan edge stitching. The preview studio is
separate from the exported mesh. No game art is included in this source file.

The source uses metres, +Z up and -Y forward. `create_standing_mount.py` rebuilds the geometry,
three 256px diffuse/normal/ORM textures, and the GLB with visible and `_VM` picking meshes. Run
the script in Blender with its actual file path, for example from the Python Console:

```python
from pathlib import Path
p = Path('/absolute/path/to/wing-walker/Art/create_standing_mount.py')
exec(compile(p.read_text(), str(p), 'exec'), {'__name__': '__main__', '__file__': str(p)})
```

The export bakes KSA part-space coordinates into the vertices (+X forward, -Z up, boot origin
X=0.01404/Y=0.00495/Z=0.2901232), with identity node transforms and Y-up conversion disabled.
`WingWalker.xml` binds the mesh and the external texture set; Blender shader nodes alone are
not used by the game. Both hosts copy `Meshes/` and `Textures/`, and the release distribution
check verifies those payloads against the source. Only runtime GLB/PNG art is distributed.

The loops were visually checked around the installed 5707.1 kitten boot reference in Blender.
Their fit with 5801 standing idle and the native editor/flight renderer still needs in-game
acceptance. [Feature validation](../../plans/WING_WALKER.md).
