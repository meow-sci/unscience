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
   `EngineAMed`, `EngineACompact`, `EngineAVernier`, `EngineATurbine`, `RCS`, `MmuRcsVac`, plus any a
   content mod adds).
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
- **Template** (switching restarts the startup transient) and **Throttle** (feeds the template's
  throttle-modifier curves, same as a real engine at partial throttle).
- **Position / Rotation** offsets, live.
- **Nozzle physics** — the per-plume knobs that drive plume *size and shape*: exit radius, throat
  radius (together = area ratio → exit Mach and expansion), chamber pressure (bar), chamber temperature
  (K), gamma and gas constant. These are converted to the same `PlumeData` a live engine produces
  (isentropic chamber → throat → exit; see `PlumePhysics.cs`), so under-/over-expansion, shock cells and
  Mach diamonds respond to altitude just like stock plumes.
- **Look** — per-plume **absorption density ×** and **refraction** (heat haze). These are written into
  the plume's own per-instance shader struct, so they never touch the shared template.
- **Save settings as preset...** — modal popup that saves the plume's current settings (template,
  offsets, throttle, nozzle physics, look) under a name, with required-name and duplicate-name
  validation. **Remove** deletes the plume.

**Presets** — the same pattern as garrys-torch's weld presets. A preset captures every per-plume
setting *except* the vehicle/part anchor: template id, position/rotation offsets, throttle, all six
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

- **Render hook** — Harmony **postfix** on `Vehicle.AddVolumetricExhaustInstances(Camera, IViewport,
  VolumetricExhaustRenderer, double)`, the per-frame, per-visible-vehicle call where the game submits its
  own engine plumes. pyro submits the plumes welded to that vehicle to the same renderer with the same
  camera and frame delta, so they land in the same batch, same pass, same transient LUT slices.
- **Per plume** the lib owns a real `VolumetricExhaustInstance` (built from a
  `VolumetricExhaustReference { Id }.Load()`), calls `UpdateState(simTime, isActive, dt, plumeData)` to
  drive the 4-slot startup/shutdown pulse tracker, then `renderer.AddInstance(posEgo, axis, instance,
  throttle)` — a 1:1 copy of `RocketNozzleState.AddExhaustInstance`.
- **Positioning** — offset in part frame → `Part.MatrixAsmb2VehicleAsmb` → `Vehicle.PosAsmbToBody` →
  `Body2Cce` → camera-ego (`Camera.GetPositionEgo(vehicle)`). Axis goes through
  `Part.Asmb2VehicleAsmb` and `Body2Cce`. Sub-parts and scaled parts are handled because the
  part matrix chain already includes them.
- **Plume data** — `PlumePhysics.TryCompute` mirrors `RocketNozzle.UpdatePlumeData` (and
  `RecomputeGasVisibilityDensity` for the visibility threshold) using pascals internally and the camera's
  ambient pressure from `PhysicalAtmosphereReference.GetAtmosphericPressure`.
- **Reflection** — two string lookups: `VolumetricExhaustTemplate.References` (internal collection, to
  list template ids; falls back to the stock id list) and `VolumetricExhaustInstance._shaderData`
  (private struct, for the per-plume look overrides; gracefully disabled if missing).

## Public API (`MeowSci.PyroLib`)

- `PyroSubmod.Instance` — singleton; `Plumes` (read-only list of `PlumeEntry`)
- `CreatePlume(vehicle, part, templateId, position, rotation, nozzle?, throttle?, absorptionDensityScale?,
  refractionIntensity?)`, `SetTemplate(plume, id)`, `FindPlume(id)`, `RemovePlume(plume)`,
  `SetAllEnabled(bool)`
- Presets: `GetPresetNames()`, `GetPreset(name)`, `PresetExists(name)`, `SavePreset(name, preset)`,
  `DeletePreset(name)`, `ApplyPreset(plume, preset)`; `PlumePreset.FromPlume(plume)` snapshots a live
  plume, `PlumePreset.Clone()` deep-copies
- `PlumeTemplates.GetTemplateIds()` / `CreateInstance(id)`; `PlumePhysics.TryCompute(...)`

## Game integration scope

See [`scope/exhaust-plumes.md`](../scope/exhaust-plumes.md).
