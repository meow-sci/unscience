# Pyro library

`PyroSubmod` implements ISubmod and owns part-anchored visual exhaust entries, presets and UI.
`PyroPatches` submits them through KSA's volumetric exhaust renderer; `PlumeEmitter` preserves the
stock startup/shutdown transients and ambient response. See [the mod README](../pyro/README.md)
for all controls and rendering limitations.

`PlumeEntry.Cycle` holds independent runtime `PlumeCycle` state. `Restart(simulationTime)`,
`Update(simulationTime)` and `Stop()` gate `EffectiveEnabled`; `PyroSubmod.SetEnabled` cancels
cycling and sets the manual master flag, as do bulk toggles. Cycle fields are absent from presets.

## Runtime on/off cycles

Each active plume now has **Repeat On / Off**, **On (s)** and **Off (s)** DragFloat controls
(0.05–3600 seconds), a phase/countdown display and **Restart cycle**. Enabling a cycle turns the
plume on immediately; editing either duration restarts at On. Durations use **simulation seconds**,
so game pause freezes the phase and warp advances it. Disabling the cycle returns to the plume's
Enabled setting. Manual Enabled/On/Off and All On/All Off cancel cycles, so All Off stays off.

Cycles are runtime only and are deliberately excluded from presets. The existing game
`VolumetricExhaustInstance.UpdateState` still receives the effective active flag, preserving stock
startup/shutdown tails; an Off interval is not a hard cut of a still-fading transient. Absolute-time
sampling avoids advancing twice for repeated renderer submissions and skips straight to the current
phase after a long frame/warp. A backward time jump restarts at On. Invalid typed durations are
sanitized before use.

Managed checks: `dotnet run --project pyro.tests` covers boundaries, repeated samples/pause,
large warp, backwards time, stop and invalid inputs. Full solution compilation validates current
KSA integration; native plume transitions retain the standing live-game validation requirement.
