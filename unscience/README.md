# Unscience — Unified Supermod

Unscience is the only distributed mod. `dotnet build` deploys one `unscience/` folder;
feature libraries remain separate projects with explicit references. Former standalone hosts are
retained for development and are not deployed or published. See [distribution](../README.md#distribution).

A unified supermod that consolidates 28 KSA feature libraries into a single ImGui window with collapsible headers. Each submod's content appears under its own header, and a gear icon context menu lets you toggle individual submod visibility.

## Included Submods

| Submod | Description |
|--------|-------------|
| Blinky — Dynamic LCD Grid | Builds and controls pixel grids on vehicle light parts |
| Bloomin' Onion | Creates and edits planetary ring systems at runtime |
| BYO Music | Imported vessel-attached 3D sounds, repeat/gaps and live volume/range |
| Camera Controller Override | 8 camera animation types (zoom, spiral, orbit, shake) with keyframe sequencing |
| Doh | Spawns EVA kittens and customizes their materials |
| Don't Stifle Me | Extends vehicle-editor scale and configurable-value limits |
| Eternal Flame — Infinite Fuel | Monitors vehicles and periodically refills all fuel tanks |
| Garry's Torch | Welds vehicles together with position/rotation offsets and independent X/Y/Z scale |
| Godzilla | Smart vessel sizing, Basic XYZ scales, independent physics/collider scaling and restoration |
| Glass — Camera Lens | Overrides camera FOV with presets or manual control |
| Graffiti — PNG Decals | Click-to-place projected PNG decals on vehicle hulls, deployed parachute cloth, and terrain |
| Free Fallin — Parachute Customizer | Applies a global stock tint, panel-tiled or cohesive full-canopy PNG, centered decal, and canopy PBR controls |
| Hot Pursuit | Mounts live secondary cameras on vehicle parts |
| Humble Arteest | Kitten colors and Hide/Show visor glass, engine emissive controls, and experimental vehicle paint |
| I Feel Seen | Forces vehicle render data updates at any distance |
| Iron Man | EVA/Iron Man flight-mode buttons, upright editing, configurable nodes, rocket controls and upright surface teleports in Iron Man mode |
| Its So Shiny | Builds and controls Blinky-style pixel grids from built-in light parts |
| Kitchen Sink | Miscellaneous editor and IVA-rendering experiments |
| Kitten Animations | Targets any live EVA kitten through a filterable picker, then plays body animations and expressions |
| Kiwi's Marbles | Welds celestial bodies to other orbiters with CCI offsets |
| Parts Now | Validates and loads part asset bundles at runtime |
| Rocky McRock Face | Swaps planetary ring meshes/textures (Saturn's rock field) with any built-in mesh |
| Pebbles — Ground Clutter | Replaces selected planet clutter types with built-in meshes or GLBs, with scale, collider editing and per-planet restore |
| Pyro | Customizes volumetric engine exhaust plumes |
| Skittles — Theme Manager | Applies and saves ImGui themes with a built-in style editor |
| Sphinx | Places imported textured GLB statics with terrain alignment, XYZ transforms and shared PNG overrides |
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

Godzilla adds session-based Smart vessel resizing, raw XYZ Basic scaling, independent **Scale physics** and **Scale colliders** runtime toggles, and restore controls.
See [Godzilla](../godzilla/README.md).

Sphinx is registered as a regular submod; `Patcher.cs` applies/removes its native static-object
render postfixes. [Sphinx usage](../sphinx/README.md) covers placements and model limits.

Iron Man is registered with `IronManPatches` on the shared Harmony instance. Per-kitten activation
is off by default; authored connector persistence and equipment rendering remain passive while
flight mode is disabled. See [usage and save requirements](../iron-man/README.md).

## Scene saves

Use KSA's ordinary **Save**, **Overwrite** and **Load**. Unscience adds `unscience.json` beside
`universe.xml` in each save directory, and automatically reconstructs scene setups when that save
is loaded. This is separate from **State → Auto save window layout**. Scene state is saved when
KSA saves the game; the window-layout timer does not save the game.

The toolbox displays the most recent save/load result and expandable diagnostics. It captures
feature-owned configuration, targets and original values needed by Restore/Unweld controls.
Created grid parts, spawned kittens and other native state are rebound after KSA reconstructs them;
they are not spawned twice. Runtime part IDs are remapped using durable tree addresses.
Loads execute at the next simulation boundary before new physics and UI work.

Loading a native save without Unscience data clears old scene setups. Missing targets/assets,
incompatible feature versions and partial restores are listed. Failed/partial feature records
remain in subsequent saves so missing entries are not silently lost. While a feature record is
retained, its current edits do not replace that record; **Use current setup for future saves**
discards retained records understood by this build so the current setup can be saved instead.
Unknown feature versions stay retained. Original save files are unchanged until overwritten.

Imported **PNG, GLB and sound libraries** and **Parts Now mod folders** must remain installed.
When moving a save to another computer, copy these dependencies too. Runtime part templates must
be available before native reconstruction; known missing part/character dependencies reject the load
before the current scene is destroyed. Optional missing media leaves a retained feature record.
PNG/audio references use catalog filenames; GLB identities additionally verify content hashes.
Named preset libraries remain global. Skittles also stores the applied style in scene saves.

Camera sequences restore stopped; audio entries restore paused with **Resume** starting from the
beginning. Iron Man modes restore with engines disarmed. Weld/light animation queues continue from captured progress; raw GPU/audio/physics execution
state and transient expression blends are not simulation checkpoints. See each
feature's README and the [implemented coverage and acceptance checklist](../plans/saves-acceptance.md) for details.

A sidecar copied beside a different `universe.xml`, malformed data, or a future document schema is
reported and not applied. Native saves remain usable without the sidecar where their required
part mods are installed. Atomic sidecar writing does not make KSA's native overwrite atomic: KSA
still deletes the old save directory first. Keep normal backup copies for valuable scenes.

Architecture: `UnscienceSaves` discovers library participants and wires shared `NativeSaveHooks`;
`SceneSaveCoordinator` orders/reset/replays them and retains failed records. See the
[implementation plan](../plans/SAVES.md) and [integration map](../scope/saves.md).
