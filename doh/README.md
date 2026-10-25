# DOH — Dynamically Originating Hominids

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Programmatic kitten spawning mod for KSA with per-kitten material customization.

## Features

- **Spawn kittens programmatically** near any vehicle with configurable body-frame offset
- **Per-kitten material tinting** — each kitten can have a unique AlbedoColor tint via runtime GPU material creation
- **Batch spawning** — spawn 1–20 kittens at once. A tinted batch shares one material set unless **Unique Each** is on
- **Vehicle naming** — edit the name/prefix (default `Kitten_doh_`); single spawns use it exactly,
  batches append numbers, and blank input keeps automatic `Kitten_doh_` numbering
- **Character selection** — choose a specific character model or spawn random ones
- **Live recoloring** — change a spawned kitten's tint color in real-time via the kitten list
- **Despawn management** — remove individual kittens or despawn all at once
- **F8 hotkey** to toggle the ImGui window

## Usage

1. Press **F8** to open the DOH window
2. Select a reference vehicle from the **Vehicle** dropdown (with filter)
3. Optionally select a specific **Character** (defaults to random), and edit **Vehicle name** /
   **Name prefix**. A single spawn uses the text exactly; a batch appends a unique numeric suffix.
   Leave it blank for automatic `Kitten_doh_` names. An occupied single name is refused.
4. Adjust the **Offset** in the vehicle's body frame (X=right, Y=up, Z=forward)
5. Set **Count** for batch spawning (1–20)
6. Enable **Custom Color** and pick a tint with the color picker
7. For batches, optionally enable **Unique Each** so each kitten gets its own material set
8. Click **Spawn Kitten(s)**

The selection sticks to that vehicle even as other vehicles, debris or kittens come and go. If the
selected vehicle is removed, the selection clears instead of jumping to another craft.
The name field applies to both **Spawn Kitten(s)** and **I'm Feeling Lucky**. Unscience scene saves
restore it with the other spawn controls; older saves and vanilla loads use the default prefix.
Native KSA saves own the names of kittens already spawned.

The status line shows how many of the game's 512 GPU material slots are in use and how many more
tinted sets fit. Each tinted set uses about 10 slots, and doh always leaves 64 free for the game.
When the pool is too full, a tinted spawn is refused with a message. Despawn tinted kittens, turn
off **Unique Each** or spawn fewer to make room. Despawned kittens return their slots.

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
