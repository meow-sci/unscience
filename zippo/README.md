# Zippo - Vehicle Light Control & Animation System

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

A lighting system that lets you select vehicles and their light components, control intensity and color in real time, queue smooth transitions, and run repeating Disco party-light recipes across one light or an entire vehicle.

## Overview

Zippo lets you:
- **Select vehicles and individual light parts** - Browse vehicle hierarchy and identify light components
- **Control light intensity** - Slider from 0.0 (off) to 1.0 (full brightness)
- **Set light color** - 950+ named XKCD colors via filterable combobox, or custom color picker
- **Toggle lights on/off** - Quickly disable/enable selected lights
- **Animate lights** - Queue single-step animations that interpolate color and intensity with easing
- **Run Disco party lights** - Independently cycle colors, moving light assemblies, and spotlight beam spread
- **Real-time updates** - Changes apply immediately in-game

## Features

- **Reflection-based light access** - Finds and manipulates internal KSA light components
- **Vehicle/part selection dropdowns** - Easy navigation of vehicle hierarchy
- **XKCD color palette** - 950+ named colors via filterable combobox (lazy-loaded via reflection)
- **Custom color picker** - ImGui color picker for precise RGB control
- **On/off toggle** - Per-part light enable/disable
- **Recursive part search** - Automatically finds light components nested in part trees
- **Queue-based animation** - Per-part animation queue (max 25) with color+intensity interpolation
- **Easing functions** - Linear, EaseIn, EaseOut, EaseInOut with configurable power parameters
- **Animation status UI** - Progress bar, elapsed/total time display
- **Disco recipes** - Editable 1-32 color palette or deterministic random rainbow hues, independent transition/hold/easing per channel, configurable phase jitter, and single-light or vehicle-wide targeting
- **Safe per-instance effects** - Disco color and cone-angle changes use module-local template copies and restore originals on stop, disappearance, or unload

## Architecture

### Core Classes

#### LightController
Provides reflection-based access to KSA's light system.

**Key Methods**:
- `GetLightParts(Vehicle vehicle)` - Finds all parts with light components in a vehicle
- `HasLights(PartTemplate part)` - Checks if a part template has light components
- `ReadIntensity(PartTemplate part)` - Reads current light intensity value
- `ReadColor(PartTemplate part)` - Reads current light color (RGB)
- `WriteIntensity(List<object> lights, float intensity)` - Sets intensity on light objects
- `WriteColor(List<object> lights, float3 color)` - Sets RGB color on light objects
- `ApplyIntensity(Part part, float intensity)` - Updates intensity for a vehicle part
- `ApplyColor(Part part, float3 color)` - Updates color for a vehicle part

#### DiscoRecipe and DiscoTiming

Describe repeating color, actuation, and spotlight-spread channels. Each active light receives a deep copy of the authored recipe, so later UI edits do not mutate a running effect.

#### DiscoLight

Owns one active light effect. It clones the runtime `LightModule.TemplateData` for instance-local color and cone angles, claims at most one matching assembly actuator, and restores owned state when stopped.

### Reflection Pattern

Zippo uses reflection to access private KSA light components:

```csharp
// Access internal KSA.LightModule+TemplateData components
var lightComponents = ReflectionHelpers.GetFieldValue<List<object>>(part, "_lightData");
```

After mutating color properties, `OnDataLoad()` is called to recompute internal KSA state:
```csharp
lightComponent.OnDataLoad();
```

### UI (Mod.cs)

ImGui window with:
- **Vehicle Selector** - Dropdown to choose which vehicle to modify
- **Light Part Selector** - Dropdown of all light-containing parts in selected vehicle
- **Intensity Slider** - 0.0 to 1.0 with preview
- **Color Presets** - Buttons for Marine, HotPink, RadioactiveGreen, BabyPurple
- **Custom Color Option** - RGB sliders for custom colors
- **Apply/Toggle Buttons** - Apply settings, quickly toggle all lights on/off
- **Disco Party Lights** - Configure channels, palette/random mode, ranges, transition/hold/easing, and target one light or every light on the selected vehicle
- **Active Disco Lights** - Inspect status, pause, toggle the assembly light switch, copy a running recipe, or stop and restore

## Disco Party Lights

