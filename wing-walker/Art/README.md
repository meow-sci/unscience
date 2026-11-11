# Standing Mount art

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
