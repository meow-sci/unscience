# 00 — Unscience supermod shell + `ksa-abstractions.lib` game-integration scope

## KSA 5482 (5438 → 5482) verification

Verified 2026-09-25 against `2026.9.22.5482` (revs 5439–5481), diffed from `2026.9.10.5438`.
Static/managed only: 74 projects build with 0 warnings and the 15 managed suites pass (`saves.tests`,
`ksa-upgrade.tests` `RenderFilterChecks`, `garrys-torch.tests` and `dent-wizard.tests` cover this
area's changes). No native KSA run was possible (macOS; KSA.dll is Windows x64). Only `KSA.dll` and
`Planet.Render.Core.dll` changed; Brutal/Bepu, StarMap.API 0.3.6 and Lib.Harmony 2.4.2 are unchanged.
Evidence: [KSA_5482_UPGRADE](../plans/KSA_5482_UPGRADE.md).

- 🔴 **Root compile break (fixed): `JobSystems.ConcurrentWorkers` → `NearestOrbitAndPerformanceWorker`**
  (rev 5480, pure rename; same single-runner scheduler, `KSA/JobSystems.cs:12`). This one error masked
  16 errors in 8 projects. `NativeSaveHooks.ResetAtJoinedBoundary` (`Persistence/NativeSaveHooks.cs:190`)
  still needs the explicit join: `PrepareFrame` waits on the worker at frame start
  (`Program.cs:2150`) but re-queues the nearest-orbit job at `:2188-2192`, before our `GetJobSimStep`
  handoff (`:2207`), and `Universe.DeserializeSave` joins only the orbit, vehicle and cloth solvers.
- 🔴 **New shared seam: `PartRenderFilter`** (`PartRenderFilter.cs`, `PartRenderFilter.Batches.cs`).
  Rev 5456 removed the per-module `*Module.UpdateRenderData` hooks that blinky and its-so-shiny
  prefixed; the replacement is one prefix/postfix set on `PartTreeRenderData.Compose` /
  `ComposeDynamic` / `ComposeGlass` plus private batch reflection. See [PartRenderFilter.cs](#partrenderfiltercs).
- 🔴 **`IvaForceRender` editor reveal (fixed; was silently dead).** The raster `Compose` branch applies
  `(!Template.Internal || IVA)` itself (`PartTreeRenderData.cs:1300`) and no longer calls the private
  `PartModel.AddInstance` sink, so the 5438 sink postfix stopped re-adding internal meshes in the
  editor. It is replaced by a prefix + finalizer on `PartTreeRenderData.Compose` that temporarily clears
  `Template.Internal` on cached internal, non-`ShadowProxy` templates. See [IvaForceRender.cs](#ivaforcerendercs).
- 🔴 **`NativeSaveHooks` write/load results (fixed; silent 5482 regressions).** `UncompressedSave.Write()`
  now returns `bool` (`KSA/UncompressedSave.cs:108-139`, `GameSave.cs:42`) and returns false instead of
  throwing when `SaveDirectory.TryReplace` or the metadata/universe write fails; the old folder can
  survive. The `Saved(UncompressedSave, bool __result, bool __runOriginal)` postfix
  (`NativeSaveHooks.cs:85-93`) now publishes `Written` only on `__result`, otherwise the new
  `WriteFailed` callback (`:20`; `unscience/UnscienceSaves.cs:27,64-68` discards the capture and reports
  the failure), so this session's feature state is never paired with an intact older `universe.xml`.
  `UncompressedSave.Load()` now catches unreadable-universe errors and returns before
  `Universe.DeserializeSave` (`UncompressedSave.cs:61-79`). `ResetBeforeLoad` sets `_nativeLoadReached`
  (`:140`), and `FinishedLoading(UncompressedSave __instance, …)` (`:112-125`) synthesizes an
  `InvalidDataException` when the file load never reached deserialization, so `LastLoadError` /
  `LoadFailed` report it. Details: [saves](saves.md).
- 🔴 **`PhysicsFrameHook.JoinOrbitReaders()` (new; pre-existing race).** The nearest-orbit job queued at
  `Program.cs:2188-2192` reads flight plans and cached orbit points while our handoff runs, and
  `Vehicle.Teleport` → `Orbit.ReleaseCachedPoints` disposes them (rev 5480 fixed only the game's own
  disposal path). `JoinOrbitReaders` (`PhysicsFrameHook.cs:29-34`) waits on
  `NearestOrbitAndPerformanceWorker` at most once per frame (flag reset at handoff start, `:109`). It is
  called automatically before a deferred world change (`:114`) and before draining queued mutations when
  any exist (`:125`), and explicitly by `garrys-torch.lib/WeldEngine.cs:120` before `Teleport` and
  `dent-wizard.lib/DentWizardSubmod.cs:48` before a launch. Idle frames do not wait.
- ⚠️ **`PrepareFrame` seam order (rev 5464).** `PartTree.FlushDirtyDerived()` and
  `FlushDirtyResourceManagers()` now run between `ExecuteNextClothSolvers` and
  `ExecuteNextVehicleSolvers` (`Program.cs:2208-2211`). The `PhysicsFrameHook` transpiler still finds all
  seven `Universe.*` calls exactly once and in order (interleaved non-seam calls are tolerated), so it
  installs unchanged. Handoff mutations are flushed in the same frame, before the vehicle-solver
  snapshot. `ExecuteNextVehicleSolvers` prefixes (inline `EternalFlamePatches`, kiwis-marbles) run
  *after* the flush, so anything they dirty is recomputed lazily on read.
- ⚠️ **HotkeyGuard: mouse-bound actions bypass it (rev 5449).** `GameSettings.OnKeyAll` (`:3407`,
  body now `Input.MatchReleased`) still guards every keyboard event: `Program.OnKey` (`:1743-1751`) calls
  it before the new private `DispatchKeyEvent(in GlfwKeyEvent)` (`:1754`). User mouse-button bindings go
  `Program.OnMouseButton` (`:2001`) → `DispatchKeyEvent` (`:2018`) and never pass `OnKeyAll`,
  `ConsoleWindow.IsOpen` or `WantCaptureKeyboard`. All 101 default bindings are keys, so only
  user-assigned mouse bindings are affected. Optional hardening: prefix `DispatchKeyEvent` and swallow
  `IsMouse` events while typing.
- ✅ **Checked unchanged:** StarMap targets `Program.OnDrawUiFrame(double)` (`:3098`),
  `OnDrawUiViewports(double)` (`:3129`) and `OnFrame(double,double)` (`:2229`), with the
  `if (DrawUI) {…}` block at `:2256-2260`. `HiddenUiFrameHook` → `OnDrawUiConsole(double)` (`:3086`) is
  still called unconditionally at `:2266`, after `DrawFps()` (`:2265`) and before `ImGui.Render()`
  (`:2276`); `DrawUI` is at `:526`. `MenuBarPatch` → `DrawProgramMenusHook()` (`:3961`, still empty,
  called `:3948`). `Universe.ExecuteNextVehicleSolvers(double, SimStep)` (`Universe.cs:2034`) is still a
  single overload; its body only lost `_vehicleUpdateTask.RemoveEligibleVehicles()` (eviction moved to
  the worker, rev 5476). Providers: `Program.ControlledVehicle` (`:502`), `MainViewport` (`:484`),
  `Editor` (`:227`), `Universe.CurrentSystem`, `CelestialSystem.All`, `LookupCollection.UnsafeAsList`,
  `Universe.GetElapsedTime` (`:2323`), `Vehicle.IsDebris/IsDisposed`, `Vehicle.Parts`,
  `PartTree.Parts/Root`, `Part.SubParts`, `KSAColor.Xkcd`. Brutal decomp folders are identical.
- **Native acceptance pending:** save overwrite with a locked old folder (no sidecar written, failure
  reported) and a normal overwrite; loading a corrupted `universe.xml` (scene unchanged, status line);
  mouse-bound action while typing; deferred load / weld / Dent Wizard launch while hovering a flight
  plan at high warp (no `ObjectDisposedException`, no hang); Force IVA in the editor; blinky/shiny
  render-skip; F2 hidden-HUD replay; Unscience menu item.

## Verification — 5402 → 5438 (historical)

`KeyHash` moved from KSA.dll to Planet.Render.Core.dll (rev 5412); the shared library now explicitly references the latter with `Private=false` for native save identity lookups. `IvaForceRender` must bind the common explicit `PartModel.AddInstance` overload because stock dent-aware wrappers were added. Its postfix now forwards the native `PerInstanceDent` into `DentInstanceList` alongside the duplicate `InstanceList` entry. `GameSettings.OnKeyAll` (NEW :3465), `Program.OnDrawUiConsole`, providers, and the seven ordered `PrepareFrame` solver seams retain their contracts. Input polling moved before `ApplyInputEvents`, still before the handoff; the new render pool does not move the solver boundary. `unscience/Patcher.cs:84` already wires IVA force render.

Verified against `2026.9.10.5438` using both supplied source/Content trees.
See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md).
Older catalog tables below retain their explicitly cited build/line numbers; this section records the current delta.

Distribution is Unscience-only: feature and legacy-host projects still compile, but only
`unscience.csproj` has a deployment target and is publishable. No game hook changed for this
packaging refactor. Legacy standalone lifecycle references below describe development hosts.

Permanent reference for the **unscience supermod shell** (`unscience/`) and the **shared
seam library** (`ksa-abstractions.lib/`). Use it to detect when a KSA game update breaks these
two foundational projects. Individual feature submods (blinky, glass, i-feel-seen, …) are
catalogued in their own `scope/` files; here they appear only in the consolidated Harmony
cross-reference table.

Verification baseline:

- **NEW decomp (current, build 2026.9.22.5482):** `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- **OLD decomp (previous, build 2026.9.10.5438):** `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`
- Decomp line numbers in the tables below are **@5438** unless a row or cell says otherwise; rows touched
  by the 5482 pass carry `@5482` lines (older passes' lines are kept only inside the dated area summaries).
- Decomp paths below are **relative to the decomp root** (e.g. `KSA/Universe.cs`). KSA game types live under `KSA/`; ImGui/console types under `Brutal.ImGuiApi*`.
- Every game target was grepped in BOTH decomps; "Δ vs OLD" records the delta (line moves are not deltas).

---

## Architecture overview

- **One StarMap host.** `unscience/Mod.cs` is the single `[StarMapMod]` entry class. StarMap.API
  (NuGet **`StarMap.API` v0.3.6**, `PrivateAssets="all"`) is the loader seam, NOT the game — StarMap
  itself Harmony-patches the game's render loop and invokes the mod's attributed methods. So the
  shell never references the game's frame loop directly; it rides StarMap's hooks.
- **Submod aggregation.** The host instantiates 29 `ISubmod` implementations (one per feature
  lib), stores them in a list, and drives them uniformly: `Initialize()` once, `Update(dt)` every
  frame (even hidden), `RenderContent()` inside a `CollapsingHeader`, `RenderFloatingWindows()`
  always, `Dispose()` on unload. The same `ISubmod` classes are reused by each feature's own
  standalone mod host.
- **Single consolidated Harmony instance.** `unscience/Patcher.cs` owns exactly one
  `new Harmony("MeowSci.Unscience")`. Each feature lib exposes a static `Apply(Harmony)`/`Remove(Harmony)`
  patch class; the supermod applies them all onto
  its one instance instead of each mod owning its own. `HotkeyGuard` (from the seam lib) is applied
  first, exactly once.
- **`ksa-abstractions.lib` is the game-facing seam.** All cross-cutting game access is funnelled
  through small static helpers here (`VehicleProvider`, `CelestialProvider`, `SimTimeProvider`,
  `PartHelpers`, `XkcdColorHelper`, `HotkeyGuard`, `HiddenUiFrameHook`, `IvaForceRender`, `PartRenderFilter` (5482),
  `PhysicsFrameHook`, `Persistence/NativeSaveHooks`, `KsaPaths`) plus pure-C#
  utilities (`ISubmod`, `EasingHelper`, `ReflectionHelpers`, `SubmodUI`). Concentrating game touchpoints here means a
  game update's blast radius is mostly this one library.

### StarMap lifecycle attributes used by `Mod.cs`

Attributes come from `StarMap.API` (`StarMap.API/BaseAttributes.cs`, `OnGuiAttributes.cs`); the
"game hook" column is the game method StarMap Harmony-patches to dispatch each attribute
(`StarMap.Core/Patches/ProgramPatcher.cs`, string-named).

| Mod.cs member (line) | Attribute | StarMap → game hook | Game method (NEW / OLD) | Δ vs OLD |
|---|---|---|---|---|
| `class Mod` (41) | `[StarMapMod]` | marks entry class (`StarMapModAttribute`) | n/a | — |
| `ImmediateUnload` prop (43) | required bool property | StarMap reads it during unload | n/a | — |
| `OnImmediateLoad` (60-61) | `[StarMapImmediateLoad]` | early load (renderer NOT live) | n/a | — |
| `OnFullyLoaded` (63-64) | `[StarMapAllModsLoaded]` | after all mods loaded → build submods + `Patcher.Patch()` (`:135`) | n/a | — |
| `OnBeforeUi(double dt)` (146-147) → `UpdateSubmods` (153) | `[StarMapBeforeGui]` | **PREFIX** of `Program.OnDrawUiFrame(double)` | `KSA/Program.cs:3098` @5482 (`:3021` @5402) | none @5482 (same sig) |
| `OnAfterUi(double dt)` (165-166, UI only) | `[StarMapAfterGui]` | **POSTFIX** of `Program.OnDrawUiViewports(double)` | `KSA/Program.cs:3129` @5482 (`:3051` @5402) | same sig; body iterates `ViewportRegistry.GameViews` and draws only `HasUi` secondary viewports (since 5402) |
| `UpdateSubmods` (registered in OnFullyLoaded, `:132`) | `HiddenUiFrameHook.BeforeGui` (**not** StarMap) | **PREFIX** of `Program.OnDrawUiConsole(double)`, active only while `Program.DrawUI == false` | `KSA/Program.cs:3086` @5482 (`:3009` @5402) | same sig; still called unconditionally (`:2266` @5482) |
| `Unload` (204-205) | `[StarMapUnload]` | mod unload → `Patcher.Unload()` (`:219`) | n/a | — |

**Hidden-HUD (F2) fallback.** `Program.OnFrame` (`KSA/Program.cs:2191-2201` @5402) calls `OnDrawUiFrame` /
`OnDrawUiViewports` / `OnDrawUiThreadSafe` only inside `if (DrawUI)`, and F2 (`InputAction.ToggleUi`,
`KSA/Input.cs:297`, handled `KSA/Program.cs:1755`) flips `Program.DrawUI` (`:527`). So while the HUD is
hidden **neither StarMap GUI hook fires** and every `Update(dt)`-driven feature freezes (welds let go
and refills stop). `ksa-abstractions.lib/HiddenUiFrameHook.cs` prefixes
`Program.OnDrawUiConsole(double)` — called unconditionally at `:2201`, in the same frame phase
(after `PrepareFrame`, inside ImGui `NewFrame`…`Render`, before `OnPreRender`) — and replays the
shell's registered `UpdateSubmods` only when `DrawUI` is false. Welds use the independent PrepareFrame handoff. ImGui rendering
(`RenderWindow`, `RenderFloatingWindows`, F11) is intentionally **not** replayed so mod windows honour
the hidden HUD. `DrawUI` only flips during `Glfw.PollEvents()` in `PrepareFrame` (or from the menu bar,
drawn later), so a frame never runs both StarMap's hooks and the fallback. Re-verified @5482:
`OnFrame` `:2229` → `if (DrawUI) {…}` `:2256-2260` → `DrawFps()` `:2265` → `OnDrawUiConsole` `:2266` →
`ImGui.Render()` `:2276`; `DrawUI` `:526`. F2 can now also arrive from a user mouse binding
(`OnMouseButton` → `DispatchKeyEvent`), which is still processed inside `PollEvents`.

`[StarMapAfterOnFrame]` (POSTFIX of `Program.OnFrame(double,double)`, `KSA/Program.cs:2164` / OLD
`:2066`) exists in StarMap but is **not** used by the supermod shell. The shell's F11 toggle uses
`ImGui.IsKeyPressed(ImGuiKey.F11)` inside `OnAfterUi` (Brutal.ImGuiApi, not a game member).

> Risk seam: StarMap dispatch depends on the **string** method names `"OnDrawUiFrame"`,
> `"OnDrawUiViewports"`, `"OnFrame"` in `ProgramPatcher.cs:10-12`. If the game renames these, **StarMap.API**
> (not unscience) must be updated. All three are present and unchanged 4680→5402.

---

## Consolidated Harmony patches (cross-reference)

Iron Man adds `IronManPatches.Apply/Remove` on the shared owner, coordinating connector
constructor/serialization, editor/root/avatar, worker/input and equipment-render hooks. Its
`IronManSubmod` uses the existing physics handoff; no additional bundled frame hook is installed.
See [complete inventory and passive-unload fallback](iron-man.md).

`unscience/Patcher.cs` applies/removes the following on its single `Harmony("MeowSci.Unscience")`
instance. Targets are listed at cross-reference granularity (type+member); per-class decomp deltas
live in each feature's own `scope/` file. **Two entries are owned by this area** (in **bold**) and
are fully verified below: the inlined `EternalFlamePatches` and `MenuBarPatch`.

Apply/Remove line numbers are the current `unscience/Patcher.cs` (re-read @5482).

| Patch class | Owning project | Apply (Patcher.cs) | Remove (Patcher.cs) | Primary game target(s) | Kind | Risk note |
|---|---|---|---|---|---|---|
| `HotkeyGuard` | **ksa-abstractions.lib** | 55 | 120 | `GameSettings.OnKeyAll(GlfwKeyEvent)` | prefix | verified ↓ @5482 (`:3407`; keyboard path intact, user mouse bindings bypass it) |
| `HiddenUiFrameHook` | **ksa-abstractions.lib** | 59 | 121 | `Program.OnDrawUiConsole(double)` (**string** "OnDrawUiConsole") | prefix (no-op while `Program.DrawUI`) | string-named — verified ↓ @5482 |
| `ThugLifeRenderPatches` | thug-life.lib | 60 | 139 | `SuperMeshRenderSystem.RenderMainPass(IViewport, CommandBuffer)` | postfix | leading `IViewport` added @5482 (compatible) — see pixel-grids scope |
| **`MenuBarPatch`** | **unscience/ (self)** | 61-65 | 122 | `Program.DrawProgramMenusHook()` | postfix | verified ↓ (no delta @5482) |
| `NativeSaveHooks` | **ksa-abstractions.lib** | 66 | 119 | `GameSave.Populate()`, `UncompressedSave.Write()` (returns `bool` @5482), `UncompressedSave.Load()`, `Universe.DeserializeSave(UniverseData)`, `Universe.LoadSystem(string)` | postfix / prefix+finalizer / prefix+postfix | write-result and unreadable-load handling changed @5482 — see ↑ and saves scope |
| `PhysicsFrameHook` | ksa-abstractions.lib | 67 (direct) and 77 (via `GarrysTorchPatches`) | 128 (via `GarrysTorchPatches.Remove`) | private `Program.PrepareFrame(double,double)` → `Universe.GetJobSimStep` call | transpiler | after result commits, before next snapshots; `JoinOrbitReaders` added @5482; see vehicle-physics timing invariant |
| `BlinkyPatches` | blinky.lib | 68 | 123 | via `PartRenderFilter`: `PartTreeRenderData.Compose` / `ComposeDynamic` / `ComposeGlass` | shared prefix + postfix ×3 | **REPLACED @5482** (the three `*Module.UpdateRenderData` targets were removed) — see pixel-grids scope and `PartRenderFilter` ↓ |
| `ShinyPatches` | its-so-shiny.lib | 69 | 124 | same shared `PartRenderFilter` set | (shared) | **REPLACED @5482** — registers a second predicate; no extra patches |
| `CameraControllerOverridePatches` | camera-controller-override.lib | 70-74 | 125 | `OrbitController.OnFrame` / `FlyController.OnFrame` (**string** "OnFrame") | prefix | string-named — see camera scope |
| **`EternalFlamePatches`** | **unscience/ (INLINE)** | 75 | 126 | `Universe.ExecuteNextVehicleSolvers` | prefix `Priority.First` | verified ↓ @5482; now also carries the fuel refill |
| `KiwisMarblesPatches` | kiwis-marbles.lib | 76 | 127 | `Universe.ExecuteNextVehicleSolvers` | prefix `Priority.First` | sim-step timing — see celestial-and-lights scope |
| `GlassPatches` | glass.lib | 81 | 132 | `Camera.ChangeFieldOfView` / `Camera.UpdateProjection` (**string**) + field `Camera._fovRadians` (**string**) | prefix | string-named — see glass scope |
| `IFeelSeenPatches` | i-feel-seen.lib | 82 | 133 | `Vehicle.GetWorldMatrix` / `Vehicle.UpdateRenderData` (**string**) | prefix | string-named — see i-feel-seen scope |
| `VehiclePaintPatches` | humble-arteest.lib | 83 | 138 | `ShaderModuleUtils.FromFile`; private `PartTreeRenderData.WriteState` / `WriteDynamicState`; `PartTreeRenderData.EnsureBuilt` | prefix / postfix ×2 / prefix | **REPLACED @5482** (was `*Module.UpdateRenderData` + `AddInstance` prefixes) — see character-and-materials A1–A5 |
| `EngineEmissivePatches` | humble-arteest.lib | 84 | 134 | private `PartModelDynamic.AddInstance` sink | prefix | unchanged @5482 (still reached per slot from `ComposeDynamic`) — see humble-arteest scope |
| `KittenVisorPatches` | humble-arteest.lib | 85 | 135 | `KittenRenderable.UpdateRenderData` (`VisorMesh.Draw(ViewHandle)` call) | transpiler | **anchor re-pinned @5482** — see character-and-materials visor rows |
| `IvaForceRender` | **ksa-abstractions.lib** | 86 | 140 | `PartModel..ctor` postfix + `PartTreeRenderData.Compose` prefix/finalizer (see IvaForceRender ↓) | postfix, prefix + finalizer | **REPLACED @5482** (was a postfix on the private `PartModel.AddInstance` sink) |
| `EditorScalePatches` | dont-stifle-me.lib | 88 | 137 | `VehicleEditor.ScaleBoundsFor` / `UpdateSelectedScale` / `QuantizeScale` | postfix/prefix | see part-editor-and-robotics scope |
| `KittenAnimationPatches` | kitten-animations.lib | 90 | 142 | `AnimatedRenderable.UpdateAnimation(double)` (**string** via `AccessTools.Method`) | prefix `(AnimatedRenderable __instance, ref double dt)` | ⚠️ **hot path** — runs for every animated renderable every frame; must stay a reference compare + early return. See character-and-materials scope |
| `PyroPatches` | pyro.lib | 91 | 145 | `Vehicle.AddVolumetricExhaustInstances` (`nameof`) | postfix | see exhaust-plumes scope |
| `GraffitiPatches` | graffiti.lib | 94 | 146 | `RenderTarget.ResolveAttachments` (`nameof`) | postfix | see decals scope |
| `HotPursuitPatches` | hot-pursuit.lib | 97 | 149 | `FixedController.OnFrame(IViewport,double)` (`nameof`) | selective prefix | skips stock math only for owned part-mounted cameras; see camera scope |

Also applied on the same instance but catalogued only in their feature scopes: `GarrysTorchPatches` /
`KittenScalePatches` (77-78 / 128-129), Godzilla `ColliderScalePatches` / `VisualScalePatches` (79-80 /
130-131), `GLoadProtectionPatches` (87 / 141), `EditorValueLimitPatches` (89 / 136), `IronManPatches`
(92 / 143), `SphinxPatches` (93 / 144), `FreeFallinPatches` (95 / 147) and Pebbles (96 / 148).

Non-Harmony cleanup also driven by `Patcher.Unload()`: `VehiclePaint.Cleanup()` (line 151) and
`EngineEmissive.Cleanup()` (line 152), both humble-arteest.lib.

Notes:
- **garrys-torch uses a shared frame transpiler.** Both hosts apply/remove `GarrysTorchPatches`,
  which wraps `Program.PrepareFrame`'s `Universe.GetJobSimStep` call after completed result
  application and before the next cloth/vehicle/orbit snapshots. `SimStep.PreviousTime` stamps
  teleports. No `OnAfterUi` or hidden-HUD weld callback remains. See vehicle-physics scope.
- `IFeelSeenPatches.Apply` takes a second argument (`IFeelSeenTracker`, wired at `Mod.cs:124`).
- `CameraControllerOverridePatches.SequencePlayer` and `MenuBarPatch.ToggleWindow` are wired before
  Apply (Patcher.cs:72, 63).
- Blinky and its-so-shiny share one `PartRenderFilter` patch set; the first `Register` installs it and
  the last `Unregister` removes it, so their Apply/Remove order does not matter.
- `KittenAnimationPatches.Driver` is wired **after** Apply, from `KittenAnimationsSubmod.Initialize()`
  (`Mod.cs` initialises submods after `Patcher.Patch()`). The prefix null-checks it, so the ordering
  is safe; before the submod initialises the patch is simply inert.

### `MenuBarPatch` (unscience/MenuBarPatch.cs) — owned by this area

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Harmony postfix | `MenuBarPatch.cs:8` (`[HarmonyPatch]`), applied `:15`, removed `:21-24` | `Program.DrawProgramMenusHook()` — `public void DrawProgramMenusHook()` (empty hook) | `KSA/Program.cs:3961` @5482 (called from `DrawMenuBar` at `:3948`) | Yes | None @5482 — identical empty instance method | Game ships this as a deliberate no-op modding hook. Postfix appends an "Unscience" `ImGui.MenuItem`. Low risk. |

### `EternalFlamePatches` (inlined in unscience/Patcher.cs) — owned by this area

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Harmony prefix (`Priority.First`) | `Patcher.cs:181` (lookup), `:189` (patch), `:192-198` (remove), `:200-210` (prefix) | `Universe.ExecuteNextVehicleSolvers(double dtPlayer, SimStep simStep)` — `public static void` | `KSA/Universe.cs:2034` @5482 (`SimStep` = `KSA/SimStep.cs:3`, readonly struct) | Yes | Signature unchanged @5482, still the only overload; body lost `_vehicleUpdateTask.RemoveEligibleVehicles()` (eviction moved to the worker, rev 5476) | Looked up by name only (`nameof`, no param-type array), so a param change would NOT break the lookup unless the method became overloaded. Prefix dispatches to `EternalFlameSubmod.Instance?.UpdateBeforeVehicleSolvers()` → `FuelManager.RefillBeforeVehicleSolvers()` (`eternal-flame.lib/EternalFlameLib.cs`), wrapped in try/catch. **Since 5482 it refills fuel as well as batteries** (fuel previously ran from the UI tick after the worker snapshot and was overwritten by the worker commit during burns). Runs after `PartTree.FlushDirtyDerived/FlushDirtyResourceManagers` (`Program.cs:2209-2210`); a refill raises the tank-contents flag (rev 5478) but dirties no lazily derived data. kiwis-marbles also patches this target; kitchen-sink's former solver hook was removed on 2026-09-13. |

---

## `ksa-abstractions.lib` — per-helper integration points

Decomp paths relative to NEW decomp root. All confirmed present in NEW; OLD line noted only where useful.

### VehicleProvider.cs

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Direct API (prop) | `VehicleProvider.cs:11` | `Program.ControlledVehicle` — `public static Vehicle? ControlledVehicle { get; set; }` (setter calls `_controlledVehicle?.ClearHeldPlayerInput()`) | `KSA/Program.cs:503` | Yes | None (OLD `:480`; already a property, not a field, at 5348) | Returned as-is from `GetControlledVehicle()`; compile-bound so field→property was harmless. |
| 2 | Direct API (prop) | `:15` | `Universe.CurrentSystem` — `public static CelestialSystem? CurrentSystem { get; private set; }` | `KSA/Universe.cs:94` | Yes | None (OLD `:94`) | Null-safe (`?.`). |
| 3 | Direct API (prop) | `:15` | `CelestialSystem.All` — `public LookupCollection<Astronomical> All => _all;` | `KSA/CelestialSystem.cs:64` | Yes | None (OLD `:57`) | |
| 4 | Direct API (method) | `:15` | `LookupCollection<Astronomical>.UnsafeAsList()` — `public List<T> UnsafeAsList()` | `KSA/LookupCollection.cs:210` | Yes | None (file byte-identical) | Then LINQ `OfType<Vehicle>()`. |
| 5 | Direct API (type) | `:11,21,29` | `Vehicle` — `public class Vehicle : Astronomical, …, IObjectId, …` | `KSA/Vehicle.cs:28` | Yes | None | |
| 5b | Direct API (prop) | `:24` | `Vehicle.IsDebris` — `public bool IsDebris { get; private set; }` | `KSA/Vehicle.cs:392` | Yes | **NEW @5402** (absent in OLD) | Set by `Vehicle.MarkAsDebris()` from `PartFailure` (`KSA/PartFailure.cs:246`). `GetAllVehicles(bool includeDebris = false)` filters on it so shed fragments stay out of every mod's picker; `FindVehicle` and the two callers that must see everything pass `true`. |
| 6 | Direct API (prop) | `:22` | `Vehicle.Id` (inherited `Astronomical.Id` via `IObjectId`) — `public virtual string Id { get; protected set; }` | `KSA/Astronomical.cs:104` | Yes | None (OLD `:104`) | `Id` is not declared on `Vehicle`; resolved through base `Astronomical`/`IObjectId`. |

Update-risk findings (4680→4750):
- **No breaking deltas.** All targets present, signatures identical.
- Behavioral (rev 4699): the game added `Vehicle.IsControllable` (`KSA/Vehicle.cs:588` @5402,
  `public virtual bool IsControllable => _overrideIsControllable || Parts.Controls.NumModules > 0;`)
  — **absent in OLD** (0 occurrences in `Vehicle.cs`), backed by new `PartTree.Controls`
  (`KSA/PartTree.cs:49`, also absent in OLD). `VehicleProvider` does **not** consume it:
  `GetControlledVehicle()` still mirrors `Program.ControlledVehicle`, and `GetAllVehicles()` returns
  every `Vehicle` regardless of controllability (it filters only debris, since 5402 — see the
  5348→5402 summary). Watch only if a consumer starts assuming a
  vehicle is controllable — control is now gated on a Control Module (capsule+kittens have one).

### CelestialProvider.cs

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Direct API | `CelestialProvider.cs:11-12,15-16` | `Universe.CurrentSystem.All.UnsafeAsList()` (as above) | `KSA/Universe.cs:94`, `KSA/CelestialSystem.cs:64`, `KSA/LookupCollection.cs:210` | Yes | None | then `OfType<Celestial>()` / `OfType<IOrbiter>()`. |
| 2 | Direct API (type) | `:12` | `Celestial` — `public abstract class Celestial : Astronomical, IOrbiter, …` | `KSA/Celestial.cs:23` | Yes | None | |
| 3 | Direct API (type) | `:16` | `IOrbiter` — `public interface IOrbiter : IFollowable, IObjectId, …` | `KSA/IOrbiter.cs:10` | Yes | None | `GetAllOrbiters()` = celestials + vehicles. |

Update-risk findings (4680→4750): **No breaking deltas detected.**

### SimTimeProvider.cs

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Direct API (method) | `SimTimeProvider.cs:15` | `Universe.GetElapsedTime()` — `public static UniverseTime GetElapsedTime()` | `KSA/Universe.cs:2114` | Yes | None (OLD `:2060`; body identical). Historical: **RENAMED @5261** from `GetElapsedSimTime()` (rev 5211) | |
| 2 | Direct API (type) | `:15` | `UniverseTime` — `public readonly struct UniverseTime : IEquatable<UniverseTime>`, backed by `Int128` nanoseconds | `KSA/UniverseTime.cs:6` | Yes | None (file byte-identical). Historical: **RENAMED + RETYPED @5261** from the retired `SimTime` (`KSA/SimTime.cs` no longer exists) | |
| 3 | Direct API (method) | consumers | `UniverseTime.Seconds()` — `public double Seconds()` | `KSA/UniverseTime.cs:95` | Yes | None | **The compatibility hinge** — still returns `double`, so no caller arithmetic changed |

Update-risk findings (5117 → 5261):

- **CONFIRMED COMPILE BREAK (rev 5211):** *"Replaced SimTime with UniverseTime, backed by 128-bit
  nanoseconds. This is a prelude to creating 64-bit nanosecond integer BubbleTime within physics
  steps…"* → **CS0246** at `SimTimeProvider.cs:9`. Because this is the suite's single game-facing
  time seam, the failure blocked **all 55 projects** — the rest of the solution's errors were hidden
  behind it until this one was fixed.
- **Fix is type-only.** `.Seconds()` survives on the new struct and the remaining consumer passes the
  value straight into `Orbit.CreateFromStateCci` (`kiwis-marbles.lib/CelestialWeldEngine.cs:33`). **No precision or
  arithmetic handling needed changing**, despite the double→`Int128` backing swap.
- **The wrapper keeps the name `SimTimeProvider`.** Renaming the class would add churn for no functional gain.
  This is exactly the blast-radius concentration this library exists for — one game rename cost
  **one line** here plus two incidental direct callers (`doh.lib`, `garrys-torch.lib`) that bypass it.

Update-risk findings (4680→4750): **No breaking deltas detected.**

### ReflectionHelpers.cs

| # | Kind | Mod code (file:line) | Game target | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Reflection (generic) | `ReflectionHelpers.cs:14,22` | none hardcoded — `Type.GetField(name, Public\|NonPublic\|Instance)` get/set | n/a | n/a | n/a | This helper has **no** compile-checked or string-literal game member of its own. Runtime risk lives entirely in **callers** that pass private field-name strings; those are catalogued per consuming submod. |

Update-risk findings (4680→4750): **No breaking deltas detected** (no game member referenced here).

### PartHelpers.cs

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Direct API (prop) | `PartHelpers.cs:14` | `Vehicle.Parts` — `public PartTree Parts { get; set; }` (setter also sets `value.OwningVehicle = this`) | `KSA/Vehicle.cs:604` | Yes | None (OLD `:598`; already a property at 5348) | |
| 2 | Direct API (prop) | `:14` | `PartTree.Parts` — `public ReadOnlySpan<Part> Parts => _parts.AsSpan();` | `KSA/PartTree.cs:95` | Yes | None (OLD `:95`) | top-level parts. |
| 3 | Direct API (prop) | `:32` | `Part.SubParts` — `public ReadOnlySpan<Part> SubParts => _subParts.AsSpan();` | `KSA/Part.cs:1079` | Yes | None (OLD `:1052`) | recursion key. |
| 4 | Direct API (type) | `:11,20,29` | `Part` | `KSA/Part.cs` | Yes | None | |

Update-risk findings (4680→4750):
- **No breaking deltas detected.** The helper traverses via `SubParts` (span recursion).
- For completeness: `Part.TreeParent` (`KSA/Part.cs:664` @5402) and `Part.TreeChildren`
  (`KSA/Part.cs:666` @5402) — the alternate tree API named in the task — both exist and are
  unchanged, but `PartHelpers` does **not** use them.


### ISubmod.cs

| # | Kind | Mod code | Game target | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|
| 1 | Pure C# interface | `ISubmod.cs` | **none** | n/a | n/a | Contract consumed by the shell + every feature lib. No game dependency. |

Update-risk findings (4680→4750): **No breaking deltas detected.**

### EasingHelper.cs / EasingType

| # | Kind | Mod code | Game target | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|
| 1 | Pure C# | `EasingHelper.cs` | **none** — `System.Math` only | n/a | n/a | No game dependency. |

Update-risk findings (4680→4750): **No breaking deltas detected.**


### XkcdColorHelper.cs

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Reflection (type) | `XkcdColorHelper.cs:22` | `KSAColor.Xkcd` — `public static class Xkcd` (nested in `struct KSAColor`) | `KSA/KSAColor.cs:23` | Yes | None (OLD `:23`) | Enumerates `GetProperties(Public\|Static)`. |
| 2 | Direct API (cast) | `:28` | `Color.Preset` (Brutal.Numerics) — property type of each Xkcd color; implicit `Color.Preset → float4` | `KSA/KSAColor.cs:25+` (props), `Brutal.Numerics/Color.cs:8` (`Preset`), `:53` (implicit `float4`) | Yes | None (`KSAColor.cs` and `Color.cs` byte-identical @5402) | Each prop is `public static Color.Preset Name => float3.Rgb(...)`. |

Update-risk findings (4680→4750):
- **No breaking deltas detected.** Reflection-driven enumeration is resilient to individual color
  additions/renames; it breaks only if the `KSAColor.Xkcd` type is removed/renamed, or if the
  `Color.Preset → float4` conversion is dropped. Neither occurred.

### HotkeyGuard.cs

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Harmony prefix | `HotkeyGuard.cs:21` (lookup), `:23` (patch), `:29-30` (unpatch) | `GameSettings.OnKeyAll(GlfwKeyEvent keyEvent)` — `public static bool` | `KSA/GameSettings.cs:3407` @5482 (OLD `:3465`) | Yes | Signature unchanged @5482; body now matches via `Input.MatchReleased(in keyEvent, …)` (rev 5449) | Prefix `Prefix(ref bool __result)`: when guard active, sets `__result = true` and returns false (skip original), swallowing the key. Looked up by `nameof`. @5482 `Program.OnKey` (`:1743-1751`) calls `OnKeyAll` first and only then the new private `DispatchKeyEvent(in GlfwKeyEvent)` (`:1754-1999`, which now holds every former key handler), so all keyboard input stays guarded. ⚠ User mouse-button bindings (`GlfwKeyEvent.IsMouse`) reach `DispatchKeyEvent` from `Program.OnMouseButton` (`:2001`, dispatch `:2018`) without passing `OnKeyAll`; all 101 default bindings are keys. |
| 2 | Direct API (field) | `:38` | `Program.ConsoleWindow` — `public static ConsoleWindow ConsoleWindow;` | `KSA/Program.cs:285` @5482 | Yes | None | |
| 3 | Direct API (prop) | `:38` | `ConsoleWindow.IsOpen` — `public bool IsOpen => _show;` | `Brutal.ImGuiApi.Abstractions/ConsoleWindow.cs:292` | Yes | None (OLD `:292`) | Guard is bypassed while the dev console is open. |
| 4 | ImGui API | `:38` | `ImGui.GetIO().WantTextInput` (Brutal.ImGuiApi) | `Brutal.ImGuiApi/*` | Yes | None observed | Detects ImGui text-input focus globally (every InputText/combo filter). See Brutal-package note below. |

Update-risk findings (4680→4750):
- **No breaking deltas detected.** `GameSettings.OnKeyAll` and `Program.ConsoleWindow.IsOpen`
  unchanged. `ImGui.GetIO().WantTextInput` compiles against the 4750 Brutal packages.

### HiddenUiFrameHook.cs

Added @5348. Keeps the shell's per-frame non-UI work running while the game HUD is hidden (F2) — see
the *Hidden-HUD fallback* note under the lifecycle table for the why.

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (5348) | In 5348? | Risk/notes |
|---|---|---|---|---|---|---|
| 1 | Harmony prefix (**string-named**) | `HiddenUiFrameHook.cs:28` (name), `:44` (lookup), `:47` (patch), `:54` (unpatch) | `Program.OnDrawUiConsole(double dt)` — `private void`, instance | `KSA/Program.cs:3086` @5482, called unconditionally from `OnFrame` at `:2266` (`:3009` / `:2201` @5402) | Yes | `AccessTools.Method` by string; a miss throws `MissingMethodException` at `Patch()` → logged and skipped by `Patcher.TryApply` (mods then freeze on F2 again, nothing else breaks). **Phase contract:** must stay a method the game calls every frame *after* the `if (DrawUI)` UI block and *before* `ImGui.Render()`/`OnPreRender` — `DrawFps()` (`:3137`, static, no `dt`) is the fallback anchor if `OnDrawUiConsole` moves. Body drift @5402 (`HoveredViewport.IsMain()` / `.ImGuiId`) does not touch the signature. |
| 2 | Direct API (static prop) | `:40`, `:64` | `Program.DrawUI` — `public static bool { get; set; }` | `KSA/Program.cs:526` @5482 (`:527` @5402) | Yes | Gate. Toggled by `InputAction.ToggleUi` (`KSA/Input.cs:297` = F2, handled `Program.cs:1755`). If the game ever gates `OnDrawUiFrame` on something else, this prefix goes dead-silent (no crash). |

Update-risk findings (5261→5348): n/a (new). Verified against 5348 by construction: `OnFrame`
(`:2066`) → `if (DrawUI) { OnDrawUiFrame; OnDrawUiViewports }` (`:2093`) → `if (DrawUI) OnDrawUiThreadSafe`
(`:2098`) → `DrawFps()` → `OnDrawUiConsole(dtPlayer)` (`:2103`) → `ImGui.Render()`.
Re-verified @5402 with the same shape: `OnFrame` (`:2164`) → `if (DrawUI) {…}` (`:2191`) →
`if (DrawUI) OnDrawUiThreadSafe` (`:2196`) → `DrawFps()` (`:2200`) → `OnDrawUiConsole(dtPlayer)` (`:2201`)
→ `ImGui.Render()` (`:2212`).

### IvaForceRender.cs

Rows re-verified @5482 (mod lines are the current `ksa-abstractions.lib/IvaForceRender.cs`).

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Harmony postfix (ctor) | `IvaForceRender.cs:47` (lookup), `:49` (patch), `:98-106` (postfix) | `PartModel..ctor(PartModelModule.Template)` — `protected PartModel(PartModelModule.Template template)` | `KSA/PartModel.cs:435` | Yes | None (body identical, still the only ctor) | `AccessTools.Constructor` finds the **protected** ctor; explicit param-type array. Since 5482 the postfix also marks the editor internal-template cache dirty. |
| 2 | Harmony prefix + finalizer | `:53-57` (name-only lookup, patch), `:114-125` `ComposePrefix(IViewport inViewport)`, `:127-133` `ComposeFinalizer(Exception? __exception)`, `:135-146` cache rebuild | `PartTreeRenderData.Compose(ref readonly double4x4, bool, IViewport inViewport, int)` — public instance | `KSA/PartTreeRenderData.cs:1258` (raster gate `(!Template.Internal \|\| IVA)` at `:1300`) | Yes | **NEW @5482** — replaces the 5438 postfix on the private `PartModel.AddInstance(PerInstanceData, PerInstanceDent, IViewport, int)` sink (`KSA/PartModel.cs:470`), which static raster parts no longer reach | Resolved by name only (single overload today; an added overload would make the lookup ambiguous). In the editor (`Program.Editor != null`), outside IVA, for viewports with `RenderPartModels`, the prefix sets `Template.Internal = false` on every cached internal non-`ShadowProxy` template that is still internal and records it; the finalizer restores exactly those, even when `Compose` throws. Stock code then appends instance, `DentInstanceList` entry and dent data consistently. The only readers of `Template.Internal` are main-thread render code (`PartTreeRenderData.cs:1300`, `PartModel.cs:486`), so nothing else observes the temporary flip. Coexists with `PartRenderFilter` on the same method. |
| 3 | Direct API (static field) | `:138` (cache rebuild), `:151` (`Enabled` mutation) | `PartModel.Instances` — `public static List<PartModel> Instances` | `KSA/PartModel.cs:409` | Yes | None | Source of the editor internal-template cache (rebuilt after the ctor postfix or `RestoreInternalHidden` marks it dirty) and of the `Enabled` mutation. |
| 4 | Direct API (field) | `:102-104,121-122,130,141,153-156,165` | `PartModelModule.Template.Internal` — `public bool Internal = false;` | `KSA/PartModelModule.cs:39` | Yes | None | Cleared permanently by `Enabled`; cleared per `Compose` call and restored in the editor. |
| 5 | Direct API (field) | `:141` | `PartModelModule.Template.RayTracing` — `public RaytracingMode RayTracing` | `KSA/PartModelModule.cs:31` | Yes | None | |
| 6 | Direct API (enum) | `:141` | `PartModelModule.RaytracingMode.ShadowProxy` | `KSA/PartModelModule.cs:14` | Yes | None | Never revealed (the raster branch skips shadow proxies too). |
| 7 | Direct API (field) | `:116` | `Program.Editor` — `public static VehicleEditor? Editor;` | `KSA/Program.cs:227` | Yes | None | editor-only branch. |
| 8 | Direct API (prop/enum) | `:116` | `IViewport.Mode` of the composed viewport (`CameraMode Mode { get; }`) vs `CameraMode.IVA` | `KSA/IViewport.cs:29`, `KSA/CameraMode.cs:14` | Yes | None | Per-viewport IVA test. (`Program.MainViewport` is no longer read by this helper.) |
| 9 | Direct API (extension) | `:117` | `IViewport.HasAny(ViewportOptionFlags.RenderPartModels)` | `KSA/ViewportEx.cs:60`; `KSA/ViewportOptionFlags.cs:20` | Yes | None | Mirrors `Compose`'s own early return. |
| ~~10~~ | ~~Harmony postfix~~ + ~~`PartModel.ViewportData.Get(...).InstanceList` / `DentInstanceList.Add`~~ + ~~`PartModel.PerInstanceData`~~ | — | private `PartModel.AddInstance` sink | `KSA/PartModel.cs:470` | — | **RETIRED @5482** | Replaced by #2. The old postfix's side effect of adding internal meshes to editor thumbnails (`ThumbnailPart` calls `AddInstance` directly) is gone. |

Update-risk findings (5438 → 5482): the editor reveal was silently dead until this fix, because the
postfix target was no longer called for raster parts (see the 5482 section at the top). Live check:
internal meshes (with dents) visible in the editor, no duplicates in IVA, `Enabled` toggle in flight.

Update-risk findings (4680→4750):
- **No breaking deltas detected.** Every IvaForceRender target is byte-for-byte unchanged
  4680→4750, including line numbers — despite the changelog's mesh churn (4693 merged
  DynamicMeshIndirect into MeshIndirect; 4745 cleaned MeshIndirect layout indices / combined
  ModelGlass+ModelEye shaders). Those changes touched mesh layout and shaders, not the `PartModel`
  instance-list / `Template.Internal` API this helper uses.
- **IvaForceRender wiring — FIXED (Phase 4).** `unscience/Patcher.cs` now calls
  `IvaForceRender.Patch(_harmony)` in `Patch()` and `IvaForceRender.Unpatch(_harmony)` in `Unload()`
  (previously wired only in the standalone `kitchen-sink/Patcher.cs:23,39`).
  The supermod's "Force IVA Rendering" toggle therefore now also handles interior parts spawned *after* the
  toggle (ctor postfix) and editor-preview internal meshes (`AddInstance` postfix), not just the
  `Enabled`-setter mutation of already-loaded `PartModel.Instances` templates. (The separate kitchen-sink
  vehicle-solver prefix behind kitchen-sink's "Flexo Part Test" *Update Physics* button remains
  standalone-only — out of scope here. Note that kitchen-sink's Flexo\* test panels are named after the
  removed flexo mod but are independent of it and were kept.)

### PartRenderFilter.cs

Added @5482 (`ksa-abstractions.lib/PartRenderFilter.cs`, `PartRenderFilter.Batches.cs`). Hides selected
parts' meshes while the parts stay in their vehicle. Consumers: blinky (`BlinkyPatches.cs:20,26`, owner
`"blinky"`) and its-so-shiny (`ShinyPatches.cs:20,26`, owner `"its-so-shiny"`); see
[pixel-grids](pixel-grids-and-render.md). Public API: `Register(Harmony, string owner, Func<Part,bool>
shouldHide)` / `Unregister(Harmony, string owner)` / `IsInstalled`. The first owner installs one shared
patch set with its Harmony instance and the last owner removes it; predicates are OR-ed and receive
`Part.FullPart`.

Why this seam: rev 5456 removed `PartModelModule` / `PartModelDynamicModule` /
`PartModelGlassModule.UpdateRenderData`. Render data is cached per tree in `PartTreeRenderData`, and the
three public `Compose*` methods append one contiguous, slot-ordered range per model to the per-viewport
instance lists every frame. The static raster branch appends without `AddInstance`, so no per-part
method decides submission. The prefix records each batch's list count; the postfix removes hidden slots
from the appended range in place (`InstanceList` and `DentInstanceList` together) before
`PartModelRenderer.UpdateRenderData` uploads. Cached render data is never modified, so predicate
changes take effect on the next frame without invalidation, and draw counts and shadow bounds stay
consistent.

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Harmony prefix + postfix | `PartRenderFilter.cs:63-78` (install), `:93-104` (exact-signature lookup), `:80-89` (uninstall); `PartRenderFilter.Batches.cs:60-98` | `PartTreeRenderData.Compose(ref readonly double4x4, bool, IViewport inViewport, int)` — public instance | `KSA/PartTreeRenderData.cs:1258` (raster `:1300-1319`, RT-IVA `:1284-1298`) | Yes | **NEW @5482** | Prefix `(PartTreeRenderData __instance, IViewport inViewport, out bool __state)`; postfix `(…, bool __state)`. A range is filtered only when exactly `batch.Count` entries were appended and the dent list grew by the same amount; otherwise (RT-IVA, internal meshes outside IVA, anything unexpected) it is left alone. |
| 2 | Harmony prefix + postfix | `PartRenderFilter.cs:69`; `Batches.cs:102-140` | `PartTreeRenderData.ComposeDynamic(...)` (same signature) | `KSA/PartTreeRenderData.cs:1152` | Yes | **NEW @5482** | Stock calls `PartModelDynamic.AddInstance` per slot, so Engine Emissive's sink prefix sees every instance before compaction. |
| 3 | Harmony prefix + postfix | `PartRenderFilter.cs:70`; `Batches.cs:144-180` | `PartTreeRenderData.ComposeGlass(...)` (same signature) | `KSA/PartTreeRenderData.cs:899` | Yes | **NEW @5482** | Instance list only (glass has no dent list). |
| 4 | Private reflection (fields, **string**) | `Batches.cs:38-40` (`FieldRefAccess<PartTreeRenderData, IList>`) | `PartTreeRenderData._batches` / `_dynamicBatches` / `_glassBatches` — `private readonly List<Batch/DynamicBatch/GlassBatch>` | `KSA/PartTreeRenderData.cs:237,243,239` | Yes | **NEW @5482** | Rename or retype fails loudly at the first `Register`. |
| 5 | Private nested types (**string**) | `Batches.cs:37,52-53` (`AccessTools.Inner`) | `PartTreeRenderData.Batch` / `DynamicBatch` / `GlassBatch` (private sealed classes) | `KSA/PartTreeRenderData.cs:23,73,162` | Yes | **NEW @5482** | Missing type → `MissingMemberException` at install. |
| 6 | Private reflection (fields, **string**) | `Batches.cs:41-49` (`FieldRefAccess`) | `Batch/DynamicBatch/GlassBatch.Model` (`PartModel` / `PartModelDynamic` / `PartModelGlass`), `.Parts : Part[]`, `.Count : int` | `Batch` `:25,27,45`; `DynamicBatch` `:75,79,99`; `GlassBatch` `:164,166,172` | Yes | **NEW @5482** | Slot `j` of `Parts` is the module parent for slot `j` of the appended range; the filter relies on that ordering (`Compose` writes `span[j]` from slot `j`). |
| 7 | Direct API | `Batches.cs:72,89,114,131,156,173` | `PartModel.ViewportData.Get(PartModel, IViewport)`, `PartModelDynamic.ViewportData.Get(...)`, `PartModelGlass.ViewportData.Get(...)` → `InstanceList`, `DentInstanceList` | `KSA/PartModel.cs:364` (lists `:358,:362`); `KSA/PartModelDynamic.cs:364` (`:358,:362`); `KSA/PartModelGlass.cs:446` (`:442`) | Yes | **NEW @5482** | Mutated only inside the `Compose*` postfix, i.e. before upload. **Never mutate these lists between `WriteInstancesToGpu` and `PartModelRenderer.ClearFrameData`** (`PartModelRenderer.cs:844`): since 5482 they are cleared at frame start and shadow culling re-reads them after upload (`PartModel.AppendShadowBounds`, `PartModel.cs:533`). |
| 8 | Direct API | `Batches.cs:55-56`; `PartRenderFilter.cs:57` | `IViewport.HasAny(ViewportOptionFlags.RenderPartModels)`; `Part.FullPart` | `KSA/ViewportEx.cs:60`; `KSA/Part.cs:1171` | Yes | **NEW @5482** | Same early-out as `Compose*`. |

**Failure behaviour.** Install resolves every member first; a missing method, nested type or field throws
out of `Register` (the host's `TryApply` logs it and that feature's render-skip is unavailable), and a
partially applied patch set is rolled back. Any exception at runtime sets `_faulted`, is logged once and
disables filtering for the session (fail-open: everything renders); it never breaks the render loop.
**Invariant:** exactly one compaction patch set. Two independent postfix pairs would conflict: the
first compaction changes `InstanceList.Count`, so the second would see `appended != batch.Count` and skip.
**Known gap vs 5438:** raytraced-IVA submissions (`Compose` RT branch → `PartModel.AddInstance` →
`RaytracingRenderer.PartRaytracingData*`; `ComposeGlass` → `RayTraceTransforms`) are not filtered.
**Tests:** `ksa-upgrade.tests/RenderFilterChecks.cs` links the production files against
`RenderDataFixture.cs`. It covers the no-owner baseline, two owners sharing one compaction, pre-existing
entries from other vehicles, full-part judgement of sub-parts, dent alignment, non-raster ranges left
alone, misaligned dents failing open, the no-`RenderPartModels` skip, unregistering one or all owners,
uninstall, and a throwing predicate failing open. Native rendering is not covered.

### KsaPaths.cs

| # | Kind | Mod code | Game target | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|
| 1 | OS path | `KsaPaths.cs:9,15` | **none** — `MyDocuments\My Games\Kitten Space Agency` plus shared `.unscience` mod-data root | n/a | n/a | No game API. Breaks only if the game changes its user-data folder name. |

`KsaPaths.ModDataDir` centralizes the suite's `.unscience` custom-data root. `PngLibrary` owns the
shared `ModDataDir/pngs` catalog, and `PngFileBrowser` provides the common ImGui import UI used by
graffiti and free-fallin. Imports always copy and auto-uniquify; scanning is startup/on-demand only,
with no filesystem watcher or polling thread. This is mod-authored data/UI, not a KSA integration
surface.

Update-risk findings (4680→4750): **No breaking deltas detected.**

### SubmodUI.cs

| # | Kind | Mod code | Game target | Decomp path | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | ImGui API | `SubmodUI.cs:28-31,40-41` | Brutal.ImGuiApi only — `PushStyleVar(WindowPadding)`, `BeginChild(AutoResizeY\|AlwaysUseWindowPadding, NoScrollbar)`, `PopStyleVar`, `Dummy`, `EndChild` | `Brutal.ImGuiApi/*` | Yes | None observed | No KSA game internals. See Brutal-package note. |

Update-risk findings (4680→4750): **No breaking deltas detected** (compiles against 4750 Brutal).

---

## `unscience/UnscienceState.cs` — persistence

Persistence only; no KSA game internals beyond `KsaPaths` + the ImGui ini API.

- **State dir:** `KsaPaths.UserDataDir + "\.unscience"` → `…\My Documents\My Games\Kitten Space Agency\.unscience`.
- **Files:** `window.ini` (ImGui window layout) and `state.toml` (submod header-open + visibility + settings).
- **ImGui ini round-trip:** load via `ImGui.LoadIniSettingsFromMemory(string)` (`:35`); save via
  `ImGui.SaveIniSettingsToMemory().ToString()` (`:48`), then `FilterIniForUnscienceWindows` keeps only
  the `[Window][Unscience Toolbox]` section so unrelated game windows aren't persisted.
- **TOML:** Tomlyn (`Toml.TryToModel<TomlTable>` / `Toml.FromModel`) — `[header_open]`, `[visibility]`,
  `[settings]` (`save_interval` clamped 1–30, `auto_save_enabled`, `show_mod_tooltips`). Pure managed
  library, no game dependency.
- **Autosave cadence:** `Mod.cs:149-156` accumulates `dt` in `OnAfterUi` and saves every
  `SaveIntervalSeconds` while the window is visible.

Update-risk findings (4680→4750): **No breaking deltas detected.** Only game-adjacent surface is
the Brutal.ImGuiApi ini API (see note below); it compiles against 4750.

---

## Area summary — Update-risk findings (5117 → 5261)

- **One breaking delta in `ksa-abstractions.lib`:** `SimTimeProvider` (rev 5211 `SimTime` →
  `UniverseTime`) — see that helper's section above. It blocked the whole solution because every
  project depends on this library; fixing it revealed the remaining four compile breaks.
- **Every other patch target is byte-identical** (line shifts only): `GameSettings.OnKeyAll`
  (HotkeyGuard → **every** top-level mod), `Program.DrawProgramMenusHook()` (MenuBarPatch),
  `Program.DrawMenuBar(Viewport,int)`, `Universe.ExecuteNextVehicleSolvers(double, SimStep)` (still a
  single overload — important, since it is resolved with no param array),
  `PartModel..ctor(PartModelModule.Template)` + `PartModel.AddInstance` (IvaForceRender).
- **StarMap seams intact:** `Program.OnDrawUiFrame`, `OnDrawUiViewports`, `OnFrame` and
  `DrawProgramMenusHook` all still present, so the suite's load path is unaffected. The
  `[StarMapAllModsLoaded]`-before-`ModLibrary.Bind()` invariant was not re-derived this pass.
- **Brutal packages:** solution builds clean with `TreatWarningsAsErrors` and **0 warnings** against
  the 5261 DLLs, so no nullability/signature shift landed in the ImGui surface actually used
  (contrast the rev-4729 bump, which cost `garrys-torch.lib` a CS8604 — now gone).
- ⚠️ **Note for the next pass:** `ksa-game-assemblies_prev` (`2026.8.5.5168`) was **never validated**.
  Two of this pass's five compile breaks originated in that window. Treat `_prev` as a diff aid only.

---

## Area summary — Update-risk findings (5018 → 5117)

- **No breaking deltas** for the supermod shell or any existing `ksa-abstractions.lib` helper. Every
  patch target is byte-identical: `GameSettings.OnKeyAll(GlfwKeyEvent) → bool`
  (`KSA/GameSettings.cs`, HotkeyGuard → **every** top-level mod),
  `Program.DrawProgramMenusHook()` (MenuBarPatch), `Program.DrawMenuBar(Viewport,int)`,
  `Universe.ExecuteNextVehicleSolvers(double, SimStep)` (still a single overload),
  `PartModel..ctor(PartModelModule.Template)` + `PartModel.AddInstance` (IvaForceRender),
  `KSAColor.Xkcd` (file unchanged).
- **StarMap load-order invariant HOLDS:** `ModLibrary.LoadAll()` (`KSA/Program.cs:965`) still precedes
  `ModLibrary.Bind()` (`KSA/Program.cs:994`), so `[StarMapAllModsLoaded]` still fires before
  `DeviceMeshInterleaved.Shared.Build()`. This is parts-now's headline standing invariant (U1).
- **Brutal packages:** solution builds clean with `TreatWarningsAsErrors` and **0 warnings** against
  the 5117 DLLs, so no nullability/signature shift landed in the ImGui surface actually used
  (contrast the rev-4729 bump, which cost `garrys-torch.lib` a CS8604).

---

## Area summary — Update-risk findings (4680 → 4750)

- **No breaking deltas** for the supermod shell or any `ksa-abstractions.lib` helper. Every game
  target (StarMap-hooked `Program` methods, `Universe.*`, `Program.*`, `GameSettings.OnKeyAll`,
  `CelestialSystem`/`LookupCollection`, `Vehicle`/`Part`/`PartTree`, `KSAColor.Xkcd`, full `PartModel`
  IVA surface) is present in 4750 with an identical signature.
- **Additive only (rev 4699):** `Vehicle.IsControllable` and `PartTree.Controls` are new in 4750
  (absent in 4680). Not consumed by the seam library → no break. Behavioral watch-area only:
  game control is now gated on a Control Module.
- **Secondary watch-area — Brutal packages (rev 4729, "latest Brutal packages, possible ImGui
  nullability/signature shifts"):** the shell's UI (`Mod.cs`), `UnscienceState` ini I/O, `SubmodUI`,
  and `HotkeyGuard.WantTextInput` all ride Brutal.ImGuiApi. The solution **builds clean against the
  4750 DLLs** (recon task #7), so no signature break in the ImGui calls actually used; flag for
  re-check on each Brutal bump.
- **IvaForceRender survived the mesh/shader churn (rev 4693/4745):** its `PartModel` instance-list /
  `Template.Internal` API is unchanged.
- **Coverage gap CLOSED (Phase 4):** the unscience supermod now applies `IvaForceRender.Patch`
  (`unscience/Patcher.cs`), so the ctor/`AddInstance` postfixes run in supermod mode too — not just the
  direct `Enabled`-setter mutation path. (Previously only the standalone kitchen-sink mod applied it.)
- **Patch chain hardened (Phase 4):** `unscience/Patcher.cs` now applies/removes each feature's patches in
  isolation (per-feature try/catch — `TryApply`/`TryRemove`), so a single feature failing to patch logs and is
  skipped instead of aborting every feature after it. This was prompted by the camera `___Transform` defect
  (see `camera.md`; the injector was **retired @5261**), whose patch-time throw had been silently aborting the rest of the chain in the supermod.
- **Highest residual runtime risk lives in the consolidated patch classes owned by other submods**
  (string-named lookups: camera `"OnFrame"`, glass `"ChangeFieldOfView"`/`"UpdateProjection"`/`_fovRadians`,
  i-feel-seen `"GetWorldMatrix"`/`"UpdateRenderData"`). They are cross-referenced above; their decomp
  deltas are catalogued in the respective feature `scope/` files. The two patches owned by this area
  (inline `EternalFlamePatches` → `Universe.ExecuteNextVehicleSolvers`; `MenuBarPatch` →
  `Program.DrawProgramMenusHook`) are verified clean.

---

## Area summary — Update-risk findings (5261 → 5348)

- ✅ **The shared provider chokepoint is unchanged.** `Universe.CurrentSystem`
  (`public static CelestialSystem? CurrentSystem { get; private set; }`) → `CelestialSystem.All`
  (`LookupCollection<Astronomical>`) → `LookupCollection<T>.UnsafeAsList()` all diff clean, so
  `VehicleProvider`, `CelestialProvider` and every feature mod's UI that reaches vehicles/celestials
  through them are safe.
- ✅ **`HotkeyGuard` clean.** `GameSettings.OnKeyAll(GlfwKeyEvent)` is unchanged, and so is the
  `Program.OnKey` call chain it sits in — so the guard still blocks game hotkeys for **every** top-level
  mod.
- ✅ **StarMap's seams are present.** `Program.OnDrawUiFrame`, `Program.OnFrame` and
  `Program.DrawProgramMenusHook` all still exist, so the suite's load path is intact. Rev 5332 changed
  `Program.DrawMenuBar` only by gating the Save/Load `MenuItem` on `!IsEditorOpen`; unscience's
  `MenuBarPatch` (a `DrawProgramMenusHook` prefix) is unaffected.
- ⚠️ **`ReflectionHelpers` has no property fallback — and rev 5329 made that matter.**
  `GetFieldValue`/`SetFieldValue` call `Type.GetField` only. Rev 5329 split `IPartParent` out of `Module`
  and moved `Parent` from a `Module<T>` **field** (`public required Part Parent;`) to a
  `ModuleBase.Parent` **auto-property** (`public required Part Parent { get; set; }`).
  **Audited: no mod in the suite reflects on `Parent`,** so nothing broke — but this is the exact shape of
  failure `ReflectionHelpers` cannot survive, and it should be the first thing checked whenever a game
  refactor moves members between base types. Consider adding a property fallback.
- ✅ **`SimTimeProvider` clean.** `Universe.GetElapsedTime() : UniverseTime` and `.Seconds()` are
  unchanged from the 5261 migration.
- ✅ **`IvaForceRender` clean.** `PartModel..ctor(PartModelModule.Template)`, `PartModel.AddInstance`,
  `PartModel.Instances`, `PartModel.ViewportData.Get`, `PartModelModule.Template.Internal` and
  `CameraMode.IVA` all resolve unchanged. Rev 5312 added receive-only raytracing for IVA kittens — worth
  a live look, not a code change.
- ✅ **`PartHelpers` clean.** `Part`, `PartTree`, `Part.Modules`, `Part.SubParts`, `Part.Asmb2ParentAsmb`
  and `Part.PositionParentAsmb` are unchanged. Note rev 5329 **removed** `Part.Sequence`,
  `SetSequence(int)`, `ActivateInStage`, `DeactivateInStage` and `ScaleTotal` — **no unscience code
  referenced any of them**, confirmed by the green build and by grep.
- ✅ **`XkcdColorHelper`, `EasingHelper`, `KsaPaths`,
  `SubmodUI`, `UnscienceState`** — no breaking deltas; the whole solution builds with
  `TreatWarningsAsErrors` on and **0 warnings**, so no Brutal/ImGui nullability shift landed in the
  surface the suite uses.
- ✅ **`IvaForceRender.Patch` IS wired in the supermod** (`unscience/Patcher.cs:74`, unpatch `:114`) — an
  earlier draft of this summary said "still open"; that was stale (the Phase-4 wiring predates 5348).

---

## Area summary — Update-risk findings (5348 → 5402)

Span note: only rev **5401** ("Fixed crash for incorrect data stride for thumbnail rendering") is
logged in `version.json`; revisions **5349–5400 are unlogged**, so the source diff (~197 changed
`KSA/*.cs` files) is the only evidence for this span. Verified against the macOS trees in the header.

- 🔴 **One compile break, fixed: `IvaForceRender.AddInstancePostfix` param type.** The game replaced
  the `Viewport` class with `IViewport`/`IGameViewport`/`ViewportBase`/`ViewportRegistry`
  (`Program.Viewports` list removed). `PartModel.AddInstance` (`KSA/PartModel.cs:408`) and
  `PartModel.ViewportData.Get` (`:314`) now take `IViewport`, so the postfix's `__1` had to become
  `IViewport` (`ksa-abstractions.lib/IvaForceRender.cs:98`) — Harmony requires an assignable type and
  the old `Viewport` symbol no longer exists (CS0246). `Program.MainViewport` is now `IGameViewport`
  (`KSA/Program.cs:485`) and `.Mode` is an interface property (`KSA/IViewport.cs:29`) rather than a
  field; both are compile-bound reads, so no further change. Solution builds clean against 5402.
- ✅ **Applied — `AddInstancePostfix` now mirrors both of the original's viewport gates**
  (`ksa-abstractions.lib/IvaForceRender.cs:107-108`). `PartModel.AddInstance` early-returns when
  `!viewport.HasAny(ViewportOptionFlags.RenderPartModels)` (`KSA/PartModel.cs:410-413`), and a Harmony
  postfix still runs after that `return`, so the mod would have pushed an internal instance into a
  `ViewportData.InstanceList` the game never drains for such a viewport; the postfix now returns on the
  same condition. The IVA test also moved from `Program.MainViewport.Mode` to `__1.Mode`, matching the
  original's now per-viewport check (`:415`, `viewport.HasAll(UseRaytracing) && viewport.Mode == IVA`;
  trailing gate `:424`) — reading the main viewport would double-add for a secondary viewport that is
  itself in IVA. Both are behaviour-preserving today: every viewport the game builds carries
  `RenderPartModels` (`KSA/Program.cs:948,949,952,956`; `KSA/ViewportPresets.cs:5-11`) and the editor
  drives the main viewport. **Still wants a live look** in the editor with Force IVA on.
- ✅ **`HotkeyGuard` clean — and the `OnKey` restructure does not weaken it.** `KSA/GameSettings.cs` is
  byte-identical (`OnKeyAll` `:3301`). `Program.OnKey` (`:1718`) split its guard chain: the first `if`
  (`:1723`) still starts `!IsLoaded || GameSettings.OnKeyAll(e) || …` and returns; camera-mode /
  controller key handling moved to a second `if` at `:1727-1731` on `InputViewport`. Because the guard
  forces `OnKeyAll` to return `true`, the first `if` still short-circuits and the second never runs while
  typing.
- ✅ **StarMap seams intact, bodies drifted.** `OnDrawUiFrame` (`:3021`) gained only
  `PartContactLoadDebug.Draw()`. `OnDrawUiViewports` (`:3051`) now iterates `ViewportRegistry.GameViews`
  and draws only non-main viewports with `HasUi` inside a `FrameScope`; `OnDrawUiConsole` (`:3009`) uses
  `HoveredViewport.IsMain()`/`.ImGuiId`. All three keep `private void (double)` and the `if (DrawUI)`
  placement in `OnFrame` (`:2191-2201`), so `[StarMapBeforeGui]`/`[StarMapAfterGui]` and the
  `HiddenUiFrameHook` phase contract hold; `ImGui.Render()` is still after them (`:2212`).
  `DrawProgramMenusHook` (`:3876`) is still the empty hook, called from `DrawMenuBar` (`:3863`).
- ✅ **Provider chokepoint unchanged.** `Universe.CurrentSystem` (`:94`) → `CelestialSystem.All`
  (`:64`) → `LookupCollection.UnsafeAsList()` (`:210`, file identical); `Astronomical.Id` (`:104`);
  `Program.ControlledVehicle` (`:503`, a property since before 5348 — the old "field" wording in the
  table was stale). `CelestialSystem.cs` did change (`AstronomicalRef` hash-validated lookups and the
  new `PartPicker` in `OnDrawUi`) but nothing the providers touch.
- ✅ **`EternalFlamePatches` / `Universe.ExecuteNextVehicleSolvers` (`:1834`)** — signature and body
  byte-identical, still the single overload; `SimStep.cs` identical. `SimTimeProvider` clean
  (`GetElapsedTime` `:2114`, `UniverseTime.cs` identical).
- ✅ **`PartHelpers` clean**, but note the parachute/structural-limits additions around it:
  `Vehicle.Parts` (`:604`, property), `PartTree.Parts` (`:95`), `Part.SubParts` (`:1079`) unchanged;
  `PartTree.UpdateRenderData` now also renders `Parachute` lines (`KSA/PartTree.cs:937-945`) and `Part`
  gained `InertMassKg`/`CrashTolerancePascals`/`StructuralPart`/`IsAttachedInternal`. No consumer of
  this library reads any of them.
- ✅ **`XkcdColorHelper`, `SubmodUI`, `UnscienceState`** — `KSAColor.cs`,
  `Brutal.Numerics/Color.cs`, `Brutal.ImGuiApi/{ImGuiCol,ImGuiStyle}.cs` and
  `ConsoleWindow.cs` are byte-identical; **no `Brutal*` file appears in the diff list** (the Brutal DLLs
  differ only by hash at identical size — a rebuild). `ModLibrary.cs` changed only at `:565-568`
  (`Program.Viewports` → `ViewportRegistry.Views`); `LoadAll()` (`Program.cs:942`) still precedes
  `BuildRenderTargets()` (`:970`) and `Bind()` (`:978`).
- ℹ️ **Knock-on for other areas, recorded here because the shell applies their patches:** every render
  prefix target that took `Viewport` now takes `IViewport` (`PartModelModule.UpdateRenderData`
  `KSA/PartModelModule.cs:87`, `PartTree.UpdateRenderData` `:912`, `PartModel.AddInstance`), and
  `PartModelModule.UpdateRenderData` swapped its light-switch bit logic for the new
  `Part.IsLightSwitchedOff()` (`KSA/Part.cs:1357`). See the pixel-grids, humble-arteest, i-feel-seen and
  thug-life scope files.
- **Live pass wanted:** F2 hidden-HUD replay (HiddenUiFrameHook) once with the new viewport code; Force
  IVA in the editor (above); Unscience menu item still appears under the game menu bar.

## Pebbles lifecycle integration

`Mod.OnFullyLoaded` instantiates `PebblesSubmod : ISubmod` and passes its `ClutterController`
to `Patcher.PebblesController`. The shared Harmony instance applies/removes its hooks through
`ApplyPatches`/`RemovePatches`. Removal is restricted to Pebbles' patch methods, never the entire
shared owner. Ordinary updates handle discovery, scene changes and deferred resource release;
the `Universe.ExecuteNextClothSolvers` prefix commits pending native transactions.
`RenderContent` includes applied-state restoration, and `RenderFloatingWindows` owns the GLB
browser and collider editor. Unload retires its resources before patch removal.
The host's HotkeyGuard and hidden-HUD updates remain unchanged. Exact targets and private
member dependencies: [ground clutter](ground-clutter.md), [GLB conversion](ground-clutter-glb-materials.md).

### Shared pre-physics mutations

`PhysicsFrameHook` owns the unchanged validated PrepareFrame transpiler formerly in Garry's Torch.
Godzilla enqueues vessel scaling/restoration; actions drain before `BeforePhysics` listeners (welds
and session cleanup), after prior results and before next snapshots. Reentrant enqueues wait one
frame; absent systems and removal clear pending work. Exceptions are isolated. Hosts install once:
Unscience uses `GarrysTorchPatches.Apply/Remove`, which delegates and subscribes the weld listener;
the Godzilla development host installs the shared hook directly. Godzilla uses the existing kitten
axis postfix as well. Both hosts also install `VisualScalePatches` for render-only transforms and
visual pixel culling, plus `ColliderScalePatches` for independent native shape scaling and nominal
bounds preservation. Both channel toggles queue current-session conversion through the same handoff.
`VehicleScaleOwnership` prevents two tools from owning source scale; keys are
weak and release checks the owner name. See vehicle-physics for typed integration and live checks.

Since 5482 the handoff also offers `PhysicsFrameHook.JoinOrbitReaders()` (`PhysicsFrameHook.cs:29-34`): a
once-per-frame wait on `JobSystems.NearestOrbitAndPerformanceWorker`, whose nearest-orbit job
(queued at `Program.cs:2188-2192`, before the handoff) reads the flight plans and cached orbit points that
`Vehicle.Teleport` disposes. It runs automatically before a deferred world change and before draining
queued mutations (when any), and explicitly before weld teleports (`WeldEngine.cs:120`) and Dent Wizard
launches (`DentWizardSubmod.cs:48`); idle frames do not wait. The game's new `PartTree.FlushDirtyDerived` /
`FlushDirtyResourceManagers` calls (`Program.cs:2209-2210`) run after the handoff and before
`ExecuteNextVehicleSolvers`, so derived data dirtied by handoff mutations is rebuilt in the same frame.

### Shared media import and BYO Music

`SharedFileLibrary`/`LibraryFileBrowser` generalize the existing PNG copy/catalog/browser flow;
`PngLibrary`/`PngFileBrowser` retain their APIs and `SoundLibrary` supplies `.unscience/sounds`.
BYO Music is now an Unscience ISubmod, updating owned audio in the shell's existing visible/hidden
HUD lifecycle. No additional shell patch. See [audio](audio.md) for native FMOD dependencies.

## Sphinx host addition

Unscience registers `SphinxSubmod` and references sphinx.lib, which deliberately references
pebbles.lib for GLB/material conversion and shared GPU upload helpers. Patcher TryApply/TryRemove
calls `SphinxPatches.Apply/Remove` on the consolidated Harmony instance. The feature owns only its
three StaticObjectRenderer postfixes and session resources; existing HotkeyGuard and hidden-HUD
Update dispatch cover it. The copied standalone host remains development-only. See [statics.md](statics.md).

## Scene persistence lifecycle

`UnscienceSaves` discovers all bundled `ISaveParticipantSource` adapters after submod initialization
and wires callbacks before `Patcher.Patch`. It correlates captured DTOs with their exact `GameSave`
object using a weak table, displays save/restore diagnostics in the existing toolbox, and disconnects
callbacks on disposal. Window-layout autosave remains separate. `SceneSaveCoordinator` performs
reverse reset / ordered replay, preserves failed/unknown records, and offers explicit replacement
with current setup on future saves. Since 5482 a native write that returns `false` routes to
`NativeSaveHooks.WriteFailed` (capture discarded, failure reported, no sidecar), and an unreadable save
is reported through `LoadFailed` instead of passing silently. Kitchen Sink persists `IvaForceRender.Enabled`; one-shot editor
refresh activity is not replayed; G-load protection saves vehicle IDs in a separate record and rebinds after reset/reconstruction.
Skittles restores the exact detached scene style and resets
to startup configured theme between scenes. See [saves](saves.md) for new Harmony/worker seams.

### Dent Wizard host integration

`unscience/Mod.cs` registers `DentWizardSubmod`; `unscience.csproj` explicitly bundles its library.
The host ensures `PhysicsFrameHook.Apply` under isolated patch application. Dent Wizard subscribes
`BeforePhysics` at initialization, consumes at most one pending shot, and unsubscribes on disposal.
Its standalone template-derived development host owns `HotkeyGuard` and `PhysicsFrameHook`.
Existing StarMap BeforeGui/AfterGui/Unload and floating-window dispatch suffice. Its transient
`ISaveParticipant` resets gestures before native replacement. See [vehicle scope](vehicle-physics.md#dent-wizard).

### Kitchen Sink G-load patch lifecycle (2026-09-13)

Unscience and the standalone Kitchen Sink host install/remove `GLoadProtectionPatches` through
their own Harmony instance. The new private `PhysicsBubble.DetectStructuralFailure` transpiler
only gates the selected vehicle's G-load decision; see [UI/customization](ui-customization.md#kitchen-sink).
The defunct KitchenSinkSolverPatch/Flexo part-transform diagnostic integration is removed.
Reverified on 5438: the detector and end-frame caller retain their exact bodies; both hosts
also retain upstream's shared IVA common-overload/dent-list fix. No new save lifecycle hook.
Reverified on 5482: detector body still identical (`PhysicsBubble.cs:958`); the shared IVA fix is now the
`PartTreeRenderData.Compose` prefix/finalizer (see IvaForceRender above).
