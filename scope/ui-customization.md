# UI / Customization Mods — Game Integration Scope

## Current verification — 5402 → 5438

Skittles and Kitchen Sink remain source-compatible with the shipped Brutal ImGui types. GameSettings.OnKeyAll, solver callbacks and PartTree.RecomputeStaticMass (NEW :802) retain their bodies. Kitchen Sink inherits the shared IVA overload correction in ksa-abstractions.lib; no separate editor clamp or UI redesign is required. Verify typing, hidden HUD and IVA display in game.

Verified against `2026.9.10.5438` using both supplied source/Content trees.
See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md).
Older catalog tables below retain their explicitly cited build/line numbers; this section records the current delta.

Permanent reference for detecting when KSA game updates break the UI/customization
mods (`skittles`, `kitchen-sink`). Every game-facing member these mods
touch is enumerated and verified against decompiled sources.

**Verified game versions**

- NEW decomp `2026.9.7.5402` root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- OLD decomp `2026.8.22.5348` root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`

Paths in the **Decomp path (NEW)** column are relative to the NEW decomp root
(namespace-foldered, e.g. `KSA/GaugeCanvas.cs`, `Brutal.ImGuiApi/ImGuiStyle.cs`); line numbers are
**@5402** unless a cell says otherwise. **Mod code** paths are relative to the repo root
`~/repos/meow-sci/unscience`.

**How these mods are hosted (both)**

Each mod ships as a thin standalone StarMap host (`<mod>/Mod.cs` + `<mod>/Patcher.cs`)
whose logic lives in a `*.lib` exposing a `MeowSci.KsaAbstractions.ISubmod`. The same
submod instances are also embedded in the **unscience** supermod
(`unscience/Mod.cs` creates `SkittlesSubmod` and `KitchenSinkSubmod`). Both hosts toggle a window with **F11** and call
`SubmodUI.BeginContentArea` / `EndContentArea` for the body.

Important hosting caveat for kitchen-sink: as of Phase 4 the supermod's `unscience/Patcher.cs`
**now calls** `IvaForceRender.Patch()` (so the IVA ctor/`AddInstance` postfixes are live in the
supermod too), but it still does **not** dispatch the vehicle-solver prefix
(`KitchenSinkSolverPatch`) to `KitchenSinkSubmod`. The Flexo "Update Physics" path is therefore
still only live in the **standalone** kitchen-sink host (`kitchen-sink/Patcher.cs`). This is a
mod-wiring detail, not a game-update risk, but it is part of the integration picture.

**Distinction — ImGui is third-party, not KSA.** skittles drives
`Brutal.ImGuiApi` (the bundled Dear ImGui wrapper), which is shipped with the game but
is *not* KSA game code. Those rows are tagged `(ImGui)` in the Risk column. The only
genuinely KSA-owned surfaces here are the editor/part/vehicle/`PartModel` types
(kitchen-sink) and `KSAColor` (button accents).

**Summary of 4680 -> 4750 risk: NO breaking deltas.** Every typed member, enum slot,
private reflected field, and patched method these three mods use is byte-for-byte
identical in signature between OLD and NEW; only source line numbers shifted. The two
changelog items that looked relevant — "Update KSA to use the latest Brutal packages"
(rev 4729) and the mesh/shader churn (MeshIndirect merge, ModelGlass/ModelEye combine,
IVA ambient-occlusion/raytracing fixes) — left the `ImGuiStyle`/`ImGuiCol` surface and
the `PartModel` public API untouched. Details per mod.

## Current area summary

- `skittles` integrates primarily with Brutal.ImGuiApi and touches KSA only for shared color accents and
  lifecycle/hotkey handling.
- `kitchen-sink` owns the editor refresh and IVA force-render surfaces; the Unscience host wires
  `IvaForceRender`, while the experimental vehicle-solver patch remains standalone-only.

**Integration-point "Kind" legend**

1. Harmony patch  2. Reflection (`AccessTools.*` / string-based field/method)
3. Direct typed API  4. Render/GPU  5. Asset  6. Lifecycle

---

## skittles

**Purpose** — Global ImGui theme manager. Mutates the shared `ImGuiStyle`
(`ImGui.GetStyle()`) so every game window/control re-themes live. Ships built-in
schemes (Game Default captured at startup, Dark/Light/Classic via ImGui presets,
"Inanimate Carbon Rod") and user `.toml` themes; wraps `ImGui.ShowStyleEditor()` for
live editing; restores the captured game default on unload. No KSA game types beyond
`KSAColor` button accents — this is almost entirely a `Brutal.ImGuiApi` (third-party)
integration.

**Unscience integration** — `SkittlesSubmod : ISubmod`
(`skittles.lib/SkittlesSubmod.cs:10`). `Initialize()` builds a `ThemeManager`, captures
the current style as "Game Default", ships the Carbon Rod preset, loads config, and
applies the saved startup theme. `RenderContent()` draws the picker; `RenderFloatingWindows()`
hosts the editor window. `Dispose()` calls `ThemeManager.RestoreDefaults()` to re-apply
the captured default. Created standalone (`skittles/Mod.cs:27`) and in the supermod
(`unscience/Mod.cs:65,90`).

**UI/hotkeys** — Standalone window "Skittles — Theme Manager", 420x360, **F11** toggle
(`skittles/Mod.cs:48,75`). Picker: Active label, filterable theme combobox
(applies on select), "Open Theme Editor" button, red "Delete" button (custom themes
only). Editor window "Skittles — Theme Editor###sk_editor", 700x800, hosts
`ImGui.ShowStyleEditor()` plus Save / Save-as-New controls (`SkittlesSubmod.cs:156-227`).

**Persistence** — Tomlyn TOML under `%USERPROFILE%\Documents\My Games\Kitten Space Agency\skittles\`
(`ThemeManager.cs:31-35`): `config.toml` (`active_theme`, `ModConfig.cs`) and
`themes\*.toml` (per-theme: `[meta]`,`[colors]` 60 named slots,`[style]` vars —
`ThemeSerializer.cs`). `inanimate-carbon-rod.toml` auto-shipped on first run
(`ThemeManager.cs:49-55`). No StarMap save hooks; no game assets.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | 3 | `skittles.lib/ThemeDefinition.cs:84,172`; `ThemeManager.cs:224` | `ImGui.GetStyle() : ImGuiStylePtr` | `Brutal.ImGuiApi/ImGui.cs:5431` | Yes | None (`:5431`) | (ImGui) core of the whole mod |
| 2 | 3 | `ThemeDefinition.cs:89-90,177-178` | `ImGuiStylePtr.Colors[i] : float4` (backing `ImGuiStyle.Colors : float4_60`) | `Brutal.ImGuiApi/ImGuiStyle.cs:188` | Yes | None | (ImGui) 60-slot inline array; index = `ImGuiCol` |
| 3 | 3 | `ThemeDefinition.cs:94-165` (capture) / `:182-238` (apply); `ThemeManager.cs:225-259` | `ImGuiStylePtr` style vars (Alpha, WindowRounding, WindowBorderHoverPadding, TabCloseButtonMinWidth*, FramePadding, ItemSpacing, AntiAliased*, … 72 members) | `Brutal.ImGuiApi/ImGuiStylePtr.cs` (72) / `ImGuiStyle.cs` | Yes | None (member-set diff empty; 72==72) | (ImGui) all read+write fields present |
| 4 | 3 | `ThemeDefinition.cs:87,175`; `ThemeSerializer.cs:12-31,55` | `ImGuiCol` enum, 60 slots `Text`(0)…`ModalWindowDimBg`(59), then `COUNT` | `Brutal.ImGuiApi/ImGuiCol.cs:5-65` | Yes | None (Text:5…ModalWindowDimBg:64, COUNT:65) | (ImGui) hard-coded `60` count matches |
| 5 | 3 | `SkittlesSubmod.cs:224` | `ImGui.ShowStyleEditor(ImGuiStylePtr ref = default)` | `Brutal.ImGuiApi/ImGui.cs:5521` | Yes | None | (ImGui) |
| 6 | 3 | `ThemeManager.cs:89,93,99` | `ImGui.StyleColorsDark/Light/Classic(ImGuiStylePtr dst = default)` | `Brutal.ImGuiApi/ImGui.cs:5552,5557,5562` | Yes | None | (ImGui) |
| 7 | 3 | `SkittlesSubmod.cs:108-109` | `ImGui.GetColorU32(ImGuiCol, float) : ImColor8` | `Brutal.ImGuiApi/ImGui.cs:5960` | Yes | None | (ImGui) feeds PushStyleColor |
| 8 | 3 | `SkittlesSubmod.cs:108-109` | `KSAColor.Xkcd.Scarlet`/`.PaleGrey : Color.Preset` | `KSA/KSAColor.cs:1561,837` (class `Xkcd`:23) | Yes | None (same lines+RGB) | **KSA type** — delete-button accent only |
| 9 | 3/6 | `skittles/Mod.cs:48` | `ImGui.IsKeyPressed(ImGuiKey.F11)` | `Brutal.ImGuiApi/ImGui.cs` | Yes | None | (ImGui) window toggle |
| 10 | 1 | `skittles/Patcher.cs:13` | `HotkeyGuard.Patch` (abstraction; patches `Brutal.ImGuiApi` IO) | `ksa-abstractions.lib/HotkeyGuard.cs` | n/a | None | shared guard, not game-typed |

**Game assets referenced** — None.

**Update-risk findings (4680 -> 4750)**

- No breaking deltas detected. `ImGuiStyle`/`ImGuiStylePtr` member set is identical
  (member-name diff empty; 72 public members both revs), `ImGuiCol` is identical
  (60 slots + `COUNT`), and all driven `ImGui.*` methods are present with unchanged
  signatures. The rev-4729 "latest Brutal packages" update did not alter the style or
  color surface.
- Standing fragility (version-independent, not a 4750 regression): `ThemeDefinition`,
  `ThemeSerializer.ColorNames`, and the `BuiltInThemes.CarbonRod()` index map all
  hard-code **60** colors and a fixed style-var list. If a future Brutal/Dear ImGui
  bump adds a color slot (raising `ImGuiCol.COUNT`) or a style var, skittles silently
  drops the new field rather than crashing — watch `ImGuiCol.cs` slot count and
  `ImGuiStyle.cs` members on every Brutal update.

---


## kitchen-sink

**Purpose** — Grab-bag of one-off editor/render fixes. Two shipped fixes plus two
experimental transform-test panels: (a) **Fix Invisible Subparts** —
`PartTree.ReinitializeDerivedValues` on the editor's part tree; (b) **Force IVA
Rendering** — `IvaForceRender` (in `ksa-abstractions.lib`) flips
`PartModelModule.Template.Internal` to false and Harmony-postfixes the `PartModel` ctor
and `PartModel.AddInstance` so interior meshes render outside IVA camera mode and in the
editor preview; (c/d) **Flexo Part/Subpart Test** — interactive `Part` transform nudging
with deferred physics resync.

**Unscience integration** — `KitchenSinkSubmod : ISubmod` (`kitchen-sink.lib/KitchenSinkLib.cs:12`),
holding a `FlexoPartTest` and `FlexoSubpartTest`. Created standalone
(`kitchen-sink/Mod.cs:30`) and in the supermod (`unscience/Mod.cs:83`).
`UpdateBeforeVehicleSolvers` is driven by a Harmony prefix on
`Universe.ExecuteNextVehicleSolvers` (`kitchen-sink/Patcher.cs:52-75`, priority First).
**Wiring (Phase 4):** the supermod now applies `IvaForceRender.Patch` (`unscience/Patcher.cs`), so
the IVA ctor/`AddInstance` postfixes (parts spawned after toggle + editor-preview fix) are live in
supermod mode as well as standalone. **Still standalone-only:** `KitchenSinkSolverPatch.Apply`
(`kitchen-sink/Patcher.cs:23-24,52-75`) — so the Flexo "Update Physics" button only fires in the
standalone kitchen-sink host. `Mod.Unload` forces `IvaForceRender.Enabled = false` to restore
templates (`kitchen-sink/Mod.cs:72`).

**UI/hotkeys** — Standalone window "Kitchen Sink", 420x300, **F11** toggle
(`kitchen-sink/Mod.cs:55,85`). Sections: "Force IVA Rendering" checkbox; "Fix Invisible
Subparts" → "Refresh Vehicle" button; "Flexo Part Test" and "Flexo Subpart Test"
(vehicle/part[/subpart] combos + Pos/Rot drag tables + Reset / Update-Physics).

**Persistence** — None. No disk I/O, no config, no StarMap save hooks. `IvaForceRender`
state is in-memory (`_mutatedTemplates`) and reset on toggle-off / unload.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | 3 | `KitchenSinkLib.cs:56` | `Program.Editor : static VehicleEditor?` | `KSA/Program.cs:226` | Yes | None (OLD:207) | null-guarded |
| 2 | 3 | `KitchenSinkLib.cs:57` | `VehicleEditor.EditingSpace : VehicleEditingSpace` (field) | `KSA/VehicleEditor.cs:545` | Yes | None (OLD:545) | |
| 3 | 3 | `KitchenSinkLib.cs:57,59` | `VehicleEditingSpace.Parts : PartTree?` (field) | `KSA/VehicleEditingSpace.cs:16` | Yes | None (OLD:16; file diff is 3 `Viewport`→`IViewport` draw signatures) | null-guarded |
| 4 | 3 | `KitchenSinkLib.cs:59` | `PartTree.States : ModuleStateList` (field) | `KSA/PartTree.cs:39` | Yes | None (OLD:39) | passed as `oldStates` |
| 5 | 3 | `KitchenSinkLib.cs:60` | `PartTree.ReinitializeDerivedValues(ModuleStateList oldStates) : void` | `KSA/PartTree.cs:308` | Yes | None (OLD:308; 0-arg overload `:302`) | `ModuleStateList.cs` byte-identical |
| 6 | 1 | `ksa-abstractions.lib/IvaForceRender.cs:42` | `PartModel..ctor(PartModelModule.Template)` **protected** (Harmony postfix via `AccessTools.Constructor`) | `KSA/PartModel.cs:384` | Yes | None (OLD:383; body identical, only ctor) | catches parts built after toggle |
| 7 | 1 | `IvaForceRender.cs:46` (lookup), `:98` (postfix sig) | `PartModel.AddInstance(PerInstanceData, IViewport, int frameIndex) : void` (Harmony postfix; captures `__0`,`__1` only) | `KSA/PartModel.cs:408` | Yes | **RETYPED @5402** `Viewport`→`IViewport` (OLD:407) — postfix `__1` updated; **NEW GATE @5402** `:410-413` early-returns unless `viewport.HasAny(ViewportOptionFlags.RenderPartModels)`; IVA/raytracing gate `:415` now per-viewport | method is **3-arg**; postfix ignores `frameIndex` (`__2`). ⚠ postfix does not yet mirror the new gate — see 5348→5402 summary |
| 8 | 3 | `IvaForceRender.cs:98,105` | `PartModel.PerInstanceData` (struct) | `KSA/PartModel.cs:332` | Yes | None (OLD:331) | postfix param `__0` |
| 9 | 3 | `IvaForceRender.cs:105` | `PartModel.ViewportData.Get(PartModel, IViewport) : ViewportData` → `.InstanceList : List<PerInstanceData>` `.Add` | `KSA/PartModel.cs:314,310` | Yes | **RETYPED @5402** param `Viewport`→`IViewport` (OLD:313/309); lookup keyed by `viewport.Id : ViewportId` | nested class `ViewportData`:308 |
| 10 | 3 | `IvaForceRender.cs:111` | `PartModel.Instances : static List<PartModel>` | `KSA/PartModel.cs:358` | Yes | None (OLD:357) | enumerated on toggle-on |
| 11 | 3 | `IvaForceRender.cs:87,89,113,116,125` | `PartModel.Template : PartModelModule.Template` (field) | `KSA/PartModel.cs:362` | Yes | None (OLD:361) | |
| 12 | 3 | `IvaForceRender.cs:87,89,101,113,116,125` | `PartModelModule.Template.Internal : bool` (field) | `KSA/PartModelModule.cs:40` | Yes | None (OLD:40) | the field flipped to force visibility |
| 13 | 3 | `IvaForceRender.cs:103` | `PartModelModule.Template.RayTracing : RaytracingMode` + `RaytracingMode.ShadowProxy` | `KSA/PartModelModule.cs:32,15` | Yes | None (OLD:32/15) | shadow-proxy skip in editor postfix |
| 14 | 3 | `IvaForceRender.cs:100` | `Program.Editor` (null check, editor-preview gate) | `KSA/Program.cs:226` | Yes | None | |
| 15 | 3 | `IvaForceRender.cs:102` | `Program.MainViewport : IGameViewport` `.Mode : CameraMode { get; }` `== CameraMode.IVA` | `KSA/Program.cs:485`; `IViewport.cs:29` (impl `ViewportBase.cs:36`); `CameraMode.cs:14` | Yes | **RETYPED @5402** — `MainViewport` was `Viewport` (OLD Program:468), `Mode` was a public field (OLD `Viewport.cs:14`); `CameraMode.cs` identical | compile-bound read; no code change |
| 16 | 1 | `kitchen-sink/Patcher.cs:56` | `Universe.ExecuteNextVehicleSolvers(double dtPlayer, SimStep simStep) : static void` (Harmony prefix; captures `dtPlayer` by name) | `KSA/Universe.cs:1834` | Yes | None (OLD:1767; body identical) | method is **2-arg**; prefix declares only `dtPlayer` — valid; single overload so `AccessTools.Method` is unambiguous |
| 17 | 3 | `FlexoPartTest.cs:184`; `FlexoSubpartTest.cs:193` (via `VehicleProvider.cs:15`) | `Universe.CurrentSystem : static CelestialSystem?` `.All : LookupCollection<Astronomical>` `.UnsafeAsList()` | `KSA/Universe.cs:94`; `CelestialSystem.cs:64` | Yes | None (OLD:94/57) | Flexo vehicle enumeration |
| 18 | 3 | `FlexoPartTest.cs:84,91`; `VehicleProvider.cs:22` | `Vehicle.Id : string` (inherited `Astronomical.Id { get; protected set; }`) | `KSA/Astronomical.cs:104` | Yes | None (OLD:104) | read-only to mod |
| 19 | 3 | `FlexoPartTest.cs:201`; `FlexoSubpartTest.cs:214` | `Vehicle.Parts : PartTree { get; set; }` → `PartTree.Parts : ReadOnlySpan<Part>` | `KSA/Vehicle.cs:604`; `PartTree.cs:95` | Yes | None (OLD:598/95; `Parts` is a property, not a field) | |
| 20 | 3 | `FlexoPartTest.cs:108,115` | `Part.Template : PartTemplate` `.Id` | `KSA/Part.cs:576` | Yes | None (OLD:568) | combo labels |
| 21 | 3 | `FlexoPartTest.cs:216,250,263` | `Part.PositionParentAsmb : double3 { get; set; }` | `KSA/Part.cs:752` | Yes | None (OLD:744; property body diffed identical) | written by Flexo |
| 22 | 3 | `FlexoPartTest.cs:217,251,264` | `Part.Asmb2ParentAsmb : doubleQuat { get; set; }` | `KSA/Part.cs:766` | Yes | None (OLD:758; property body diffed identical) | written by Flexo |
| 23 | 3 | `FlexoPartTest.cs:227` | `Part.TreeChildren : List<Part>` (field) | `KSA/Part.cs:666` | Yes | None (OLD:658) | descendant snapshot |
| 24 | 3 | `FlexoPartTest.cs:302`; `FlexoSubpartTest.cs:230` | `Part.SubParts : ReadOnlySpan<Part>` | `KSA/Part.cs:1079` | Yes | None (OLD:1052) | cache invalidation walk |
| 25 | 3 | `FlexoPartTest.cs:253,266,279,286,306` | `Part.BoundingBoxVehicleAsmb : (double3,double3) { get; set; }` + `ComputeBoundingBoxVehicleAsmb() : (double3 Min, double3 Max)` | `KSA/Part.cs:831,1464` | Yes | Property none (OLD:823). Method **body refactored @5402** (OLD:1424): now `ComputeSubPartBoundingBox(inVehicleAsmb: true)` (`:1484`) accumulating **all** `MeshViewModule`s per sub-part via `AccumulateMeshBounds` (`:1504`) instead of only `span[0]`; signature unchanged | recompute after move; bounds may grow slightly for multi-mesh subparts |
| 26 | 3 | `FlexoPartTest.cs:320`; `FlexoSubpartTest.cs:291` | `Vehicle.UpdateAfterPartTreeModification() : void` | `KSA/Vehicle.cs:1881` | Yes | None (OLD:1727; body identical) | deferred to solver prefix |
| 27 | 2 | `FlexoPartTest.cs:319`; `FlexoSubpartTest.cs:290` | `PartTree.RecomputeStaticMass() : void` **private** (HarmonyLib `Traverse.Method("RecomputeStaticMass")`) | `KSA/PartTree.cs:778` | Yes | None (OLD:778; public `RefreshStaticMass()` wrapper at `:773`) | **string-based reflection** — silently caught if renamed |
| 28 | 1 | `kitchen-sink/Patcher.cs:22` | `HotkeyGuard.Patch` (abstraction) | `ksa-abstractions.lib/HotkeyGuard.cs` | n/a | None | shared guard |

**Game assets referenced** — None (operates on already-loaded `PartModel`/`PartTree`
instances; no `Content/` paths).

**Update-risk findings (4680 -> 4750)**

- No breaking deltas detected. `KSA/PartModel.cs` and `KSA/PartModelModule.cs` are
  byte-identical between revs (same members, same line numbers), so the IVA force-render
  feature (#6-#15) is fully intact despite the rev-4693/4745-era mesh/shader churn —
  that churn (MeshIndirect merge, ModelGlass/ModelEye combine, IVA AO/raytracing fixes)
  touched shaders and GPU paths, not the `PartModel` C# API or `Template.Internal` gate.
- `PartTree.ReinitializeDerivedValues(ModuleStateList)` and the private
  `RecomputeStaticMass()` are unchanged; `ModuleStateList` still exists. Fix-Invisible-
  Subparts (#4-#5) and the Flexo physics resync (#26-#27) are safe.
- `Universe.ExecuteNextVehicleSolvers` is still the single 2-arg
  `(double dtPlayer, SimStep simStep)` overload in both revs — the by-name `dtPlayer`
  prefix and the name-only `AccessTools.Method` resolution remain unambiguous.
- Watch items (version-independent): the protected `PartModel..ctor` resolved by
  parameter-type array (#6) breaks if a `PartModelModule.Template` overload is added or
  the param type changes; `Traverse.Method("RecomputeStaticMass")` (#27) is the only
  string-named member in this mod and fails *silently* (caught, logged) if renamed.
```

---
