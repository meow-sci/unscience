# Sphinx

Sphinx places imported textured GLBs as decorative models fixed to a celestial surface. It ships
inside **Unscience**; this project is a compile-checked development host, not a separate release.
Open Unscience with F11 and expand Sphinx.

1. **Import GLB…** copies a model into the common `<UNSCIENCE_DATA>/glbs` library. Files imported
   through Pebbles appear automatically too. Select one in the filterable model picker.
2. Keep **Embedded GLB materials** for the easy path. Optionally import/select a PNG from the same
   shared `pngs` catalog used by Graffiti and Free Fallin to override color and transparency.
3. Choose **Place on ground…**, then click terrain in the main view. Escape cancels; a miss lets
   you try again. Increase pick range for distant terrain. **Place beside controlled vessel** is
   an alternative, projecting an eastward offset onto that vessel's parent body's terrain.
4. Select a placed model to edit it. Uniform or XYZ scale, XYZ rotations in degrees, local metre
   offsets and terrain-slope alignment **apply live** as you edit. Y is up; Y rotation is
   heading. Rotated/scaled bounds are centered horizontally and grounded before offsets.
5. Use visibility, duplicate, snap-to-ground, remove or remove-all controls to manage placements.
   Snap projects the current translated anchor back onto terrain and clears its local offset.

**Texture scale UV** and **Texture offset UV** are available before placement and on each selected
static, and apply live to existing statics. Each pair is U then V. Scale defaults to (1, 1), with
independent 0.01–1000 factors: 2 repeats the texture twice as often on that axis; 0.5 makes it twice
as large. Offsets default to (0, 0); 0.5 shifts by half a repeat and negative values shift back.
Ctrl-click a number to type a precise value. **Reset texture mapping** restores the imported UVs.
The mapping is `imported UV × scale + offset`, shared by every material and its color, alpha,
normal and PBR maps, including a selected PNG override. Duplicates inherit it independently.
Selecting a different texture also applies immediately. Failed texture edits retain the previous
texture; correct the settings or use **Retry texture edit** after fixing the image. Transform
edits still work independently of a failed texture edit.

Placements remain fixed in planet coordinates as the planet rotates and the camera moves. They
last for the current session; imported files persist. Large objects on rough ground may need
positive Y offsets. Models are visible in flight views whose camera has that nearby celestial.

## Model support and limits

Sphinx reuses [Pebbles' GLB reader and material conversions](../pebbles.lib/README.md): static glTF
2.0 triangles, baked scene transforms, embedded PNG/JPEG textures, material factors, normal/PBR
maps and material fallbacks with visible import warnings. Unsupported advanced features follow
that reader's approximations or rejection rules; arbitrary skins/animations/Draco/external assets
are not supported. Alpha masks and GLB blends are approximated as cutouts; PNG override alpha also
uses a 0.5 cutoff. UV scale/offset adjusts the existing layout; it cannot unwrap a mesh or repair
individual UV islands, so an unrelated PNG may still need a matching layout prepared externally.
Double-sided materials render both faces with reversed backface normals.

These are **decorative models without physics colliders or new shadow casters**. They receive
native lighting/shadows and participate in scene depth; transparent surfaces skip the stock normal
prepass because it ignores alpha. Imported emissive extensions retain Pebbles' fallback behavior.

Limits: 32 placements, two million rendered vertices per model (including double-sided copies),
eight million across placements, 128 MiB GLBs, 64 MiB PNG overrides and the shared decoder's 4096px
image limit. Pebbles' import cache retains up to 16 source versions; **Remove all statics** releases
Sphinx's cache without deleting common files. GPU uploads/retirement wait for completion and can
briefly hitch for large models. Errors appear in the panel and game log.

## Implementation and validation

[SphinxLib](../sphinx.lib/README.md) owns the implementation and native render hooks.
[Managed tests](../sphinx.tests/README.md) check grounding, transforms and UV mapping behavior. Full solution
compilation checks typed APIs against KSA 2026.9.7.5402. Native rendering, terrain interaction and
GPU lifetime acceptance still require an in-game run; see [integration scope](../scope/statics.md).
