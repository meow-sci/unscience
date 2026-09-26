# UI / Customization Mods — Game Integration Scope

## KSA 5482 (5438 → 5482) verification

Verified 2026-09-25 against `2026.9.22.5482` (revs 5439–5481), diffed from `2026.9.10.5438`.
Static/managed only: the whole solution builds and `kitchen-sink.tests` passes; no native KSA run was
possible. **No skittles or kitchen-sink code change was needed**; Kitchen Sink inherits the shared
`IvaForceRender` fix in ksa-abstractions.lib. Evidence: [KSA_5482_UPGRADE](../plans/KSA_5482_UPGRADE.md).

- ✅ **G-load protection target intact.** Private static
  `PhysicsBubble.DetectStructuralFailure(VehicleUpdateState)` moved OLD :873 → NEW `PhysicsBubble.cs:958`
  with a byte-identical body and still exactly one `StructuralLoad.GLoadFraction` read (`:975`). Its
  caller `FullPhysicsEndFrame` (`:1595`, call `:1622`) is identical and is reached via `EndStepFrame`.
  `StructuralLoad.cs` and `VehicleStructuralLimits.cs` are byte-identical; `VehicleUpdateState.ReadOnlyVehicle`
  (`:14`), `Vehicle.IsDisposed` (`Vehicle.cs:618`) and `Astronomical.Id` (`:104`) are unchanged. Rev
  5452 physics islands run `ContactIsland` batch jobs on `VehicleWorkerPool` (the old `BubbleStepJob`
  was already parallel), so the concurrent registry remains the right structure.
- ✅ **Force IVA Rendering (Kitchen Sink toggle) — editor reveal fixed in ksa-abstractions.** Since
  rev 5456 the static raster path (`PartTreeRenderData.Compose`, `:1300`) applies the
  `(!Template.Internal || IVA)` gate itself and never calls the private `PartModel.AddInstance` sink,
  so the 5438 sink postfix (rows 7–10 below) went silently dead and internal meshes vanished from the
  editor. `IvaForceRender` now reveals cached internal, non-`ShadowProxy` templates for the duration
  of each editor `Compose` call (prefix + finalizer). The flight `Enabled` toggle (template mutation)
  is unchanged. Side effect: internal meshes no longer appear in editor thumbnails (the old postfix
  also reached the thumbnail path). Current rows: [00-architecture → IvaForceRender](00-architecture-and-abstractions.md#ivaforcerendercs).
- ℹ️ **Editor refresh (rows 1–5) unchanged, now lazy underneath.** `Program.Editor` (`Program.cs:227`),
  `VehicleEditor.EditingSpace` (`:545`), `VehicleEditingSpace.Parts` (`:16`), `PartTree.States`
  (`PartTree.cs:64`) and `PartTree.ReinitializeDerivedValues(ModuleStateList)` (`:425`, same signature
  and body). Its trailing `RecomputeAllDerivedData()` now only marks derived data dirty (rev 5464), so
  recomputation happens on first read or at the next `PrepareFrame` flush (`Program.cs:2209-2210`).
  The button still works; derived values can settle a frame later.
- ✅ **skittles unchanged.** The Brutal decomp folders (`Brutal.ImGuiApi`, `.Abstractions`,
  `Brutal.GlfwApi`, `Brutal.Concurrency.Jobs`) are identical and the Brutal DLLs differ only by
  rebuild hash; `KSAColor.cs` is byte-identical. Only `KSA.dll` and `Planet.Render.Core.dll` changed.
- **Native acceptance pending:** G-load protection under island-parallel crashes; Force IVA in the
  editor (internal meshes with dents, no duplicates in IVA) and in flight; Kitchen Sink "Refresh
  Vehicle" in the editor; skittles theme apply/restore.

## Verification — 5402 → 5438 (historical)

Skittles and Kitchen Sink remain source-compatible with the shipped Brutal ImGui types.
Kitchen Sink's local Flexo removal retires its solver callback and RecomputeStaticMass reflection.
Its new G-load target, `PhysicsBubble.DetectStructuralFailure` (OLD :782, NEW :873), and
`FullPhysicsEndFrame` caller are source-identical; `StructuralLoad` and `VehicleStructuralLimits`
are unchanged. The registry and version-1 save records require no migration. Kitchen Sink
inherits the shared IVA common-overload/dent-list correction in ksa-abstractions.lib. Verify
typing, hidden HUD, IVA display, G-load/pressure isolation and scene replay in game under the
updated segmented physics scheduler. See [reconciliation](../plans/KSA_5438_RECONCILIATION.md).

Verified against `2026.9.10.5438` using both supplied source/Content trees.
See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md).
Older catalog tables below retain their explicitly cited build/line numbers; this section records the current delta.

