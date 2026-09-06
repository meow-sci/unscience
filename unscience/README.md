# Unscience — Unified Supermod

Unscience is the only distributed mod. `dotnet build` deploys one `unscience/` folder;
feature libraries remain separate projects with explicit references. Former standalone hosts are
retained for development and are not deployed or published. See [distribution](../README.md#distribution).

A unified supermod that consolidates 24 KSA feature libraries into a single ImGui window with collapsible headers. Each submod's content appears under its own header, and a gear icon context menu lets you toggle individual submod visibility.

## Included Submods

| Submod | Description |
|--------|-------------|
| Blinky — Dynamic LCD Grid | Builds and controls pixel grids on vehicle light parts |
| Bloomin' Onion | Creates and edits planetary ring systems at runtime |
| Camera Controller Override | 8 camera animation types (zoom, spiral, orbit, shake) with keyframe sequencing |
| Doh | Spawns EVA kittens and customizes their materials |
| Don't Stifle Me | Extends vehicle-editor scale and configurable-value limits |
| Eternal Flame — Infinite Fuel | Monitors vehicles and periodically refills all fuel tanks |
| Garry's Torch | Welds vehicles together with position/rotation offsets and independent X/Y/Z scale |
| Glass — Camera Lens | Overrides camera FOV with presets or manual control |
| Graffiti — PNG Decals | Click-to-place projected PNG decals on vehicle hulls, deployed parachute cloth, and terrain |
| Free Fallin — Parachute Customizer | Applies a global stock tint, panel-tiled or cohesive full-canopy PNG, centered decal, and canopy PBR controls |
| Hot Pursuit | Mounts live secondary cameras on vehicle parts |
| Humble Arteest | Kitten colors, engine emissive controls, and experimental vehicle paint |
| I Feel Seen | Forces vehicle render data updates at any distance |
| Its So Shiny | Builds and controls Blinky-style pixel grids from built-in light parts |
| Kitchen Sink | Miscellaneous editor and IVA-rendering experiments |
| Kitten Animations | Targets any live EVA kitten through a filterable picker, then plays body animations and expressions |
| Kiwi's Marbles | Welds celestial bodies to other orbiters with CCI offsets |
| Parts Now | Validates and loads part asset bundles at runtime |
| Rocky McRock Face | Swaps planetary ring meshes/textures (Saturn's rock field) with any built-in mesh |
| Pebbles — Ground Clutter | Replaces selected planet clutter types with built-in meshes or GLBs, with scale, collider editing and per-planet restore |
| Pyro | Customizes volumetric engine exhaust plumes |
| Skittles — Theme Manager | Applies and saves ImGui themes with a built-in style editor |
| Thug Life | Renders a custom textured quad through KSA's main render pass |
| Zippo — Light Control | Controls light appearance and queued transitions, plus repeating Disco color, actuation, and spotlight-spread cycles |

## Usage

- **F11** — Toggle the unscience window
- **Gear icon (⚙)** — Opens a popup to show/hide individual submods
- Each submod has a **collapsible header** that can be expanded or collapsed
- The **Skittles Theme Editor** opens in a separate window via the "Open Theme Editor" button

## Architecture

- **`ISubmod`** interface (from `ksa-abstractions.lib`) defines the submod contract: `Name`, `Initialize()`, `Update(dt)`, `RenderContent()`, `Dispose()`
- **`Mod.cs`** orchestrates all submods — instantiates lib submod classes directly, calls `Update()` every frame for all (even hidden), renders only visible ones
- **Hidden-HUD (F2) resilience**: `HiddenUiFrameHook.BeforeGui` replays `UpdateSubmods(dt)` while KSA skips StarMap UI callbacks. Welds run independently through `GarrysTorchPatches` in `Program.PrepareFrame`; no after-GUI weld callback is registered. Mod windows and the F11 toggle remain hidden with the HUD.
- **`Patcher.cs`** consolidates Harmony patches from blinky (render-skip), camera-controller-override (sequence playback via `CameraControllerOverridePatches`), free-fallin (canopy material substitution and material-gated full-canopy shader projection via `FreeFallinPatches`), garrys-torch (KittenEva XYZ render-scale correction via `KittenScalePatches`), glass (FOV override), graffiti (projected-decal render pass via `GraffitiPatches`), i-feel-seen (render distance), pyro (exhaust submission via `PyroPatches`), skittles (hotkey blocking), and dont-stifle-me (editor scale and configurable-value limits via `EditorScalePatches` / `EditorValueLimitPatches`), delegating to patch helpers in each lib
- **Garry's Torch update timing**: the shared `GarrysTorchPatches` frame transpiler runs weld animation and teleports after completed orbit/vehicle/cloth results are applied and before any next-step physics snapshots. Teleports use `SimStep.PreviousTime`, preserving source actuator state and body/origin time alignment. Ordinary submod and UI updates do not advance welds.
- Submod implementations live in their respective **`.lib` projects** (for example `BlinkySubmod` + `BlinkyPatchState` in `blinky.lib`, `CameraControllerOverrideSubmod` in `camera-controller-override.lib`, and `KittenAnimationsSubmod` in `kitten-animations.lib`)
- **`unscience/Submods/`** directory has been removed — no intermediate wrapper layer
- Each lib submod owns its own ImGui `RenderContent()` — unscience just calls it

## Dependencies

The supermod references each included feature's `.lib` project plus `ksa-abstractions.lib`; see `unscience.csproj` for the authoritative dependency list.

## Pebbles integration

Pebbles uses the existing collapsible submod panel and feature-owned session state. Author a
mesh/GLB and colliders, select a planet and clutter types, then Apply. Applied clutter and import
counts appear below the form, alongside restore-type, restore-planet and release-all controls.
The floating collider editor/browser continue rendering when the main panel is collapsed.
`Patcher.cs` wires its controller into the shared Harmony instance; cleanup removes only
Pebbles methods. The host's existing HotkeyGuard and hidden-HUD update hook cover Pebbles.
No newux shell, workspace persistence or Live State framework is included.
See [Pebbles README](../pebbles.lib/README.md) for usage and limitations.
