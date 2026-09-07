# Sphinx

Sphinx places imported textured GLBs as models with optional physics colliders fixed to a celestial surface. It ships
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
5. **Colliders** defaults to **Auto**. A complete axis-aligned box in the imported geometry uses a
   fitted box; other models use their triangles, preserving architectural openings. Choose **Mesh**
   to force triangles, **Fitted box** for a cheap solid envelope, or **Off** for decoration only.
   The selected entry shows the actual detected shape and any fallback warning. Collider mode and
   transforms apply together; failed edits retain the previous transform and collider, with retry
   and current-settings controls.
6. Use visibility, duplicate, snap-to-ground, remove or remove-all controls to manage placements.
   Hiding disables collision too; showing restores it. Duplicates inherit collider mode independently.
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

These models **do not add new shadow casters**. They receive
native lighting/shadows and participate in scene depth; transparent surfaces skip the stock normal
prepass because it ignores alpha. Imported emissive extensions retain Pebbles' fallback behavior.

Limits: 32 placements, two million rendered vertices per model (including double-sided copies),
eight million across placements, 128 MiB GLBs, 64 MiB PNG overrides and the shared decoder's 4096px
image limit. Pebbles' import cache retains up to 16 source versions; **Remove all statics** releases
Sphinx's cache without deleting common files. GPU uploads/retirement wait for completion and can
briefly hitch for large models. Errors appear in the panel and game log.

## Collision behavior and limits

Auto recognizes complete closed bounds boxes conservatively, including duplicated backfaces. A
mesh with a missing face, interior vertex or non-box geometry keeps mesh collision. Imported scene
transforms are already baked by the shared reader. Collider placement follows independent XYZ
scale, rotation, slope alignment, centering, grounding and offsets, matching the visible model.
Mesh surfaces collide from both sides. **Texture alpha does not cut collision holes**; holes must
exist in the geometry. Fitted boxes fill all openings and enclose disconnected components.

Mesh collision permits **100,000 source triangles per placement**, **500,000 across placements**;
native collision duplicates triangles for both sides. Auto falls back to a box above the per-model
limit and displays a warning; explicit Mesh rejects the oversized edit. Simplify detailed GLBs for
walkable interiors. Zero-area mesh triangles are skipped. Colliders allow a 20 km scaled diagonal
and 100 km local center offset; larger decorative models can use Off. These bounds are independent
of the render budget. Collision edits rebuild a private Bepu shape and may briefly hitch.

Nearby statics participate in each matching body's surface physics bubble, including vehicle and
EVA kitten ground contact. Handles refresh before physics steps and after origin snaps; hidden,
removed and retired-body entries are detached, and pooled/disposed simulations drop their handles.
On-rails/time-warp collision behavior follows the game's surface physics. Physics changes wait
for vehicle and cloth solvers before mutating shapes. Collider state is session-only.

## Implementation and validation

[SphinxLib](../sphinx.lib/README.md) owns the implementation and native render hooks.
[Managed tests](../sphinx.tests/README.md) check grounding, transforms, UV mapping, conservative box detection and collider/render alignment. Full solution
compilation checks typed APIs against KSA 2026.9.7.5402. Native rendering, terrain interaction and
GPU lifetime acceptance still require an in-game run; see [integration scope](../scope/statics.md).