Permanent reference for detecting when KSA game updates break the UI/customization
mods (`skittles`, `kitchen-sink`). Every game-facing member these mods
touch is enumerated and verified against decompiled sources.

**Verified game versions**

- NEW decomp `2026.9.22.5482` root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- OLD decomp `2026.9.10.5438` root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`

Paths in the **Decomp path (NEW)** column are relative to the NEW decomp root
(namespace-foldered, e.g. `KSA/GaugeCanvas.cs`, `Brutal.ImGuiApi/ImGuiStyle.cs`); table line numbers
are **@5402** unless a cell or heading says otherwise (the G-load table is @5482; 5482 lines for the
editor rows are in the section above). **Mod code** paths are relative to the repo root
`~/repos/meow-sci/unscience`.

**How these mods are hosted (both)**

Each mod ships as a thin standalone StarMap host (`<mod>/Mod.cs` + `<mod>/Patcher.cs`)
whose logic lives in a `*.lib` exposing a `MeowSci.KsaAbstractions.ISubmod`. The same
submod instances are also embedded in the **unscience** supermod
(`unscience/Mod.cs` creates `SkittlesSubmod` and `KitchenSinkSubmod`). Both hosts toggle a window with **F11** and call
`SubmodUI.BeginContentArea` / `EndContentArea` for the body.

Kitchen Sink installs both IvaForceRender and GLoadProtectionPatches in Unscience and the standalone development host. The defunct Flexo solver hook and diagnostic panels were removed on 2026-09-13.

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
  `IvaForceRender` and selected-vehicle G-load protection. The Flexo experiments are removed.

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

**Purpose:** editor refresh, Force IVA Rendering, and selected-vehicle G-load invincibility.
The defunct Flexo Part/Subpart Test panels, transform/bounds/mass mutations, and standalone
`KitchenSinkSolverPatch` / `Universe.ExecuteNextVehicleSolvers` hook are removed.

**Hosting:** `KitchenSinkSubmod : ISubmod` is shared by Unscience and the standalone development
host. Both patchers install/remove `IvaForceRender` and `GLoadProtectionPatches`. The standalone
F11 window defaults to 540x440; Unscience uses its existing toolbox panel.

**UI/runtime:** case-insensitive filtered vehicle dropdown, Add G-load Invincible button,
and a table of all protected vehicles with per-row Delete. Duplicate adds are disabled.
`GLoadProtection` uses a concurrent dictionary with reference identity for safe worker reads.
No same-name replacement inherits protection. Updates prune missing/disposed targets; selection
is an object reference. Protection does not depend on UI visibility.

**Persistence:** the existing version-1 `kitchen-sink` boolean record still stores IVA visibility.
A separate version-1 `kitchen-sink-g-load` string-array record stores stable vehicle IDs. Its reset
clears the registry/picker before reconstruction, then replay uses VehicleProvider.FindVehicle
for exact, unambiguous rebinding. Missing/disposed targets warn and retain the original record;
patch unavailability fails restoration visibly. Legacy/vanilla saves without the new record leave
protection empty. Dispose/unpatch clears live references. No new native lifecycle hook.

**Historical editor/IVA integration points** (game baseline 5402; the 5438 common-overload and
paired dent-list correction above superseded rows 7–9, and at 5482 rows 7–10 were retired in favour
of a `PartTreeRenderData.Compose` prefix/finalizer — the current IvaForceRender table is in
[00-architecture](00-architecture-and-abstractions.md#ivaforcerendercs); rows 1–5 remain current):

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | 3 | `KitchenSinkLib.cs (editor refresh)` | `Program.Editor : static VehicleEditor?` | `KSA/Program.cs:226` | Yes | None (OLD:207) | null-guarded |
| 2 | 3 | `KitchenSinkLib.cs (editor refresh)` | `VehicleEditor.EditingSpace : VehicleEditingSpace` (field) | `KSA/VehicleEditor.cs:545` | Yes | None (OLD:545) | |
| 3 | 3 | `KitchenSinkLib.cs (editor refresh)` | `VehicleEditingSpace.Parts : PartTree?` (field) | `KSA/VehicleEditingSpace.cs:16` | Yes | None (OLD:16; file diff is 3 `Viewport`→`IViewport` draw signatures) | null-guarded |
| 4 | 3 | `KitchenSinkLib.cs (editor refresh)` | `PartTree.States : ModuleStateList` (field) | `KSA/PartTree.cs:39` | Yes | None (OLD:39) | passed as `oldStates` |
| 5 | 3 | `KitchenSinkLib.cs (editor refresh)` | `PartTree.ReinitializeDerivedValues(ModuleStateList oldStates) : void` | `KSA/PartTree.cs:308` | Yes | None (OLD:308; 0-arg overload `:302`) | `ModuleStateList.cs` byte-identical |
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

**G-load integration points (added 2026-09-13):**

| Kind | Mod code | Game target | Decompiled source @5482 | Contract |
|---|---|---|---|---|
| Harmony transpiler / string method lookup | `GLoadProtectionPatches.Apply/Remove/Transpile` | private static `PhysicsBubble.DetectStructuralFailure(VehicleUpdateState) : void` | `KSA/PhysicsBubble.cs:958` (caller `FullPhysicsEndFrame` `:1595`, call `:1622`) | Body byte-identical to 5438:873 and 5402:782. Exact parameter signature, static/void validation, exactly one GLoadFraction getter required; installation fails explicitly on missing/ambiguous layouts. |
| Typed getter / IL injection point | `GLoadProtectionPatches.Transpile` | `StructuralLoad.GLoadFraction : double` | `KSA/StructuralLoad.cs:15`; detector `PhysicsBubble.cs:975` | Unchanged (5438:890, 5402:799; `StructuralLoad.cs` byte-identical). Inject `(fraction, vehicleState) -> fraction or 0` after the getter. Only the destruction comparison sees the filtered value; stored telemetry is not modified. |
| Typed identity | `GLoadProtectionPatches.FilterGLoadFraction` | `VehicleUpdateState.ReadOnlyVehicle : Vehicle` | `KSA/VehicleUpdateState.cs:14` | Look up exact vehicle reference in concurrent registry on solver worker (5482 island jobs run in parallel on `VehicleWorkerPool`). |
| Typed liveness / labels | `GLoadProtection`, `KitchenSinkSubmod.GLoadProtection.cs` | `Vehicle.IsDisposed`; inherited `Vehicle.Id`; `VehicleProvider.GetAllVehicles` | `KSA/Vehicle.cs:618`; `KSA/Astronomical.cs:104`; `ksa-abstractions.lib/VehicleProvider.cs` | Picker excludes debris; pruning includes live debris and never retargets by name. |
| Shared host / reset | both `Patcher.cs` hosts; `KitchenSinkSubmod.Saves.cs` | shared Harmony instance; `ISaveParticipantSource`, `ISubmod.Update/Dispose` | local abstraction contracts | Add disabled if patch unavailable. Reset/unload discard live registrations; save replay rebinds recorded IDs after reconstruction. HotkeyGuard stays installed. |

The protected vehicle's G-failure boolean stays false for every contact situation, including
terrain/ocean impacts. The native dynamic-pressure branch still runs and assigns its normal
cause when both loads would exceed limits. PartFailure.Detect, contact solving, measured peaks,
StructuralLoad values, and VehicleStructuralLimits/FlightComputer limits are untouched.
The detector's existing pending-event early return remains intact.

**Update risk:** recheck the detector's getter/comparison relationship and that its single static
argument still identifies the evaluated vehicle. An extra GLoadFraction read rejects installation.
Check native contact/part-failure precedence and pressure cause selection on game updates.
No game assets, shader dependencies, or new Bepu mutations are added.

**Validation:** full solution compilation and `kitchen-sink.tests` exercise the production
registry/transpiler against managed 5438 decision fixtures (unchanged from 5402; the detector is still byte-identical at 5482): multiple targets, same-name isolation,
G/pressure thresholds, contact causes, telemetry, pending events, removal/pruning/reset,
concurrent reads, unpatch restoration and missing/duplicate getter rejection. Another 26 checks
exercise the real save adapter/resolver/coordinator for JSON round-trip, legacy/vanilla loads,
A-B-A/repeated rebind, invalid targets and retained-record recovery. Native picker,
HUD-hidden behavior and actual Bepu cart collisions remain in-game acceptance checks.
