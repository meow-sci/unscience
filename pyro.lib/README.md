# Pyro library

`PyroSubmod` implements ISubmod and owns part-anchored visual exhaust entries, presets and UI.
`PyroPatches` submits them through KSA's volumetric exhaust renderer; `PlumeEmitter` preserves the
stock startup/shutdown transients and ambient response. See [the mod README](../pyro/README.md)
for all controls and rendering limitations.

`PlumeEntry.Cycle` holds independent runtime `PlumeCycle` state. `Restart(simulationTime)`,
`Update(simulationTime)` and `Stop()` gate `EffectiveEnabled`; `PyroSubmod.SetEnabled` cancels
cycling and sets the manual master flag, as do bulk toggles. Cycle fields are absent from presets.

KSA 5438 removed the `EngineAVernier` and `EngineATurbine` templates. `PlumeTemplates.NormalizeId`
maps those exact legacy IDs to `EngineAAuxiliary` only when an installed content mod does not still
provide the legacy ID. Unknown IDs remain failures. Instance creation, UI selection, presets and scene
restore all pass through this resolver, so old data is rewritten with the current ID.

## Runtime on/off cycles

Each active plume now has **Repeat On / Off**, **On (s)** and **Off (s)** DragFloat controls
(0.05–3600 seconds), a phase/countdown display and **Restart cycle**. Enabling a cycle turns the
plume on immediately; editing either duration restarts at On. Durations use **simulation seconds**,
so game pause freezes the phase and warp advances it. Disabling the cycle returns to the plume's
Enabled setting. Manual Enabled/On/Off and All On/All Off cancel cycles, so All Off stays off.

Cycles are runtime only and are deliberately excluded from presets. The new game
`VolumetricExhaustInstance.UpdateState` receives the effective active flag plus physical gas and transform
state, preserving stock startup/shutdown tails; `LastPlumeData` is updated only while active, so an Off
interval is not a hard cut of a still-fading transient. Absolute-time
sampling avoids advancing twice for repeated renderer submissions and skips straight to the current
phase after a long frame/warp. A backward time jump restarts at On. Invalid typed durations are
sanitized before use.

Managed checks: `dotnet run --project pyro.tests` covers boundaries, repeated samples/pause,
large warp, backwards time, stop and invalid inputs. Full solution compilation validates current
KSA integration; native plume transitions retain the standing live-game validation requirement.

## Scene saves

The Unscience save participant captures every live plume's exact vehicle/part anchor, template,
nozzle/look settings, offsets, enabled state and On/Off durations/phase. Native load recreates
plume instances without changing native parts; missing anchors/templates produce save-status
warnings. Startup/shutdown shader transients restart when the render instance is recreated, while
the logical cycle resumes at its saved phase. Presets remain separate from scene snapshots.

A separate `pyro.templates` participant captures the shared Template Editor's absorption, emission
colors/brightness, Mach diamonds, density/shape/radial noise, length weights and quality values.
The first edit captures original values; vanilla/new-world loads and unload restore those originals,
while modded loads apply saved templates before recreating standalone plumes. Real engine nozzle
instances are refreshed through the existing TemplateRefresher path. This covers shared template
edits even when no standalone plume exists.

The 5438 renderer rebuilds absorption and refraction from the shared template during `AddInstanceCore`.
Pyro therefore scopes its look override around the final public `AddInstance(ExhaustInstance, int)` call,
with a `try/finally` restoration, leaving stock engine submissions and shared template values untouched.
The native renderer currently clears `_hasRefractionInstances` each frame without setting it, so KSA's
refraction pass remains a standing live acceptance risk even though Pyro's submitted value is correct.
