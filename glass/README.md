# Glass - Camera Field of View Control

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

A camera lens system that provides 8 photographic lens presets plus manual FOV control. Allows you to quickly switch between telephoto, wide-angle, fisheye, and other lens configurations for different viewing scenarios.

## Overview

Glass lets you:
- **Switch lens presets** - 8 options from 10mm fisheye to 200mm super telephoto
- **Manual FOV adjustment** - Fine-tune field of view with slider (1° to 179°)
- **Real-time FOV changes** - Updates applied every frame
- **Lock override** - Toggle override on/off to return to game defaults

## Features

- **8 lens presets** - Real camera lens equivalents for precise control
- **Harmony patching** - Cleanly intercepts FOV changes without game conflicts
- **Field validity** - Clamps FOV to safe range (1° to 179°)
- **Game default preset** - 50° standard FOV for quick reset
- **Real-time updates** - FOV changes apply immediately during gameplay

## Lens Presets

Each preset represents a real camera lens focal length:

| Preset | FOV | Equivalent Focal Length | Use Case |
|--------|-----|------------------------|----------|
| Fisheye | 120° | 10mm | Ultra-wide sky/landscape |
| Ultra Wide | 100° | 14mm | Wide landscape views |
| Wide Angle | 75° | 28mm | General wide views |
| Standard | 50° | 50mm | Default game view |
| Portrait | 30° | 85mm | Detail focus |
| Telephoto | 20° | 135mm | Distant targets |
| Super Telephoto | 15° | 200mm | Far zoom |
| Game Default | 50° | 50mm | Game's native FOV |

## Architecture

### FOV State (`glass.lib/FovController`)

FOV state and control logic lives in `glass.lib` as `FovController` (namespace `MeowSci.GlassLib`), not in the mod itself. This allows other projects to control camera FOV by referencing `glass.lib` without depending on the `glass` mod.

`GlassSubmod` also lives in `glass.lib` and implements `ISubmod` from `ksa-abstractions.lib`. It is instantiated directly by the unscience supermod.

Key API:
- `FovController.SetFov(float degrees)` — clamps to [1°, 179°] and activates override
- `FovController.DisableOverride()` — returns control to the game
- `FovController.ApplyFov()` — applies current override to the camera (call on game thread)
- `FovController.GetCurrentFovDegrees()` — reads live camera FOV (game thread)
- `FovController.IsOverrideActive` / `FovController.OverrideFovDegrees` — state properties

### UI (Mod.cs)

ImGui window with:
- **Preset buttons** - Quick select any of 8 presets (F9 toggle for window)
- **Manual FOV slider** - 1° to 179° with numeric input
- **Override toggle** - Enable/disable FOV override
- **Current FOV display** - Shows active FOV in degrees

### Harmony Patches

**Patched Methods**:
1. `Camera.ChangeFieldOfView(float)` - Prefix that blocks game's FOV input when override is active
2. `Camera.UpdateProjection()` - Prefix that applies override FOV value

**Patch Strategy**:
- When override is active, game's `ChangeFieldOfView` calls are ignored
- Every frame in `UpdateProjection`, override FOV is directly applied
- When override is disabled, game's normal FOV system resumes

### Field Access

Glass uses `AccessTools` to locate the internal `_fovRadians` field:

```csharp
// Field is stored in radians internally
_fovRadians = fovDegrees * (π / 180)

// On patch, override is applied directly:
if (IsOverrideActive)
    _fovRadians = OverrideFovDegrees * (π / 180)
```

## Implementation Details

### FOV Conversion
```
Degrees to Radians: radians = degrees × (π / 180)
Radians to Degrees: degrees = radians × (180 / π)
```

### Harmony Patch Pattern

```csharp
// In Camera.UpdateProjection prefix:
if (IsOverrideActive && OverrideFovDegrees > 0)
{
    var fovRadians = OverrideFovDegrees * Mathf.Pi / 180f;
    _fovRadians = fovRadians;
    return true;  // Skip original UpdateProjection
}
return false;  // Allow original to run
```

### Clamping
```csharp
const float MinFov = 1f;
const float MaxFov = 179f;

fov = Mathf.Clamp(fov, MinFov, MaxFov);
```

## Usage Example

```csharp
// Activate override and set to telephoto
IsOverrideActive = true;
OverrideFovDegrees = 20f;  // Telephoto preset

// Manual adjustment
OverrideFovDegrees = 35f;  // Custom wide-angle

// Disable to return to game default
IsOverrideActive = false;
```

## Configuration

All settings configured through ImGui window:

| Setting | Range | Notes |
|---------|-------|-------|
| FOV | 1° - 179° | Manual slider control |
| Override Active | true/false | Enable/disable |
| Preset Selection | 8 options | Quick preset select |

## Performance

- **Lightweight**: Simple field assignment per frame
- **No texture/rendertarget changes**: Only projection matrix updates
- **Instant feedback**: FOV changes visible immediately

## Notes for Future Development

- **Cinematic presets**: Could add movie/cinema-style FOV configurations
- **Zoom transitions**: Animate between FOV values over time
- **Save preferences**: Remember user's favorite FOV settings
- **Aspect ratio**: Could adjust presets based on screen aspect ratio
- **Lock position**: Hold camera in place while changing FOV

## Technical Details

### Camera Math

Field of View affects the camera's projection matrix:

```
projection matrix ∝ cot(fov/2)
```

Larger FOV → wider view, smaller projected objects
Smaller FOV → narrower view, larger projected objects

### Harmony Offset

The `_fovRadians` field is located using `AccessTools.DeclaredField`:
```csharp
AccessTools.DeclaredField(typeof(Camera), "_fovRadians")
```

This ensures compatibility across KSA versions without hard-coding field offsets.

## Dependencies

- **HarmonyLib**: For Camera method patching
- **KSA Game**: Camera class and projection system
