# Vehicle Manipulation / Physics Mods — Game Integration Scope

Permanent reference for detecting when KSA game updates break the vehicle-manipulation /
physics mods (`eternal-flame`, `garrys-torch`, `i-feel-seen`). Every game-facing member
these mods touch is enumerated and verified against decompiled sources.

**Verified game versions**

- NEW decomp `2026.9.7.5402` root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- OLD decomp `2026.8.22.5348` root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`

Paths in the **Decomp path (NEW)** column are relative to the NEW decomp root
(namespace-foldered, e.g. `KSA/Vehicle.cs`). **Mod code** paths are relative to the repo
root `~/repos/meow-sci/unscience`.

**How these mods are hosted (all three)**

- Logic + game access live in the `*.lib` project; each `.lib` exposes an `ISubmod`
  (`MeowSci.KsaAbstractions.ISubmod`) consumed two ways:
  1. **Standalone** StarMap mod (`<mod>/Mod.cs`) — own ImGui window, F11 toggle, own
     `Patcher`.
  2. **Embedded** in the **unscience** supermod (`unscience/Mod.cs` `OnFullyLoaded`,
     submods created at `unscience/Mod.cs:60-85`) as collapsible sections, with a single
     shared `Harmony("MeowSci.Unscience")` instance (`unscience/Patcher.cs`).
- Vehicle enumeration is funneled through `ksa-abstractions.lib/VehicleProvider.cs`
  (`GetAllVehicles`/`GetControlledVehicle`), so that helper's game touchpoints are part of
  each mod's effective surface and are listed per mod.
- Every top-level mod also applies the shared `HotkeyGuard` (`ksa-abstractions.lib/HotkeyGuard.cs`,
  patches `GameSettings.OnKeyAll`) — catalogued in the master integration surface and not repeated
  in full here; listed as one row per mod.

**Summary of 4680 -> 4750 risk**

- **eternal-flame** — NO breaking deltas. Every member it touches is signature-identical
  OLD->NEW. The rev 4681 electrical refactor does **not** reach it: it refills batteries by
  calling `Battery.Refill(ref BatteryState)` (which the game refactored internally but kept
  signature-stable), never naming `Joules`/`JoulesReference`/`EnergyReference`/`Charge`.
- **garrys-torch** — **1 confirmed compile break** (CS8604, rev 4729 Brutal nullability) at
  `garrys-torch.lib/GarrysTorchSubmod.cs:457`. All ~25 typed/reflected game touchpoints are
  signature-identical OLD->NEW. One behavioral watch item from rev 4699 (`Vehicle.IsControllable`).
- **i-feel-seen** — NO breaking deltas. Both string-resolved Harmony targets
  (`Vehicle.GetWorldMatrix`, `Vehicle.UpdateRenderData`) and every prefix-body member are
  signature-identical OLD->NEW.

---

## eternal-flame (`eternal-flame` / `eternal-flame.lib`)

**Purpose** — Infinite fuel + electricity. Keeps selected vehicles topped up: periodically
calls `Vehicle.RefillConsumables()` (fuel/resource tanks) and refills every `Battery`
module to `MaximumCapacity`. Battery refills are driven from a Harmony **prefix** on
`Universe.ExecuteNextVehicleSolvers` so the new charge is copied into the next electrical
simulation step; fuel refills run on the normal UI update tick.

**Unscience integration** — `EternalFlameSubmod : ISubmod`
(`eternal-flame.lib/EternalFlameSubmod.cs:10`), holding a `FuelManager`
(`eternal-flame.lib/EternalFlameLib.cs:25`). `Update(dt)` -> `FuelManager.Update` (fuel);
`UpdateBeforeVehicleSolvers()` -> `FuelManager.UpdateElectricityBeforeVehicleSolvers`
(batteries). Standalone host `eternal-flame/Mod.cs:27` (`new EternalFlameSubmod()`), with the
solver prefix wired in `eternal-flame/Patcher.cs:43-69` (`EternalFlameSolverPatch`). Embedded
host: `unscience/Mod.cs:69` (`new EternalFlameSubmod()`); the supermod re-declares the
identical solver prefix as `EternalFlamePatches` in `unscience/Patcher.cs:144-178`
(applied at `unscience/Patcher.cs:64`). `EternalFlameSubmod.Instance` (static) is the bridge
the prefix calls into.

**UI/hotkeys** — Standalone window "Eternal Flame - Infinite Fuel", 500x450, toggled by
**F11** (`eternal-flame/Mod.cs:58,91`). Content (`EternalFlameSubmod.RenderContent`,
`eternal-flame.lib/EternalFlameSubmod.cs:34`): filterable vehicle combo + Add, monitored-vehicle
table with per-row Fuel/Elec checkboxes and remove, refill-interval `DragInt` (0–5000 ms).
All ImGui via `Brutal.ImGuiApi`.

**Persistence** — None. Monitored list, interval, and toggles are in-memory
(`FuelManager._monitored`, `RefillIntervalMs`) and reset on reload. No disk I/O, no save hooks.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | Harmony (prefix) | `eternal-flame/Patcher.cs:47,55` (standalone) and `unscience/Patcher.cs:148,156` (supermod) | `Universe.ExecuteNextVehicleSolvers(double dtPlayer, SimStep simStep)` — `public static void`; resolved `AccessTools.Method(typeof(Universe), nameof(...))` (no param array), prefix is param-less `void` (priority First) | `KSA/Universe.cs:1834` | Yes | Same (OLD `Universe.cs:1767`); 5402 body diff = removal of a clutter debug-draw block only | Single overload, so no-arg resolution is unambiguous. Prefix returns void -> original always runs. Highest-value chokepoint for this mod. Since 5402 `Universe.ExecuteNextClothSolvers` is kicked **before** this method (`KSA/Program.cs:2144-2145`); irrelevant to battery refill. |
| 2 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:80` | `Vehicle.RefillConsumables()` — `public void` | `KSA/Vehicle.cs:3169` | Yes | Same (OLD `Vehicle.cs:3008`; body identical) | Internally calls `Parts.RefillConsumables()` + `RecomputeMassProperties` + `FlightComputer.ReadUpdatedVehicleConfiguration` (all internal; not touched directly). |
| 3 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:128` | `Vehicle.Parts` — `public PartTree Parts` (field) | `KSA/Vehicle.cs:604` | Yes | Same (OLD `Vehicle.cs:598`) | Entry to battery state list. |
| 4 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:128` | `PartTree.Batteries` — `public ModuleStateful<Battery,BatteryState,EmptyStruct,EmptyStruct>.StateList Batteries` (field) | `KSA/PartTree.cs:53` | Yes | Same (OLD `PartTree.cs:53`) | Generic `StateList`. |
| 5 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:129` | `StateList.NumModules` — `public int NumModules` | `KSA/ModuleStateful.cs:274` | Yes | Same (file byte-identical) | Early-out when 0. |
| 6 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:132` | `StateList.Modules` — `public Span<TModule> Modules` | `KSA/ModuleStateful.cs:266` | Yes | Same (file byte-identical) | Iterates `Battery[]`. |
| 7 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:136` | `StateList.GetModuleAndAllMutableStatesForInitialization(TModule)` — returns `ModuleAndAllMutableStatesRef` | `KSA/ModuleStateful.cs:479` | Yes | Same (file byte-identical) | Returns ref struct with `.Module` + `.State`. |
| 8 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:137` | `ModuleAndAllMutableStatesRef.Module` / `.State` (Battery / BatteryState) | `KSA/ModuleStateful.cs` (nested ref struct) | Yes | Same | Game uses the same `.Module.Refill(ref ...State)` shape in `KSA/ResourceManager.cs`. |
| 9 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:137` | `Battery.Refill(ref BatteryState state)` — `public void` (sets `state.Charge = MaximumCapacity`) | `KSA/Battery.cs:63` | Yes | Same (file byte-identical 5348→5402) | **Insulates the mod from rev 4681.** Body unchanged OLD->NEW. |
| 10 | Direct typed API (indirect) | via #9 | `Battery.MaximumCapacity` — `public required Joules MaximumCapacity` | `KSA/Battery.cs:23` | Yes | Same (file byte-identical) | Read only inside `Refill`; mod never names `Joules`. |
| 11 | Direct typed API | `eternal-flame.lib/EternalFlameLib.cs:74,111` (lookup) | `Vehicle.Id` — `public virtual string Id` (inherited `Astronomical.Id`) | `KSA/Astronomical.cs:104` | Yes | Same (OLD `Astronomical.cs:104`) | Monitored-vehicle key matching. |
| 12 | Direct typed API | `ksa-abstractions.lib/VehicleProvider.cs:14` (called `EternalFlameLib.cs:65,102`; `EternalFlameSubmod.cs:54,109`) | `Universe.CurrentSystem` (`KSA/Universe.cs:94`) -> `CelestialSystem.All` (`KSA/CelestialSystem.cs:64`) -> `LookupCollection<Astronomical>.UnsafeAsList()` (`KSA/LookupCollection.cs:210`) | `KSA/Universe.cs:94` | Yes | Same (`CelestialSystem.All` OLD `:57`) | Shared enumerator; a break here cascades to all three mods' UI. Since 5402 the list also contains debris fragments (`Vehicle.IsDebris`, `KSA/Vehicle.cs:392`). |
| 13 | Harmony + Reflection | `eternal-flame/Patcher.cs:20` -> `HotkeyGuard.cs:21` | `GameSettings.OnKeyAll(GlfwKeyEvent)` — `public static bool`; `nameof`-resolved, prefix `ref bool __result` | `KSA/GameSettings.cs:3301` | Yes | Same (file byte-identical) | Shared guard (full row in the master integration surface). |
| 14 | Lifecycle | `eternal-flame/Mod.cs:19-87` | StarMap attrs: `StarMapMod`, `StarMapImmediateLoad`, `StarMapAllModsLoaded`, `StarMapBeforeGui`, `StarMapAfterGui`, `StarMapUnload` (StarMap.API) | (StarMap.API package) | Yes | Same | Fuel in `OnBeforeUi`; battery via the solver prefix. |

**Game assets referenced** — None.

**Update-risk findings (4680 -> 4750)**

- **No breaking deltas.** All 11 typed members + the patched `ExecuteNextVehicleSolvers` are
  signature-identical OLD->NEW (line shifts only).
- **rev 4681 electrical refactor — confirmed NOT impacting this mod.** The refactor renamed the
  *serialization* type `JoulesReference` -> `EnergyReference` and changed `Battery.SaveData`
  (`KSA/Battery.cs:12-13`, `110`, `120` differ OLD vs NEW) and `Battery.DrawStateInfo`
  (`JoulesReference.ToNearest` -> `EnergyReference.ToNearestElectrical`, `Battery.cs:94-95`).
  The mod touches **none** of those — it calls `Battery.Refill(ref BatteryState)`, whose body
  (`state.Charge = MaximumCapacity`) is byte-identical OLD->NEW, and `MaximumCapacity` is still
  `Joules`. If a future build changes `Battery.Refill`'s signature or removes it, this mod's
  electricity path breaks; watch that method specifically.
- `RefillConsumables` internals changed shape across builds but its public no-arg signature is
  stable; the mod only calls the public method.

---

## garrys-torch (`garrys-torch` / `garrys-torch.lib`)

**Purpose** — Vehicle-to-vehicle welding. Every frame it teleports a *source* vehicle to a
pose relative to a *target* vehicle (optionally anchored to a specific target `Part`), with
position/rotation offset, independent local-axis X/Y/Z part scaling (including `KittenEva` avatars), and
optional rotation lock. Also supports eased animation of weld params.

**Unscience integration** — `GarrysTorchSubmod : ISubmod` holds weld state and animation;
`WeldEngine.UpdateWeld(entry, stateTime)` computes and teleports each source.
`GarrysTorchPatches` is installed/removed by both `garrys-torch/Patcher.cs` and
`unscience/Patcher.cs`. It wraps the single `Universe.GetJobSimStep(dtPlayer)` call in the private
`Program.PrepareFrame(double, double)` caller: obtain the original step, advance welds with player
delta and `step.PreviousTime`, return the unchanged step. Ordinary submod/UI callbacks do no welding.
The hidden-HUD hook no longer dispatches weld updates. `KittenScalePatches` remains a separate
render-only postfix on `KittenRenderable.ModelToBodyMatrix()`.

**Result-retention fix (2026-09-06)** — The old after-UI teleport removed the source from its bubble
before `PhysicsBubble.ApplyResultsToVehicles` (`KSA/PhysicsBubble.cs:664`) could call
`Vehicle.UpdateFromTaskResultsUnsynchronized` (`KSA/Vehicle.cs:2403`). That skipped
`Parts.UpdateFromTaskResults` (`:2499`; `KSA/PartTree.cs:949`) and its bulk module-state commit.
`KeyframeAnimationModule.UpdateModules` (`KSA/KeyframeAnimationModule.cs:156`) had advanced worker
`TimeCurrent`, but `ModuleStateful.StateUpdater.Prepare` (`KSA/ModuleStateful.cs:777`) discarded it
on the next tick. The handoff now lets those results commit first; it does not manually tick actuators.
Disposed source/target welds are removed before animation updates, because applying results can
destroy vehicles; scale restoration skips disposed sources. Per-kitten animation targeting and the
skeletal render pipeline are unchanged.

**Standing timing invariant** — In 5402, `Program.PrepareFrame:2103-2109` waits on orbit/vehicle/cloth
workers and applies all three result sets. The `GetJobSimStep` call at `:2143` follows, before cloth,
vehicle and orbit scheduling at `:2144-2146`. The transpiler requires exactly one of each of these
seven Universe calls, in that order, and rejects unexpected layouts. It patches the caller to avoid
relying on a previously inlined solver callee. On game updates, also re-read the wait/result/snapshot
semantics: lexical call order alone cannot prove they remain equivalent. `SimStep.PreviousTime`
is the just-applied state time; the former `NextTime` stamp is wrong at this earlier handoff.

**Validation** — `garrys-torch.tests` runs the production Harmony patch on a warmed-up managed
fixture and tests retained actuator progress, one update per frame, start-time stamps, pause/warp,
missing system/submod, exception isolation, unload and malformed call sequences. This does not run
native KSA physics. In-game: weld a separate light craft at a non-overlapping offset, actuate forward
and back (stock and Zippo Disco), toggle Weld Enabled, hide HUD with F2, exercise pause/warp and a
weld chain anchored to a moving target part, and unload. Watch for body/origin time mismatches,
collection/shape-lock errors and part destruction; compare with the same unwelded light craft.

**UI/hotkeys** — Standalone window "Garry's Torch", 450x500, toggled by **F11**
(`garrys-torch/Mod.cs:51,85`). Content (`GarrysTorchSubmod.RenderContent:105`): Create-Weld
header (filterable source / target / target-part / preset combos), position/rotation
`DragFloat3`, scale `DragFloat3`, lock-rotation checkbox, active-weld child panels with
per-weld edit + Save-as-preset / Unweld, and delete/save modals.

**Persistence** — Named **presets** only (not active welds). `PresetManager`
(`garrys-torch.lib/PresetManager.cs`) reads/writes TOML at
`<MyDocuments>/My Games/Kitten Space Agency/.unscience/garrys-torch-presets.toml`
(`PresetManager.cs:23-24`, dir from `ksa-abstractions.lib/KsaPaths.cs:9` via
`Environment.SpecialFolder.MyDocuments`). Active welds are in-memory (`_welds`) and lost on
reload. TOML via `Tomlyn`; new presets store `scale_x`/`scale_y`/`scale_z`, while the loader expands
the legacy scalar `scale` key uniformly for backwards compatibility.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | **Harmony transpiler / private method** | `ksa-abstractions.lib/PhysicsFrameHook.cs` | `Program.PrepareFrame(double currentPlayerTime, double dtPlayer)`; wraps the single `Universe.GetJobSimStep(double)` call | `KSA/Program.cs:2094,2143` | Yes | New mod hook against unchanged 5402 surface | Requires unique ordered ApplyOrbit/Vehicle/Cloth, GetJobSimStep, ExecuteNextCloth/Vehicle/Orbit calls. Re-read game wait/snapshot semantics on updates. No UI fallback. |
| 2 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:19,75` | `Vehicle.Parent` — `public IParentBody Parent => Orbit.Parent` | `KSA/Vehicle.cs:372` | Yes | Same (OLD `Vehicle.cs:370`) | Reference-compared for parent-body match; `.GetCci2Cce()` called on it (#10). |
| 3 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:28` | `Vehicle.GetPositionCci()` — `public double3` | `KSA/Vehicle.cs:2590` | Yes | Same (OLD `Vehicle.cs:2433`) | Target world position. |
| 4 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:29` | `Vehicle.GetVelocityCci()` — `public double3` | `KSA/Vehicle.cs:2538` | Yes | Same (OLD `Vehicle.cs:2381`) | Source velocity = target velocity. |
| 5 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:30,90` | `Vehicle.GetBody2Cci()` — `public doubleQuat` | `KSA/Vehicle.cs:3095` | Yes | Same (OLD `Vehicle.cs:2934`) | Orientation transforms. |
| 6 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:58` | `Vehicle.CenterOfMassAsmb` — `public double3 CenterOfMassAsmb` | `KSA/Vehicle.cs:564` | Yes | Same (OLD `Vehicle.cs:558`) | Part-anchor offset base. |
| 7 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:85,92` | `Vehicle.BodyRates` — `public double3 BodyRates` | `KSA/Vehicle.cs:510` | Yes | Same (OLD `Vehicle.cs:504`) | Passed to `Teleport`; NaN-guarded by mod. |
| 8 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:126` | `Vehicle.Orbit` — `public Orbit Orbit => Patch.Orbit` (reads `.OrbitLineColor`) | `KSA/Vehicle.cs:370` | Yes | Same (OLD `Vehicle.cs:368`) | Source orbit's line color reused for new orbit. |
| 9 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:129` | `Vehicle.Teleport(Orbit? orbit, doubleQuat? body2Cce, double3? bodyRates)` — `public void` | `KSA/Vehicle.cs:2209` | Yes | Same (OLD `Vehicle.cs:2053`; body identical bar a log line-number constant) | The core mutation. Nullable params; mod passes non-null. No new gating in 5402 — but the vehicle it moves is now subject to the new `PartFailure` contact-pressure system (see 5348→5402 summary). |
| 10 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:130` | `Vehicle.UpdatePerFrameData()` — `public override void` | `KSA/Vehicle.cs:2613` | Yes | Same (OLD `Vehicle.cs:2456`; body identical) | Refresh caches post-teleport. |
| 11 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:75` | `IParentBody.GetCci2Cce()` — `doubleQuat` (interface) | `KSA/IParentBody.cs:51` | Yes | Same (file byte-identical) | Called on `Vehicle.Parent`. |
| 12 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:58` | `Part.PositionVehicleAsmb` — `public double3` (computed property) | `KSA/Part.cs:704` | Yes | Same (OLD `Part.cs:696`) | Part-anchor position. |
| 13 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:61` | `Part.Asmb2VehicleAsmb` — `public doubleQuat` (computed property) | `KSA/Part.cs:720` | Yes | Same (OLD `Part.cs:712`) | Part-anchor orientation. (5402 also added `Asmb2VehicleAsmb` to the nested `Part.Connection.IConnector` interface, `Part.cs:483` — unrelated to this binding.) |
| 14 | Direct typed API (write) | `garrys-torch.lib/WeldEngine.cs` | `Part.Scale` — `public double3 Scale { get; set; }` (setter calls `ResetCachedPosMatrixValues`) | `KSA/Part.cs:815` | Yes | Same (OLD `Part.cs:807`) | Recursive XYZ scale write. KSA's separate `ScaleFactors(double3)` collapses module rescaling to the largest axis; Garry's Torch does not claim anisotropic mass/module physics. |
| 15 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:157,201` | `Part.SubParts` — `public ReadOnlySpan<Part> SubParts`; `PartTree.Parts` — `public ReadOnlySpan<Part> Parts` | `KSA/Part.cs:1079`; `KSA/PartTree.cs:95` | Yes | Same (OLD `Part.cs:1052`; `PartTree.cs:95`) | Part-tree walk for scaling + target-part list. |
| 16 | Direct typed API | `garrys-torch.lib/GarrysTorchSubmod.cs:190,198` | `Part.Template` (`public PartTemplate Template`) -> `PartTemplate.Id` (`public string Id`, inherited `SerializedId.Id`); `Part.Id` (`public string Id { get; init; }`) | `KSA/Part.cs:576`,`698`; `KSA/SerializedId.cs:13` | Yes | Same (OLD `Part.cs:568`,`690`) | Target-part combo labels. |
| 17 | Direct typed API | `ksa-abstractions.lib/PhysicsFrameHook.cs` | `Universe.GetJobSimStep(double)`; `SimStep.PreviousTime : UniverseTime` | `KSA/Universe.cs:2322`; `KSA/SimStep.cs:5` | Yes | Same | The wrapper computes the original step once and returns it unchanged. PreviousTime stamps the source orbit before workers start. |
| 18 | Behavioral / callback argument | `ksa-abstractions.lib/PhysicsFrameHook.cs` | `Program.PrepareFrame` supplies `dtPlayer` to `GetJobSimStep` | `KSA/Program.cs:2143` | Yes | Same | Player delta also advances weld interpolation. No direct `Program.GetPlayerDeltaTime()` dependency remains in Garry's Torch. |
| 19 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:121` | `Orbit.CreateFromStateCci(IParentBody parent, UniverseTime stateTime, double3 positionCci, double3 velocityCci, byte4 orbitLineColor)` — `public static Orbit` | `KSA/Orbit.cs:1563` | Yes | Same (OLD `Orbit.cs:1563`) | 5-arg factory; arg order/types unchanged since the 5261 `SimTime`→`UniverseTime` rename. |
| 20 | Direct typed API | `garrys-torch.lib/WeldEngine.cs:126` | `Orbit.OrbitLineColor` — `public byte4 OrbitLineColor` (field) | `KSA/Orbit.cs:1138` | Yes | Same (OLD `Orbit.cs:1138`) | — |
| 21 | Direct typed API | `garrys-torch.lib/WeldEngine.cs` | `vehicle is KittenEva`; `KittenEva.Renderable : KittenRenderable` | `KSA/KittenEva.cs:13,59` | Yes | Same | Compile-checked replacement for the former type-name + `_renderable` reflection. |
| 22 | Reflection (private field, string) | `garrys-torch.lib/WeldEngine.cs` | `KittenRenderable._characterAvatar` — `private CharacterAvatar _characterAvatar` | `KSA/KittenRenderable.cs:12` | Yes | Same | **String field name.** Entry to scalar X fallback. |
| 23 | Reflection (public field, string) | `garrys-torch.lib/WeldEngine.cs` | `CharacterAvatar.Core` — `public CharacterCore Core` (**struct** field) | `KSA/CharacterAvatar.cs:211` | Yes | Same | Mod writes the boxed struct back via `SetValue`; requires `Core` to remain a value-type field. |
| 24 | Reflection (public field, string) | `garrys-torch.lib/WeldEngine.cs` | `CharacterCore.Scale` — `public float Scale = 0.01f` (field) | `KSA/CharacterAvatar.cs:34` | Yes | Same | Stores `scale.X * 0.01f`; property fallback retained. |
| 25 | **Harmony postfix + Reflection** | `garrys-torch.lib/KittenScalePatches.cs` | private `KittenRenderable.ModelToBodyMatrix() : float4x4` | `KSA/KittenRenderable.cs:106-109` | Yes | Same | Load-bearing for anisotropic KittenEva rendering. Postfix pre-multiplies `(1, Y/X, Z/X)` into the original matrix; weak-table lookup makes non-welded kittens a constant-time no-op. Loud `MissingMethodException` at patch apply if renamed. |
| 26 | Direct typed API (UI color) | `garrys-torch.lib/GarrysTorchSubmod.cs` | `KSAColor.Xkcd.Scarlet`, `KSAColor.Xkcd.PaleGrey` — `static Color.Preset` | `KSA/KSAColor.cs:1561`,`837` | Yes | Same (file byte-identical) | Unweld-button styling only; failure is visual, not functional. |
| 27 | Direct typed API | `ksa-abstractions.lib/VehicleProvider.cs:14` | `Universe.CurrentSystem` / `CelestialSystem.All` / `LookupCollection.UnsafeAsList` / `Vehicle.Id` | `KSA/Universe.cs:94` etc. | Yes | Same | Shared enumerator (see eternal-flame #12). |
| 28 | Harmony + Reflection | `garrys-torch/Patcher.cs` -> `HotkeyGuard.cs:21` | `GameSettings.OnKeyAll(GlfwKeyEvent)` — `public static bool`, `nameof`-resolved | `KSA/GameSettings.cs:3301` | Yes | Same (file byte-identical) | Shared guard. |
| 29 | Lifecycle | `garrys-torch/Mod.cs`; `unscience/Mod.cs`; both `Patcher.cs` hosts | StarMap initializes/disposes the submod and installs/removes `GarrysTorchPatches` | (StarMap.API package) | Yes | Weld execution moved out of UI callbacks | Welds keep running with the HUD hidden without the after-GUI fallback. |

**Game assets referenced** — None (TOML preset file is mod-authored under `.unscience/`, not a game asset).

**XYZ scale enhancement (2026-09-06)**

- Weld state, live UI editing, presets, queued animation, and the public/RPC APIs now carry a
  `float3` scale. Each axis is validated to `0.05..20`; animation lerps all three components.
- Old TOML `scale = n` values and old HTTP numeric `scale` inputs are expanded to `(n,n,n)`.
  Responses and newly saved presets use explicit XYZ values.
- Ordinary parts use their existing compile-checked `Part.Scale : double3`. KittenEva requires the
  new row #25 because its separate character render path exposes only one scalar. Live-test both a
  normal multi-part vehicle and a kitten with visibly unequal axes, then unweld and confirm identity.

**Historical update-risk findings (5117 → 5261)**

The following records describe the former UI/drain implementation. The 2026-09-06 handoff above supersedes its drain call and tick-end timestamp.

- **CONFIRMED COMPILE BREAK (revs 5208–5216, vehicle-threading rewrite):**
  `garrys-torch.lib/GarrysTorchSubmod.cs:93` — `KSA.JobSystems.VehicleSolvers.Wait()` → **CS0117**.
  The rework replaced the single multi-runner scheduler with two objects:

  | OLD (≤5168) | NEW (5261) |
  |---|---|
  | `VehicleSolvers` — `JobScheduler(0.75×count)`, priority Highest | `VehicleSolver` — `JobScheduler(1)` orchestrator |
  | — | `VehicleWorkerPool` — `DynamicWorkerPool(count−1)` parallel physics-bubble islands |

  → Fixed to `JobSystems.VehicleSolver.Wait()`. **Waiting on the orchestrator alone is the complete
  drain**, which matters because this call is correctness-critical (it prevents `Collection was
  modified` inside `VehicleUpdateTask` and `SnapToLeader body/origin time mismatch`):
  `DynamicWorkerPool` exposes **no `Wait()`** and is only ever driven through scoped
  `ParallelBatch()` fork/join blocks inside `VehicleUpdateTask`/`PhysicsBubble`/
  `Universe.ApplyVehicleSolvers`, so all pool work is joined before the queued `_vehicleUpdateTask`
  completes. **The game itself drains identically** — `Universe.DeserializeSave` calls
  `JobSystems.VehicleSolver.Wait()`. Reasoning is recorded at the call site.

- **CONFIRMED COMPILE BREAK (rev 5211, `SimTime` → `UniverseTime`):**
  `garrys-torch.lib/WeldEngine.cs:119` — the local `SimTime tickEndTime =
  Universe.GetJobSimStep(...).NextTime` → **CS0246**. `SimTime` became `UniverseTime` (backed by
  `Int128` nanoseconds instead of double seconds); `SimStep.NextTime` followed the rename.
  → Fixed to `UniverseTime`. No arithmetic changed — the value is still passed straight into
  `Orbit.CreateFromStateCci`, and `.Seconds()` still returns a `double` on the new type.

- ⚠️ **Needs a live pass.** Both fixes are signature-correct and the drain is provably complete, but
  the *parallelism model* underneath changed (per-vehicle parallel batch jobs, object-pooled
  `PhysicsBubble`/`ConstraintSim`, rev 5237's stale-resource-handle crash fix). garrys-torch mutates
  vehicle state from outside the solver, so the error spam recorded in
  [`../ISSUES.md`](../ISSUES.md) must be re-checked in game.

- `Vehicle.Teleport(Orbit?, doubleQuat?, double3?)` is **signature-identical** (line shift only), as
  is `Universe.GetJobSimStep(double)`. `KittenEva` gained ladder/jump/control-mode members but lost
  none, so the `_renderable` → `_characterAvatar` → `CharacterAvatar.Core` → `CharacterCore.Scale`
  reflection chain still resolves.

**Update-risk findings (4680 -> 4750)**

- **CONFIRMED COMPILE BREAK (rev 4729, Brutal package nullability):**
  `garrys-torch.lib/GarrysTorchSubmod.cs:457` (now `:467`, null-coalesced) — `ImGui.Text($"Are you sure you want to delete\npreset '{_deleteConfirmName}'?");`.
  `_deleteConfirmName` is `string?` (declared `GarrysTorchSubmod.cs:52`) and is interpolated into
  `ImGui.Text`'s `ImString` interpolated-string handler, whose `AppendFormatted(string value, ...)`
  parameter became **non-nullable** in the rev 4729 Brutal update -> **CS8604** "possible null
  reference argument" at col 64. This is the only such site because it is the only `string?`
  interpolated into an ImGui call without a preceding null-check (`_weldError`/`_savePresetError`
  are guarded by `IsNullOrEmpty` before use). Fix is a null-coalesce / local non-null capture;
  no game-symbol change involved.
- **Behavioral watch — rev 4699 `Vehicle.IsControllable`** (`KSA/Vehicle.cs:526`,
  `public virtual bool IsControllable => _overrideIsControllable || Parts.Controls.NumModules > 0`;
  **absent in OLD** — confirmed new). The mod does not read it, but player/Flight-Computer control
  is now gated on it. Welding teleports a *source* vehicle every frame; if that source has **no
  Control Module** (debris, a separated part), it is uncontrollable by the new rule — independent of
  welding. Welding does not strip control modules, and `KittenEva.IsControllable => true`
  (`KSA/KittenEva.cs:15`), so welded capsules/kittens stay controllable. Net new risk is low but
  worth noting for user expectations (e.g. welding a control-less hull won't make it drivable).
- **No symbol deltas** otherwise: all 25 typed/reflected game members (incl. the full KittenEva
  reflection chain `_renderable` -> `_characterAvatar` -> `Core` -> `Scale`) are signature-identical
  OLD->NEW (line shifts only). rev 4708 (orbit time printout) and rev 4722 (≤2-collider memory fix)
  are internal and do not change any signature the mod uses.
- **Standing reflection fragility** (not a 4750 delta, but the highest runtime-risk surface here):
  items #21-#25 are string-keyed. None are compile-checked; a rename of `KittenEva`,
  `_renderable`, `_characterAvatar`, `CharacterAvatar.Core`, or `CharacterCore.Scale` in any future
  build silently disables avatar scaling (caught by the mod's try/catch, logged, no crash).

---

## i-feel-seen (`i-feel-seen` / `i-feel-seen.lib`)

**Purpose** — Render-distance / LOD-cull override. For user-selected ("tracked") vehicles,
two Harmony **prefixes** replace the vehicle's render-matrix and render-data computation so
the vehicle is drawn regardless of camera distance.

**Unscience integration** — `IFeelSeenSubmod : ISubmod`
(`i-feel-seen.lib/IFeelSeenSubmod.cs:8`) owns a `VehicleTracker`
(`i-feel-seen.lib/VehicleTracker.cs:13`) exposed via `IFeelSeenSubmod.Tracker`. The two
prefixes live in `IFeelSeenPatches` (`i-feel-seen.lib/IFeelSeenPatches.cs`). Standalone host
`i-feel-seen/Mod.cs:27-29` calls `Patcher.Patch(_submod.Tracker)`
(`i-feel-seen/Patcher.cs:11`). Embedded host: `unscience/Mod.cs:60` (`var iFeelSeen = new IFeelSeenSubmod()`,
added at `:75`), tracker handed to the supermod patcher at `unscience/Mod.cs:106`
(`Patcher.IFeelSeenTracker = iFeelSeen.Tracker`), patches applied at
`unscience/Patcher.cs:71` (`IFeelSeenPatches.Apply(_harmony, IFeelSeenTracker!)`).

**UI/hotkeys** — Standalone window "I Feel Seen", 400x350, toggled by **F11**
(`i-feel-seen/Mod.cs:47,73`). Content (`IFeelSeenSubmod.RenderContent:27`): filterable vehicle
combo + Add, tracked-vehicle table with per-row "SeeMe" checkbox and del.

**Persistence** — None. Tracked list is in-memory (`VehicleTracker.Tracked`), cleared on
reload (`IFeelSeenSubmod.Dispose` -> `VehicleTracker.Clear`).

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | Harmony (prefix) + Reflection (string) | `i-feel-seen.lib/IFeelSeenPatches.cs:27,30` (prefix body `:52`) | `Vehicle.GetWorldMatrix(Camera camera)` — `public float4x4?`; resolved `AccessTools.Method(typeof(Vehicle), "GetWorldMatrix")` (string), prefix `(Vehicle __instance, Camera camera, ref float4x4? __result)` | `KSA/Vehicle.cs:3662` | Yes | Same (OLD `Vehicle.cs:3501`; body identical) | **String-resolved**; method is `public`, non-virtual, single overload. Only game caller in both trees is `KittenEva.UpdateRenderData` (`KSA/KittenEva.cs:1065`). |
| 2 | Harmony (prefix) + Reflection (string) | `i-feel-seen.lib/IFeelSeenPatches.cs:28,31` (prefix body `:64`) | `Vehicle.UpdateRenderData(IViewport viewport, int inFrameIndex)` — `public virtual void`; resolved `AccessTools.Method(typeof(Vehicle), "UpdateRenderData")`, prefix `(Vehicle __instance, IViewport viewport, int inFrameIndex)` | `KSA/Vehicle.cs:3675` | Yes | **Retyped @5402** — `Viewport` → `IViewport` (OLD `Vehicle.cs:3514`); mod prefix updated. Still the single `UpdateRenderData` overload. | **String-resolved.** `virtual`; `KittenEva` overrides it (`KSA/KittenEva.cs:1062`, also `IViewport`) — see findings. Cull gate (`objectDiameterPixels < 1.0`) and the non-kitten call site (`KSA/Program.cs:4210`) unchanged; `viewport == Program.MainViewport` became `viewport.IsMain()`. |
| 3 | Direct typed API (prefix body) | `i-feel-seen.lib/IFeelSeenPatches.cs:57` | `Camera.GetPositionEgo(IPosition astronomical)` — `public double3` | `KSA/Camera.cs:231` | Yes | Same (OLD `Camera.cs:231`; body identical) | Passes `__instance` (Vehicle is `IPosition`). |
| 4 | Direct typed API (prefix body) | `i-feel-seen.lib/IFeelSeenPatches.cs:59` | `Vehicle.Body2Cce` — `public doubleQuat Body2Cce` | `KSA/Vehicle.cs:475` | Yes | Same (OLD `Vehicle.cs:469`) | Rotation for the override matrix. |
| 5 | Direct typed API (prefix body) | `i-feel-seen.lib/IFeelSeenPatches.cs:69` | `IViewport.GetCamera()` — `Camera` (interface member; implemented by `GameViewport` via `ViewportBase`) | `KSA/IViewport.cs:51` | Yes | **Retyped @5402** — was `Viewport.GetCamera()` at `KSA/Viewport.cs:366`; `Viewport.cs` no longer exists | Mod receives the `IViewport` from the prefix and calls through the interface. |
| 6 | Direct typed API (prefix body) | `i-feel-seen.lib/IFeelSeenPatches.cs:69` | `Vehicle.GetMatrixAsmb2Ego(Camera camera)` — `public double4x4` | `KSA/Vehicle.cs:1256` | Yes | Same (OLD `Vehicle.cs:1204`) | — |
| 7 | Direct typed API (prefix body) | `i-feel-seen.lib/IFeelSeenPatches.cs:70` | `Vehicle.IsEditedVehicle` — `public bool` | `KSA/Vehicle.cs:408` | Yes | Same (OLD `Vehicle.cs:402`) | Passed to `PartTree.UpdateRenderData`. |
| 8 | Direct typed API (prefix body) | `i-feel-seen.lib/IFeelSeenPatches.cs:70` | `PartTree.UpdateRenderData(ref readonly double4x4 matrixAsmb2Ego, bool isEditedVehicle, IViewport viewport, int frameIndex)` — `public void` (via `Vehicle.Parts`, `KSA/Vehicle.cs:604`) | `KSA/PartTree.cs:912` | Yes | **Retyped @5402** — `Viewport` → `IViewport` (OLD `PartTree.cs:912`); body also gained a `Parachute.UpdateLineRenderData` loop (`:938-945`) | Mod passes `in matrixAsmb2Ego` -> `ref readonly`. Re-implements the original's body to bypass the cull check; because it calls the real `PartTree.UpdateRenderData`, tracked vehicles get chute lines too. Chute canopies are drawn by the new, uncalled-by-mod `Vehicle.UpdateParachuteRenderData(IViewport)` (`Vehicle.cs:3706`, invoked without a distance cull from `Program.cs:4329,4524`). |
| 9 | Direct typed API | `i-feel-seen.lib/IFeelSeenSubmod.cs:29` + `VehicleTracker` | `VehicleProvider.GetAllVehicles()` chain + `Vehicle.Id`; tracked entries compared by reference | `KSA/Universe.cs:94` etc. | Yes | Same | Shared enumerator (see eternal-flame #12). |
| 10 | Harmony + Reflection | `i-feel-seen/Patcher.cs:15` -> `HotkeyGuard.cs:21` | `GameSettings.OnKeyAll(GlfwKeyEvent)` — `public static bool`, `nameof`-resolved | `KSA/GameSettings.cs:3301` | Yes | Same (file byte-identical) | Shared guard. |
| 11 | Lifecycle | `i-feel-seen/Mod.cs:19-69` | StarMap attrs (full set) | (StarMap.API package) | Yes | Same | Patches applied in `OnFullyLoaded` after tracker init. |

**Game assets referenced** — None.

**Update-risk findings (4680 -> 4750)**

- **No breaking deltas.** Both string-resolved patch targets and all six prefix-body members are
  signature-identical OLD->NEW (line shifts only).
- **Highest runtime risk (not a delta, but the thing to recheck every build):** the two patch
  targets are resolved by **string** (`"GetWorldMatrix"`, `"UpdateRenderData"`) via
  `AccessTools.Method`, so a rename/removal or signature change surfaces as a **runtime patch
  failure**, never a compile error. Both verified present + unchanged in 4750. (Note: the README
  mislabels these as "private/instance" — they are actually `public`; `AccessTools` finds them
  either way.)
- **Virtual-dispatch nuance for `UpdateRenderData` (pre-existing, unchanged):** it is `virtual`
  and `KittenEva` overrides it (`KSA/KittenEva.cs:62`, which calls `base.UpdateRenderData`). For a
  tracked normal `Vehicle`, the prefix fires on the direct call; for a tracked `KittenEva` the
  prefix fires only via the `base` call, after the override has already begun. `GetWorldMatrix` is
  non-virtual, so it is intercepted uniformly. This behavior is identical OLD->NEW; flagged only so
  a future change to `KittenEva`/virtual layout is evaluated here.
- **README drift (not a break):** `i-feel-seen/README.md` shows aspirational pseudocode
  (`ComputeWorldMatrix`, `ForceUpdateRenderData`, `vehicle.RenderData.Position`, a 2-arg
  `GetWorldMatrix` prefix). The real prefixes use the API rows above; those README symbols do not
  exist in the game and should not be used for triage.

---

## Cross-cutting notes (all three mods)

- **Shared chokepoints to watch first** (a change breaks multiple mods at once):
  - `VehicleProvider` chain — `Universe.CurrentSystem` (`KSA/Universe.cs:94`),
    `CelestialSystem.All` (`KSA/CelestialSystem.cs:64`),
    `LookupCollection<Astronomical>.UnsafeAsList()` (`KSA/LookupCollection.cs:210`),
    `Vehicle.Id` (`KSA/Astronomical.cs:104`), `Program.ControlledVehicle` (`KSA/Program.cs:503`).
    Drives every mod's vehicle list. All signature-identical OLD->NEW.
  - `Universe.ExecuteNextVehicleSolvers(double, SimStep)` (`KSA/Universe.cs:1834`) — patched by
    eternal-flame and central to garrys-torch's timing rationale.
  - `GameSettings.OnKeyAll` (`KSA/GameSettings.cs:3301`) — shared `HotkeyGuard`, `nameof`-resolved.
- **Embedded vs standalone Harmony:** when the unscience supermod is loaded it owns one
  `Harmony("MeowSci.Unscience")` that re-registers eternal-flame's solver prefix
  (`unscience/Patcher.cs:144-178`), garrys-torch's KittenEva matrix postfix, and i-feel-seen's render
  prefixes. Running a standalone mod *and* the supermod simultaneously would double-patch these
  targets — not a game-version risk, but a packaging note.
- **Mutation vs read:** eternal-flame and garrys-torch **write** game state
  (`Battery.Refill`/`Part.Scale`/`Vehicle.Teleport`); i-feel-seen **replaces** render computation
  via skip-original prefixes. None of these were affected by the 4680->4750 signature surface;
  the only build-induced breakage is the garrys-torch CS8604 (Brutal nullability, rev 4729).

---

## Area summary — Update-risk findings (5261 → 5348)

- ✅ **The physics-bubble rewrite does not move the eternal-flame seam.**
  `Universe.ExecuteNextVehicleSolvers(double dtPlayer, SimStep simStep)` keeps its signature and remains
  a **single overload**, so the prefix shared by eternal-flame, kiwis-marbles and kitchen-sink still attaches.
  Its **body** was substantially rewritten (revs 5331/5339): physics-bubble ownership moved entirely into
  `VehicleUpdateTask`, merge/split checks were made much less naive and moved onto the vehicle solver
  worker threads, and the method no longer walks `_physicsBubbles` itself — it now calls
  `RemoveEligibleVehicles()` / `PrepareVehicleWorkers()` / `SyncGroundClutter()` and queues
  `_vehicleUpdateTask`. The prefix still runs **before** `JobSystems.VehicleSolver.ExecuteJobs()`, so the
  refill timing is preserved.
- ✅ **garrys-torch's drain is intact.** `JobSystems.VehicleSolver` (single-runner `JobScheduler`,
  priority `Highest`) and `JobSystems.VehicleWorkerPool` (`DynamicWorkerPool`) are both unchanged;
  `GarrysTorchSubmod.cs:103` calls `KSA.JobSystems.VehicleSolver.Wait()`, which still exists.
  (Several nearby comments still say `VehicleSolvers` — comment-only staleness from the 5261 rename.)
- ✅ **eternal-flame's refill path is byte-identical.** `KSA/Battery.cs` diffs clean;
  `Battery.Refill(ref BatteryState)`, `Vehicle.RefillConsumables()` and
  `PartTree.Batteries.GetModuleAndAllMutableStatesForInitialization(...)` are all unchanged.
  The rev-5326 power rework touched circuit *construction* and *draw*, not refill.
- ✅ **garrys-torch / i-feel-seen typed surfaces unchanged.**
  `Vehicle.Teleport(Orbit?, doubleQuat?, double3?)`, `Vehicle.GetWorldMatrix(Camera)`,
  `Vehicle.UpdateRenderData(...)` and `Camera.GetPositionEgo` all keep their signatures.
  `Vehicle.IsControllable` is unchanged (`Vehicle.cs:582`).
- ✅ **Coordinate frames unchanged.** Rev 5280 extracted CCF/CCI/CCE quaternion composition into
  `KSA/CelestialFrameMath.cs` (`ComputeCcf2Cci`, `ComposeCcf2Cce`), but `Celestial.GetCcf2Cci`,
  `GetCci2Ccf`, `GetCci2Cce` and `GetCce2Cci` keep the same signatures and semantics — a pure
  extraction. garrys-torch's `GetCci2Cce` welding math is unaffected.
- ✅ **The KittenEva → `CharacterCore.Scale` chain is intact and still field-shaped**, so garrys-torch's
  `SetValue` still works. Rev 5329 turned `Module.Parent` into a property; garrys-torch does not
  reflect on it.
- ⚠️ **Ground-clutter collisions are new** (revs 5263/5274/5303/5307), default **off** behind
  *Settings → Simulation → Ground Clutter → "[Experimenta] Enable Collisions"*. Clutter is destroyed above
  25 J/kg impact energy, and kitten contact counts. garrys-torch teleports a vehicle **every frame** —
  with the setting on, that could now interact with clutter statics. Worth a live check.
- ℹ️ Re-test the [`../ISSUES.md`](../ISSUES.md) error spam for garrys-torch under the rewritten
  bubble model; the spam's shape may have changed. (The paired flexo entry is moot — flexo was removed.)

---

## Area summary — Update-risk findings (5348 → 5402)

Revisions 5349–5400 are **unlogged** (no changelog entries); the only logged commit in this span is
rev 5401 *"Fixed crash for incorrect data stride for thumbnail rendering"*. Everything below comes from
the decomp diff. Solution builds clean against 5402.

- ✅ **eternal-flame clean.** `Universe.ExecuteNextVehicleSolvers(double dtPlayer, SimStep simStep)`
  (`KSA/Universe.cs:1834`) keeps its signature, is still the only overload, and its body differs from
  5348 only by the removal of a `ClutterEcotypePhysicalData.DebugDrawColliders` block at the end of
  `SyncGroundClutter`. `Vehicle.RefillConsumables()` (`Vehicle.cs:3169`) is byte-identical;
  `KSA/Battery.cs`, `ModuleStateful.cs`, `LookupCollection.cs` and `GameSettings.cs` are byte-identical.
- ✅ **Viewport rework knock-ons are absorbed.** KSA replaced the `Viewport` class with
  `IViewport` / `IGameViewport` / `GameViewport` / `ViewportBase` / `ViewportRegistry`
  (`Viewport.Index` → `IViewport.ShaderSlot`; `Program.MainViewport` is now `IGameViewport`,
  `Program.cs:485`). For this area that retyped exactly three bindings — `Vehicle.UpdateRenderData(IViewport,int)`
  (`Vehicle.cs:3675`), `PartTree.UpdateRenderData(…, IViewport, int)` (`PartTree.cs:912`) and
  `IViewport.GetCamera()` (`IViewport.cs:51`) — all in i-feel-seen's prefix, already fixed
  (`IFeelSeenPatches.cs:64-70`). `KittenEva.UpdateRenderData` still overrides with the same
  `base.` call (`KittenEva.cs:1062`). garrys-torch and eternal-flame touch no viewport type.
- ✅ **i-feel-seen's cull bypass still lands.** `Vehicle.UpdateRenderData`'s gate is unchanged
  (`objectDiameterPixels < 1.0`), the non-kitten call site is unchanged (`Program.cs:4210`), and
  `Vehicle.GetWorldMatrix` (`Vehicle.cs:3662`) is byte-identical with `KittenEva.cs:1065` its only caller.
  `PartTree.UpdateRenderData` gained a parachute line-render loop (`PartTree.cs:938-945`), which the prefix
  inherits because it calls the real method; canopies go through the new, uncullled
  `Vehicle.UpdateParachuteRenderData(IViewport)` (`Vehicle.cs:3706`).
- ✅ **garrys-torch typed + reflected surface intact.** `JobSystems.VehicleSolver` (`JobSystems.cs:16`),
  `Vehicle.Teleport` (`Vehicle.cs:2209`, body identical bar a log line number) and `UpdatePerFrameData`
  (`:2613`, identical), `GetJobSimStep` (`Universe.cs:2322`), `Orbit.CreateFromStateCci` (`Orbit.cs:1563`)
  and every `Part` accessor still resolve. `KittenEva.Renderable` is public; the remaining
  `_characterAvatar` → `CharacterAvatar.Core` → `CharacterCore.Scale` chain is intact and
  field-shaped, and private `KittenRenderable.ModelToBodyMatrix()` remains a unique no-arg method
  (`KittenRenderable.cs:106-109`). `CharacterCore` only gained a `HeadMeshIndices` list and
  `KittenRenderable` a `HideHead` flag.
- **Parachute cloth scheduling** — `Universe.ExecuteNextClothSolvers` is called before vehicle
  scheduling (`Program.cs:2144-2145`). The 2026-09-06 Garry's Torch handoff runs before both, so the
  cloth snapshot sees the welded pose. The former after-UI teleport occurred after the cloth
  snapshot. Other mods' `ExecuteNextVehicleSolvers` prefixes still run after cloth starts.
- ⚠️ **Part structural failure + debris are new and reach welded vehicles.** `PartFailure.Detect`
  (`KSA/PartFailure.cs:47`, called from `PhysicsBubble.cs:1459`) runs for every non-kitten, non-on-rails
  vehicle and compares Bepu contact-pressure accumulators against `Part.CrashTolerancePascals`
  (`Part.cs:853`); `PartFailureEvent.Apply` sheds debris (`Vehicle.SpawnSubPartDebris`, `Vehicle.cs:1719`),
  isolates/destroys parts and can call `Universe.DestroyVehicle(vehicle, CrewDisposition.Kill)`.
  `GameSettings.cs` is byte-identical and no global off-switch symbol exists. garrys-torch teleports a
  source vehicle into contact range of its target every frame; contacts that were previously harmless can
  now destroy parts. `Vehicle.Teleport` itself gained **no** gating.
  ✅ **Guard applied** (`garrys-torch.lib/WeldEngine.cs:19-28`): `UpdateWeld` now returns `false`
  — unwelding cleanly — when either `entry.Source.IsDisposed` or `entry.Target.IsDisposed`
  (`KSA/Vehicle.cs:617`, set by `Dispose` at `:3741`), before the `entry.Source.Parent` dereference that
  would otherwise throw into the weld callback. The caller already treats `false` as "remove this weld"
  (`GarrysTorchSubmod.cs:107`). ⚠ **Still open:** live-test a two-capsule weld and watch for
  *"exceeded its crash tolerance"* log lines / debris — the guard makes the aftermath survivable but
  does not stop the game from destroying a welded craft.
- ✅ **Debris no longer fills every vehicle list.** `Vehicle.IsDebris` (`Vehicle.cs:392`) and
  `Class => IsDebris ? "Debris" : "Vehicle"` (`:423`) are new, and every vehicle picker in the suite
  enumerates through `VehicleProvider.GetAllVehicles()`, so shed fragments would have appeared in all of
  them. `GetAllVehicles` now takes `bool includeDebris = false` and filters on `IsDebris` by default
  (`ksa-abstractions.lib/VehicleProvider.cs:14-24`); `FindVehicle` passes `true` so an id held from
  before a part failure still resolves. Two callers opt back in deliberately:
  `parts-now.lib/Runtime/RuntimeModUnloadGate.cs:78` (a fail-closed gate — a debris fragment still
  holding a runtime part template must keep the mod pinned) and `graffiti.lib/DecalPicker.cs:82`
  (the pick should hit whatever is visible under the cursor).
- ℹ️ `Universe.DestroyVehicle` gained an optional `CrewDisposition` parameter and now hands cameras off
  (`HandOffCameras`, `Universe.cs:1778`); `Vehicle.Split` gained a `(Connection, IConnector, …)` overload;
  `Part.Connection.IConnector` gained `Asmb2VehicleAsmb`. None are called by these mods.
- **Needs a live pass:** the weld + part-failure interaction above (highest priority), welding a vehicle
  with deployed chutes, and the standing [`../ISSUES.md`](../ISSUES.md) garrys-torch error-spam check.

## godzilla (`godzilla` / `godzilla.lib`)

New Unscience `ISubmod` panel for Smart uniform and Basic raw XYZ vessel scaling. No new game patch:
`GarrysTorchPatches` now delegates the validated caller transpiler to shared `PhysicsFrameHook` and
registers welding as a listener. Godzilla queues Apply/Restore before those listeners. The original
weld timing invariant and managed Harmony tests remain in force.

| Integration | Game source / invariant | Owner |
|---|---|---|
| `Vehicle.Parts.Parts`, `Part.SubParts`, `Part.Scale`, `PositionParentAsmb`, `Vehicle.CenterOfMassAsmb` | `Part.cs:704,738,787,815`; full parts have assembly-space positions; subparts inherit parent matrices. Smart scales full-part offsets about captured COM and multiplies full-part authored scales only. | `godzilla.lib/VesselScaleSnapshot.cs` |
| `Part.ResetCachedPosMatrixValues`, `RefreshScale`, `UpdateBounds` | `Part.cs:1182,1192,1571`; a parent setter does **not** invalidate descendant caches. RefreshScale walks IRescale modules, connectors and subparts. `ScaleFactors(double3)` chooses the **largest axis**, so Basic XYZ physics is a uniform approximation. | snapshot refresh |
| `PartTree.RecomputeAllDerivedData`, `Vehicle.UpdateAfterPartTreeModification` | `PartTree.cs:358`; `Vehicle.cs:1881`; rebuild scale-sensitive mass, stores, attachments, seat alignment, collider compound, aero and flight-computer data without replacing keyframe module state arrays. | snapshot refresh |
| `KittenEva.Renderable`, private `KittenRenderable._characterAvatar`, typed `CharacterAvatar.Core.Scale` | `KittenRenderable.cs:12,108`; `CharacterAvatar.cs:34,211`; preserve captured avatar scalar and call shared `KittenScalePatches.SetScale` for XYZ correction. Same private matrix postfix as Garry's Torch. | snapshot character scaling |
| `JobSystems.OrbitSolvers/VehicleSolver/ClothSolvers.Wait()` | `Program.cs:2103-2105`; queued edits already run after waits; unload explicitly waits before restoring native modules/shapes. | `GodzillaSubmod.Dispose` |
| `Vehicle.IsDisposed`, live system vehicle identity, part reference topology | Skip destroyed/unloaded objects. Staging/docking restores surviving captured parts and releases ownership; do not mutate departed pieces' modules. | `GodzillaSubmod.CheckSessions` |
| StarMap / ISubmod | Thin development host + Unscience registration; only Unscience deploys. `HotkeyGuard`, shared physics hook and kitten correction installed by hosts. | `godzilla/Mod.cs`, `Patcher.cs`, `unscience/Mod.cs` |

`VehicleScaleOwnership` in abstractions uses weak vehicle keys and owner-checked acquire/release.
Godzilla sessions and Garry's Torch weld sources cannot simultaneously own scale. Godzilla may scale
a weld target; the queue runs before welding so anchors use refreshed geometry.

Validation: production snapshot/ownership linked into managed `godzilla.tests` checks Smart spacing,
non-cumulative authored scales, animation state preservation, mode changes, restore, detached parts,
kitten baseline and owner exclusion. Production shared Harmony patch also passes the Garry fixture,
including queued edits before welds, deferred reentrant work, exceptions and unloaded-system discard.
Full solution compiles against 5402. Native collisions, scale-sensitive module behavior, kitten fur,
actuation at scale, docking/staging and unload still need a live game pass.