Select a vehicle and optionally a light part, then expand **Disco Party Lights**. Choose a single light or enable **All lights on selected vehicle**, select any combination of these channels, and start Disco:

- **Color** cycles through an editable palette of 1-32 colors, or through independently seeded random rainbow hues.
- **Actuation** alternates a matching light assembly's keyframe animation between normalized minimum and maximum positions. Unsupported lights skip this channel. KSA moves toward each goal at the mechanism's own rate, so very short cycles can outpace it.
- **Beam spread** alternates between two inner/outer cone half-angle pairs. Point lights skip this channel.

Each channel has independent transition duration, hold duration, and easing. **Phase jitter** gives every active light and every channel its own stable random time offset from zero up to the configured number of seconds, preventing color, actuation, and beam spread from moving in lockstep. The default is 1 second; set it to 0 for deliberately synchronized playback. Pause freezes the recipe clock, although a mechanism may finish moving toward its most recent goal. Starting Disco again replaces the selected light's previous Disco effect and clears its ordinary Zippo animation queue. Applying an ordinary light edit or queuing a normal animation stops Disco on that light first. Both the standalone mod and the Unscience-hosted feature drive active effects from their frame lifecycle; Unscience's existing hidden-HUD fallback also keeps them moving while F2 hides the game UI.

Color and spread are isolated to each runtime light module rather than mutating the shared part template. An assembly actuator can have only one Disco owner; a later start takes ownership. Stop, part disappearance, and mod unload restore the original module template and restore the actuator goal when it is still owned. If another feature replaces the template or changes the goal, Zippo does not overwrite that external state.

## Light Components

KSA lights are accessed through:
- **Part Template**: Defines what lights a part type has (static, design-time)
- **Light Objects**: Individual light components on parts (instances at runtime)
- **TemplateData**: Contains intensity, color, and other light properties

### Light Properties

| Property | Type | Range | Notes |
|----------|------|-------|-------|
| Intensity | float | 0.0 - 1.0 | Brightness level |
| Color (R/G/B) | float3 | 0.0 - 1.0 | RGB color values |
| Enabled | bool | true/false | Light on/off |

## Color Presets

Pre-defined colors using XKCD naming:

```csharp
new float3(0.0f, 0.5f, 0.7f)      // Marine
new float3(1.0f, 0.0f, 0.6f)      // HotPink
new float3(0.4f, 1.0f, 0.0f)      // RadioactiveGreen
new float3(0.7f, 0.3f, 1.0f)      // BabyPurple
```

Adding new presets is as simple as:
1. Define new float3 RGB values in the color preset list
2. Add button to ImGui window
3. Call `WriteColor()` with new values

## Implementation Details

### Part Scanning
```csharp
var lightParts = LightController.GetLightParts(vehicle);
// Returns only parts with light components, cached for performance
```

### Intensity Update
```csharp
LightController.ApplyIntensity(part, 0.5f);  // Set to 50% brightness
```

### Color Update
```csharp
var newColor = new float3(1.0f, 0.0f, 0.0f);  // Red
LightController.ApplyColor(part, newColor);
```

## Usage Example

```csharp
// Find vehicle to modify
var vehicle = VehicleProvider.GetControlledVehicle();

// Get all light parts
var lightParts = LightController.GetLightParts(vehicle);

// Set all lights to 80% intensity with HotPink color
foreach (var part in lightParts)
{
    LightController.ApplyIntensity(part, 0.8f);
    LightController.ApplyColor(part, new float3(1.0f, 0.0f, 0.6f));
}
```

## Notes for Future Development

- **Performance**: Light updates are reflected immediately; consider batching for many lights
- **Live validation**: Exercise color isolation, moving-light actuation, spotlight cone spread, pause, external template replacement, craft destruction, and unload restoration after KSA updates
- **Save/Load**: No persistence currently; could save/load light configurations
- **Part Naming**: Light parts are identified by KSA's part template system; no manual naming needed
- **Asset Colors**: Could load colors from external XKCD color database instead of hardcoding

## Dependencies

- **MeowSci.KsaAbstractions**: For vehicle and part queries
- **HarmonyLib**: For initialization/cleanup
- **KSA Game**: For light component access via reflection
