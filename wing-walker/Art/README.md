# Wing Walker art

`BeachChair.blend` is the seated part's editable model: an interlaced rainbow webbing seat/back,
rounded gold foil tube frame, two folding U-shaped legs and stippled white plastic armrest pads.
`BeachChair-preview.png` shows the model using the actual native foil maps for the preview only.
The source contains a portable procedural gold proxy; the runtime XML binds the real Core material.

`create_beach_chair.py` builds the geometry and three 512px diffuse/normal/ORM maps for the
webbing and plastic. The GLB has separate `Unscience_WingWalker_BeachChair` and `_Frame` meshes
for their two native materials, plus one combined `_VM` picking mesh. Frame UVs select an interior
wrinkled region of `CoreSurfacesB_Subpart_TrussFFoilA`, used by
`CoreStructuralA_Prefab_StrutOcto2HB`: GLB U=0.495–0.605, V=0.39–0.49. Native XML references
`CoreSurfacesB_Material`; no native textures or geometry are copied into the source or GLB.
Blender's UV editor flips V relative to these GLB coordinates.

The chair's ground plane is KSA Z=0.3821768. The existing seated eye point and model offset are
retained. A temporary installed kitten/seated-idle reference was used to check the seat, back and
armrest alignment, then removed from the source. Native 5801 render/physics acceptance is pending.
The rebuild script shares the PNG writer from `create_standing_mount.py` and runs the same way
as the example below; use `create_beach_chair.py` for the chair.

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
