# I-Feel-Seen - Vehicle Render Distance Override

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Allows vehicles to bypass the camera rendering distance limit (LOD culling). Selectively track vehicles and toggle their visibility independent of distance from camera, enabling viewing of far-away vehicles that would normally disappear.

## Overview

I-Feel-Seen lets you:
- **Track specific vehicles** - Select vehicles to manage render visibility
- **Override LOD culling** - Keep vehicles visible regardless of distance
- **Per-vehicle toggle** - Enable/disable visibility override per vehicle
- **Selective viewing** - Only affects tracked vehicles; others use normal LOD
- **Silent rendition** - Vehicles render even when far away without visual artifacts

## Features

- **Vehicle-selective rendering** - Override visibility per vehicle on demand
- **Harmony interception** - Patches GetWorldMatrix and UpdateRenderData
- **Transparent integration** - Works seamlessly with game rendering
- **Per-vehicle state** - Track enable/disable flag for each vehicle
- **Multi-vehicle management** - Manage many vehicles simultaneously
- **Vehicle tracking list** - ImGui dropdown for selection

## Architecture

### Core Classes

#### VehicleTracker
Container and manager for tracked vehicles.

**Data Structure**:
```csharp
public class VehicleTracker
{
    public List<TrackedVehicle> TrackedVehicles { get; set; }
    public bool Enabled { get; set; }
}

public class TrackedVehicle
{
    public Vehicle Vehicle { get; set; }
    public bool SeeMe { get; set; }  // Enable/disable visibility override
    public string Name => Vehicle.DisplayName;
}
```

**Key Methods**:
- `IsTracked(Vehicle vehicle)` - Check if vehicle is tracked and SeeMe is true
- `AddVehicle(Vehicle vehicle)` - Add to tracking list
- `RemoveVehicle(Vehicle vehicle)` - Remove from tracking list
- `GetTrackedVehicle(Vehicle vehicle)` - Get tracker entry for vehicle

#### Harmony Patches

Two critical patches intercept the rendering system:

##### Patch 1: GetWorldMatrix
```csharp
[HarmonyPatch(typeof(Vehicle), nameof(Vehicle.GetWorldMatrix))]
public static class GetWorldMatrixPatch
{
    public static bool Prefix(Vehicle __instance, ref Transform3D __result)
    {
        if (!VehicleTracker.IsTracked(__instance))
            return true;  // Use original implementation
        
        // Return matrix that forces rendering
        // Even if distance > LOD limit, matrix is valid
        __result = ComputeWorldMatrix(__instance);
        return false;  // Skip original
    }
}
```

**Purpose**: Overrides the world matrix calculation to ensure tracked vehicles always return valid positions for rendering.

##### Patch 2: UpdateRenderData
```csharp
[HarmonyPatch(typeof(Vehicle), nameof(Vehicle.UpdateRenderData))]
public static class UpdateRenderDataPatch
{
    public static bool Prefix(Vehicle __instance)
    {
        if (!VehicleTracker.IsTracked(__instance))
            return true;  // Use original implementation
        
        // Force render data update
        __instance.ForceUpdateRenderData();
        return false;  // Skip original culling checks
    }
}
```

**Purpose**: Forces render data updates for tracked vehicles, bypassing distance-based culling decisions.

### How LOD Culling Works

KSA uses camera distance to determine rendering:

```
Distance < ThresholdA:  Render with high detail
ThresholdA < Distance < ThresholdB:  Medium detail
Distance > ThresholdB:  Don't render (culled)
```

I-Feel-Seen bypasses this by:
1. Intercepting `GetWorldMatrix` to return valid position
2. Intercepting `UpdateRenderData` to force updates
3. Marking vehicle as tracked to bypass distance checks

### UI (Mod.cs)

ImGui window with:
- **Vehicle tracking list** - Dropdown of all vehicles in scenario
- **Add button** - Add selected vehicle to tracking list
- **Remove button** - Remove vehicle from tracking list
- **SeeMe checkbox** - Toggle visibility override per vehicle
- **Tracked vehicles panel** - List of currently tracked vehicles
- **Enable/disable all** - Global toggle for all tracked vehicles
- **F9 or F11 toggle** - Window visibility hotkey

