# free-fallin — parachute appearance customizer

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

`free-fallin` applies one appearance to every deployed KSA parachute canopy. It works both as a
standalone F11 mod and as the **Free Fallin - Parachute Customizer** section in `unscience`.

## Features

- **Stock + tint** keeps KSA's authored canopy pattern and multiplies it by any RGB color and brightness.
- **Replace** maps an imported PNG across the canopy's authored UVs.
- **Full canopy** projects an imported PNG once across the complete canopy in bind-pose X/Z space,
  so adjacent gores receive adjacent parts of one cohesive image. Rotation can be adjusted in the UI.
  The projection follows skeletal cloth deformation while the stock UVs continue to drive the cloth
  normal and PBR maps.
- **Center decal** alpha-composites an imported PNG over KSA's stock albedo before upload. Because
  the result is the canopy material—not a world-space Graffiti projection—it bends with the cloth.
  KSA's runtime BC7 canopy texture is reopened from its source KTX2 and transcoded to RGBA8 for
  this CPU composition step. If a game distribution stores native, non-transcodable BC7 instead,
  the decal is composed over a flat tintable base rather than failing.
- **PBR controls** either multiply the stock AO/roughness/metallic texture or replace it with
  uniform 0–1 values. Uniform mode can make the canopy genuinely metallic even when the stock
  metallic channel is zero.
- Changes apply globally to existing and future parachutes. **Restore Stock** reverts every canopy
  observed by the mod during the session.

## Usage

1. Press F11 (standalone) or expand Free Fallin in unscience.
2. Choose Stock, Replace, Full canopy, or Center decal. Use **Replace** for a repeating panel pattern;
   use **Full canopy** for one image spanning the deployed parachute.
3. For PNG modes, select **Import PNG...** and pick a file. Imports are copied to the shared
   `My Games/Kitten Space Agency/.unscience/pngs/` catalog used by Graffiti and can also be dropped
   there manually. Use **Rescan PNGs** after adding or changing files by hand.
4. Tune tint and PBR values, then press **Apply to All Parachutes**.
5. Deploy a parachute to preview the result. Press **Restore Stock** to undo it.

The setting is session-only; imported PNG files remain in the library for later sessions.

## Projects

- `free-fallin/` — StarMap host, F11 window, Harmony and HotkeyGuard lifecycle.
- `free-fallin.lib/` — reusable `ISubmod`, shared PNG-browser consumer, CPU decal compositor, runtime
  material builder, canopy projection shader patch, and canopy draw patch.

## Compatibility

Cataloged against KSA `2026.9.7.5402`. The mod depends on the dedicated
`ParachuteCanopy_Material`, `ChuteRenderable.Draw`, its private `_renderable` field, and
`AnimatedRenderable.MaterialIndices`. See [`../scope/parachutes.md`](../scope/parachutes.md).
