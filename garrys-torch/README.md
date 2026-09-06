# Garry's Torch - Vehicle Welding System

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

A vehicle docking/attached system that welds one vehicle to another with full support for position offsets, rotation alignment, and independent X/Y/Z scaling. Welds are persistent per-frame—children move relative to their parent vehicle.

## Overview

Garry's Torch allows you to:
- **Weld two vehicles together** - Attach a source vehicle to a target vehicle
- **Anchor to a specific part** - Pick any part on the target vehicle as the anchor point; offsets are relative to that part, not the vehicle CoM
- **Configure relative position** - Separate the vehicles on XYZ axes in the target part's local frame
- **Rotate freely** - Apply pitch/yaw/roll rotations relative to the target part's orientation
- **Scale each axis independently** - Resize or stretch the source vehicle on local X/Y/Z axes (including avatars)
- **Manage multiple welds** - A vehicle can have multiple welds simultaneously
- **Use presets** - Built-in configurations for common docking scenarios

## Features

- **Real-time vehicle positioning** - Welds update every frame to maintain relative position/rotation
- **Part-anchored welding** - Anchor to any part on the target vehicle; the weld tracks that part, not the vehicle CoM. Immune to CoM drift as fuel burns, and naturally follows robotics-moved parts
- **Physics-loop safe updates** - Welds run immediately before KSA queues vehicle solver jobs, avoiding worker-thread state races in the refactored physics loop
- **Part-frame coordinates** - Positions and rotations specified in the target part's local coordinate system
- **Rotation locking** - Option to prevent source vehicle from rotating relative to target
- **Parent validation** - Welds automatically break if vehicles cross celestial body boundaries
- **Quaternion-based math** - Proper 3D rotation handling with Euler angle conversion
- **Physics safety** - Guards against NaN values in body rates to prevent simulation corruption
- **Preset system** - Quick apply common configurations (Ridin' Dirty 1-3, Shotgun, Not Shotgun)

## Architecture

### Core Classes

#### Weld update timing

`GarrysTorchPatches` transpiles the private `Program.PrepareFrame(double, double)` caller.
It replaces the single `Universe.GetJobSimStep(dtPlayer)` call with a wrapper that obtains the
same step, updates welds, and returns that step unchanged. Both the standalone mod and Unscience
install this shared patch; UI callbacks no longer advance welds, so F2 cannot stop or double them.

KSA 5402 applies completed orbit, vehicle and cloth results before this call, then queues the next
cloth, vehicle and orbit solvers after it. The patch validates those seven calls occur exactly once
and in that order; an unexpected layout rejects installation with a logged error. Patching the
caller also avoids depending on solver calls that may already have been inlined before mod loading.

The old UI callback waited for workers, then teleported the source **before their results were
applied**. `Vehicle.Teleport` removes it from its physics bubble, so `ApplyResultsToVehicles` skipped
its module-state commit. A light actuator repeatedly started from the same `TimeCurrent` instead
of accumulating progress toward `TimeGoal`. Waiting for a worker is not the same as applying its
results. Moving welding to this handoff lets KSA commit those results before the source is removed
and reattached for the next tick.

The orbit timestamp is now the supplied `SimStep.PreviousTime`, matching the just-applied body and
bubble-origin time. `NextTime` would put it one tick ahead. Weld interpolation still uses the
player delta time, preserving its existing pause/time-warp behavior. Kitten animation targeting is
unchanged; light actuation depends on simulation module state rather than the skeletal renderer.

The internal update entry point takes the committed state time explicitly. The old public
`UpdateWelds(dt)` and `UpdateBeforeVehicleSolvers(dt)` callbacks were removed: hosts should install
`GarrysTorchPatches` instead of invoking teleports from the UI.

#### WeldEngine
Stateless computation engine for vehicle welding. Contains all physics/math logic.

**Key Methods**:
- `UpdateWeld(WeldEntry weld, UniverseTime stateTime)` - Teleports source vehicle to maintain relative position/rotation to target, then refreshes per-frame vehicle caches
- `EulerDegreesToQuat(float pitch, float yaw, float roll)` - Converts Euler angles to quaternion with ZYX intrinsic convention
- `ApplyVehicleScale(Vehicle vehicle, float3 scale)` - Applies independent X/Y/Z scale to all parts

**Key Logic**:
- Uses quaternion multiplication: `worldRotation = targetRotation * relativeRotation`
- Position computed in body frame then transformed to world space
- NaN guard for body rates: prevents physics corruption from invalid angular velocities

#### WeldEntry
Container for an active weld between two vehicles.

```csharp
public class WeldEntry
{
    public Vehicle Source { get; set; }           // Vehicle being welded
    public Vehicle Target { get; set; }           // Vehicle being welded to
    public Part? TargetPart { get; set; }         // Anchor part on target (null = vehicle CoM fallback)
    public float3 RelativePosition { get; set; }  // Offset relative to anchor (part frame or body frame)
    public float3 RelativeRotation { get; set; }  // Pitch/Yaw/Roll relative to anchor orientation (degrees)
    public float3 Scale { get; set; }             // XYZ factors (0.05 to 20.0 per axis)
    public bool LockRotation { get; set; }        // Prevent relative rotation
}
```

#### WeldPreset
Data container for preset weld configuration (position, rotation, XYZ scale, lock rotation).

#### PresetManager
Manages named presets persisted to a TOML file at `My Games/Kitten Space Agency/.unscience/garrys-torch-presets.toml`.
- Load/save/delete named presets
- Cached preset name list for UI performance
- TOML format via Tomlyn library

### UI (Mod.cs / GarrysTorchSubmod)

`Mod.OnBeforeUi` and `Mod.OnAfterUi` do not advance weld physics. `GarrysTorchPatches` owns that work in the simulation handoff described above; the UI edits weld configuration.

ImGui window with:
- **Create Weld section** - Collapsible header with filterable source/target vehicle combos
- **Preset system** - Filterable preset combo with delete button and confirmation modal
- **Position Controls** - Full-width 3-axis drag float inputs for body-frame offset
- **Rotation Controls** - Full-width 3-axis drag float inputs for pitch/yaw/roll
- **XYZ Scale + Lock Rotation** - Three-axis scale editor and rotation lock checkbox
- **Active Welds list** - Bordered child windows per weld with live-edit controls
- **Save as preset** - Modal popup to save active weld settings as a named preset
- **Weld Management** - Create/unweld with validation and error messages

## Key Implementation Details

### Rotation Handling
Rotations use the **ZYX intrinsic Euler convention**:
```csharp
// Pitch (rotation around vehicle's forward/X axis)
// Yaw (rotation around vehicle's up/Z axis)
// Roll (rotation around vehicle's right/Y axis)
```

Conversion to quaternion:
1. Convert each angle (degrees) to radians
2. Create three quaternions for each axis rotation
3. Multiply in order: `Qz * Qy * Qx` (intrinsic ZYX)

### Position Calculation
```
anchorPosCci   = targetVehicleCoM + (targetPart.PositionVehicleAsmb - vehicleCoMInAsmb).Transform(body2Cci)
anchorOrientation = targetPart.Asmb2VehicleAsmb * vehicleBody2Cci
worldPosition  = anchorPosCci + relativePosition.Transform(anchorOrientation)
```

When no `TargetPart` is set (legacy path), `anchorPosCci = vehicleCoMPosCci` and `anchorOrientation = vehicleBody2Cci`.

The part anchor means a +10 offset on Z moves the source vehicle along the target **part's** local Z axis, tracking changes in that part's orientation (e.g., from robotics).

### Parent Body Validation
Welds automatically break if:
- Target vehicle changes parent body
- Source vehicle's parent body doesn't match target's

This prevents welds from stretching across planetary bodies.

### Scaling
Each scale component is written to `Part.Scale` in the part's local X/Y/Z axes. KittenEva bypasses the ordinary part render transform, so Garry's Torch patches its private model-to-body matrix and applies the missing Y/X and Z/X corrections after retaining X in the game's scalar `CharacterCore.Scale` field.

The game exposes only a scalar `ScaleFactors` value to rescalable modules (derived from the largest axis). Garry's Torch therefore provides a true anisotropic part/model transform, but it does not invent anisotropic mass or module physics that KSA itself does not expose.

## Configuration Options

All weld parameters are configured through the ImGui window:

| Parameter | Range | Notes |
|-----------|-------|-------|
| Position X | -50 to +50 m | Body frame offset |
| Position Y | -50 to +50 m | Body frame offset |
| Position Z | -50 to +50 m | Body frame offset |
| Pitch | -180 to +180° | Rotation around forward axis |
| Yaw | -180 to +180° | Rotation around up axis |
| Roll | -180 to +180° | Rotation around right axis |
| Scale X/Y/Z | 0.05 to 20.0x each | Independent local-axis scaling |
| Lock Rotation | true/false | Freeze relative orientation |

## Usage Example

```csharp
// Create a new weld
var weld = new WeldEntry
{
    Source = sourceVehicle,
    Target = targetVehicle,
    Position = new float3(0, 0, 5),  // 5m above target
    Rotation = new float3(0, 0, 0),  // No rotation offset
    Scale = new float3(1.0f, 0.75f, 1.25f),
    LockRotation = false
};

// The installed GarrysTorchPatches drives registered welds automatically.
// Low-level use is only safe in the same pre-solver handoff:
WeldEngine.UpdateWeld(weld, simStep.PreviousTime);
```

## Math Reference

### Quaternion Multiplication
```
q_result = q1 * q2  (Hamilton product)
```

Composing rotations:
```
q_world = q_target * q_relative
```

### Euler to Quaternion (ZYX Intrinsic)
```
q_z = cos(yaw/2) + sin(yaw/2)*k
q_y = cos(pitch/2) + sin(pitch/2)*j  
q_x = cos(roll/2) + sin(roll/2)*i
q_result = q_z * q_y * q_x
```


### Animation System

The animation system (`WeldAnimation`, `WeldAnimationManager`) enables smooth interpolation of all weld parameters, including each scale axis independently:

- **Easing types**: Linear, EaseIn, EaseOut, EaseInOut
- **Configurable power**: `easingPowerStart` and `easingPowerEnd` control the sharpness of the ease function
- **Queue**: Multiple animations can be queued per weld; each starts when the previous completes
- **Frame update**: Animations run in `GarrysTorchSubmod.UpdateBeforeVehicleSolvers(dt, stateTime)` from the PrepareFrame hook before the weld engine teleport, ensuring smooth motion without racing KSA vehicle solver jobs
- **Snap to target**: Animation completes by snapping to exact target values to prevent floating-point drift


## Validation

Run `dotnet run --project garrys-torch.tests/garrys-torch.tests.csproj` for the managed timing
regression, including actual Harmony installation on a warmed-up fixture caller, result retention,
start-of-step timestamps, pause/warp, unload, and rejected game-loop layouts. These checks do not
run KSA's native physics or renderer. See [the library README](../garrys-torch.lib/README.md) for
the in-game checks still required.

## Notes for Future Development

- **Performance**: Welds update every frame—high weld counts may impact performance
- **Physics**: The weld system teleports vehicles; no actual physics constraints are applied
- **Unwelds**: Welds break automatically on parent body mismatch; implement manual unweld via UI button
- **Animation**: Consider smooth transitions when applying presets vs. sharp position changes
- **Save/Load**: Persistent welds would require save/load system integration

The caller transpiler now lives in `ksa-abstractions.lib/PhysicsFrameHook`; Garry's Torch registers
its weld callback. Queued Godzilla edits run before this callback. Source scale ownership is exclusive:
restore Godzilla before welding, or unweld before applying Godzilla. Managed checks also cover queued
mutation ordering, reentrant deferral, exception isolation and stale-system queue disposal.
