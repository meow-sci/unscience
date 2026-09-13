# Its So Shiny Lib

Reusable implementation for the `its-so-shiny` light-part pixel grid mod.

## Purpose

This library contains the submod UI and all game-facing behavior so the standalone mod, unscience, and future RPC integrations can reuse the same state and APIs.

## Main Types

- `ItsSoShinySubmod` - `ISubmod` implementation with the Blinky-style ImGui create/manage UI
- `ShinyGridBuilder` - creates and destroys runtime `LightPart` grids on vehicles
- `ShinyGridManager` - registers grids and exposes off, pattern, static display, and scroll operations
- `ShinyPixelGrid` - scans `shiny_{gridName}_{row}_{col}` parts back into a grid
- `ShinyPixelCell` - wraps one host `LightPart` and its actual light-bearing subpart
- `ShinyGridConfig` - layout and placement settings for grid creation

## Implementation Notes

Each pixel is one built-in `LightPart`. The builder attaches created light parts under the vehicle root, connects them to battery-bearing parts when available, rebuilds the part tree once, and then controls pixel state through each light's stock `PowerConsumer` light switch. Color and intensity reuse Zippo's `LightController` helper.
## Scene saves

Unscience saves grid/cell ownership, durable host/light part paths, appearance, active-mask metadata,
mesh visibility and scrolling pixels/speed/phase. Native KSA already saves the created parts and
switch state; replay rebinds them without creating another grid. Render membership also consults
the manager because KSA omits ordinary `shiny_*` part names. Native power connections remain in
the native part tree. Shared-template appearance writes are also captured by Zippo's template
ledger; existing shared-template color behavior is preserved. Missing cells produce a load warning.

Pending grid removals are saved as intent and restarted against restored parts; old-world deferred
callbacks are cleared during scene reset. Removal delays restart rather than running against stale
vehicle references.
