# Camera Controller Override - Advanced Camera Animation System

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

A powerful camera animation system with 8+ animation types, easing functions, and keyframe sequencing. Enables cinematic camera movements, zoom animations, orbits, spirals, and shakes—all with fine-grained control over timing and easing curves.

## Overview

Camera Controller Override lets you:
- **Animate camera zoom** - Smooth zoom in/out with configurable easing
- **Orbital movements** - Orbit and loopy-orbit animations with circular paths
- **Spiral effects** - Combined rotation and zoom for dramatic camera movements
- **Shake effects** - Vibration animations for impact/collision scenes
- **Pan movement** - Translate the camera by an offset while tracking the target
- **Rotation control** - Rotate camera look-direction (yaw/pitch) from a fixed point
- **Keyframe sequences** - Chain multiple animations together
- **Animation groups** - Run multiple animations simultaneously with composited effects
- **Custom easing** - Adjust acceleration/deceleration with easing power parameter
- **Return-to-start** - Automatically animate back to initial camera position

## Features

- **10 animation types** - Zoom in/out, spiral zoom, orbit, loopy orbit, shake, pan, rotate, and more
- **Animation groups** - Combine animations to play simultaneously (e.g., zoom + pan)
- **Easing function support** - Linear, EaseIn, EaseOut and configurable power parameter
- **Duration control** - Specify animation length in seconds
- **Keyframe sequencing** - Play multiple animations in sequence
- **Return-to-start** - Auto-animate back to camera start position after sequence
- **Real-time preview** - See animations live as you configure parameters
- **Interceptor pattern** - Cleanly overrides camera input without patching visual systems
- **Reusable API** - `CameraControllerOverrideSubmod.Instance` exposes the active controller to other mods

## Architecture

### Core Components

#### KeyframeSequencePlayer
Main playback engine that manages animation sequence execution.

**Key Methods**:
- `Update(Controller, Transform3D, double delta)` - Advances playback state, returns true to override normal camera control
- `Play()` / `Pause()` / `Stop()` - Playback controls
- `AddKeyframe(IKeyframeAnimation)` - Add animation to sequence
- `RemoveKeyframe(int id)` - Remove animation by ID
- `ClearKeyframes()` - Clear all animations

**State Machine**:
1. **Idle** - No animations, normal camera control
2. **Playing** - Executing current animation keyframe
3. **Transitioning** - Moving to next animation in sequence
4. **Returning** - Auto-animating back to start position

#### IKeyframeAnimation Interface
Base interface for all animation implementations.

```csharp
#### CameraControllerOverrideSubmod
ISubmod implementation that owns all animation configuration state and UI.

**Architecture**:
- Implements `ISubmod` (from `ksa-abstractions.lib`): `Name="Camera Controller Override"`, `Initialize()`, `Update(dt)`, `RenderContent()`, `Dispose()`
- Owns all 30+ config fields (speed, duration, easing, easing power, degrees, offsets per animation type)
- Owns `KeyframeSequencePlayer _sequencePlayer` instance; exposes it via `SequencePlayer` property for patch wiring
- `RenderContent()` renders all 8 animation configuration CollapsingHeaders and the Keyframe Sequence panel — no window framing
- Used standalone via `camera-controller-override/Mod.cs` (thin shell) and embedded in unscience's collapsible header

#### CameraControllerOverridePatches
Shared Harmony patch class (Apply/Remove pattern) in `camera-controller-override.lib`.

**Architecture**:
- `SequencePlayer` static property — set by caller before `Apply()`
- `Apply(Harmony)` — manually patches `OrbitController.OnFrame` and `FlyController.OnFrame` with a prefix
- `Remove(Harmony)` — unpatches all
- Prefix: if sequence is playing, calls `SequencePlayer.Update()` and returns false to skip normal camera update
- Used by both standalone `camera-controller-override/Patcher.cs` and `unscience/Patcher.cs`

#### IKeyframeAnimation Interface
Base interface for all animation implementations.

```csharp
public interface IKeyframeAnimation
{
    float Duration { get; }
    float Easing { get; }
    float EasingPower { get; }
    EaseType EaseType { get; }
    ILookAtProvider LookAtProvider { get; }
    
    void Initialize(Transform3D start);
    Transform3D Evaluate(float progress);  // 0.0 to 1.0
}
```

**Key Concept**: Animations are evaluated from 0.0 (start) to 1.0 (end). Easing functions map this progress to non-linear curves.

#### Animation Types

| Type | Purpose | Key Parameters |
|------|---------|-----------------|
| ZoomOut | Move camera away from target | speed, duration, easing |
| ZoomIn | Move camera toward target | speed, duration, easing |
| ZoomInToOffset | Zoom to specific position | speed, duration, offset (x/y/z), easing |
| SpiralZoomIn | Rotate + zoom in simultaneously | speed, duration, degrees, easing |
| SpiralZoomOut | Rotate + zoom out simultaneously | speed, duration, degrees, easing |
| Orbit | Circular orbit around target | duration, degrees, easing |
| LoopyOrbit | Oscillating orbital figure-8 | loop interval, amplitude, duration, easing |
| Shake | Vibration animation | duration, count, amplitude, speed, easing |
| Pan | Translate camera by offset | offset (x/y/z), duration, easing |
| Rotate | Rotate camera look-direction | yaw, pitch, duration, easing |
| Group | Run multiple animations simultaneously | child animations (2+) |

### Easing Functions

Easing functions control acceleration/deceleration:

```
Linear:   f(t) = t
EaseIn:   f(t) = t^p  (slow start, fast end)
EaseOut:  f(t) = 1-(1-t)^p  (fast start, slow end)
```

**Easing Power** parameter (1.0 to 10.0) controls curve intensity:
- 1.0: Linear-like
- 3.0: Moderate easing
- 5.0+: Strong easing effect

### Harmony Patches

**Patches Applied**:
- `OrbitController.OnFrame` (Prefix) - Intercepts camera update when animation is playing
- `FlyController.OnFrame` (Prefix) - Same for free-flight camera mode

When animation is active, patches return `true` to skip normal camera input, allowing animation to take over.

## UI (Mod.cs)

Extensive ImGui window organized by animation type:

Each animation type has:
- **Duration slider** - Animation length in seconds (0.5 to 30.0)
- **Speed slider** - Animation speed multiplier (0.1 to 10.0)
- **Easing dropdown** - Linear, EaseIn, EaseOut
- **Easing power slider** - 1.0 to 10.0 for curve intensity
- **Type-specific parameters** - Offset, degrees, amplitude, etc.

Global controls:
- **Play / Pause / Stop** - Sequence control
- **Return to Start** - Auto-animate back
- **Clear** - Reset all keyframes
- **Add Keyframe** - Add current animation to sequence
- **Remove / Reorder** - Manage keyframes list
- **Live Preview** - Checkbox to apply animations in real-time

## Implementation Details

### Easing Formula
```
progress: 0.0 to 1.0 (duration elapsed / total duration)

