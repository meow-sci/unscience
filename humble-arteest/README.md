# Humble Arteest

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Standalone KSA mod providing visual customization features. Toggle with F11.

## Features

- **Vehicle Paint** — Recolor individual part instances (or a whole part type, or everything) at
  runtime. Tick **Enable painting**, pick a brush color, then tick the parts you want. Blend modes:
  *Multiply* (keeps texture detail, can only darken), *Tint* (recolors by luminance, can brighten),
  *Replace* (flat color). Works in flight and in the vehicle editor.
- **Kitten Color** — Character model tinting via GPU material buffer AlbedoColor writes
- **Engine Emissive** — Per-engine glow control via Temperature field override

## Notes on Vehicle Paint

- Enabling paint, or changing the blend mode, triggers a **renderer rebuild** — a brief one-off hitch,
  the same one the game performs when you change a graphics setting. Picking colors is free.
- Nothing on disk is modified. The game's shader files are read, patched **in memory**, and compiled.
- Paint is not saved; it lives for the session and is cleared on unload.
- Windows (glass parts) are deliberately not painted.

## Architecture

See [humble-arteest.lib/README.md](../humble-arteest.lib/README.md) for comprehensive technical documentation including shader modification details, struct layouts, rendering pipeline analysis, and maintenance guidance.

## Unscience Integration

All features are also available as submods in the unscience supermod via `humble-arteest.lib`.