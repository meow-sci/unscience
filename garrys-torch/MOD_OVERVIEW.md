# Garry's Torch — Mod Overview

## What It Does

Garry's Torch is a vehicle welding mod for KSA. It lets you attach ("weld") one vehicle to another in-game so the source vehicle follows the target vehicle's position and orientation each frame. Welded vehicles can be repositioned, rotated, and scaled via an ImGui control panel toggled with F11. Multiple simultaneous welds are supported.

## Features

### 1. Vehicle Welding
Select any two vehicles in the current system and weld the source to the target. The source's orbit is overwritten each frame to track the target's position/velocity, effectively making it a rigid child of the target.

### 2. Position Offset
Per-weld XYZ offset (metres) in the target's body frame, adjustable via drag-float sliders.

### 3. Rotation Offset
Per-weld pitch/yaw/roll (degrees) layered on top of the orientation captured at weld time. Can be toggled off ("Lock Rotation" checkbox) to let the source rotate freely while still tracking position.

### 4. Vehicle Scaling
Per-weld X/Y/Z scale factors applied to all parts (and sub-parts) of the source vehicle. KittenEva characters retain X in `CharacterAvatar.Core.Scale`; a narrow Harmony postfix corrects the private model-to-body matrix for independent Y and Z scaling.

### 5. ImGui Control Panel
F11-toggled window listing all active welds with collapsible sections, plus an "Add New Weld" combo-box UI.

### 6. Auto-Unweld on Parent Mismatch
If the source and target end up orbiting different parent bodies the weld is automatically removed to avoid nonsensical state.

## Code Map

### Mod.cs — Mod lifecycle + UI + weld logic

| Symbol | Purpose |
|---|---|
| `Mod` class | StarMap mod entry point; holds weld list and UI state |
| `OnFullyLoaded()` | Initialises Harmony patches |
| `OnAfterUi(dt)` | Per-frame loop: toggle window on F11 and render UI; weld physics uses the PrepareFrame hook |
| `Unload()` | Unpatches Harmony, marks disposed |
| `RenderWindow()` | Draws the ImGui window — active weld editors + new-weld combo UI |
| `InitiateWeld(source, target)` | Captures rotation offset and creates a `WeldEntry` |
| `UpdateWeld(entry, stateTime)` → `bool` | Per-frame: computes new orbit + orientation for source from target + offsets, calls `Teleport`. Returns `false` on parent mismatch to trigger removal |
| `RemoveWeld(entry)` | Resets source scale to `(1,1,1)` and removes the weld |
| `ApplyVehicleScale(vehicle, scale)` | Sets XYZ `Part.Scale` recursively; reflection + render-matrix correction for KittenEva avatar scaling |
| `SetPartScaleRecursive(part, scale)` | Recursive helper for part + sub-part XYZ scale |
| `EulerDegreesToQuat(pitch, yaw, roll)` | Converts Euler degrees (ZYX intrinsic) to `doubleQuat` |
| `WeldEntry` class | Data object: Source, Target, RotationOffset, Position, Rotation, Scale, LockRotation |

### Patcher.cs — Harmony setup

| Symbol | Purpose |
|---|---|
| `Patcher.Patch()` | Applies `HotkeyGuard`, `GarrysTorchPatches`, and `KittenScalePatches` |
| `Patcher.Unload()` | Removes the owned patches and nulls the Harmony instance |

`KittenScalePatches` postfixes private `KittenRenderable.ModelToBodyMatrix()` so a KittenEva can
render unequal scale axes even though the game's `CharacterCore.Scale` field is scalar.

`GarrysTorchPatches` wraps the `GetJobSimStep` call in `Program.PrepareFrame`, after results are
applied and before any next-step physics snapshots. It advances welds at `SimStep.PreviousTime`,
so removing the source from its bubble no longer discards actuator progress. See the README.

### Key KSA APIs Used

- `Vehicle.GetPositionCci()` / `GetVelocityCci()` / `GetBody2Cci()` — read vehicle state in CCI frame
- `Vehicle.Teleport(orbit, body2Cce, bodyRates)` — reposition a vehicle
- `Orbit.CreateFromStateCci(...)` — build an orbit from position + velocity
- `Vehicle.Parts.Parts` / `Part.Scale` / `Part.SubParts` — part tree traversal
- `VehicleProvider.GetAllVehicles()` — enumerate vehicles through the shared abstraction
- `Universe.GetJobSimStep(...).PreviousTime` — committed state time for orbit creation at the pre-solver handoff
