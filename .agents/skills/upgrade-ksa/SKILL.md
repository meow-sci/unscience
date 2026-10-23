---
name: upgrade-ksa
description: >-
  Validate the unscience mod suite against a new upstream Kitten Space Agency (KSA) game build — the
  break-check playbook run when the game / its decompiled sources are bumped and you must decide what
  unscience needs fixed. Covers how to diff the CURRENT (new) vs PREVIOUS (old) game-assemblies trees,
  build-as-alarm (and the KSAFolder trap that fakes a catastrophic break), the string-reflection
  watchlist that fails silently at runtime, GLSL/byte-offset/render coupling the compiler cannot see,
  semantic drift with no symbol change, the known-broken baseline you must NOT re-report as new, and
  which scope/ + plans/ docs to update in lockstep. Use when asked to "check unscience against the new
  KSA build", "upgrade KSA", run the version-diff / break-check, or review a game update's impact.
  REQUIRES two KSA game-assemblies trees: CURRENT and PREVIOUS.
---

# upgrade-ksa — validate the unscience suite against a new KSA game build

When KSA ships an update, this skill is the operational procedure to (a) find every place the update
*could* have broken unscience, (b) determine whether it *did*, and (c) produce a review that says
clearly **which mods need changes and where**. It is the executable form of the workflow in
[`scope/FULL_SCOPE.md`](../../../scope/FULL_SCOPE.md) → "How to use this on a game update" and
[`AGENTS.md`](../../../AGENTS.md) → "Working against a game update".

The deliverable is an **impact review** first. Only make code changes after the review shows a real
break/drift and the requester confirms the fix approach — with one exception: **compile breaks block
everything** (`AGENTS.md`: a task is not complete until `dotnet build` passes), so surface those
immediately and get approval to fix them first.

---

## 0. Required inputs — resolve these before starting

This skill is **path-agnostic**; the paths below are this machine's convention, not a hard-code.
Confirm each one exists, and ask for whatever is missing.

