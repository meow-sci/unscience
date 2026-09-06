# Its So Shiny - Light-Part Pixel Grids

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Its So Shiny builds Blinky-style pixel grids from KSA's built-in `LightPart` instead of engine parts. Each pixel is a real light part attached to a selected vehicle and controlled through the light switch/power consumer path used by the stock lights.

## Features

- Build named light grids on any loaded vehicle
- Flat or cylindrical layouts
- Configurable columns, rows, spacing, offset, light scale, color, and intensity
- Pattern controls: off, all on, alternating rows, alternating columns, and checkerboard
- Scan existing `shiny_*` grids on loaded vehicles
- Destroy owned grids and remove their runtime-created parts
- Embeddable `ItsSoShinySubmod` for the unscience supermod

## Usage

- Standalone mod: press F11 to open the `its-so-shiny` window
- In unscience: expand `Its So Shiny - Light Grids`
- Pick a vehicle, enter a grid name using letters, digits, or hyphens, tune the layout, then press **Create**

The mod uses the built-in `LightPart` template. Light parts consume electrical power, so grids work best on vehicles with batteries or other available electrical storage.

## Architecture

- `its-so-shiny` hosts the standalone StarMap entry point and applies the required hotkey guard.
- `its-so-shiny.lib` contains all reusable behavior: grid creation/destruction, scanning, pattern control, scroll support, and ImGui submod UI.
- `ShinyGridBuilder` creates one `LightPart` per pixel, wires the new subtree first, then rebuilds the vehicle once instead of mutating the live vehicle per part.
- New grids are registered directly from the freshly created parts, avoiding a post-build whole-vehicle rescan.
- `ShinyGridManager` deduplicates color and intensity writes by underlying `PartTemplate`, which cuts repeated reflection work when large grids are created or recolored.
- `ShinyGridManager` is the public control surface for registered grids and can be reused by aggregate mods or future RPC endpoints.