# pyro — standalone engine plumes

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Place the game's volumetric engine plume effect anywhere on a vehicle — **no engine part required**.
Each plume is "welded" to a vehicle → part → sub-part anchor with a position and rotation offset, and is
rendered through KSA's own `VolumetricExhaustRenderer`, so it looks, ignites, shuts down and reacts to
atmosphere exactly like a real engine's exhaust.

Two projects, following the repo's submod pattern:

- `pyro/` — StarMap host (`Mod.cs`, `Patcher.cs`, `mod.toml`). F11 toggles the window.
- `pyro.lib/` — all logic as `PyroSubmod : ISubmod` + `PyroPatches.Apply/Remove(Harmony)`, consumed by
  both the standalone host and the `unscience` supermod.

## Using it

**Create Plume**
1. Pick a **Vehicle**, a top-level **Part**, and optionally a **Sub-part** (or "(part itself)").
2. Pick an exhaust **Template** (the game's registered `VolumetricExhaustTemplate`s: `EngineALarge`,
   `EngineAMed`, `EngineACompact`, `EngineAAuxiliary`, `RCS`, `MmuRcsVac`, plus any content mod adds).
   Saves from builds that used `EngineAVernier` or `EngineATurbine` migrate to `EngineAAuxiliary` when
   those old IDs are unavailable.
3. Optionally pick a **Preset** (filterable combo). Selecting one loads its template and offsets into
   the form and carries its throttle, nozzle physics and look settings into the plume you're about to
   create; the **del** button (with confirmation) deletes the selected preset.
4. Set the **position offset** (metres, in the anchor part's local frame) and **rotation offset**
   (degrees about the part-local X/Y/Z axes). The plume fires along the part's **-X** axis by default —
   the same convention every stock engine nozzle uses — so rotate to aim it.
5. **Create Plume**. The plume plays its startup transient immediately.

**Active Plumes** — one bordered section per plume, each fully independent:
- **Enabled** checkbox + **On / Off** button (quick toggle; Off plays the template's shutdown transient
  then stops rendering). **All On / All Off** at the top of the list.
- **Template** (switching restarts the startup transient) and **Throttle** (scales synthetic chamber
  pressure, retaining the partial-throttle effect after KSA removed throttle modifier curves).
- **Position / Rotation** offsets, live.
- **Nozzle physics** — the per-plume knobs that drive plume *size and shape*: exit radius, throat
  radius (together = area ratio → exit Mach and expansion), chamber pressure (bar), chamber temperature
  (K), gamma and gas constant. These are converted to the same `PlumeData` a live engine produces
  (isentropic chamber → throat → exit; see `PlumePhysics.cs`), so under-/over-expansion, shock cells and
  Mach diamonds respond to altitude just like stock plumes.
- **Look** — per-plume **absorption density ×** and **refraction** (heat haze). They are applied at the
  final `ExhaustInstance` submission seam, so they never touch the shared template or stock engine instances.
  KSA 5438 currently never enables its refraction pass (`_hasRefractionInstances` remains false), so
  the refraction control has no visible effect until that native defect is fixed.
- **Save settings as preset...** — modal popup that saves the plume's current settings (template,
  offsets, throttle, nozzle physics, look) under a name, with required-name and duplicate-name
  validation. **Remove** deletes the plume.

**Presets** — the same pattern as garrys-torch's weld presets. A preset captures every per-plume
appearance setting (runtime cycle and enabled state are excluded), without the vehicle/part anchor: template id, position/rotation offsets, throttle, all six
nozzle-physics values and both look overrides. Presets persist across game sessions as TOML at
`My Games/Kitten Space Agency/.unscience/pyro-presets.toml` (active plumes themselves are **not**
persisted).

**Template Editor** — the same controls as the game's hidden *View → Show Exhaust Debug* window
(absorption, emission brightness + 4-colour gradient, Mach diamonds, density/shape/radial noise, core
length weights, quality). These edit the game's **shared** templates: they affect every pyro plume *and*
every real engine using that template, and last until the game restarts. Colour/brightness/noise cannot
be made per-plume on the current game build — since KSA 5348 those fields live in a per-template GPU
buffer (`ExhaustTemplateData`) indexed by `templateIndex`, not in the per-instance struct.

Plumes whose vehicle disappears (or whose anchor part leaves the vehicle's tree) are removed
automatically.

## How it works

- **Render hook** — Harmony **postfix** on `Vehicle.AddVolumetricExhaustInstances(Camera,
  VolumetricExhaustRenderer, double)`, the per-frame, per-visible-vehicle call where the game submits its
  own engine plumes. pyro submits the plumes welded to that vehicle to the same renderer with the same
  camera and frame delta, so they land in the same batch, same pass, same transient LUT slices.
- **Per plume** the lib owns a real `VolumetricExhaustInstance` (built from a
  `VolumetricExhaustReference { Id }.Load()`), supplies gas, exhaust, transforms and air state to the new
  `UpdateState(...)` API, stores `LastPlumeData` only while active, and submits with
  `renderer.AddInstance(instance, bendTarget, fade, diamondFade)`. `IsLive` gates the submission so KSA's
  startup/shutdown pulse tracker controls the tail.
- **Positioning** — offset in part frame → `Part.MatrixAsmb2VehicleAsmb` → `Vehicle.PosAsmbToBody` →
  `Body2Cce` → camera-ego (`Camera.GetPositionEgo(vehicle)`). Axis goes through
  `Part.Asmb2VehicleAsmb` and `Body2Cce`. Sub-parts and scaled parts are handled because the
  part matrix chain already includes them.
- **Plume data** — `PlumePhysics.TryCompute` preserves the isentropic nozzle model, scales synthetic
  chamber pressure by throttle, and calls KSA's public `PlumeData.Compute` and
  `RocketNozzle.ComputeMinGasVisibilityDensity` using pascals internally.
- **Reflection** — one string lookup remains: `VolumetricExhaustTemplate.References` (internal collection,
  to list template IDs; falls back to the current stock IDs). Per-plume look overrides use the typed public
  `AddInstance(ExhaustInstance, int)` Harmony seam; no private `_shaderData` mutation remains.

## Public API (`MeowSci.PyroLib`)

- `PyroSubmod.Instance` — singleton; `Plumes` (read-only list of `PlumeEntry`)
- `CreatePlume(vehicle, part, templateId, position, rotation, nozzle?, throttle?, absorptionDensityScale?,
  refractionIntensity?)`, `SetTemplate(plume, id)`, `FindPlume(id)`, `RemovePlume(plume)`,
  `SetAllEnabled(bool)`
- Presets: `GetPresetNames()`, `GetPreset(name)`, `PresetExists(name)`, `SavePreset(name, preset)`,
  `DeletePreset(name)`, `ApplyPreset(plume, preset)`; `PlumePreset.FromPlume(plume)` snapshots a live
  plume, `PlumePreset.Clone()` deep-copies
- `PlumeTemplates.GetTemplateIds()` / `CreateInstance(id)` / `NormalizeId(id)`; `PlumePhysics.TryCompute(...)`

## Game integration scope

See [`scope/exhaust-plumes.md`](../scope/exhaust-plumes.md).

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
