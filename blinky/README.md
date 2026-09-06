# blinky — Dynamic LCD Engine Pixel Grid

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

A KSA mod that dynamically creates LCD pixel grids of engine parts at runtime and attaches them to existing vehicles. Supports **multiple named grids per vehicle**, each independently configured and controlled through its ImGui UI.

## Overview

**blinky** builds NxM grids of engine parts on demand by:
1. Looking up an engine `PartTemplate` from `ModLibrary`
2. Creating `Part` instances for each grid cell (a/b pairs for balanced thrust)
3. Wiring them to the vehicle's root part via manual `TreeParent`/`TreeChildren` assignment
4. Connecting each pixel engine's **declared feed connector** to a fuel-bearing part so it can draw propellant
5. Rebuilding the `PartTree` once with `PartTree.CreateFromNewPartTree()`
6. Naming them `pixel_{gridName}_{row}_{col}_{a|b}` for grid lookup

A pixel "lights" by activating a real engine, so what you actually see is the engine's exhaust
plume — the meshes are scaled to ~1% and are effectively invisible on their own.

Each vehicle can have multiple grids, distinguished by a user-chosen **grid name**. Grid names must contain only alphanumeric characters and hyphens (`[a-zA-Z0-9-]`) — underscores are reserved as the part ID delimiter.

## Controls

- **F11** — Toggle the blinky window

## Window Sections

| Section | Description |
|---------|-------------|
| **Menu Bar** | Debug menu with global "Scan for blinky grids" across all vehicles |
| **Create Blinky Grid** | Collapsible 4-column table: grid size, spacing, engine scale, position, layout, engine preset, grid name, vehicle selector, and Create button |
| **Per-Grid Sections** | Collapsible header per registered grid with info table, pattern buttons, and destroy |

## Features

### Multi-Grid Support
Each vehicle can host multiple independent named grids. Grids are keyed by `(vehicleId, gridName)` throughout the system. The UI shows collapsible sections for each registered grid.

### Pattern Presets
Built-in pattern buttons per grid: All On, Off, Alternating Rows, Alternating Cols, Checkerboard.

### Static Display
Paints a set of pixels directly. Supports intelligent reset mode that only changes the pixels that need updating (diffs current vs new state).

### Global Scan (Debug Menu)
Auto-discovers all named blinky grids on all loaded vehicles by parsing `pixel_{gridName}_{row}_{col}_{a|b}` part IDs and registering each discovered grid.

### Propellant Feed