eased = ApplyEasing(progress, easeType, power)
```

For EaseOut with power=3:
```
eased = 1.0 - pow(1.0 - progress, 3.0)
```

### Zoom Animation
```
targetDistance = startDistance + (speed * duration)
// Easing applied to distance interpolation
```

### Orbit Animation
```
angle = (progress * degrees) converted to radians
// Rotate camera around target by angle
// Distance remains constant
```

### Spiral Animation
```
// Combine rotation and distance change
angle = progress * degrees
distance = startDistance + progress * speedDelta
```

### Shake Animation
```
// Apply sinusoidal perturbation
for each axis:
    shake = sin(time * speed * TAU) * amplitude * sin(π * progress)
    // Shake intensity fades as progress approaches 1.0
```

### Pan Animation
```
// Absolute offset interpolation from start position
position = startPosition + offset * easedProgress
// Camera continues tracking the target throughout the pan
```

### Rotate Animation
```
// Absolute rotation from start orientation using yaw/pitch
yawQuat = CreateFromAxisAngle(startUpAxis, yaw * easedProgress)
pitchQuat = CreateFromAxisAngle(startRightAxis, pitch * easedProgress)
rotation = yawQuat * pitchQuat * startRotation
// Camera position stays fixed; only look-direction changes
// Positive yaw = look right, negative = look left
// Positive pitch = look up, negative = look down
```

### Animation Group (Simultaneous Animations)

AnimationGroup allows multiple animations to play at the same time by compositing
their effects. It implements `IKeyframeAnimation` so it fits into the existing
keyframe sequence as a single keyframe.

**How it works**:
1. Each position-contributing animation (Pan, Zoom, Orbit, etc.) runs in an isolated
   virtual transform. Position deltas from the base state are summed to produce the
   final composed position.
2. After the composed position is set, LookAt rotation is computed from the new
   camera position to the target.
3. Rotation-only animations (Rotate, Shake) then apply their yaw/pitch contributions
   on top of the LookAt rotation, using the current view axes.

**UI workflow**:
1. Click "Start Building Group" in the Animation Group section
2. Configure animation parameters and click "+ Add to Group" (buttons change text)
3. Review the pending list; remove individual animations with the x button if needed
4. Click "Finish Group" (requires 2+ animations) to add the group as a single keyframe
5. The group appears in the Keyframe Sequence list showing its child animations

**Example combinations**:
- Zoom Out + Pan → camera moves away while translating sideways
- Zoom In + Rotate → camera approaches target while panning the view
- Orbit + Shake → orbital movement with vibration overlay
- Pan + Rotate → translate camera while rotating the view direction

**Duration**: The group's duration equals the longest child animation. Shorter
animations freeze at their final state when they complete.

## Usage Example

```csharp
// Create zoom in animation
var zoomIn = new ZoomInAnimation
{
    Duration = 2.0f,
    Speed = 10.0f,
    EaseType = EaseType.EaseOut,
    EasingPower = 3.0f
};

// Add to sequence
KeyframeSequencePlayer.AddKeyframe(zoomIn);

// Start playback
KeyframeSequencePlayer.Play();

// With return-to-start enabled, will automatically animate back to start
```

## Configuration Reference

All parameters configurable via ImGui:

| Parameter | Range | Notes |
|-----------|-------|-------|
| Duration | 0.5 - 30.0 sec | Animation length |
| Speed | 0.1 - 10.0x | Distance traveled per second |
| Easing Power | 1.0 - 10.0 | Curve intensity |
| Degrees | 0 - 360° | For orbit/spiral animations |
| Amplitude | 0.1 - 50.0 | For shake animations |
| Offset | ±100 m | Target position offset |

## Performance Considerations

- **Frequent Updates**: Animations update every frame during playback
- **Keyframe Limit**: Sequences can have many keyframes; performance linear in count
- **Easing Calculation**: Lightweight; uses simple math per frame
- **No Physics**: Animations only affect camera; no vehicle physics involved

## Notes for Future Development

- **Camera Paths**: Could extend to support arbitrary curve-based camera paths
- **Replay System**: Save/load animation sequences
- **Smooth Transitions**: Transition between animations instead of hard stops
- **Look-At Tracking**: Support tracking moving targets during animations
- **Performance**: Consider LOD system for complex scenes while animating

## Dependencies

- **HarmonyLib**: For camera controller patching
- **KSA Game**: Camera/controller classes (OrbitController, FlyController)
