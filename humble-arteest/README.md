# Humble Arteest

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Standalone KSA mod providing visual customization features. Toggle with F11.

## Features

- **Vehicle Paint** — Recolor individual part instances (or a whole part type, or everything) at
  runtime. Tick **Enable painting**, pick a brush color, then tick the parts you want. Blend modes:
  *Multiply* (keeps texture detail, can only darken), *Tint* (recolors by luminance, can brighten),
  *Replace* (flat color). Works in flight and in the vehicle editor.
- **Kitten Visors** — **Hide visor glass** / **Show visor glass** controls all kitten visors,
  including new spawns. It does not affect helmet shells or vehicle windows. The Unscience host
  saves this toggle with the scene; scene reset and unload restore drawing. Kitten tinting and
  material presets are now in [War Paint](../war-paint.lib/README.md).
- **Engine Emissive** — Per-engine glow control via Temperature field override

## Notes on Vehicle Paint

For the reported white-vessel glow on Enable, use the logged activation details and process-local
stock shader comparison described in [the 5801 investigation](../plans/HUMBLE_ARTEEST_5801_PAINT_INVESTIGATION.md).
It documents `UNSCIENCE_PAINT_SHADER_MODE=stock`, the required fresh process, and restoring normal
painting. The native rendering cause remains unconfirmed.

- Enabling paint, or changing the blend mode, triggers a **renderer rebuild** — a brief one-off hitch,
  the same one the game performs when you change a graphics setting. Picking colors is free.
- Nothing on disk is modified. The game's shader files are read, patched **in memory**, and compiled.
- Unscience saves vehicle paint, engine glow, and visor visibility with native scene sidecars.
  This development host supplies the UI; sidecar saves are supplied by Unscience.
- Windows (glass parts) are deliberately not painted.
- KSA 5482 caches part render state per vehicle. Paint is written into that cache, and each paint
  change invalidates it once, so a change appears on the next frame. Paint also shows in raytraced
  IVA; part thumbnails stay unpainted. The four paint hooks (previously five) and the per-view visor
  draw hook have only compile and managed checks on 5482; they still need an in-game pass.

## Architecture

See [humble-arteest.lib/README.md](../humble-arteest.lib/README.md) for comprehensive technical documentation including shader modification details, struct layouts, rendering pipeline analysis, and maintenance guidance.

## Unscience Integration

All features are also available as submods in the unscience supermod via `humble-arteest.lib`.