Pixel engines only fire if they can reach propellant. KSA's resource graph refuses the first hop out
of a consumer part unless the connection sits on a connector the part template declares in its
`ConsumerFeedWiring`/`FeedsFrom` wiring **and** that connection carries the combustor's plumbing
capability (`BulkFluid` for these engines). blinky therefore connects
`RocketCore.FeedConnectors[i]` (e.g. EngineA3's `_connector3`) to a fuel-bearing `Part`, and
stage-aligns each pixel part to its fuel anchor so the `…SameStage` flow rules still find the tank.

A bare part-to-part connection satisfies neither requirement — that was the long-standing bug where
grids added vehicle mass but never lit.

After building, the console reports how many pixel parts reached at least one tank:

```
blinky: fuel-fed 128/128 pixel parts via their declared feed connectors
blinky: propellant feed check — 128/128 pixel parts reached at least one tank
```

Gas-generator cores that burn a propellant the vehicle does not carry are reported as starved; that
is expected and does not stop the main thrust chamber from firing.

> **The pixel engines burn Hydrolox.** Every liquid `CorePropulsionA_Prefab_EngineA2..A6` thrust
> chamber is authored `<Reaction Id="Hydrolox">`, so the host vehicle's tanks must actually contain
> LH2/LOX. If they hold something else, `Tank.ContainsAny(Mix)` fails and the grid stays dark even
> with perfect wiring — the console reports `no pixel can light. The engines burn 'Hydrolox'…`.
> blinky never reconfigures the vehicle's tank contents, so it cannot break the real engines.

### Repair Feed

Grids discovered by the **global scan** — after a save/load, or built by an older blinky — have no
declared feed connection and stay dark. **Repair Feed** (per-grid button, or
`POST /blinky/grids/repair`) re-wires them in place and forces the resource managers to rebuild via
`ResourceGroupList.CalculateStages()`, without rebuilding the part tree.

### Ignition & Throttle

Nothing lights unless the vehicle is ignited (`VehicleEngine.MainIgnite`) **and** the throttle is
above zero. blinky ignites the vehicle for you whenever it drives pixels, and warns in the window
when the throttle is at zero or the flight computer is in `Auto` burn mode (which clears ignition
every frame).

### Diagnose

Logs, per grid: vehicle ignition/throttle/burn-mode, how many engine controllers reach propellant,
and for a sample pixel — controller activity, stage, each declared feed connector's target, and the
`ResourceManager` flow rule with its `ConsumptionOrder` level/tank counts.

### Render Toggle
Checkbox to toggle engine mesh rendering for a significant performance boost — hides part meshes while keeping the pixel grid fully functional.


## Grid Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| Columns | 8 | Number of pixel columns |
| Rows | 8 | Number of pixel rows |
| Layout | Flat | Flat plane or Cylinder (sides only) |
| Spacing (m) | 5.0 | Metres between pixel centres |
| Position X/Y/Z | 0, 0, 0 | Offset from vehicle root origin |
| Engine Scale | 0.010 | Scale factor for engine part meshes |
| Engine | EngineA3 | Part template ID (A2–A6 filtered quick-select; `EngineA1` no longer exists in the game) |

## Grid Naming

Grid names are user-chosen identifiers that distinguish multiple grids on the same vehicle.

| Rule | Detail |
|------|--------|
| Allowed characters | `a-z`, `A-Z`, `0-9`, `-` (hyphen) |
| Not allowed | `_` (underscore) — reserved as part ID delimiter |
| Part ID format | `pixel_{gridName}_{row}_{col}_{a\|b}` |

## Project Structure

```
blinky/                       ← Mod entry point (ImGui UI + lifecycle)
├── Mod.cs                    ← Main mod class (F11 window, UI controls)
├── Patcher.cs                ← Harmony render-skip patches for pixel parts
├── blinky.csproj
└── mod.toml

blinky.lib/                   ← Core reusable logic (headless)
├── BlinkyGridManager.cs      ← Static singleton: compound (vehicleId, gridName) key APIs
├── ScrollAnimation.cs        ← Scrolling animation engine
├── PixelGrid.cs              ← Vehicle pixel grid scanner + engine controller cache
├── PixelPatterns.cs           ← Built-in pattern functions
├── LcdGridConfig.cs          ← Grid configuration data class
├── LcdGridBuilder.cs         ← Runtime Part creation, tree wiring, propellant feed, repair
├── BlinkyPixelGrid.cs        ← PixelGrid wrapper with owned-parts lifecycle
├── NonLcdEngineCache.cs      ← Lazily cached non-pixel EngineControllers per vehicle
├── BuiltInScrollPixels.cs    ← Default built-in scroll animation pixel data
└── blinky.lib.csproj
```

## Dependencies

- `ksa-abstractions.lib` — `VehicleProvider` and `PartHelpers`

## Architecture

- **blinky.lib** is fully self-contained
- **BlinkyGridManager** is the static singleton used by the mod UI and reusable library API
- Grids are registered by compound key `(vehicleId, gridName)` and discoverable from any consumer
- Multiple grids per vehicle are fully independent (own config, scroll state, active pixels)
- The mod UI (`Mod.cs`) is a thin ImGui layer that delegates all logic to `BlinkyGridManager`
- **`BlinkySubmod`** lives in `blinky.lib` and implements `ISubmod` from `ksa-abstractions.lib`; it is instantiated directly by the unscience supermod
