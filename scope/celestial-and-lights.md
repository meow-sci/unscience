# Scope: Celestial Welding & Lights (kiwis-marbles, zippo)

Permanent reference cataloging how two unscience mods integrate with the KSA game,
for detecting when a game update breaks them.

**Versions compared**
- NEW = `2026.9.7.5402` — decomp root `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- OLD = `2026.8.22.5348` — decomp root `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`
- Decomp paths below are relative to `<root>/KSA/` unless noted. Line numbers are NEW (5402); "OLD" = 5348.
  Verified by grep/diff of both trees.

**Shared integration (both mods)** — each mod's `Patcher.cs` calls
`HotkeyGuard.Patch/Unpatch` (`ksa-abstractions.lib/HotkeyGuard.cs`). HotkeyGuard Harmony-patches
`GameSettings.OnKeyAll(GlfwKeyEvent) : bool` (NEW `GameSettings.cs:3301`, prefix with `ref bool __result`;
`GameSettings.cs` is byte-identical 5348↔5402).
Both call `_harmony.PatchAll(...)` but define **no** `[HarmonyPatch]` methods of their own, so PatchAll
is a no-op aside from HotkeyGuard. Lifecycle is StarMap attributes (`[StarMapMod]`,
`[StarMapImmediateLoad]`, `[StarMapAllModsLoaded]`, `[StarMapBeforeGui]`, `[StarMapAfterGui]`,
`[StarMapUnload]`) on `Mod.cs`, plus `MeowSci.KsaAbstractions.ISubmod` implemented by each `*Submod`.
None of the three persists any state.

---

## kiwis-marbles

**Purpose** — "Celestial welding": teleports a `Celestial` (planet/moon = *source*) every frame to maintain a
user-set CCI offset relative to any `IOrbiter` (*target*; celestial or vehicle). Re-parents the source via
`SetOrbit` when the target sits under a different parent. Multiple welds are processed in dependency order
(Kahn topological sort) so weld chains (Moon→Earth→Mars) resolve correctly.

**Unscience integration** — Standalone StarMap mod hosting `KiwisMarblesSubmod : ISubmod` (also bundled in
the unscience toolbox). Weld application is driven by `KiwisMarblesPatches`, a `Priority.First` Harmony prefix
on `Universe.ExecuteNextVehicleSolvers` → `KiwisMarblesSubmod.Instance.UpdateBeforeVehicleSolvers()`, which
calls `CelestialWeldEngine.UpdateWeld` per weld and applies deferred unweld restores. `ISubmod.Update(dt)`
(`[StarMapBeforeGui]`) is a deliberate no-op — see *Timing* below. Discovers bodies through
`CelestialProvider` (abstractions). Stateless math in `CelestialWeldEngine`.

**Timing (why the solver prefix)** — Since 2026.8.x `Celestial`s are propagated by `CelestialUpdateTask`
jobs on `JobSystems.OrbitSolvers` worker threads: `Universe.ExecuteNextOrbitSolvers` queues one per body
(snapshots `Celestial.Orbit`, computes `GetStateVectorsAt(simStep.NextTime)`); next frame
`Program.PrepareFrame` does `OrbitSolvers.Wait()` → `Universe.ApplyOrbitSolvers()` (`Orbit.UpdatePosition`) →
`Universe.ApplyVehicleSolvers()` (ends in `CelestialSystem.UpdatePerFrameData()`) → `ExecuteNextVehicleSolvers`
→ `ExecuteNextOrbitSolvers`. Mutating `Orbit` from a render-loop hook races the worker and is overwritten by
the staged result (the pre-fix symptom: welds had no visible effect). The prefix runs in the only safe window:
main thread, solvers drained, results applied, next step not yet queued, all target positions current.

**UI/hotkeys** — **F9** toggles the window (`Mod.cs:51`). ImGui (Brutal.ImGuiApi): filterable Source/Target
combos, `DragFloat3` offset + unit combo (m/km/Mm/Gm), per-weld live offset editor with a surface/lat-lon mode,
red "Unweld" button. Renders inside the Unscience toolbox via `ISubmod.RenderContent`.

**Persistence** — None. `CelestialWeldEntry.OriginalOrbit` is captured in memory at weld time to restore the
body on unweld; welds are lost on reload (README §Notes).

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | Direct typed | `CelestialWeldEngine.ApplyOrbit` | `Celestial.SetOrbit(Orbit newOrbit)` | `Celestial.cs:153` | Yes | Same | Bare `Orbit = newOrbit`. Does **not** touch `Children` (never did — earlier "auto-reparents" note was wrong); engine re-parents explicitly (#2b). |
| 2 | Direct typed | `CelestialWeldEngine.ApplyOrbit` | `IParentBody.UpdatePerFrameDataTree() : void` (default interface method) | `IParentBody.cs:110` | Yes | Same | Refreshes cached CCI/CCE/ECL data for the body + its subtree after the swap (replaces the old bare `UpdatePerFrameData()` call). |
| 2b | Direct typed | `CelestialWeldEngine.Reparent` | `IParentBody.Children : List<IOrbiter>`; `Orbit.Parent : IParentBody`; `Celestial.Parent => Orbit.Parent` | `IParentBody.cs:27`; `Orbit.cs:1186`; `Celestial.cs:73` | Yes | Same | Cross-parent weld/restore moves the body between old/new parent lists (drives `UpdatePerFrameDataTree` order + orbit-tree UI). |
| 2c | Harmony prefix (`Priority.First`) | `KiwisMarblesPatches.cs:24-32` (`AccessTools.Method` by name) | `Universe.ExecuteNextVehicleSolvers(double dtPlayer, SimStep simStep) : static void` | `Universe.cs:1834` | Yes | Body identical (OLD `:1767`) | Sim-step driver for all weld work. Shared keystone with eternal-flame/kitchen-sink; single overload so by-name lookup is safe. Sequence dependency: must stay *after* `ApplyOrbitSolvers`/`ApplyVehicleSolvers` and *before* `ExecuteNextOrbitSolvers` in `Program.PrepareFrame` (`Program.cs:2103-2146`). 5402 inserted the parachute cloth solvers into the same sequence (`ClothSolvers.Wait()`/`ApplyClothSolvers()` before, `ExecuteNextClothSolvers` immediately before this call at `:2144`) — the weld window is unchanged. |
| 3 | Direct typed | `CelestialWeldEngine.cs:42-48` | `Orbit.CreateFromStateCci(IParentBody, UniverseTime, double3, double3, byte4) : Orbit` (static) | `Orbit.cs:1563` | Yes | **Identical sig** (OLD `:1563`) | 5-arg state-vector → orbit. Arg types must stay (IParentBody/UniverseTime/double3/double3/byte4). `UniverseTime` replaced `SimTime` at rev 5211. |
| 4 | Direct typed | `CelestialWeldEngine.cs:47` | `Celestial.OrbitColor : byte4 { get; protected set; }` (via IOrbiter) | `Celestial.cs:77`; `IOrbiter.cs:24` | Yes | Same (OLD `:77`) | Passed as orbit line color to #3. |
| 5 | Direct typed | `CelestialWeldEngine.cs:32,37` | `IOrbiter.Parent : IParentBody { get; }` (= `Orbit.Parent`) | `IOrbiter.cs:18` | Yes | Same | Null-checked before weld. |
| 6 | Direct typed | `CelestialWeldEngine.cs:32`; `KiwisMarblesSubmod.cs:483` | `IOrbiter.Orbit : Orbit { get; }` / `Celestial.Orbit { get; set; }` | `IOrbiter.cs:16`; `Celestial.cs:71` | Yes | Same (OLD `:71`) | Source `.Orbit` saved for restore. |
| 7 | Direct typed | `CelestialWeldEngine.cs:35` | `IOrbiter.GetPositionCci() : double3` | `IOrbiter.cs:48` | Yes | Same | Target CCI position each frame. |
| 8 | Direct typed | `CelestialWeldEngine.cs:36` | `IOrbiter.GetVelocityCci() : double3` | `IOrbiter.cs:62` | Yes | Same | Target CCI velocity each frame. |
| 9 | Direct typed | `KiwisMarblesSubmod.cs:196-197,369,382` | `Celestial.MeanRadius : double` (override) | `Celestial.cs:91` | Yes | Same (OLD `:91`) | Surface-placement helper only. |
| 10 | Direct typed | `KiwisMarblesSubmod.cs:113,114` (via `CelestialProvider`) | `Universe.CurrentSystem : CelestialSystem? { get; }` → `.All : LookupCollection<Astronomical>` → `.UnsafeAsList()` | `Universe.cs:94`; `CelestialSystem.cs:64`; `LookupCollection.cs:210` | Yes | Same (`All` OLD `:57`) | Source list `OfType<Celestial>()`, target list `OfType<IOrbiter>()`. |
| 11 | Direct typed | `CelestialWeldEngine.cs:44` (via `SimTimeProvider`) | `Universe.GetElapsedTime() : UniverseTime` (static) | `Universe.cs:2114` | Yes | Same (OLD `:2060`) | State time for #3. (Was `GetElapsedSimTime() : SimTime` before rev 5211.) |
| 12 | Cast/type | `CelestialWeldEngine.cs:119`; `KiwisMarblesSubmod.cs:194,257` | `(IOrbiter)Celestial` cast; `IParentBody` as parent type | `IOrbiter.cs`, `IParentBody.cs` | Yes | Same | Celestial implements IOrbiter (topo-sort edge test). |
| 13 | Lifecycle/Harmony | `Patcher.cs:22,39` | `HotkeyGuard` → `GameSettings.OnKeyAll(GlfwKeyEvent) : bool` | `GameSettings.cs:3301` | Yes | Same | Shared guard; PatchAll defines no own patches. |

**Game assets referenced** — None. Bodies are discovered live from `Universe.CurrentSystem`; no model/texture/path lookups.

**Update-risk findings (4680→4750)** — No breaking deltas detected. `Celestial`, `IOrbiter`, `IParentBody`,
`Orbit.CreateFromStateCci`, `Universe`/`CelestialSystem`/`LookupCollection` members are byte-for-byte identical
across versions (only line numbers shifted). All access is typed (no string reflection), so the compile against
4750 DLLs (already green) fully covers this mod's surface.

---

## zippo

**Purpose** — Select a vehicle and one of its light parts, then control intensity/color in real time, toggle
on/off, queue single-step color+intensity animations with easing, or run repeating Disco recipes on one
light or every light on a vehicle. Disco independently cycles color, moving-light actuation and spotlight
beam spread. `ZippoSubmod` also exposes public methods for reuse by other mods.

**Unscience integration** — `ZippoSubmod : ISubmod` (with a static `Instance` public API). `Update(dt)` drives
`LightAnimationManager` and every `DiscoLight` each frame. Ordinary light access is centralized in the
stateless `LightController`; Disco uses typed runtime modules so color and spread remain per-instance.
Vehicles come from `VehicleProvider`, part-tree walking from `PartHelpers`, and XKCD colors from
`XkcdColorHelper` (all abstractions). Starting either animation mode stops the other for its exact target.

**UI/hotkeys** — **F11** toggles the window (`Mod.cs:47`). Vehicle/light-part filterable combos, intensity
`DragFloat`, "Default/preset/(Custom)" color combo, `ColorEdit4` picker, animation builder (start/end XKCD color
combos + intensity/duration/easing/power) with a progress bar, a Disco recipe editor and active-effect
inspectors, and a Debug "Dump Parts" button.

**Persistence** — None. `_originalColors`, ordinary animation queues, authored Disco settings and active
Disco records are session-only.

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | **Reflection (string type)** | `LightController.cs:39` | type name `"KSA.LightModule+TemplateData"` (nested) | `LightModule.cs:12` (`[XmlType("Light")] class TemplateData`) | Yes | Same | **High runtime risk**: hard-coded full name. Rename/move of nested type silently yields zero light parts. |
| 2 | **Reflection (string field)** | `LightController.cs:33` | `PartTemplate.Components : List<ModuleBase.TemplateDataBase>` (field) | `PartTemplate.cs:113` | Yes | Same (OLD `:107`; shifted by new `CrashTolerance`/`SubPartGroups` fields) | Field name `"Components"` must persist. |
| 3 | **Reflection (string field)** | `LightController.cs:50,71` | `LightModule.TemplateData.Intensity : FloatReference` (field) → `FloatReference.Value : float` | `LightModule.cs:30`; `FloatReference.cs:9` | Yes | Same | Intensity read/write **works**. Field names `"Intensity"`/`"Value"` must persist. |
| 4 | Reflection (string field) — **FIXED (Phase 4)** | `LightController.cs:59,80` | reads/writes field `"ColorRgb"` on `TemplateData` | `LightModule.cs:33` (`ColorRgbReference ColorRgb`) | Yes | Same | Was `"Color"` (the `[XmlElement("Color")]` XML name, not the C# field) ⇒ `GetField`→null ⇒ color was a silent no-op in both 4680 and 4750. Now `"ColorRgb"`; the C# field name must persist. |
| 5 | Reflection (string field/method) + **typed enum** | `LightController.cs:61-63,82-89` | `ColorRgbReference.R/G/B : float` + `OnDataLoad(Mod) : void`; write side clears `IndexedColor` to `KSA.IndexedColor.Invalid` | `ColorRgbReference.cs:10,13,16,19,35` | Yes | Same | Now reachable (post-#4). `OnDataLoad` re-derives R/G/B from `IndexedColor` unless it is `Invalid`, so `WriteColor` sets `IndexedColor = KSA.IndexedColor.Invalid` (typed — **compile-checked**, breaks loudly) before `OnDataLoad(null)`. |
| 6 | Direct typed | `ZippoSubmod.cs:152,441,465` | `Part.LightSwitch : PowerConsumer?` (field) | `Part.cs:686` | Yes | Same (OLD `:678`) | On/off path. Consumer side changed in 5402: `LightModule.IsActive`/`PartModelModule.UpdateRenderData` now read the new `Part.IsLightSwitchedOff()` (`Part.cs:1357-1369` = `!LightIsActive \|\| !IsSwitchedOn()`, plus a `lightSwitch.Parent.Tree != Tree ⇒ not off` precondition). `LightIsActive` is still the first term, so the write still works. |
| 7 | Direct typed | `ZippoSubmod.cs:152,441,465` | `Part.FullPart : Part { get; }` | `Part.cs:1123` | Yes | Same (OLD `:1056`) | `.FullPart.LightSwitch` fallback. |
| 8 | Direct typed | `ZippoSubmod.cs:161,442,467` | `PowerConsumer.LightIsActive : bool` (field) | `PowerConsumer.cs:30` | Yes | Same | On/off toggle. Electrical refactor (4681) didn't touch this field; 5402 added `PowerConsumer.IsSwitchedOn()` (`:50-54`, bounds-checked `StatesIdx`) next to it. |
| 9 | Direct typed | `LightController.cs:95,98,102,106` | `Part.Template : PartTemplate` (field) | `Part.cs:576` | Yes | Same (OLD `:568`) | Feeds reflection in #1–#5. |
| 10 | Direct typed | `ZippoSubmod.cs:405`; `LightController.cs:102` (via `PartHelpers`) | `Vehicle.Parts : PartTree` → `PartTree.Parts : ReadOnlySpan<Part>` | `Vehicle.cs:604`; `PartTree.cs:95` | Yes | Same (OLD `:598`; `:95`) | Part enumeration root. |
| 11 | Direct typed | `LightController.cs:133-134` (recursion); `PartHelpers.cs` | `Part.SubParts : ReadOnlySpan<Part>` | `Part.cs:1079` | Yes | Same (OLD `:1052`) | Recursive light search. |
| 12 | Direct typed | `ZippoSubmod.cs:444-445` (combo labels) | `Part.Id : string { get; init; }`, `Part.DisplayName : string { get; init; }` | `Part.cs:698,700` | Yes | Same (OLD `:690,692`) | Display/keys. 5402 initialises `DisplayName` from `Template.DisplayName` when it differs from `Template.Id` (`Part.cs:1391`; was `= Id`) — labels may change, keys (`Id`) don't. |
| 13 | Reflection (palette) | `ZippoSubmod.cs:253,284` (via `XkcdColorHelper.GetAll`) | `KSAColor.Xkcd` static props → `Color.Preset` | `KSAColor.cs:23` | Yes | Same | Reflects all `Xkcd` static color props; cast `(Color.Preset)`. Rename of `Xkcd`/prop-type change would empty the combo. |
| 14 | Direct typed | `LightController.cs:20-27` | hard-coded preset float3 (Marine/HotPink/RadioactiveGreen/BabyPurple) | n/a (constants from `KSAColor.cs`) | n/a | n/a | Hard-coded RGB; cosmetic only, no runtime dependency. |
| 15 | Lifecycle/Harmony | `Patcher.cs:19,31` | `HotkeyGuard` → `GameSettings.OnKeyAll` | `GameSettings.cs:3301` | Yes | Same | Shared. |

### Zippo Disco extension (backported 2026-09-06)

| # | Kind | Mod code | Game target (KSA 2026.9.7.5402) | Decomp path | Ownership / risk |
|---|---|---|---|---|---|
| D1 | Direct typed | `DiscoLight.cs` | `Part.Modules.Get<LightModule>()`; writable `LightModule.Template` | `KSA/Part.cs:680`; `KSA/ModuleList.cs`; `KSA/LightModule.cs:62` | Each live effect installs a complete module-local template and restores the original only when the module still points to its owned copy. A competing external replacement is preserved. |
| D2 | Direct typed | `DiscoLight.cs` | `LightModule.TemplateData.{Id,Type,Transform,Range,Intensity,ColorRgb,InnerAngle,OuterAngle,RayTracing,DisableInIva}` | `KSA/LightModule.cs:12-45` | Every field is copied. Color and spotlight angle references become private only for enabled channels; point lights skip cone updates. Field additions to the game require review so the copy remains complete. |
| D3 | Direct typed | `DiscoLight.cs` | `ColorRgbReference(float3)`, `R/G/B/IndexedColor`, `OnDataLoad(Mod)`; `FloatReference(float)`, `Value` | `KSA/ColorRgbReference.cs`; `KSA/FloatReference.cs` | Per-instance RGB refresh and degree-to-radian half-angle interpolation. No shared part-template mutation or GPU resource is introduced. |
| D4 | Direct typed | `ZippoSubmod.Disco.cs`; `DiscoLight.cs` | `Part.FullPart.Modules.Get<KeyframeAnimationModule>()`; `Shared.{Duration,PartLookup}`; `TimeGoal` | `KSA/KeyframeAnimationModule.cs:74,76`; `KSA/KeyframeAnimationData.cs:223,225` | Drivers are selected only when their animation targets the light subpart ID. One Disco record owns a shared assembly driver; later starts release the earlier owner. Goals restore only if the last Zippo-written value is still current. KSA's mirrored-part fan-out still needs a live check. |
| D5 | Direct typed | `DiscoLight.cs`; `ZippoSubmod.Disco.cs` | `Part.LightSwitch`, `Part.FullPart.LightSwitch`, `PowerConsumer.LightIsActive` | `KSA/Part.cs:686,1123`; `KSA/PowerConsumer.cs:30` | Start leaves the switch unchanged. The active inspector may toggle it; stop restores the captured value only if Zippo still owns the last write. |
| D6 | Direct typed | `ZippoSubmod.cs`; `ZippoSubmod.Disco.cs` | `Part.InstanceId`; live `Vehicle`/`Part` reference identity | `KSA/Part.cs:574` | Ordinary queues use runtime-unique instance keys; active Disco records use reference identity and labels include the instance ID. Each update scans vehicles including debris; disappeared exact references are disposed rather than retargeted. |
| D7 | StarMap lifecycle | `zippo/Mod.cs`; `unscience/Mod.cs` | `[StarMapBeforeGui]` → `Program.OnDrawUiFrame(double)` | `KSA/Program.cs:2639` | Standalone Zippo now calls `ZippoSubmod.Update(dt)` from its hook; Unscience calls the same method through `UpdateSubmods`. This is essential for ordinary queues and Disco. Unscience also uses `HiddenUiFrameHook` while F2 hides the HUD; standalone playback follows StarMap and pauses while that game UI hook is skipped. |

`DiscoTiming` samples repeating hold/transition phases directly from elapsed time, so skipped frames do
not require a catch-up loop. Every active light receives independent, stable color/actuation/spread phase
offsets within the recipe's configurable jitter window; zero jitter deliberately restores lockstep timing.
Random hues are stable per step and independently seeded per active light.
Pause freezes recipe time; it does not stop a mechanism already moving toward its last goal. Starting
Disco cancels the ordinary queue for that light; every ordinary UI write and queue action stops Disco
first. `Dispose()` stops all effects and restores owned state for both the standalone mod and Unscience.

**Disco assets/Harmony** — None. No Harmony target, shader, render-pass, byte layout, or game asset is added.
Native color isolation, actuator selection/ownership, spotlight cone rendering, pause, external-template
replacement, craft destruction/debris handoff, and unload restoration require an in-game smoke pass.

**Game assets referenced** — None.

**Update-risk findings (4680→4750)**
- **No breaking deltas from the update.** `LightModule.TemplateData`, `FloatReference`, `ColorRgbReference`,
  `Part`, `PowerConsumer`, `Vehicle`/`PartTree`, `KSAColor.Xkcd` are identical across 4680 and 4750.
- **Color get/set — FIXED (Phase 4).** Zippo previously reflected the field `"Color"`, but the C# field is
  `ColorRgb` (`[XmlElement("Color")]` is only the XML element name) ⇒ `GetField`→null ⇒ color was a silent
  no-op in both 4680 and 4750 (intensity and on/off always worked). `LightController` now reads/writes
  `"ColorRgb"`, and `WriteColor` additionally clears `IndexedColor` (`KSA.IndexedColor.Invalid`) so
  `ColorRgbReference.OnDataLoad` keeps the written RGB instead of re-deriving it from a named/indexed color.
- **Watch (string reflection surface):** items #1–#5 and #13 are the only update-fragile points. A future
  rename of `LightModule.TemplateData`, its `Components`/`Intensity`/`ColorRgb` fields, `FloatReference.Value`,
  `ColorRgbReference.{R,G,B,OnDataLoad}`, or `KSAColor.Xkcd` would fail silently at runtime (no compile error).
  Zippo now also has **one typed** game dependency — `KSA.IndexedColor.Invalid` in `WriteColor` — which would
  fail at **compile** (not silently) if that enum is renamed/moved.
- **Electrical refactor (4681):** `LightModule.UpdateRenderData` now also gates on the part's PowerConsumer
  state, but the on/off switch remains `PowerConsumer.LightIsActive` (unchanged) — no zippo impact.

## Current area summary

- `kiwis-marbles` remains fully typed apart from shared Harmony method lookup; its solver-phase ordering
  is the principal behavioral invariant.
- `zippo` owns the light reflection surface (`LightModule.TemplateData`, `Intensity`, `ColorRgb`,
  `ColorRgbReference`, and XKCD palette discovery) plus Disco's per-instance template and actuator ownership.
- Neither mod references a game asset by hard-coded id in this area.

---