| Input | What it is | Conventional location | Called below |
|---|---|---|---|
| **CURRENT** assemblies | The **new** build being validated (upgrading *to*) | `…/meow-sci/ksa-game-assemblies` (an additional working dir) | `<NEW>` |
| **PREVIOUS** assemblies | The **prior** build, for diffing (upgrading *from*) | `…/meow-sci/ksa-game-assemblies_prev` | `<OLD>` |
| **Live game install** | What `dotnet build` compiles against **by default** | `C:\Program Files\Kitten Space Agency\` | `<INSTALL>` |
| **Recorded baseline** | The build `scope/` was last verified against | header of [`scope/FULL_SCOPE.md`](../../../scope/FULL_SCOPE.md) + [`game-integration-surface.md`](../../../scope/game-integration-surface.md) | `<BASELINE>` |

Each tree is a game-assemblies checkout with an inner `current/` folder (present in every checkout
regardless of version — produced by `ksa-game-assemblies/copy-ksa.ts` copying `Brutal*.dll`,
`KSA.dll`, `Planet*.dll` out of the install):

```
<NEW>/current/
  version.json   build id + date + fromRevision/toRevision + the per-revision commit log  → step 1
  dll/           reference assemblies (~37) — what a KSAFolder-overridden build compiles against → step 2
  decomp/        decompiled C# (KSA/*.cs, Brutal*/, Planet*/) — the diff target             → steps 3-4
  Content/       game data + GLSL shaders (Core/Shaders/**, part/asset XML)                 → step 5
```

**Before anything else, report these four back to the requester:**

1. `<NEW>` build id and `<OLD>` build id (from each `current/version.json` → `build`).
2. Whether `<INSTALL>` is the same build as `<NEW>` — read `KSA.dll`'s version, or ask. **The default
   build compiles against `<INSTALL>`, not `<NEW>`.** If they differ, either point the build at
   `<NEW>/current/dll` (step 2) or say plainly that the compile check ran against a different build.
3. `<BASELINE>` vs `<OLD>`. **These are frequently not the same.** At the time of writing, `scope/` is
   verified against `2026.6.9.4750` while the trees on disk are far newer — so the honest diff span is
   `<BASELINE>` → `<NEW>`, not `<OLD>` → `<NEW>`, and the intermediate revisions appear in **no**
   `version.json` on disk (see step 1). Say so rather than silently reviewing one hop.
4. Which of the two decomp trees you will treat as authoritative: **the provided trees are**, not the
   in-repo `decomp/ksa` copy (`AGENTS.md` says so explicitly, and that copy is older still).

If only one tree is available you can still do steps 2 + 5 (build-as-alarm and in-game/render
re-verification) but you **cannot** do the drift diff (steps 1, 3, 4) — say that explicitly rather
than skipping it silently.

---

## 1. What bounds the task in *this* repo

Read this before assuming a bounded blast radius: **unscience has no firewall.** Unlike a mod that
funnels game access through one seam, this repo is 25 top-level projects + 26 `.lib` libraries + one
test project (52 projects in `ksa-mod-experiments.slnx`) that touch KSA types **directly, everywhere**, and there is no
per-member attribute in the code marking those touches. Consequences:

- **`scope/` is the source of truth for game coupling**, and it is only as good as its last update —
  which is why `AGENTS.md` makes updating it mandatory in the same change as any integration edit.
  Its four working views:
  - [`scope/game-integration-surface.md`](../../../scope/game-integration-surface.md) — master index:
    **§3** master table by game type (member → kind → decomp path → *Used by* mods → mod `file:line` →
    status), **§4** string-reflection watchlist, **§5** shaders & assets, **§6** confirmed
    broken/changed summary.
  - The 11 per-area files (`vehicle-physics.md`, `celestial-and-lights.md`, `camera.md`, `telemetry.md`,
    `pixel-grids-and-render.md`, `character-and-materials.md`, `part-editor-and-robotics.md`,
    `ui-customization.md`, `standalone-mods.md`, `00-architecture-and-abstractions.md`) —
    per-mod **Integration points** tables (`# | Kind | Mod code file:line | Game target + signature |
    Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes`) plus an **Update-risk findings (OLD → NEW)**
    section each. These tables use the same NEW/OLD vocabulary as this skill.
  - [`scope/FULL_SCOPE.md`](../../../scope/FULL_SCOPE.md) — entrypoint, version baseline, ToC, status.
  - [`plans/FIX_CURRENT_GAPS_PLAN.md`](../../../plans/FIX_CURRENT_GAPS_PLAN.md) — the remediation record.
- **Shared chokepoints fan out to many mods at once.** One change here is a multi-mod break, so check
  them first (per the master index "Watch the Harmony keystones"):

| Chokepoint | Breaks |
|---|---|
| `GameSettings.OnKeyAll` (via `HotkeyGuard`) | **every top-level mod** |
| `Universe.ExecuteNextVehicleSolvers` | eternal-flame, kiwis-marbles, kitchen-sink (+ garrys-torch via `JobSystems.VehicleSolvers.Wait()`) |
| `Universe.CurrentSystem` → `CelestialSystem.All` → `LookupCollection.UnsafeAsList()` (`VehicleProvider`/`CelestialProvider`) | ~every feature mod's UI |
| the three `*Module.UpdateRenderData` prefixes, `PartModel.AddInstance` | blinky, its-so-shiny, humble-arteest, i-feel-seen (`PartModelRenderer.UpdateRenderData` went unowned when flexo was removed) |
| `ksa-abstractions.lib` helpers (`PartHelpers`, `IvaForceRender`, `XkcdColorHelper`) | the mods listed per-helper in `scope/00-architecture-and-abstractions.md` |
| StarMap.API lifecycle attrs + `Program.OnDrawUiFrame`/`OnFrame` hooks it patches | the whole suite's load path (a StarMap release is its own event, not a game update) |

- **Standalone mods still count.** byo-music is **not** loaded by the supermod but remains in the
  solution and can break the same way; it lives in [`scope/standalone-mods.md`](../../../scope/standalone-mods.md).

---

## 2. Procedure

Run in order. Steps 2–5 each catch a **different class** of breakage and none is sufficient alone: a
member can keep its signature and change meaning; string reflection and GLSL anchors never fail to
compile. Record findings as you go for the §6 report.

### Step 1 — Changelog scan (decide what to even look at)

```
<NEW>/current/version.json      → build, date, fromRevision, toRevision, commits[]{rev,date,author,lines[]}
<OLD>/current/version.json      → same, for the previous window
```

**Each `version.json` covers only its own revision window, not cumulative history.** e.g. `<NEW>`
`fromRevision: 4980 → toRevision: 5018` while `<OLD>` covers `4939 → 4980`. So:

- If `<OLD>.build == <BASELINE>`, the delta to review is exactly `<NEW>`'s `commits[]`.
- If `<BASELINE>` is older than `<OLD>` (the usual case here), the revisions between `<BASELINE>` and
  `<OLD>.fromRevision` are in **no file on disk**. Review `<OLD>`'s + `<NEW>`'s commit lists together,
  state the still-unlogged gap in the report, and lean harder on steps 3–5 (which compare source, not
  changelogs, and therefore cover the gap regardless).

Flag every commit line touching a subsystem unscience couples to:

| Changelog keywords | Mods at risk | Area file |
|---|---|---|
| `Vehicle`, `Teleport`, `RefillConsumables`, solver/`JobSystems`, `IsControllable`, controllability gating | eternal-flame, garrys-torch, i-feel-seen, kiwis-marbles, doh, kitchen-sink | `vehicle-physics.md` |
| electrical, `Battery`, `Joules`/`Energy…`/`Power…`, `SolarPanel`, `Generator`, `PowerConsumer` | eternal-flame, its-so-shiny | `vehicle-physics.md`, `part-editor-and-robotics.md` |
| `Celestial`, `Orbit`/`SetOrbit`, `IOrbiter`, `ShowOrbit`, orbit lines, SOI | kiwis-marbles | `celestial-and-lights.md` |
| `LightModule`, `LightSwitch`, `ColorRgb`/`FloatReference`, `KeyframeAnimationModule` | zippo, its-so-shiny | `celestial-and-lights.md` |
| `Camera`, `Controller`/`OrbitController`/`FlyController`, FOV, `Viewport` | glass, camera-controller-override | `camera.md` |
| `Part`/`PartTree`/`PartTemplate`, connectors, face-snapping, editor tags/categories, thumbnails, part size, **editor scale clamp / scale gizmo** | dont-stifle-me, blinky, its-so-shiny, kitchen-sink | `part-editor-and-robotics.md` |
| render pass, shaders/GLSL, `SuperMeshRenderSystem`, `PerInstanceData`, `MaterialData`, `GpuMaterialSystem`, MSAA, Vulkan | **thug-life, humble-arteest, doh, blinky, its-so-shiny** | `pixel-grids-and-render.md`, `character-and-materials.md` |
| `KittenEva`, `CharacterAvatar`, animations, `CatExpressionAnim`, `EVADoor`, characters | doh, kitten-animations, garrys-torch | `character-and-materials.md` |
| `GaugeCanvas`, HUD, gauges, menu bar / `View` menu / file bar | unscience `MenuBarPatch` | `ui-customization.md` |
| `ImGui`, `Brutal.*` package bump, nullability | **any mod** — see the `TreatWarningsAsErrors` note in step 2 | wherever it lands |
| `ModLibrary` asset ids, part templates, meshes, FMOD/audio, substances | blinky, its-so-shiny, thug-life, doh, humble-arteest, byo-music | master index §5 |

> A changelog line that moves **no symbol** can still break a mod. Treat behavior and UX changes as
> real integration changes even when every referenced member remains source-compatible.

### Step 2 — Build against CURRENT = the alarm system

Build the **whole solution** — there is no game-free subset worth excluding:

```powershell
dotnet build ksa-mod-experiments.slnx
```

That compiles against `<INSTALL>` (`Directory.Build.props` → `KSAFolder`, default
`C:\Program Files\Kitten Space Agency\`). To compile against a specific tree instead, override
`KSAFolder` — it is env-var/`-p:`-overridable because the props file only sets it when empty:

```powershell
$env:KSAFolder = 'C:\…\ksa-game-assemblies\current\dll\'   # NOTE the trailing \
dotnet build ksa-mod-experiments.slnx
```

> **⚠ The trailing separator is mandatory and its absence is a trap.** Every `<Reference>` is guarded
> by its own `Condition="Exists('$(KSAFolder)<Assembly>.dll')"` and the `HintPath` is the same raw
> `$(KSAFolder)<Assembly>.dll` concatenation — no path joining, no separator inserted. Drop the
> trailing `\` (or point at a wrong folder) and every game reference **silently disappears** — you get
> a flood of `CS0246: The type or namespace name 'KSA'/'Brutal' could not be found` that reads like a
> total game rewrite. Verified failure mode: with the separator the same project builds clean; without
> it, `ksa-abstractions.lib` alone emits 11 CS0246s. **A wall of CS0246 on `KSA`/`Brutal` namespaces
> means a bad `KSAFolder`, not a break — fix the path and rebuild before reporting anything.**

Two more build-time facts specific to this repo:

- **The game holds a lock on the deploy folder.** Every mod csproj copies its output to
  `$(SelectedDistModDir)<mod>\` (= `%USERPROFILE%\Documents\My Games\Kitten Space Agency\mods\`) in an
  `AfterBuild` target. If KSA is running, the *copy* fails and masks the compile result. Redirect it:
  `$env:KSAUserDir = 'C:\…\scratchpad\dist\'` (it is `Condition`-guarded the same way), or close KSA.
- **`TreatWarningsAsErrors` is on** repo-wide (`Directory.Build.props`), so a Brutal/StarMap
  **nullability** change is a hard build break in ordinary UI code with no KSA member involved.
  Precedent: the rev-4729 Brutal bump made `ImString.AppendFormatted` non-nullable and broke
  `garrys-torch.lib` with CS8604 on a plain `ImGui.Text($"…")`. Classify these as *toolchain drift*,
  not game-API drift — the fix is usually a null-coalesce, not a relocation.

**Every remaining compile error is a renamed/removed/retyped member some mod binds to; that error list
is the first work list.** Map each to its `scope/` row and mod.

> **A green build does NOT mean unscience is safe** — it clears only the typed, compile-visible
> bindings. Steps 3–5 exist for everything the compiler cannot see, and in this repo that is the
> majority of the highest-risk surface.

### Step 3 — String/reflection watchlist (highest silent-break risk — do this even on a green build)

`AccessTools`/`System.Reflection`/`Traverse` lookups by **string** are not compile-checked: a rename
turns them into a silent runtime no-op, a null, or a swallowed exception at patch-install time. This
repo has ~25 such entries and both `FULL_SCOPE.md` and the master index put them **first**.

Work
[master index §4 "String-based reflection watchlist"](../../../scope/game-integration-surface.md)
top to bottom. For each entry, confirm the exact name still exists in `<NEW>` and compare against
`<OLD>`:

```powershell
# member/field names (also grep <OLD> to prove whether a miss is new or pre-existing)
rg -n '_fovRadians|_expressionPose|_renderable|_characterAvatar' `
   '<NEW>\current\decomp\KSA'
# whole-type / nested-type names used as strings
rg -n 'class KittenEva|KSA\.LightModule\+TemplateData|struct TemplateData' '<NEW>\current\decomp'
```

Highest-value single checks in this suite, all of which fail **silently**:

- `Camera._fovRadians` (glass — rename ⇒ FOV override dies quietly).
- The avatar chain `KittenEva` (compared by **type-name string**) → `_renderable` →
  `_characterAvatar` → `CharacterAvatar.Core` → `CharacterCore.Scale` (garrys-torch, doh,
  kitten-animations). `Core` must stay a **value-type field** for garrys-torch's `SetValue` to work.
- `"KSA.LightModule+TemplateData"` hard-coded nested-type name (zippo — rename ⇒ zero light parts).
- The doh/humble-arteest render-system bridge (`Program.MaterialSystem`/`SuperMeshRenderSystem`/
  `CharacterRenderSystem`, `GpuObjectSystem.{BigBuffer,DeviceCtx,CreateObject}`, `AssetManager.*`).
- `ShaderReference.{Shader,+k__BackingField,DoLoad,ModPath,LocalPath}` and
  `RenderCore.ShaderModuleUtils.FromFile` (humble-arteest paint — cross-assembly private names).
- `ModLibrary.AllParts`/`AllCharacters` internals (doh).
- Harmony **field injectors** (`___Name` parameters) and any patch resolved with an explicit overload
  param array: these throw at `Apply` time and are typically swallowed, so the feature is simply inert
  with no crash. `Controller.___Transform` is the standing example (§5).

Note in the report, per entry, whether a miss is **new in `<NEW>`** or **pre-existing in `<OLD>` too**
— the area files already record which are which, and mislabelling a long-standing bug as an update
regression sends the fix work to the wrong place.

### Step 4 — Typed touchpoints: signature *and* semantic drift

For every subsystem flagged in step 1, open the area file's Integration points table and diff the
cited decomp file in both trees:

```
<NEW>/current/decomp/KSA/<File>.cs      vs      <OLD>/current/decomp/KSA/<File>.cs
```

A pure line-number shift is fine (the tables' `Δ vs OLD` column already expects that). Look for:

- **Renamed/removed/re-signatured members** — cross-check against step 2's error list; anything the
  compiler flagged should have a row here, and anything here that *didn't* fail the build deserves a
  second look (it may be reached by reflection instead).
- **Quantity/unit type swaps — a high-value catch.** Similar swaps can also change serialized output,
  which fails at runtime rather than compile time, so check both sides.
- **New gating / changed preconditions** that alter when a read or write is valid — e.g. the rev-4699
  `Vehicle.IsControllable` + `PartTree.Controls` work. Compiles clean, behaves differently.
- **Frames and numerics** — CCI/CCE/CCF/ECL conventions and `Brutal.Core.Numerics`
  (`double3`/`doubleQuat`). A convention change silently corrupts every derived value in garrys-torch
  (`GetCci2Cce`), kiwis-marbles (`GetPositionCci`/`GetVelocityCci`), thug-life (ego-space MVP) and
  camera-controller-override.
- **Struct layout / byte offsets** — `MaterialData` (`AlbedoColor` at offset 16, stride 80) for
  doh/humble-arteest, and `PartModel.PerInstanceData` padding for humble-arteest. Diff the
  `[StructLayout]` declarations byte-for-byte; a field added anywhere before the offset moves the
  write target with no compile error.
- **Content XML** — `<NEW>/current/Content` vs `<OLD>/current/Content` for the ids and attributes in
  master index §5 (part templates like `CorePropulsionA_Prefab_EngineA1..A6` / `LightPart`, meshes,
  characters, substances, and sounds), plus any schema drift affecting live asset consumers.

> **Decomp can lag the shipping binary.** If a member looks present but a read returns null/`-1` or a
> reflection lookup misses in game, the DLL wins — dump the real structure at runtime with the
> approach in the ksa skill [`debug.md`](../ksa/debug.md).

### Step 5 — Render, GPU and shader coupling (deepest, highest-churn — re-verify every update)

Render internals churn faster than gameplay APIs and are **not reliably changelog-covered**, so check
this set even on a clean changelog and a green build. Sources:
[master index §5](../../../scope/game-integration-surface.md),
[`pixel-grids-and-render.md`](../../../scope/pixel-grids-and-render.md),
[`character-and-materials.md`](../../../scope/character-and-materials.md).

| Coupling | Consumer | What to diff |
|---|---|---|
| `SuperMeshRenderSystem.RenderMainPass` postfix; `UnlitMeshVert`/`UnlitMeshFrag` via `ModLibrary.Get<ShaderReference>`; offscreen MSAA pass; `Camera.MVP.viewProjection`; `Part` ego transforms | **thug-life** (own Vulkan pipeline, descriptor, VB/IB, texture upload) | the patch target's signature, the two shader ids in `Content/Core/DefaultAssets.xml`, `Content/Core/Shaders/Mesh/UnlitMesh.*`, sample count / reverse-Z assumptions |
| Runtime **GLSL text editing** by anchor string (`ShaderModuleUtils.FromFile` + `ShaderReference` swap) | **humble-arteest** Vehicle Paint | `Content/Core/Shaders/Mesh/MeshIndirect.frag` in both trees — anchor strings, struct declarations, `#ifdef ENABLE_*` variants |
| `PerInstanceData` padding-byte hijack (bytes 68–79) via `PartModel.AddInstance` prefix | **humble-arteest** | the struct's field list/offsets, and whether the game started **using** a byte the mod writes |
| `GpuMaterialSystem.BigBuffer` staged Vulkan writes at `handle*80+16` | **doh**, **humble-arteest** Kitten Color | `MaterialData` layout + `ModelPbr.frag`/`Common/MaterialSet.glsl` albedo path |
| Temperature/TFI emissive LUT read (`#ifdef ENABLE_TEMPERATURE`) | **humble-arteest** Engine Emissive | which shader file hosts the LUT (it *moved* files in rev 4693 and the feature survived) |
| `*Module.UpdateRenderData` render-skip prefixes; `PartTree.CreateFromNewPartTree`; `EngineController.SetIsActive` | **blinky**, **its-so-shiny**, **i-feel-seen** | patch target signatures + per-frame cost assumptions |
| `GenericGizmo` (scale gizmo segment data) | **dont-stifle-me** | ctor/render-data shapes. `OrbitLinePass` is **unowned** since flexo's removal — nothing left to check |

Runtime safety net is uneven here: some features self-disable on detection, others just draw wrong.
**A silently mis-drawn quad, an unpainted part, or a grid that renders at the wrong scale is only
caught by a live in-game pass** (§6). Thug-life render internals are documented in the ksa skill
[`quad.md`](../ksa/quad.md).

### Step 6 — Fix and re-document in lockstep (only for confirmed findings)

Compile breaks: fix them (the repo must build). Everything else: confirm the approach with the
requester first. Then, **in the same change** (`AGENTS.md` scope/ maintenance is MANDATORY):

1. Fix the mod code, keeping the touchpoint's `file:line` citations in `scope/` accurate.
2. Update the owning **area file** row (`In NEW?` / `Δ vs OLD` / risk notes) and its **Update-risk
   findings** section, retitled to the new build pair.
3. Update the **master index**: the §3 row status, §4 watchlist status, §5 asset/shader status, the §6
   broken/changed summary, **and the "Verification baseline" header**.
4. Update [`scope/FULL_SCOPE.md`](../../../scope/FULL_SCOPE.md): the **Version baseline** block
   (cataloged-against / diffed-from / how verified) and the **Current status** summary — keeping it
   short, per the "keep FULL_SCOPE small" rule.
5. Record remediation in `plans/` (extend or supersede
   [`plans/FIX_CURRENT_GAPS_PLAN.md`](../../../plans/FIX_CURRENT_GAPS_PLAN.md)), and reconcile
   [`ISSUES.md`](../../../ISSUES.md) if an entry there is now explained or fixed.
6. Update [`REPOSITORY_INDEX.md`](../../../REPOSITORY_INDEX.md) and the mod's `README.md` **iff
   user-visible behavior changed** (e.g. a feature now self-disables on this build) — required by
   `CLAUDE.md`/`AGENTS.md` whenever a mod is modified.
7. `dotnet build` green. **There is no test suite in this repo** — no test projects exist, so
   `dotnet build` plus a live in-game pass is the whole verification story. Do not imply otherwise in
   the report.

---

## 3. Surface checklist — every mod, its dominant coupling, its area file

Use this so nothing is missed. Detail (with `file:line` and decomp paths) lives in the area files.

| Mod (+ `.lib`) | Dominant KSA coupling | Area file |
|---|---|---|
| **unscience** (supermod shell) | StarMap lifecycle attrs, consolidated Harmony instance, `MenuBarPatch`, inlined `EternalFlamePatches` | `00-architecture-and-abstractions.md` |
| **ksa-abstractions.lib** | `VehicleProvider`/`CelestialProvider`/`SimTimeProvider`/`PartHelpers`/`HotkeyGuard`/`IvaForceRender`/`XkcdColorHelper` — fans out to nearly everything | `00-architecture-and-abstractions.md` |
| eternal-flame | `Universe.ExecuteNextVehicleSolvers` prefix, `Vehicle.RefillConsumables`, `Battery.Refill(ref BatteryState)` | `vehicle-physics.md` |
| garrys-torch | `Vehicle.Teleport` per-frame, `JobSystems.VehicleSolvers.Wait()`, KittenEva/`CharacterCore.Scale` reflection | `vehicle-physics.md` |
| i-feel-seen | `Vehicle.GetWorldMatrix`/`UpdateRenderData` prefixes, `Camera.GetPositionEgo` | `vehicle-physics.md` |
| kiwis-marbles | `Celestial.SetOrbit`/`UpdatePerFrameData`, `IOrbiter` CCI reads | `celestial-and-lights.md` |
| zippo | `LightModule+TemplateData` reflection (`Intensity`, `ColorRgb`), `ColorRgbReference.OnDataLoad` | `celestial-and-lights.md` |
| glass | `Camera._fovRadians` (private), `ChangeFieldOfView`/`UpdateProjection` prefixes | `camera.md` |
| camera-controller-override | `OrbitController`/`FlyController.OnFrame` prefixes + field injector | `camera.md` |
| blinky | runtime part creation + `PartTree.CreateFromNewPartTree`, `EngineController.SetIsActive`, render-skip prefix | `pixel-grids-and-render.md` |
| its-so-shiny | `LightPart` template, `Connection.Connect`, battery anchors, render-skip prefix | `pixel-grids-and-render.md` |
| thug-life | **own Vulkan pipeline** + `SuperMeshRenderSystem.RenderMainPass` postfix + `UnlitMesh` shaders | `pixel-grids-and-render.md` |
| doh | `KittenEva` ctor, GPU material cloning, `MaterialData` byte writes, fur/attachment `MaterialIndices` | `character-and-materials.md` |
| humble-arteest | **GLSL text-edit shader swap**, `PerInstanceData` byte hijack, emissive LUT | `character-and-materials.md` |
| kitten-animations | `AnimatedRenderable.SetAnimation`, `CatExpressionAnim._expressionPose` cache bust | `character-and-materials.md` |
| skittles | `ImGui.GetStyle()` only — no Harmony, no KSA types (still exposed to Brutal.ImGui churn) | `ui-customization.md` |
| kitchen-sink | `ReinitializeDerivedValues`, `IvaForceRender` template mutation + ctor patch | `ui-customization.md` |
| byo-music *(standalone)* | `ModLibrary.Get<MusicPlayList>` / FMOD | `standalone-mods.md` |
| fixme-mod-name | template only — canonical `HotkeyGuard` wiring reference | — |

---

## 4. Priority order

1. **String-reflection watchlist (§4 of the master index)** — silent, and the largest cluster of risk.
2. **Render/GPU/GLSL/byte-offset set** — silent, highest churn, needs a live pass to clear.
3. **Compile breaks** — loud and fast, but they block the build, so fix in parallel with 1–2.
4. **Semantic drift** on typed members (units, gating, frames) — quiet and easy to miss.
5. **Asset/XML ids and schemas** — runtime-only failures (missing id ⇒ `ModLibrary.Get` throws).
6. **Cosmetic/typed-stable rows** (e.g. `KSAColor.Xkcd` accents) — lowest.

---

## 5. Known-broken baseline — do **not** re-report these as new regressions

Recorded against `<BASELINE>` (`2026.6.9.4750`) in master index §6 and `FULL_SCOPE.md`. Re-check
whether each has since been **fixed** (the repo has had "updates for latest ksa" commits since), then
carry it forward or close it — but never file it as caused by the new build:

- **camera-controller-override** — `Controller.___Transform` Harmony field injector targets a field
  that does not exist on KSA controllers (the field is `Camera`); `Apply` throws and is swallowed, so
  the animation prefix never attaches. Pre-existing. Also surfaces as an inert
  Intended fix: inject `Camera ___Camera`.
- **zippo** — `GetField("Color")` on `LightModule+TemplateData`; the real field is `ColorRgb`, so
  colour get/set is a silent no-op. Pre-existing.
- **humble-arteest Vehicle Paint** — dead by design change: rev 4693 moved part-colour compilation to
  `ShaderReference.CompileVariantWithCustomOptions()`, which recompiles `MeshIndirect` from disk per
  `ENABLE_*` variant and ignores `ShaderReference.Shader`, so the module swap can never take effect.
  Feature **self-detects and disables**. Engine Emissive and Kitten Color are unaffected. Reviving it
  is a redesign, not a patch.
- **garrys-torch** — CS8604 from the rev-4729 Brutal nullability bump (toolchain, not KSA API).
- **unscience supermod** — `Patcher.cs` never wires `IvaForceRender.Patch`, so kitchen-sink's IVA
  force-render is partial inside the supermod.
- **Behavioral watch items** — `Vehicle.IsControllable` gating (4699); editor tag/category schema
  ("Interstage" removed, "Stages"→"Resource Groups", tags moved to XML — 4731/4732/4741);
  face-snapping/connector rules (4687–4740); part-size XML (4721).
- [`ISSUES.md`](../../../ISSUES.md) additionally carries **user-reported** breakage (blinky, eternal
  flame refill during burns, garrys-torch error spam, kitten-animations repeating one
  expression). Treat these as prior art when triaging, and check whether the new build explains any.

---

## 6. When you must go beyond the sources

This repo has **no automated verification of game integration**: no test projects, and no runtime
accessor-health surface. Two classes of finding therefore cannot be settled statically — flag them for
a live pass rather than asserting they pass:

- **Silent reflection misses and swallowed patch-install failures.** The tell is a feature that is
  simply inert. Confirm in game (the log is `Console.WriteLine`), or dump real structure at runtime
  via ksa skill [`debug.md`](../ksa/debug.md).
- **Render correctness** — thug-life's quad, humble-arteest's paint/emissive, blinky/its-so-shiny grid
  scale and lighting, dont-stifle-me's scale gizmo. Only a live flight/editor session confirms these
  draw correctly.

Practical live pass: build, launch KSA, open the **unscience** window (**F11**) — the bundled submods load
through it, so it is the fastest broad smoke test — then exercise the specific mods implicated by the
findings (standalone windows are mostly **F11**; doh **F8**, kiwis-marbles **F9**, thug-life **F12**).

---

## 7. Deliverable — the impact review

1. **Builds validated** — `<NEW>` build id, `<OLD>` build id, `<BASELINE>` from `scope/`, whether
   `<INSTALL>` matches `<NEW>`, and the `dotnet build ksa-mod-experiments.slnx` result (clean, or the
   exact error → mod → `scope/` row work list). State which DLL set the build used.
2. **Changelog delta** — new revisions reviewed (cite `rev`s), filtered to the subsystems in step 1's
   table, **plus** any revision range covered by no `version.json` on disk.
3. **Findings** — each as: mod + `file:line`, the game member/asset, change class (renamed / retyped /
   signature / **semantic-drift** / new-gating / **reflection-miss** / **shader-anchor** /
   **byte-offset** / asset-id / toolchain), the evidence from **both** trees (decomp or Content path),
   the blast radius (which other mods share that touchpoint — use the master index *Used by* column),
   and whether it is **new in `<NEW>`** or **pre-existing**.
4. **Verdict per finding** — `no change needed` / `code change required` / `needs live in-game
   re-verification`, and be explicit that reflection + render findings cannot be cleared statically.
5. **Known-broken reconciliation** — for each §5 item: still broken / now fixed / newly worsened.
6. **If changes are required** — the specific files to touch and the full lockstep doc set (step 6).
   Do not edit beyond compile-blocking fixes until the approach is confirmed.

If everything is clean, say so plainly, list what was checked (the §3 mod checklist, the §4 watchlist
in full, the §5 render/shader set), and name what still needs a **live** pass before the upgrade can
be called validated — a green `dotnet build` is not that, and in this repo it is a small fraction of
the risk.

---

## 8. Genuinely game-free surface (don't spend the review here)

Nothing in this repo is firewalled from KSA, but these parts couple to no game member and only break
via toolchain/package churn:

- TOML persistence (Tomlyn) and its file layouts: skittles themes and garrys-torch presets.
- Pure-ImGui layout/state code, easing math, ring buffers, topological sorts,
  and `mkmod.ts`.
- The **StarMap.API** loader ABI (and the other NuGet deps: Lib.Harmony and Tomlyn) — a StarMap or
  Harmony release is its own event, not a KSA game update. Note the
  versions in the report, but don't attribute game breakage to them. StarMap is, however, the thing
  that patches `Program.OnDrawUiFrame`/`OnFrame` on the suite's behalf, so if the game moves those the
  break shows up as *StarMap not loading anything* rather than as a unscience error — check the game's
  render-loop entrypoints before blaming the loader.

The caveat: `Brutal.*` and `Brutal.ImGui` ship **with the game**, so a game update can still break
"game-free" UI code through nullability/API changes (step 2). That is a real finding — classify it as
toolchain drift and fix it, don't dismiss it as out of scope.
