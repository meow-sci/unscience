# Kiwi's Marbles

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

A KSA mod for repositioning celestial bodies (planets, moons) by "welding" them to follow other celestial bodies or vehicles at user-defined offsets.

## Overview

Kiwi's Marbles lets you attach a planet or moon to any orbiter (another celestial body or vehicle). Once welded, the source body's orbit is rewritten on every sim step to maintain its position relative to the target — effectively overriding physics for that body. Multiple welds are supported and processed in dependency order via topological sort.

Toggle the window with **F9**.

## Features

- **Celestial welding**: Weld any planet or moon to any other orbiter (celestial or vehicle)
- **Offset in CCI frame**: Specify an XYZ offset in the CCI (inertial) frame of the target's parent body
- **Unit scale selector**: Enter offsets in m / km / Mm / Gm for convenience; computed double-precision offset is displayed
- **Live offset editing**: Adjust the weld offset in real-time from the active welds panel, with a per-weld unit selector
- **Cross-parent welding**: Source body's parent automatically changes via `SetOrbit()` when target has a different parent
- **Multiple welds**: Create as many welds as needed; processed in topological order so weld chains work correctly
- **Unweld**: Remove any active weld instantly

## Usage

1. Press **F9** to open the Kiwi's Marbles window.
2. Choose a **Source** (the planet/moon to move) from the first dropdown.
3. Choose a **Target** (anything it should follow — another planet, moon, or vehicle).
4. Enter an **offset** (X / Y / Z) and pick a scale unit (m / km / Mm / Gm).
5. Click **Create Weld**. The source body will immediately begin following the target.
6. Use the **Active Welds** panel to adjust the offset in real-time or click **Unweld** to detach.

### Offset Conventions

- Offsets are in the **CCI (Celestial-Centered Inertial)** frame of the target's parent body.
- X ≈ along the major axis (roughly sunward/anti-sunward), Y and Z are transverse.
- Planetary distances are typically millions to billions of meters — use Mm or Gm units.
- Example: offset `(384.4, 0, 0) km` ≈ Moon–Earth distance.

## Architecture

| Component | Purpose |
|-----------|---------|
| `kiwis-marbles/Mod.cs` | StarMap entry: window toggle (F9), ImGui host, applies `Patcher` |
| `kiwis-marbles/Patcher.cs` | Harmony instance: `HotkeyGuard` + `KiwisMarblesPatches` |
| `kiwis-marbles.lib/KiwisMarblesSubmod.cs` | `ISubmod` UI + weld list; `UpdateBeforeVehicleSolvers()` runs welds and deferred unweld restores |
| `kiwis-marbles.lib/KiwisMarblesPatches.cs` | Harmony prefix on `Universe.ExecuteNextVehicleSolvers` → `KiwisMarblesSubmod.Instance.UpdateBeforeVehicleSolvers()` |
| `kiwis-marbles.lib/CelestialWeldEntry.cs` | Data class: Source (Celestial), Target (IOrbiter), Offset (double3), OriginalOrbit |
| `kiwis-marbles.lib/CelestialWeldEngine.cs` | Per-step repositioning via `SetOrbit` + explicit re-parenting + `UpdatePerFrameDataTree`; topological sort |
| `ksa-abstractions.lib/CelestialProvider.cs` | `GetAllCelestials()` and `GetAllOrbiters()` from `Universe.CurrentSystem` |

## Timing: why the weld runs from a solver prefix

Since the 2026.8 builds KSA propagates every `Celestial` on worker threads: `Universe.ExecuteNextOrbitSolvers`
queues a `CelestialUpdateTask` per body (it snapshots `Celestial.Orbit` and computes the state vectors at the
next sim time), and the next frame's `Program.PrepareFrame` does `JobSystems.OrbitSolvers.Wait()` →
`Universe.ApplyOrbitSolvers()` (`Orbit.UpdatePosition(newState)`) → `Universe.ApplyVehicleSolvers()` (which ends
with `CelestialSystem.UpdatePerFrameData()`) → `ExecuteNextVehicleSolvers` → `ExecuteNextOrbitSolvers`.

Mutating a celestial from the StarMap render hooks (`[StarMapBeforeGui]`, the old approach) therefore both
races a worker that may be reading `Orbit` and gets its result overwritten by the staged propagation. The one
safe main-thread window is between the Apply calls and the next Execute calls, which is exactly where a
`Priority.First` prefix on `Universe.ExecuteNextVehicleSolvers` lands (the same hook eternal-flame and
kitchen-sink use). In that window all target positions (celestial or vehicle) are current, no job is in flight,
and the welded orbit is what the next `CelestialUpdateTask` propagates. `ISubmod.Update(dt)` is intentionally
a no-op; unweld restores are queued and applied in the same window.

## Key Game APIs

- `Universe.ExecuteNextVehicleSolvers(double, SimStep)` — Harmony prefix target (per-sim-step, main thread)
- `Celestial.SetOrbit(Orbit)` — bare `Orbit = newOrbit`; `Celestial.Parent` follows `Orbit.Parent`, but **nothing re-parents** `IParentBody.Children`, so the engine moves the body between the old/new parent's `Children` lists itself
- `IParentBody.UpdatePerFrameDataTree()` — refreshes cached CCI/CCE/ECL data for the body and its whole subtree after the swap
- `Orbit.CreateFromStateCci(parent, UniverseTime, posCci, velCci, color)` — creates new orbit from state vectors
- `IOrbiter.GetPositionCci()` / `GetVelocityCci()` — target state read (fresh, since solver results were just applied)
- `CelestialSystem.All` — all `Astronomical` objects (filter with `OfType<Celestial>()`)

## Notes

- Stars (`StellarBody`) cannot be sources — they have no orbit and always sit at origin.
- Source body's children (moons of the moved planet) automatically follow since their orbits are defined relative to their parent.
- Weld chains (Moon → Earth → Mars) work correctly: the engine sorts welds topologically so Earth is moved before Moon's weld is applied.
- Welds are not persisted across mod reloads.
