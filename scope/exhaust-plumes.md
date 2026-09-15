# Exhaust Plumes (pyro) — Game Integration Scope

Permanent reference for detecting when KSA game updates break **pyro** (standalone volumetric engine
plumes). Every game-facing member the mod touches is enumerated with its decompiled-source path.

**Verified game versions**

- NEW decomp **`2026.9.10.5438`** root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp` (namespace-foldered)
- OLD decomp **`2026.9.7.5402`** root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`
- NEW Content root: `~/repos/meow-sci/ksa-game-assemblies/current/Content`
- OLD Content root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/Content`

Originally written against 5348; line numbers in the table are **NEW (5438)** unless marked otherwise.
The in-repo `decomp/ksa` copy is **older** and materially different for this area (pre-5348
`PlumeData`/`ExhaustInstance` layouts) — always check this file's members against the provided tree,
not the repo copy.

**How the mod is hosted:** all logic in `pyro.lib` (`PyroSubmod : ISubmod`, `PyroPatches.Apply/Remove`),
consumed by the standalone host (`pyro/Mod.cs`, `pyro/Patcher.cs`) and by unscience
(`unscience/Mod.cs` adds `PyroSubmod`; `unscience/Patcher.cs` → `TryApply("pyro", …)`). Findings apply to
both hosts identically.

---

## Integration model

1. **One Harmony postfix** on `Vehicle.AddVolumetricExhaustInstances(Camera,
   VolumetricExhaustRenderer, double)` (`KSA/Vehicle.cs:5518`). The game calls this from
   `Program.OnPreRender` once per visible vehicle, after `VolumetricExhaustRenderer.UpdateFrameData()`
   reset the instance list. pyro's postfix (`PyroPatches.cs:35`) hands the same `camera`/`renderer`/
   `frameDeltaTime` to `PyroSubmod.SubmitPlumes`, which submits every plume welded to `__instance`.
2. **Per plume, pyro owns a real `VolumetricExhaustInstance`** built from
   `new VolumetricExhaustReference { Id }.Load()` (`PlumeTemplates.cs`) and drives it like the new vehicle
   path: `UpdateState` receives gas, exhaust, core velocity, emitter/rest transforms and air state, then
   `renderer.AddInstance(instance, bendTarget, fade, diamondFade)` (`PlumeEmitter.cs`). `LastPlumeData` is
   assigned only while active and `IsLive` gates the direct submission, preserving shutdown tails. The air state
   (`ComputeAirState`)
   mirrors the game's own derivation in `Vehicle.AddVolumetricExhaustInstances` (`:5524-5531`) — 5438
   uses it to fold/bend plumes in atmosphere.
3. **`PlumeData` is synthesised**, not read from an engine: `PlumePhysics.TryCompute` preserves the
   isentropic nozzle model, scales synthetic chamber pressure by throttle, then calls public
   `PlumeData.Compute` and `RocketNozzle.ComputeMinGasVisibilityDensity` from user nozzle settings +
   `PhysicalAtmosphereReference.GetAtmosphericPressure(camera)`.
4. **Positioning** chain: part-local offset → `Part.MatrixAsmb2VehicleAsmb` / `Part.Asmb2VehicleAsmb`
   (`KSA/Part.cs:736,720`) → `Vehicle.PosAsmbToBody` (`:1270`) → `Vehicle.Body2Cce` (`:475`) →
   `Camera.GetPositionEgo(vehicle)` (`KSA/Camera.cs:231`). Base axis is part-local **-X**, matching every
   stock `<ExhaustDirection X="-1">` in `Core/CorePropulsionAGameData.xml`.
5. **Template Editor** writes the shared `VolumetricExhaustTemplate` sub-objects (same fields and same
   `ColorRgbReference(float3)` + `OnDataLoad(new Mod())` idiom as the game's `VolumetricExhaustRenderer.
   OnDrawUi`, `:2126-2148`), then `TemplateRefresher` calls `OnSettingsChanged()` on every affected
   instance and `RecomputeVolumetricExhaustLimits` on every real nozzle — the debug editor's own `changed`
   path (`:2321-2345`) minus the transient-LUT rebake (pyro does not edit transients).

**Persistence** — `PlumePresetManager` stores named **presets**
(`pyro.lib/PlumePresetManager.cs`) reads/writes TOML at
`<MyDocuments>/My Games/Kitten Space Agency/.unscience/pyro-presets.toml`
(dir from `ksa-abstractions.lib/KsaPaths.cs:9`). Mod-authored file, not a game asset —
no game integration point beyond the `KsaPaths` directory convention. The suite's scene-save
adapter separately restores active plume recipes through `PyroSubmod`; both creation paths
normalize removed template IDs. See [saves](saves.md) for sidecar identity and lifecycle policy.

## Touchpoints

| # | Kind | Mod code | Game member | Decomp path (5438) | Status | Notes |
|---|---|---|---|---|---|---|
| 1 | Harmony postfix | `PyroPatches.cs:19,73` | `Vehicle.AddVolumetricExhaustInstances(Camera camera, VolumetricExhaustRenderer renderer, double frameDeltaTime)` | `KSA/Vehicle.cs:5518` (viewport argument removed) | ✅ | Target is explicitly bound by the three parameter types; postfix names (`camera`, `renderer`, `frameDeltaTime`) remain Harmony-bound and must not drift. The body derives air state (`:5524-5531`) and queues stock plumes for grouped rendering. |
| 2 | Direct API | `PlumeEmitter.cs` | `VolumetricExhaustRenderer.AddInstance(VolumetricExhaustInstance, in ExhaustBendTarget, in ExhaustAxialFade, in ExhaustDiamondFade) : ExhaustSubmission` | `KSA/VolumetricExhaustRenderer.cs:814` | ✅ **fixed 5438** | Direct submissions use default bend target, `ExhaustAxialFade.NoFadeOut` and `ExhaustDiamondFade.None`; `IsLive` gates the call. Do not open a grouping frame for standalone plumes, or stock pending hierarchy entries are lost. |
| 3 | Direct API | `PyroSubmod.cs:75` | `VolumetricExhaustRenderer.Disabled` | `:352` (was `:312`) | ✅ | |
| 4 | Direct API | `PlumeTemplates.cs:53-59` | `VolumetricExhaustReference { Id }`, `.Load()`, `.Template`; `new VolumetricExhaustInstance(ref)` | `KSA/VolumetricExhaustReference.cs`; `KSA/VolumetricExhaustInstance.cs:72` | ✅ | file byte-identical 5348↔5402 |
| 5 | Direct API | `PlumeEmitter.cs` | `VolumetricExhaustInstance.UpdateState(double, bool, double, in GasProperties, in GasConditions, float, float3, float3, float3, float3, float, float3, float) : void` | `KSA/VolumetricExhaustInstance.cs:111` | ✅ **fixed 5438** | Stores emitter/rest transforms and air state; `LastPlumeData` remains caller-owned and is updated only while active. `IsLive` replaces the old bool return and gates final submission; 4-slot pulse tracker remains native. |
| 6 | Direct API | `TemplateRefresher.cs:20,42` | `VolumetricExhaustInstance.OnSettingsChanged()` | `KSA/VolumetricExhaustInstance.cs:243` | ✅ | |
| 7 | **Harmony prefix (typed, scoped)** | `PyroPatches.cs` / `PlumeEmitter.cs` | `VolumetricExhaustRenderer.AddInstance(ExhaustInstance, int)` final enqueue; `ExhaustInstance.absorptionDensity`, `.refractionIntensity` | `KSA/VolumetricExhaustRenderer.cs:759`; `KSA/ExhaustInstance.cs:25,69` | ✅ **fixed 5438** | AddInstanceCore rebuilds these values from the template (`:1010-1015`), so Pyro applies its override only during its direct submission and restores scope in `finally`. Stock engines/shared templates remain untouched. No `_shaderData` reflection remains. |
| 8 | Struct layout | `PyroPatches.cs` prefix | `ExhaustInstance` sequential pack-1 layout, 272 → 320 bytes by source field sizes; final submission fields above | `KSA/ExhaustInstance.cs` | ✅ | Twelve floats appended in 5438. Pyro writes named fields only; new fade/shape fields are native initialized by AddInstanceCore. Never copy or hardcode the struct stride. Native `_hasRefractionInstances` remains a 5438 live risk. |
| 9 | Direct API (object init) | `PlumePhysics.cs` | `PlumeData.Compute(in GasProperties, in GasConditions, float, float, float, float, float, float, float, float, float)` | `KSA/PlumeData.cs:65` | ✅ **fixed 5438** | Calls the public constructor helper so `StagnationPressure`, `CoreVelocity` and `MassFlow` stay coherent with KSA. |
| 10 | Direct API | `PlumePhysics.cs:30-89` | `GasProperties{Gamma,SpecificGasConstant}.ComputeSpeedOfSound/…PressureAngle/…PressureMach/ComputePrandtlMeyer`; `GasConditions{Pressure,Temperature}.ComputeDensity` | `KSA/GasProperties.cs`; `KSA/GasConditions.cs` | ✅ | pressures **Pa** |
| 11 | Direct API | `PlumePhysics.cs:33,61` | `RocketDesign.SolveMachNumberFromAreaRatio(GasProperties,double)`, `ComputeAreaRatioFromMachNumber(double,double)` | `KSA/RocketDesign.cs:168,187` | ✅ | |
| 12 | Direct API | `PlumePhysics.cs:113` | `PhysicalAtmosphereReference.GetAtmosphericPressure(Camera) : double` (**atm**) | `KSA/PhysicalAtmosphereReference.cs:50` | ✅ | ×101325 → Pa |
| 13 | Direct API | `PlumePhysics.cs` | `RocketNozzle.ComputeMinGasVisibilityDensity(VolumetricExhaustTemplate, double)` | `KSA/RocketNozzle.cs:197` | ✅ **fixed 5438** | Uses KSA's public visibility formula directly. |
| 14 | Direct API | `PlumeEmitter.cs:69-74` | `Part.MatrixAsmb2VehicleAsmb`, `Part.Asmb2VehicleAsmb`, `Vehicle.PosAsmbToBody(double3)`, `Vehicle.Body2Cce`, `Camera.GetPositionEgo(IPosition)`, `doubleQuat.NormalizedOrZero()` (ext, `KSA/QuaternionEx.cs:280`) | `KSA/Part.cs:736,720`; `KSA/Vehicle.cs:1270,475`; `KSA/Camera.cs:231` | ✅ | line moves only |
| 15 | Direct API | `PyroSubmod.cs:77-78` | `Universe.GetElapsedSeconds()`, `Universe.GetSimulationSpeed()` | `KSA/Universe.cs:2108,2026` (was `:2054,1972`) | ✅ | |
| 16 | Direct API | `PyroSubmod.CreateUi.cs:135,148`; `PyroSubmod.cs:187-192`; `PyroUi.cs:12` | `Vehicle.Parts.Parts`, `Part.SubParts`, `Part.PartParent`, `Part.Template.Id`, `Part.Id` | `KSA/Part.cs:1079,660,576,698` | ✅ | anchor pick + dead-anchor pruning |
| 17 | **Reflection (internal, string)** | `PlumeTemplates.cs` | `VolumetricExhaustTemplate.References : SerializedCollection<T>` → `GetList()` | `KSA/VolumetricExhaustTemplate.cs:38`; `KSA/SerializedCollection.cs:42` | ✅ | Only template cataloging uses reflection; look override's retired `_shaderData` lookup is no longer a dependency. Fallback IDs include current `EngineAAuxiliary`. |
| 18 | Direct API (read+write) | `PyroSubmod.TemplateUi.cs` | `VolumetricExhaustTemplate.Absorption/Emission/Noise/LengthWeights/Quality` sub-objects; `DoubleReference.Value`, `BoolReference.Value`, `Quality.VolumetricVesselShadows`, `ColorGradient.Color0..3`, `Flow.MachDiamonds.{LeadIn,LeadOut,MiddleRadius}` | `KSA/VolumetricExhaustTemplate.cs:12-27` + sub-type files | ✅ | GPU `ExhaustTemplateData` rebuilt from these each `Render()` (`VolumetricExhaustRenderer.cs:859-866`, was `:1236-1243`); all sub-type files byte-identical |
| 19 | Direct API | `PyroSubmod.TemplateUi.cs:121-129` | `ColorRgbReference.Value.AsFloat3`, `new ColorRgbReference(float3)`, `.OnDataLoad(new Mod())` | `KSA/ColorRgbReference.cs:22,28,35`; `KSA/Mod.cs` | ✅ | identical to the game editor (`VolumetricExhaustRenderer.cs:2126-2148`) |
| 20 | Direct API | `TemplateRefresher.cs` | `PartTree.RocketNozzles.ModulesAndAllStates` enumerator → `.FxState.VolumetricExhaust`, `.Module.RecomputeVolumetricExhaustLimits(in …)` | `KSA/PartTree.cs:403`; `KSA/RocketNozzle.cs:190` | ✅ **fixed 5438** | Recomputes real nozzle visibility and maximum extent after shared template edits, in try/catch. |
| 21 | Asset IDs + migration | `PlumeTemplateIds.cs`, `PlumeTemplates.cs`, presets/save/UI | `EngineALarge`, `EngineAMed`, `EngineACompact`, `EngineAAuxiliary`, `RCS`, `MmuRcsVac`; old `EngineAVernier`/`EngineATurbine` aliases | `Core/ExhaustAssets.xml:1331` / `:1670` (removed), `:1331` (Auxiliary replacement) | ✅ **fixed 5438** | Exact legacy aliases resolve to Auxiliary only when unavailable, preserving user-provided legacy IDs. Unknown IDs remain explicit failures. |
| 22 | Build refs | `pyro.lib.csproj` | `Brutal.Vulkan`, `Brutal.Vulkan.Abstractions`, `BepuUtilities` | — | ✅ | needed so `VolumetricExhaustRenderer` / `Symmetric3x3` (`Part` matrix API) resolve |

## Update-risk findings

- **Loud breaks (compile):** `PlumeData` required-member churn (#9), `AddInstance` signature (#2),
  `AddVolumetricExhaustInstances` rename (#1 via `nameof`), any template sub-object field rename (#18).
- **Silent breaks (runtime):** postfix **parameter renames** (#1 — Harmony binds by name, throws at
  `Apply`, pyro is skipped with a console line); the two string lookups (#7, #17) — both degrade
  gracefully and say so in the UI.
- **Semantic drift with no symbol change:** `AddInstanceCore` may start reading new `PlumeData` fields that
  pyro leaves at defaults — symptom is a wrong-shaped plume, not an error. Re-diff the native nozzle path
  against `PlumePhysics.TryCompute` on every bump. Likewise the final `AddInstance(ExhaustInstance,int)`
  seam or template reconstruction can change, silently turning the scoped Look sliders into no-ops.
- **Unit assumption:** pressures are Pa game-side (`PressureReference` stores Pa; `Combustor
  MaxPressure Bar="49"`), ambient from `GetAtmosphericPressure` is atm (`× 9.869e-6`). If either flips,
  plumes become absurdly long/short.
- **Not done / known limits:** no per-plume colour (see #8); Template Editor does not edit
  startup/shutdown transients (would need `TransientAnimationLut.BakeAnimationLutData`, which is private
  renderer state); plumes only update while their vehicle is in `Program.VehiclesInFrame` (same as
  stock engines).

### 5348 → 5402 (historical baseline)

Revisions 5349–5400 are **unlogged** in any KSA changelog (only rev 5401 "Fixed crash for incorrect
data stride for thumbnail rendering" is logged), so the source diff is the only evidence for this pass.

- 🔴 **COMPILE BREAK — fixed.** `VolumetricExhaustRenderer.AddInstance` lost its 4-arg overload; the
  only remaining emitter-side overload is `float AddInstance(float3, float3, VolumetricExhaustInstance,
  float throttle, float3 airVelocity, float airDensity)` (`:710`, was `void …(…, float)` at `:860`). The
  game's own caller changed the same way (`RocketNozzleState.AddExhaustInstance` `:81/:88`). Fixed in
  `pyro.lib/PlumeEmitter.cs:76-78` by passing a new `ComputeAirState` (`:87-98`) that mirrors
  `Vehicle.AddVolumetricExhaustInstances` (`:5518-5525`): surface velocity in CCE via
  `Vehicle.GetSurfaceVelocityCci()` (new API, `:2922`) × `Parent.GetCci2Cce()`, density from
  `Parent.GetAtmosphereReference()?.Physical.GetAtmosphericDensityAtAltitude(altitude)` (0 in vacuum /
  no atmosphere). The float return (`visualExpansionRadius`) is discarded. Solution builds clean
  (52 projects, 0 warnings, 0 errors) against 5402.
- ⚠️ **Game-side regression — refraction pass never runs (needs live confirmation, nothing applied).**
  5348 set `_hasRefractionInstances = true` inside `AddInstance` when `refractionIntensity > 0.0001`
  (`:960-963`); 5402 only resets it (`:654`) and reads it (`:907`, `:1084`, `:1129`) — no assignment to
  `true` anywhere in the decomp. So the screen-copy/blur/refraction-UV passes are dead in 5402 for stock
  engines *and* pyro. pyro's per-plume **Refraction** slider (#7/#8) is therefore a no-op; the write is
  harmless and the field still exists. If a live pass confirms no heat-haze on any plume, annotate the
  slider as inert for 5402 (do **not** remove the write — the field is still consumed at `:803`).
- ℹ️ **`ExhaustAssets.xml` gradient retune.** Ids and line positions unchanged; `Emission/ColorGradient
  Color0..3` re-tuned for `EngineALarge` (`:322-325`), `EngineAMed` (`:665-668`), `EngineACompact`
  (`:1008-1011`), `EngineAVernier` (`:1347-1350`), `EngineATurbine` (`:1686-1689`) — e.g. `Color0`
  `0.5/0.5/0.5` → `0.998/1/0.904`. `RCS`/`MmuRcsVac` untouched. Saved pyro presets that captured the old
  gradient will override the new stock look when applied — expected, not a bug. `PlumeTrailAssets.xml`
  gained `LiquidEnginePlumeTrail` + `Color`/`Lifetime`/`DensityMultiplier`; pyro has no plume-trail
  references.
- ✅ **Optional hardening, not applied.** 5402 extracted the two formulas pyro mirrors into public
  statics with no math change: `RocketNozzle.ComputePlumeData(in GasProperties, in GasConditions exhaust,
  in GasConditions inlet, float stagnationPressure, float actualExhaustVelocity, float ambientPressure,
  float nozzleExitRadius, float throatRadius, float designMach, float densityThreshold)` (`:266`) and
  `RocketNozzle.ComputeMinGasVisibilityDensity(VolumetricExhaustTemplate, double fxExitRadius)` (`:197`).
  `PlumePhysics.TryCompute`/`ComputeMinVisibleDensity` could call these directly and stop drifting.
- ✅ **Verified clean:** `PlumeData`, `GasProperties`, `GasConditions`, `RocketDesign`,
  `PhysicalAtmosphereReference`, `VolumetricExhaustInstance`, `VolumetricExhaustReference`,
  `VolumetricExhaustTemplate` (+ all sub-type files), `ColorRgbReference`, `ExhaustTemplateData`,
  `SerializedCollection` are **byte-identical** 5348↔5402. `_shaderData` (#7) and `References` (#17)
  still resolve with the same kind and type. `ExhaustInstance` grew 224 → 272 B with the bend/fold
  fields appended **after** `absorptionDensity`/`refractionIntensity` (#8). `AddVolumetricExhaustInstances`
  only changed `Viewport`→`IViewport` (param names intact, single overload) and the
  `UpdateFrameData()` → `AddVolumetricExhaustInstances` call order in `Program.cs` (`:2298`/`:2303`) is
  unchanged. `VolumetricExhaust.vert` now includes `PlumeBend.glsl` and reads `boundingLength` /
  `foldParameters.w` / `bendDirectionAndAngle` — all populated by the renderer for pyro's instances too.
- 🔍 **Needs a live pass:** (a) plumes still render and follow the anchor in flight after the
  signature fix; (b) atmospheric plumes fold/bend with wind (the new `airVelocity`/`airDensity` path);
  (c) whether any plume shows refraction/heat-haze (expected: none — see regression above); (d) the
  Look sliders (`absorptionDensity`) still visibly change a single plume.

### 5402 → 5438 (2026-09-10)

The source diff and render audit found four Pyro migration points. The managed implementation is updated;
the native renderer still needs an in-game pass.

- 🔴 **Compile/API break — fixed.** `VolumetricExhaustInstance.UpdateState` is now `void` and accepts gas,
  exhaust, core velocity, emitter/rest transforms, ambient pressure and air state. Pyro passes the complete
  synthetic values and updates `LastPlumeData` only while active. `IsLive` gates direct rendering so startup
  and shutdown pulses retain native behavior.
- 🔴 **Renderer API break — fixed.** Standalone plumes now call
  `AddInstance(instance, in ExhaustBendTarget, in ExhaustAxialFade, in ExhaustDiamondFade)` with default bend,
  `NoFadeOut` and `None`. The postfix binds the three-argument Vehicle method explicitly; the removed viewport
  argument is not referenced. Pyro does not call `BeginPlumeGrouping`, preserving the stock pending hierarchy.
- 🔶 **Silent data drift — fixed.** `PlumePhysics` uses public `PlumeData.Compute`, including stagnation pressure,
  core velocity and mass flow, and KSA's public visibility formula. Throttle scales synthetic chamber pressure;
  inactive shutdown frames retain the instance's last active physics.
- 🔶 **Silent look drift — fixed.** Since `AddInstanceCore` rebuilds absorption/refraction from the shared
  template, Pyro's final typed `AddInstance(ExhaustInstance,int)` prefix applies look values only inside the
  direct submission's `try/finally` scope. `_shaderData` reflection is retired. The native 5438 renderer still
  resets `_hasRefractionInstances` without setting it; refraction remains a live acceptance risk for stock and
  Pyro plumes.
- 🔶 **Asset migration — fixed.** Removed `EngineAVernier` and `EngineATurbine` IDs resolve to
  `EngineAAuxiliary` only when an installed content mod does not provide the legacy ID. Unknown IDs remain
  explicit failures. Current IDs are normalized on instance, preset, save and UI paths.
- 🔍 **Needs a live pass:** plume anchor following and startup/shutdown tails, full/fractional throttle,
  atmosphere wind bend, absorption isolation from stock engines, old preset/save alias replay, and the native
  refraction pass once KSA enables it.

## Runtime on/off cycling

`PlumeEntry.Cycle` / `PlumeCycle` add session-only simulation-second gating. Both submod Update
and `PlumeEmitter.Submit` sample existing `Universe.GetElapsedSeconds()` / supplied simulation time;
absolute phase prevents double advancement on repeated render submissions. `EffectiveEnabled`
combines manual Enabled and cycle phase before the existing
`VolumetricExhaustInstance.UpdateState(simulationTime,isActive,simulationDeltaTime, in gas, in exhaust,
coreVelocity, emitterPosition, emitterAxis, restPosition, restAxis, ambientPressure, airVelocity, airDensity)`
call (`KSA/VolumetricExhaustInstance.cs:111`). Startup/shutdown pulse tracking stays stock; `IsLive` gates
the new direct AddInstance seam.
No cycle-specific patch or reflection dependency is added; the existing final-instance prefix remains scoped.
Manual/bulk toggles cancel cycles; presets do not
serialize them. Long frames/warp sample current phase; backward time restarts On. Managed phase tests
and full solution build pass; live transient appearance remains unverified.

### Scene persistence

`PyroSubmod.Persistence` captures exact vehicle/part addresses and existing `PlumePreset` settings,
then recreates `VolumetricExhaustInstance` through `CreatePlume` after native load. `PlumeCycle`
restores On/Off phase relative to `Universe.GetElapsedTime().Seconds()`; no engine/native part is
created. Render transients restart, but logical cycle timing survives. Old plume references are
cleared at the joined save-load boundary. No new Harmony target or shader layout dependency.
Managed cycle checks cover saved On/Off phase and its next boundary; native render acceptance open.

`pyro.templates` also persists all existing shared Template Editor controls: `Absorption` scalar/
clean-burn values, `Emission` brightness/color gradient/Mach diamonds, `Noise` subtypes,
`LengthWeights` and `Quality`. `SavedPlumeTemplate` copies explicit values only, capturing originals
on first edit and refreshing live nozzle settings through existing TemplateRefresher APIs. Replay
order 30 applies shared definitions before standalone plume recreation. Reset clears old UI part
caches and restores global templates even on vanilla loads. No additional game reflection is used.
