# Beverage Cooler art

`BeverageCooler.blend` is the portable editable source, with original geometry, packed PBR
textures, an isolated preview scene and a keyed lid hinge. Nothing from Core is embedded.
`Red-closed.png`, `Red-open.png` and `Stainless-closed.png` show the two exterior finishes and
the molded white interior. The references supplied for this task guided the proportions and
hardware; no pixels or meshes from those photographs are included.

The cooler has a rounded enamel or stainless shell, black protective base, hollow insulated
white tub, recessed lid panel, two metal side handles with black grips, front grip and latch,
bottle opener, rear hinges and a captive drain plug. Nominal body dimensions are 65 × 40 cm;
the closed lid reaches 46.3 cm above its mounting base. Hardware slightly exceeds the body bounds.

Rebuild in Blender's Python Console or through Blender MCP, supplying the actual script path:

```python
from pathlib import Path
art = Path('/absolute/path/to/beverage-cooler/Art')
for name in ('create_cooler', 'create_animation', 'preview_cooler'):
    p = art / (name + '.py')
    exec(compile(p.read_text(), str(p), 'exec'), {'__name__': '__main__', '__file__': str(p)})
```

`create_cooler.py` rebuilds only its named collection, preserving unrelated Blender content.
`cooler_textures.py` produces deterministic 512px diffuse/normal/ORM maps: red enamel, brushed
stainless and a shared trim atlas. The trim atlas uses four U strips for liner, rubber, chrome
and dark hardware. Color maps are sRGB; normal and ORM maps are linear, with AO/Roughness/Metallic
in R/G/B. The script exports four visible one-primitive meshes with normals, UVs and tangents,
and separate combined body/lid picking meshes. GLB materials are omitted; native XML binds the
external maps. Only runtime GLB/PNG/XML files ship; the source and previews stay in the repository.

Authoring coordinates are metres, +Z up, −Y front. Export bakes `(x,y,z)` to KSA `(-y,-x,-z)`
without exporter Y-up conversion and leaves node transforms at identity. Body vertices use the
base origin. Lid vertices are relative to the authoring hinge `(0,.19,.409)`, which becomes
KSA `(-.19,0,-.409)`. The native XML places both `Lid` and `LidCollider` at that hinge.

`create_animation.py` writes the separate native animation GLB, independently runnable in Python.
The first clip is `Actuation`: the animated `Hinge` node turns +100° about KSA Y over 1.2 seconds.
Unanimated children named exactly `Lid` and `LidCollider` inherit its world matrix. KSA's importer
skips animated nodes when binding placed subparts, so retain that hierarchy. The source Blender
hinge uses −100° about authoring X at frames 1–37 (30 fps); this is equivalent to the runtime track.

`preview_cooler.py` makes an isolated studio, renders the three previews and writes a compressed
source library with packed textures. The source includes only that studio and its dependencies.
The source animation is for authoring previews; runtime uses the separate native keyframe asset.

Native 5801 CPU checks import and sample the actual animation GLB, validate the variant family
and supply capacity, and round-trip native save records. Blender renders verify geometry/material
appearance. In-game lighting, thumbnails, collision contacts, variant switching and repeated
scene load still need [native acceptance](../../plans/saves-acceptance.md#beverage-cooler).
