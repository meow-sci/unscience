# Exhaust Plumes (pyro) — Game Integration Scope

Permanent reference for detecting when KSA game updates break **pyro** (standalone volumetric engine
plumes). Every game-facing member the mod touches is enumerated with its decompiled-source path.

**Verified game versions**

- NEW decomp **`2026.9.7.5402`** root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp` (namespace-foldered)
- OLD decomp **`2026.8.22.5348`** root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`
- NEW Content root: `~/repos/meow-sci/ksa-game-assemblies/current/Content`
- OLD Content root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/Content`

Originally written against 5348; line numbers in the table are **NEW (5402)** unless marked otherwise.
The in-repo `decomp/ksa` copy is **older** and materially different for this area (pre-5348
`PlumeData`/`ExhaustInstance` layouts) — always check this file's members against the provided tree,
not the repo copy.

**How the mod is hosted:** all logic in `pyro.lib` (`PyroSubmod : ISubmod`, `PyroPatches.Apply/Remove`),
consumed by the standalone host (`pyro/Mod.cs`, `pyro/Patcher.cs`) and by unscience
(`unscience/Mod.cs` adds `PyroSubmod`; `unscience/Patcher.cs` → `TryApply("pyro", …)`). Findings apply to
both hosts identically.

---

## Integration model

1. **One Harmony postfix** on `Vehicle.AddVolumetricExhaustInstances(Camera, IViewport,
   VolumetricExhaustRenderer, double)` (`KSA/Vehicle.cs:5512`). The game calls this from
   `Program.OnPreRender` once per visible vehicle, after `VolumetricExhaustRenderer.UpdateFrameData()`
   reset the instance list. pyro's postfix (`PyroPatches.cs:35`) hands the same `camera`/`renderer`/
   `frameDeltaTime` to `PyroSubmod.SubmitPlumes`, which submits every plume welded to `__instance`.
2. **Per plume, pyro owns a real `VolumetricExhaustInstance`** built from
   `new VolumetricExhaustReference { Id }.Load()` (`PlumeTemplates.cs:55-59`) and drives it exactly like
   `RocketNozzleState.AddExhaustInstance` (`KSA/RocketNozzleState.cs:81`): `UpdateState(simTime,
   isActive, dt, plumeData)` then `renderer.AddInstance(posEgo, axis, instance, throttle, airVelocity,
   airDensity)` (`PlumeEmitter.cs:56,76-78`). The air state (`ComputeAirState`, `PlumeEmitter.cs:87-98`)
   mirrors the game's own derivation in `Vehicle.AddVolumetricExhaustInstances` (`:5518-5525`) — 5402
   uses it to fold/bend plumes in atmosphere.
3. **`PlumeData` is synthesised**, not read from an engine: `PlumePhysics.TryCompute` mirrors
   `RocketNozzle.UpdatePlumeData` (`KSA/RocketNozzle.cs:254`, now a thin wrapper over the public static
   `ComputePlumeData` `:266`) and `RecomputeGasVisibilityDensity` (`:182`, formula extracted to public
   static `ComputeMinGasVisibilityDensity` `:197`) from user nozzle settings +
   `PhysicalAtmosphereReference.GetAtmosphericPressure(camera)`.
4. **Positioning** chain: part-local offset → `Part.MatrixAsmb2VehicleAsmb` / `Part.Asmb2VehicleAsmb`
   (`KSA/Part.cs:736,720`) → `Vehicle.PosAsmbToBody` (`:1270`) → `Vehicle.Body2Cce` (`:475`) →
   `Camera.GetPositionEgo(vehicle)` (`KSA/Camera.cs:231`). Base axis is part-local **-X**, matching every
   stock `<ExhaustDirection X="-1">` in `Core/CorePropulsionAGameData.xml`.
5. **Template Editor** writes the shared `VolumetricExhaustTemplate` sub-objects (same fields and same
   `ColorRgbReference(float3)` + `OnDataLoad(new Mod())` idiom as the game's `VolumetricExhaustRenderer.
   OnDrawUi`, `:2126-2148`), then `TemplateRefresher` calls `OnSettingsChanged()` on every affected
   instance and `RecomputeGasVisibilityDensity` on every real nozzle — the debug editor's own `changed`
   path (`:2321-2345`) minus the transient-LUT rebake (pyro does not edit transients).

**Persistence** — Named **presets** only (not active plumes). `PlumePresetManager`
(`pyro.lib/PlumePresetManager.cs`) reads/writes TOML at
`<MyDocuments>/My Games/Kitten Space Agency/.unscience/pyro-presets.toml`
(dir from `ksa-abstractions.lib/KsaPaths.cs:9`). Mod-authored file, not a game asset —
no game integration point beyond the `KsaPaths` directory convention.

## Touchpoints

| # | Kind | Mod code | Game member | Decomp path (5402) | Status | Notes |
|---|---|---|---|---|---|---|
| 1 | Harmony postfix | `PyroPatches.cs:16,35` | `Vehicle.AddVolumetricExhaustInstances(Camera camera, IViewport viewport, VolumetricExhaustRenderer renderer, double frameDeltaTime)` | `KSA/Vehicle.cs:5512` (was `:5303`, `Viewport`) | ✅ | resolved with `nameof` — a rename is a compile break. Param **names** (`camera`, `renderer`, `frameDeltaTime`) are bound by Harmony: a param rename silently unbinds → `Apply` throws → `TryApply` logs and skips pyro. 5402: `Viewport`→`IViewport` only (names unchanged, single overload); body now also derives `airVelocity`/`airDensity` (`:5518-5525`) |
| 2 | Direct API | `PlumeEmitter.cs:76-78` (+ `ComputeAirState` `:87-98`) | `VolumetricExhaustRenderer.AddInstance(float3, float3, VolumetricExhaustInstance, float throttle, float3 airVelocity, float airDensity) : float` | `KSA/VolumetricExhaustRenderer.cs:710` (was `:860`, 4-arg `void`) | ✅ **fixed 5402** | 🔴 5402 **removed the 4-arg overload** (compile break, fixed). Return is `visualExpansionRadius` (discarded). `ComputeAirState` uses `Vehicle.GetSurfaceVelocityCci()` (`KSA/Vehicle.cs:2922`, **new API in 5402**), `IParentBody.GetCci2Cce()`/`.GetAtmosphereReference()`/`.MeanRadius` (`KSA/IParentBody.cs:51,57`), `IPosition.GetPositionEcl()` (`KSA/IPosition.cs:7`), `PhysicalAtmosphereReference.GetAtmosphericDensityAtAltitude(double)` (`:85`). Reads `instance.ShaderData` (copy) + `LastPlumeData`; consumes `ApparentExhaustVelocity`, `ThroatRadius`, `ThroatDensity` (new @5348); 5402 adds wind fold/bend via `ExhaustPlumeDeformation` (`:809-811`) |
| 3 | Direct API | `PyroSubmod.cs:75` | `VolumetricExhaustRenderer.Disabled` | `:352` (was `:312`) | ✅ | |
| 4 | Direct API | `PlumeTemplates.cs:53-59` | `VolumetricExhaustReference { Id }`, `.Load()`, `.Template`; `new VolumetricExhaustInstance(ref)` | `KSA/VolumetricExhaustReference.cs`; `KSA/VolumetricExhaustInstance.cs:72` | ✅ | file byte-identical 5348↔5402 |
| 5 | Direct API | `PlumeEmitter.cs:56` | `VolumetricExhaustInstance.UpdateState(double, bool, double, PlumeData) : bool` | `KSA/VolumetricExhaustInstance.cs:91` | ✅ | 4-slot pulse tracker (was 2 pre-5348) |
| 6 | Direct API | `TemplateRefresher.cs:20,42` | `VolumetricExhaustInstance.OnSettingsChanged()` | `KSA/VolumetricExhaustInstance.cs:243` | ✅ | |
| 7 | **Reflection (private, string)** | `PlumeEmitter.cs:25,103-106` | `VolumetricExhaustInstance._shaderData : ExhaustInstance` via `FieldRefAccess` | `KSA/VolumetricExhaustInstance.cs:48` | ✅ | writes `absorptionDensity`, `refractionIntensity`. Soft-fails: `PerPlumeLookAvailable=false`, UI notice |
| 8 | Struct layout | `PlumeEmitter.cs:105-106` | `ExhaustInstance.absorptionDensity` (`:25`), `.refractionIntensity` (`:69`) | `KSA/ExhaustInstance.cs:25,69` | ✅ | ⚠ 5348 moved colours/brightness/noise/sample counts to `ExhaustTemplateData` (per-template buffer, `templateIndex`) — per-plume colour intentionally not offered. 5402: struct grew **224 → 272 B** — `padding0/padding1` replaced by `float bendExponent`, `float boundingLength`, `float4 bendDirectionAndAngle`, `float4 foldParameters`, `float4 foldAxisOffset` (`:81-89`, mirrored in `VolumetricExhaust/Data/InstanceData.glsl:55-70`). All **after** the two fields pyro writes and populated by the renderer (`:787,:809-811`); pyro uses typed field access, so no offset exposure. ⚠ `refractionIntensity` is inert in 5402 — see findings |
| 9 | Direct API (object init) | `PlumePhysics.cs:70-92` | `PlumeData` (all `required`) | `KSA/PlumeData.cs` | ✅ | any added/renamed `required` member = compile break |
| 10 | Direct API | `PlumePhysics.cs:30-89` | `GasProperties{Gamma,SpecificGasConstant}.ComputeSpeedOfSound/…PressureAngle/…PressureMach/ComputePrandtlMeyer`; `GasConditions{Pressure,Temperature}.ComputeDensity` | `KSA/GasProperties.cs`; `KSA/GasConditions.cs` | ✅ | pressures **Pa** |
| 11 | Direct API | `PlumePhysics.cs:33,61` | `RocketDesign.SolveMachNumberFromAreaRatio(GasProperties,double)`, `ComputeAreaRatioFromMachNumber(double,double)` | `KSA/RocketDesign.cs:168,187` | ✅ | |
| 12 | Direct API | `PlumePhysics.cs:113` | `PhysicalAtmosphereReference.GetAtmosphericPressure(Camera) : double` (**atm**) | `KSA/PhysicalAtmosphereReference.cs:50` | ✅ | ×101325 → Pa |
| 13 | Direct API | `PlumePhysics.cs:98-107` | `template.Emission.Brightness.Value`, `Absorption.ScatteringBrightness.Value`, `Absorption.Density.Value` | `KSA/Emission.cs`, `KSA/Absorption.cs` | ✅ | visibility threshold formula copied from `RocketNozzle.RecomputeGasVisibilityDensity`; 5402 extracted it unchanged into public static `RocketNozzle.ComputeMinGasVisibilityDensity(VolumetricExhaustTemplate, double)` (`:197`) — optional hardening: call it directly |
| 14 | Direct API | `PlumeEmitter.cs:69-74` | `Part.MatrixAsmb2VehicleAsmb`, `Part.Asmb2VehicleAsmb`, `Vehicle.PosAsmbToBody(double3)`, `Vehicle.Body2Cce`, `Camera.GetPositionEgo(IPosition)`, `doubleQuat.NormalizedOrZero()` (ext, `KSA/QuaternionEx.cs:280`) | `KSA/Part.cs:736,720`; `KSA/Vehicle.cs:1270,475`; `KSA/Camera.cs:231` | ✅ | line moves only |
| 15 | Direct API | `PyroSubmod.cs:77-78` | `Universe.GetElapsedSeconds()`, `Universe.GetSimulationSpeed()` | `KSA/Universe.cs:2108,2026` (was `:2054,1972`) | ✅ | |
| 16 | Direct API | `PyroSubmod.CreateUi.cs:135,148`; `PyroSubmod.cs:187-192`; `PyroUi.cs:12` | `Vehicle.Parts.Parts`, `Part.SubParts`, `Part.PartParent`, `Part.Template.Id`, `Part.Id` | `KSA/Part.cs:1079,660,576,698` | ✅ | anchor pick + dead-anchor pruning |
| 17 | **Reflection (internal, string)** | `PlumeTemplates.cs:44-48` | `VolumetricExhaustTemplate.References : SerializedCollection<T>` → `GetList()` | `KSA/VolumetricExhaustTemplate.cs:38`; `KSA/SerializedCollection.cs:42` | ✅ | soft-fails to the 7 stock ids via public `Get(id)` (`:50`); both files byte-identical 5348↔5402 |
| 18 | Direct API (read+write) | `PyroSubmod.TemplateUi.cs` | `VolumetricExhaustTemplate.Absorption/Emission/Noise/LengthWeights/Quality` sub-objects; `DoubleReference.Value`, `BoolReference.Value`, `Quality.VolumetricVesselShadows`, `ColorGradient.Color0..3`, `Flow.MachDiamonds.{LeadIn,LeadOut,MiddleRadius}` | `KSA/VolumetricExhaustTemplate.cs:12-27` + sub-type files | ✅ | GPU `ExhaustTemplateData` rebuilt from these each `Render()` (`VolumetricExhaustRenderer.cs:859-866`, was `:1236-1243`); all sub-type files byte-identical |
| 19 | Direct API | `PyroSubmod.TemplateUi.cs:121-129` | `ColorRgbReference.Value.AsFloat3`, `new ColorRgbReference(float3)`, `.OnDataLoad(new Mod())` | `KSA/ColorRgbReference.cs:22,28,35`; `KSA/Mod.cs` | ✅ | identical to the game editor (`VolumetricExhaustRenderer.cs:2126-2148`) |
| 20 | Direct API | `TemplateRefresher.cs:35-44` | `PartTree.RocketNozzles.ModulesAndAllStates` enumerator → `.FxState.VolumetricExhaust`, `.Module.RecomputeGasVisibilityDensity(in …)` | `KSA/Vehicle.cs:5527` (game usage); `KSA/RocketNozzle.cs:182` | ✅ | in try/catch; failure only means real engines lag on threshold updates |
| 21 | Asset ids | `PlumeTemplates.cs:13`; `PlumeEntry.cs:46` default `EngineALarge` | `EngineALarge, EngineAMed, EngineACompact, EngineAVernier, EngineATurbine, RCS, MmuRcsVac` | `Core/ExhaustAssets.xml:307,650,993,1331,1670,3,2009` | ✅ | fallback list only. Ids unchanged in 5402; the five `EngineA*` templates had their `Emission/ColorGradient` retuned (see findings) |
| 22 | Build refs | `pyro.lib.csproj` | `Brutal.Vulkan`, `Brutal.Vulkan.Abstractions`, `BepuUtilities` | — | ✅ | needed so `VolumetricExhaustRenderer` / `Symmetric3x3` (`Part` matrix API) resolve |

## Update-risk findings

- **Loud breaks (compile):** `PlumeData` required-member churn (#9), `AddInstance` signature (#2),
  `AddVolumetricExhaustInstances` rename (#1 via `nameof`), any template sub-object field rename (#18).
- **Silent breaks (runtime):** postfix **parameter renames** (#1 — Harmony binds by name, throws at
  `Apply`, pyro is skipped with a console line); the two string lookups (#7, #17) — both degrade
  gracefully and say so in the UI.
- **Semantic drift with no symbol change:** `AddInstance` may start reading new `PlumeData` fields that
  pyro leaves at defaults (as 5348 did with `ThroatRadius`) — symptom is a wrong-shaped plume, not an
  error. Re-diff `RocketNozzle.UpdatePlumeData` against `PlumePhysics.TryCompute` on every bump.
  Likewise the `_shaderData` fields pyro overrides (#8) could migrate to the template buffer, silently
  turning the Look sliders into no-ops.
- **Unit assumption:** pressures are Pa game-side (`PressureReference` stores Pa; `Combustor
  MaxPressure Bar="49"`), ambient from `GetAtmosphericPressure` is atm (`× 9.869e-6`). If either flips,
  plumes become absurdly long/short.
- **Not done / known limits:** no per-plume colour (see #8); Template Editor does not edit
  startup/shutdown transients (would need `TransientAnimationLut.BakeAnimationLutData`, which is private
  renderer state); plumes only update while their vehicle is in `Program.VehiclesInFrame` (same as
  stock engines).

### 5348 → 5402 (2026-09-02)

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

## Runtime on/off cycling

`PlumeEntry.Cycle` / `PlumeCycle` add session-only simulation-second gating. Both submod Update
and `PlumeEmitter.Submit` sample existing `Universe.GetElapsedSeconds()` / supplied simulation time;
absolute phase prevents double advancement on repeated render submissions. `EffectiveEnabled`
combines manual Enabled and cycle phase before the existing
`VolumetricExhaustInstance.UpdateState(simulationTime,isActive,simulationDeltaTime,plumeData)` call
(`KSA/VolumetricExhaustInstance.cs:91`). Startup/shutdown pulse tracking and AddInstance stay stock.
No new patch, reflection or shader dependency. Manual/bulk toggles cancel cycles; presets do not
serialize them. Long frames/warp sample current phase; backward time restarts On. Managed phase tests
and full solution build pass; live transient appearance remains unverified.