**Window Layout**:
```
┌─ I Feel Seen ──────────────────┐
│ Available Vehicles:             │
│ [Dropdown ▼] [Add]              │
│                                 │
│ Tracked Vehicles:               │
│ ☑ Vehicle1 [SeeMe] [Remove]     │
│ ☐ Vehicle2 [SeeMe] [Remove]     │
│                                 │
│ [Enable All] [Disable All]      │
└─────────────────────────────────┘
```

## Implementation Details

### Tracking Pattern

```csharp
public static bool IsTracked(Vehicle vehicle)
{
    var tracked = tracker.GetTrackedVehicle(vehicle);
    return tracked != null && tracked.SeeMe;
}
```

Every frame during rendering, this check determines if vehicle bypasses LOD.

### World Matrix Override

```csharp
// Original would check distance and return identity if culled
// We force return of actual world matrix
public static Transform3D ComputeWorldMatrix(Vehicle vehicle)
{
    return new Transform3D
    {
        Position = vehicle.GetWorldPosition(),
        Rotation = vehicle.GetWorldRotation()
    };
}
```

### Render Data Force Update

```csharp
// Original checks distance before updating
// We force update regardless of distance
public static void ForceUpdateRenderData(Vehicle vehicle)
{
    vehicle.RenderData.Position = vehicle.GetWorldPosition();
    vehicle.RenderData.Rotation = vehicle.GetWorldRotation();
    vehicle.RenderData.Valid = true;
}
```

## Usage Example

```csharp
// Track a vehicle
var vehicle = VehicleProvider.GetControlledVehicle();
tracker.AddVehicle(vehicle);

// Toggle visibility
tracked.SeeMe = true;  // Now visible even if far

// Later, stop tracking
tracker.RemoveVehicle(vehicle);
```

## Configuration

All settings via ImGui:

| Setting | Type | Notes |
|---------|------|-------|
| Tracked vehicles | List | Vehicles to override |
| SeeMe flag | bool | Per-vehicle visibility toggle |
| Global enable | bool | Override all tracking |

## Performance Considerations

- **Per-track overhead**: Minimal—one distance bypass per vehicle
- **Rendering**: Full rendering still occurs (no save there)
- **Memory**: Tracker list is small (dozens of vehicles max)
- **Rendering pipeline**: All normal GPU operations still apply

**Impact**: Very low—mostly just allows more objects to render.

## Use Cases

1. **Photography** - Render far-away vehicles for scenic shots
2. **Debugging** - View specific vehicles across large scenarios
3. **Docking assistance** - Keep target vehicle visible despite distance
4. **Documentation** - Capture all vehicles in one scene
5. **Cinematics** - Compose multi-vehicle shots

## Implementation Notes

### Harmony Patch Order
Patches are applied in declaration order. GetWorldMatrix is patched before UpdateRenderData—order may matter if they interact.

### Distance Independence
Unlike typical visibility systems, I-Feel-Seen doesn't check distance. It simply forces the vehicle to report a valid world matrix.

### Physics Unaffected
Patching doesn't affect physics—vehicles still collide and interact normally. Only rendering is overridden.

## Potential Issues

### Performance with Many Tracked Vehicles
If tracking 50+ vehicles, rendering performance may degrade due to high render load. This is a rendering bottleneck, not a patching issue.

### Partial Visibility
If vehicle is partially off-screen and culled, forcing render may cause clipping or Z-fighting depending on LOD implementation.

### Distant Detail
Far vehicles may have low LOD models—details won't increase even when visible.

## Notes for Future Development

- **Persistent tracking**: Save/load tracked vehicle lists
- **Conditional visibility**: Track based on criteria (part count, name pattern, etc.)
- **Fade effects**: Gradually fade visibility with distance instead of hard cutoff
- **Performance groups**: Track related vehicles together
- **Automation**: Auto-track specific vehicle types

## Dependencies

- **MeowSci.KsaAbstractions**: For vehicle access
- **HarmonyLib**: For GetWorldMatrix and UpdateRenderData patching
- **KSA Game**: Vehicle rendering system
