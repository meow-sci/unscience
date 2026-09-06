# DOH — Dynamically Originating Hominids

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Programmatic kitten spawning mod for KSA with per-kitten material customization.

## Features

- **Spawn kittens programmatically** near any vehicle with configurable body-frame offset
- **Per-kitten material tinting** — each kitten can have a unique AlbedoColor tint via runtime GPU material creation
- **Batch spawning** — spawn 1–20 kittens at once with optional unique colors per kitten
- **Character selection** — choose a specific character model or spawn random ones
- **Live recoloring** — change a spawned kitten's tint color in real-time via the kitten list
- **Despawn management** — remove individual kittens or despawn all at once
- **F8 hotkey** to toggle the ImGui window

## Usage

1. Press **F8** to open the DOH window
2. Select a reference vehicle from the **Vehicle** dropdown (with filter)
3. Optionally select a specific **Character** (defaults to random)
4. Adjust the **Offset** in the vehicle's body frame (X=right, Y=up, Z=forward)
5. Set **Count** for batch spawning (1–20)
6. Enable **Custom Color** and pick a tint with the color picker
7. For batches, optionally enable **Unique Each** so each kitten gets its own material set
8. Click **Spawn Kitten(s)**

Spawned kittens appear in the list below with:
- Inline color editor for live recoloring (when custom materials were applied)
- Individual despawn button per kitten
- **Despawn All** button to remove everything

## Architecture

All spawning and material logic lives in `doh.lib` (headless library). This mod project only handles ImGui UI and StarMap lifecycle. The library API is reusable by other mods.

## Dependencies

- `doh.lib` — core spawning/material logic
- `ksa-abstractions.lib` — VehicleProvider, HotkeyGuard, SubmodUI
- StarMap.API, Lib.Harmony
- KSA game DLLs (KSA, Brutal.Core.Numerics, Brutal.ImGui, etc.)
