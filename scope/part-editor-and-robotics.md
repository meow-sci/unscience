# Part Editor & Robotics — Game Integration Scope

Permanent reference for how the **parts-now** (runtime Part/SubPart loading) and **dont-stifle-me**
(editor scale un-limiter) mods bind to the Kitten Space Agency (KSA) game, so that future game updates
that break them can be detected and root-caused quickly.

> With flexo gone this area has **no robotics implementation** — the title is kept for the game
> surface it maps. Historical findings live in git history and the dated upgrade plans.

- **Current baseline:** `2026.9.7.5402` (NEW), diffed from `2026.8.22.5348` (OLD). See
  [`FULL_SCOPE.md`](FULL_SCOPE.md) for the version block. Revisions 5349–5400 are **unlogged** in any
  changelog (only rev 5401 "Fixed crash for incorrect data stride for thumbnail rendering" is logged),
  so the decomp diff is the only evidence for this span.
- **Build status against 5402:** `parts-now.lib` and `dont-stifle-me.lib` both **compile clean**
  after the `Viewport` → `IViewport` fixes (whole solution: 52/52 projects, 0 warnings, 0 errors).
- **Decomp (source of truth):** `~/repos/meow-sci/ksa-game-assemblies/current/decomp` (NEW, 5402) and
  `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp` (OLD, 5348); Content under the sibling
  `…/current/Content` folders.
- **Older sections below keep their original version pairs** (4680↔4750, 5018↔5117, 5117↔5261,
  5261↔5348) — each heading states its own span, so genuine regressions can be told apart from older
  drift. All decomp line numbers in the two live tables below were refreshed against **5402**.

Legend for *In NEW?*: ✅ present & signature-compatible · ⚠️ present but changed · ❌ removed/renamed.

---

## iron-man

The new EVA editor feature is mapped in [iron-man.md](iron-man.md), including instance connectors,
save-index reconstruction, body/root guards, character rendering and per-kitten flight activation.
The existing `KittenEva` object enters/exits the stock editor; no template XML is modified.

## dont-stifle-me

### Purpose
Undoes editor restrictions. The original feature targets two restrictions introduced in
`2026.8.22.5348`: the **0.5x–2.0x** top-level part
scale clamp (`VehicleEditor.MINIMUM_SCALE` / `MAXIMUM_SCALE`, surfaced through `ScaleBoundsFor`) and
**uniform-only** scale-gizmo drags (`UpdateSelectedScale` writes `new double3(s, s, s)`), and can
bypass the 0.25 m diameter **snapping** (`QuantizeScale`). Runtime toggles in `EditorScaleSettings`
(`Enabled`, `Snap`); patches are installed once and gate themselves per call. The extensible
`EditorLimitSettings.JplSaidNoClamps` toggle additionally changes the parachute editor diameter range
from each chute's authored bounds (20–50 m in current stock content) to **2–1000 m**.

### Unscience integration
- `DontStifleMeSubmod : ISubmod` (`dont-stifle-me.lib/DontStifleMeSubmod.cs`).
- `dont-stifle-me/Patcher.cs` applies `HotkeyGuard.Patch`, `EditorScalePatches.Apply(_harmony)` and
  `EditorValueLimitPatches.Apply(_harmony)`, then `MenuBarPatch.Apply(_harmony)`;
  `unscience/Patcher.cs` applies both patch groups under isolated `TryApply` / `TryRemove` calls (the
  supermod has its own menu entry).

### UI / hotkeys
- Standalone: **"Don't Stifle Me"** top-level menu (`Enabled`, `Snap scaling`, and **jpl said no
  clamps** checkboxes) drawn
  from a postfix on `Program.DrawProgramMenusHook` — the same hook unscience's `MenuBarPatch` uses.
  No window, no hotkey.

### Persistence
- None (in-memory toggles; scale controls default on, **jpl said no clamps** defaults off).

### Integration points

All four patch targets and both helper delegates are resolved by **name string** via
`AccessTools.Method` (`EditorScalePatches.cs:16-20`), so a rename fails at `Apply()` (logged +
red notice in the UI), not at compile time. Every one is also listed in
[`game-integration-surface.md`](game-integration-surface.md) §4.

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | Harmony (postfix, by-name) | `EditorScalePatches.cs:56,89` | `VehicleEditor.ScaleBoundsFor(Part)` **private static `(double Min, double Max)`** | `KSA/VehicleEditor.cs:3995` | ✅ | none @5402 (moved from `:3877`; body identical; **single overload**) | Postfix rewrites `__result` to `(1e-6, +inf)`. Return type is `ValueTuple<double,double>` — a change to a struct/record breaks the `ref __result` binding at patch time. Consumers: `UpdateSelectedScale:3981`, `QuantizeScale:4033`. |
| 2 | Harmony (prefix, by-name) | `EditorScalePatches.cs:58,124` | `VehicleEditor.UpdateSelectedScale(ref readonly double4x4 matrixVehicleAsmb2Ego, IViewport inViewport)` **private void** | `KSA/VehicleEditor.cs:3959` | ✅ | ⚠️ **signature @5402** — `Viewport` → `IViewport` (param names unchanged; body line-identical to 5348; **single overload**, so the by-name `AccessTools.Method` cannot hit `AmbiguousMatchException`). Compile break fixed in `EditorScalePatches.cs:124` / `PerAxisScaleDrag.cs:28`. | Prefix param `ref double4x4 matrixVehicleAsmb2Ego` binds to the `ref readonly` original **by name** — parameter rename = patch failure. Returns `false` to skip stock. |
| 3 | Harmony (postfix, by-name) | `EditorScalePatches.cs:60,113` | `VehicleEditor.UpdateScaleGizmo(ref readonly double4x4, doubleQuat, IViewport, double)` **public void** | `KSA/VehicleEditor.cs:3732` | ✅ | ⚠️ signature @5402 (`Viewport` → `IViewport`; body identical; single overload) | Only `__instance` injected; reads `GizmoGrabbed` to end the per-axis drag session. Single overload assumed. |
| 4 | Harmony (prefix, by-name) **+** delegate | `EditorScalePatches.cs:48,52,62,103` | `VehicleEditor.QuantizeScale(Part, double rawScale) : private static double` | `KSA/VehicleEditor.cs:4025` | ✅ | none @5402 (moved from `:3907`; single overload; still `0.25 / FindLargestDiameter()`) | Prefix: when `Snap` is off, `__result = Clamp(rawScale, ScaleBoundsFor(part))`, return false — param `rawScale` bound **by name**. Delegate (`MethodDelegate<Func<Part,double,double>>`) is what the per-axis drag calls, so it sees the prefix too. Snap-on path keeps the 0.25 m step (`SCALE_DIAMETER_INCREMENT_M` / `PartTemplate.FindLargestDiameter`). |
| 4b | Reflection (private static → delegate) | `EditorScalePatches.cs:53` | `VehicleEditor.ScaleBoundsFor(Part)` (same target as #1) | `KSA/VehicleEditor.cs:3995` | ✅ | none @5402 | `MethodDelegate<Func<Part,(double,double)>>` used by the snap-off prefix to clamp; goes through the patched body, so it returns the widened bounds. |
| 5 | Reflection (private static → delegate) | `EditorScalePatches.cs:50,54` | `VehicleEditor.ForEachPartWithSymmetry(Part, Action<Part>)` | `KSA/VehicleEditor.cs:4000` | ✅ | none @5402 (moved from `:3881`; single overload — the new generic `ForEachModuleWithSymmetry<TModule>` at `:1363` is a different name) | `AccessTools.MethodDelegate<Action<Part,Action<Part>>>`; propagates the axis scale to symmetry siblings exactly like stock. |
| 6 | Typed API (editor state) | `PerAxisScaleDrag.cs:32-43` | `VehicleEditor.{Selected, HighlightedGizmoSegmentIndex, ScaleGizmo, CursorPositionScreen, CursorPositionScreenLastFrame, GizmoGrabbed}` (public fields) | `KSA/VehicleEditor.cs:551,579,573,681,683,581` | ✅ | none | Segment index → axis `0=X,1=Y,2=Z` is an **invariant of `ScaleGizmo`'s 3-segment construction** (`:1179`), not checkable by grep. |
| 7 | Typed API (math) | `PerAxisScaleDrag.cs:36-49` | `IViewport.GetCamera()`, `Camera.ScreenToEgoNearPlane(double2)`, `GenericGizmo.GetSegmentDataByViewport(IViewport)` / `PerSegmentData.Body2Cce`, `Part.PositionEgo(in double4x4)` | `KSA/IViewport.cs:51`, `KSA/Camera.cs:684`, `KSA/GenericGizmo.cs:277,176`, `KSA/Part.cs:1155` | ✅ | ⚠️ @5402: `KSA.Viewport` deleted → `IViewport`; `GetSegmentDataByViewport` now keys its lookup by `ViewportId` (`GenericGizmo.cs:206,279,298` use `inViewport.Id`) — transparent to the caller | Mirrors stock `UpdateSelectedScale` math line-for-line; **semantic drift** in the stock routine (e.g. a different depth heuristic) would leave per-axis drags feeling different from uniform ones without any symbol change. |
| 8 | Typed API (apply) | `PerAxisScaleDrag.cs:69-74` | `Part.Scale { get; set; }` (`double3`), `Part.RefreshScaleAndReposition()`, `Part.Tree`, `PartTree.RefreshStaticMass()` | `KSA/Part.cs:815,1592,662`, `KSA/PartTree.cs:773` | ✅ | none @5402 (`RefreshScale :1571` / `RefreshScaleAndReposition :1592` bodies untouched) | 🔶 **Standing limitation:** `Part.RefreshScale` collapses `double3` to `new ScaleFactors(max axis)` for connectors, `IRescale` modules and mass. Non-uniform parts keep a non-uniform *mesh* but uniform connector offsets. If the game ever re-derives `Part.Scale` from `ScaleFactors` (uniformizes on load/refresh), per-axis scaling silently stops sticking. |
| 8a | Harmony (prefix, by-name) | `EditorValueLimitPatches.cs:29-35,73-83` | `VehicleEditor.DrawParachuteSection(Part, ReadOnlySpan<Parachute>) : private void` | `KSA/VehicleEditor.cs:1932` | ✅ | new dont-stifle-me consumer @5402 | Before the stock diameter slider reads `parachute.Tuning.MinDiameterM` / `MaxDiameterM` (`:1977-1985`), expands every chute in the selected subtree to 2 / 1000 while the toggle is on. The prefix intentionally binds only `part`, avoiding a Harmony patch parameter for the byref-like `ReadOnlySpan`. |
| 8b | Harmony (prefix, typed signature) | `EditorValueLimitPatches.cs:31-37,85-92` | `Parachute.SetDiameter(float diameterM) : public void` | `KSA/Parachute.cs:369` | ✅ | new dont-stifle-me consumer @5402 | Expands all chute modules on the receiving part before the stock method calls `Tuning.ClampDiameter`; required because `VehicleEditor.ForEachModuleWithSymmetry` invokes `SetDiameter` on counterparts whose slider was not drawn. Original per-instance min/max pairs are tracked and restored on toggle-off/unload. |
| 8c | Typed API (runtime bounds) | `EditorValueLimitPatches.cs:63-70,94-106` | `Parachute.Tuning : ChuteTuning`; `ChuteTuning.{MinDiameterM, MaxDiameterM, DiameterM, ClampDiameter(float)}`; `Part.SubtreeModules` / `Part.Modules` | `KSA/Parachute.cs:140,369-378`; `KSA/ChuteTuning.cs:5,33-35,61`; `KSA/Part.cs` | ✅ | new dont-stifle-me consumer @5402 | Only the bounds are changed; stock `SetDiameter`, symmetry propagation, inert-mass refresh, save data, drag physics, cloth and rendering continue to consume `Tuning.DiameterM`. Disabling does not retroactively clamp a value already chosen, matching the scale feature's non-destructive toggle behavior. |

| 9 | Harmony (postfix, `nameof`) — standalone only | `dont-stifle-me/MenuBarPatch.cs:15,20` | `Program.DrawProgramMenusHook() : public void` (empty hook called inside the main menu bar) | `KSA/Program.cs:3876` (call site `:3863`) | ✅ | none | Same target as unscience's `MenuBarPatch`; draws `ImGui.BeginMenu("Don't Stifle Me")`. |

### Watch items
- `MINIMUM_SCALE`/`MAXIMUM_SCALE` are **consts inlined** into `ScaleBoundsFor`; if a later build
  inlines `ScaleBoundsFor` itself into its two callers, patch #1 has no target → clamp returns.
- The TRS debug menu (`DrawTransformMenu`, `:6836`) still writes `part.Scale` per-axis with no clamp;
  unaffected by, and independent of, this mod.
- `DrawParachuteSection` is private and resolved by name. A rename or overload addition disables the
  editor-limit patch group with a logged error and red UI warning; the scale patch group remains
  independently usable in unscience.

---

## flexo — **REMOVED @5348**

flexo (robotics — articulated hinge/rotor Parts on top of KSA's static Part system) was deleted from
the repo at baseline `2026.8.22.5348`. It was **not** a compile break: `flexo.lib` built clean against
5348. It was removed because the approach never worked in-game and will not be reattempted this way —
the hinge implementation depended on undocumented `Part` transform/bounds cache-invalidation semantics
(recorded below as **R-flexo-2**, the area's highest silent-breakage risk) and was the source of the
long-standing "flexo throws errors but works" entry in [`../ISSUES.md`](../ISSUES.md).

Removed in one pass: `flexo/` and `flexo.lib/`, both solution entries, the `unscience.csproj`
`ProjectReference`, and the supermod wiring in `unscience/Mod.cs` (`FlexoSubmod`) and
`unscience/Patcher.cs` (`FlexoPatches.Apply` / `.Remove`). No game part XML was ever written by flexo,
so nothing on disk needs migrating; its orphaned TOML under `~/.flexo/flexo_part_*.toml` can be deleted
by hand.

**Retired integration points** (no longer verify these on a game update):

- The former flexo `PartModelRenderer.UpdateRenderData` hook was retired. Iron Man now uses a distinct
  `PartModelRenderer.UpdateRenderData(IViewport,int)` prefix for EVA equipment upload timing; see [scope](iron-man.md).
- `OrbitLinePass.AddLineVertex` / `.AddLineEnd` — likewise unowned; flexo's editor scene was the last user.
- The whole hinge rotation surface: `Part.Asmb2ParentAsmb`, `Part.PositionParentAsmb`,
  `Part.BoundingBoxVehicleAsmb` / `ComputeBoundingBoxVehicleAsmb()`, `Part.TreeChildren`, `Part.SubParts`
  setter-touch cache invalidation, `Vehicle.UpdateAfterPartTreeModification()`.
- `PartTree.RecomputeStaticMass()` via `Traverse` (**R-flexo-3**, string-reflection watchlist entry — now
  off the list).
- Risk items **R-flexo-1** (by-name `ExecuteNextVehicleSolvers` prefix), **R-flexo-2** (private
  cache-invalidation contract), **R-flexo-3** and **R-flexo-4** (solver-phase tree mutation) are all closed.

**Still live elsewhere, keep verifying:**

- `Universe.ExecuteNextVehicleSolvers(double, SimStep)` — eternal-flame, kiwis-marbles, kitchen-sink and
  the unscience supermod all prefix it (by `nameof`, not by string), see
  [`vehicle-physics.md`](vehicle-physics.md) and [`celestial-and-lights.md`](celestial-and-lights.md).
- `GenericGizmo` — **dont-stifle-me** uses `VehicleEditor.ScaleGizmo.GetSegmentDataByViewport(Viewport)`
  (see its section above).
- `PartTree.UpdateRenderData(...)`, `Part.Template.Id`, `Part.Connections` / `Connection.OtherPart` — all
  still reached by other mods; catalogued in their own sections.

Kept here only so a future reader who finds flexo in git history knows why it is gone. Its full
integration table, the 4680→4750 verification and the R-flexo-* findings live in git history and in
earlier upgrade plans.

---

## parts-now

### Purpose
Loads **Parts and SubParts into a running game** — no restart. Two flows: paste KSA `<Assets>` XML
into a new managed mod folder ("install"), or load / reload / unload an existing mod folder that KSA
did **not** load at boot. Runs the whole boot asset pipeline by hand on a per-mod basis: validate →
write folder → build a `Mod` → `AssetBundle.OnDataLoad` → run `ILoader`s on a worker → mesh-budget
check → `IBinder.Bind` (GPU upload) → incremental `PartGameData` attach → warm the `PartModel`
family → render a part-browser thumbnail per new Part → reset the editor's diameter cache. An
exact-inverse purge (12 numbered steps) makes unload and reload possible.

The two things that make it work at all are **mesh-buffer headroom reserved before KSA allocates its
one shared interleaved vertex/index buffer**, and a **single reflection choke point**
(`Runtime/GameRegistry.cs`) over KSA's `internal static` asset registries.

### Unscience integration
- `PartsNowSubmod : ISubmod` (`parts-now.lib/PartsNowSubmod.cs`) is the entry point; appears as a
  panel in the Unscience Toolbox. `Initialize()` runs `GameRegistry.SelfTest()` then
  `MeshBudget.Reserve()` — it **must** be called from `[StarMapAllModsLoaded]` (see U1 below).
- `Update(dt)` calls `MeshBudget.OnFirstFrame()` once, then `RuntimeModLoader.Step()` **exactly once
  per frame** — the loader's `Bind` and `Thumbnails` states submit command buffers and block on
  fences, which is only safe inside `Program.OnDrawUiFrame`.
- Standalone path: `parts-now/Mod.cs` (F10 by default, from `parts-now.toml`) + `parts-now/Patcher.cs`.
  parts-now patches **nothing** of its own; `Patcher.Patch()` applies only `HotkeyGuard.Patch`
  (`parts-now/Patcher.cs:23`, unpatched at `:36`).
- Threading rule, repeated at the top of every file: game thread only, except
  `RuntimeModLoader`'s `RunLoaders` worker, which touches only `ILoader.Load()`.

### UI / hotkeys
- F10 toggle (standalone; configurable via `hotkey` in `parts-now.toml`). No game menu is injected.
- Panels: Status (self-test, mesh budget, bindless-texture budget), Paste XML (3 tabs — Assets /
  Part / GameData), Mod folders (scan + Load/Reload/Unload), Results (per-part status + thumbnail).
- No floating windows; `RenderFloatingWindows()` is empty.

### Persistence
- Writes a real **KSA mod folder** under `ModLibrary.LocalModsFolderPath` — `mod.toml` (Tomlyn,
  merged if it already exists) plus up to three `<modId>-{assets,part,gamedata}.xml` files, each
  written atomically via a `.tmp` sibling (`Io/ModFolderWriter.cs`).
- Adds an **enabled, non-new `ModEntry`** to `ModLibrary.Manifest` and calls `ModManifest.Save()`
  so the mod also loads at the next launch (deliberately not `new ModEntry(id, count)`, which sets
  `Enabled=false, New=true` and pops the game's "confirm mods" dialog).
- Its own settings live in `<mods>/parts-now/parts-now.toml` (`Runtime/PartsNowSettings.cs:65`).
- `LoadedModRecord` is **session state only** — nothing about a runtime load is persisted.

### Integration points

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | Reflection (internal static field, string) | `Runtime/GameRegistry.cs:72,292` | `ModLibrary.AllParts : internal static readonly SerializedCollection<PartTemplate>` | `KSA/ModLibrary.cs:86` | ✅ | new | Literal `"AllParts"`; `BindingFlags.Static\|NonPublic\|Public`. Fatal on miss → `IsHealthy=false` → all Load buttons disabled. |
| 2 | Reflection (internal static field, string) | `Runtime/GameRegistry.cs:73,292` | `ModLibrary.AllMeshes : SerializedCollection<MeshReference>` | `KSA/ModLibrary.cs:80` | ✅ | new | Literal `"AllMeshes"`. Fatal. |
| 3 | Reflection (internal static field, string) | `Runtime/GameRegistry.cs:74,292` | `ModLibrary.AllFiles : SerializedCollection<FileReference>` | `KSA/ModLibrary.cs:68` | ✅ | new | Literal `"AllFiles"`. Fatal. |
| 4 | Reflection (internal static field, string) | `Runtime/GameRegistry.cs:75,292` | `ModLibrary.AllMaterials : SerializedCollection<PbrMaterialReference>` | `KSA/ModLibrary.cs:70` | ✅ | new | Literal `"AllMaterials"`. Fatal. |
| 5 | Reflection (internal static field, string) | `Runtime/GameRegistry.cs:76,292` | `ModLibrary.AllPartGameDataReferences : SerializedCollection<PartGameDataReference>` | `KSA/ModLibrary.cs:78` | ✅ | new | Literal `"AllPartGameDataReferences"` — note the plural `References` suffix, unlike its siblings. Fatal. |
| 6 | Reflection (internal static field, string) | `Runtime/GameRegistry.cs:77,292` | `ModLibrary.AllEditorTagDefinitions : SerializedCollection<EditorTagDefinition>` | `KSA/ModLibrary.cs:144`; `KSA/EditorTagDefinition.cs:5` | ✅ | new | Literal `"AllEditorTagDefinitions"`. Fatal. Feeds V7's known-tag set. |
| 7 | Reflection (private instance field, string) | `Runtime/GameRegistry.cs:356-357` (`CollectionFields<T>`), used `:154-165` | `SerializedCollection<T>._collection : private readonly ConcurrentDictionary<KeyHash,T>` | `KSA/SerializedCollection.cs:14` | ✅ | new | Literal `"_collection"`, `Instance\|NonPublic`, probed once per closed generic at static-ctor time (`:83`). **The whole unload/reload story depends on it** — `SerializedCollection<T>` has no removal API (U4). Also type-checked: a non-`ConcurrentDictionary<KeyHash,T>` throws a descriptive error rather than corrupting the registry. |
| 8 | Reflection (private static field, string) | `Runtime/GameRegistry.cs:320` | `VehicleEditor._editorTagLookup : private static Dictionary<uint,string>` | `KSA/VehicleEditor.cs:537` | ✅ | new | Literal `"_editorTagLookup"`, type-checked against `Dictionary<uint,string>`. **Degraded, not fatal**: V7 falls back to the six built-in tags + `AllEditorTagDefinitions` ids. |
| 9 | Direct API (registry read/write) | `Runtime/GameRegistry.cs:151-152,170-194`; `Runtime/RuntimeModLoaderDeltas.cs:30-35,262` | `SerializedCollection<T>.{GetList() : List<T>, Find(KeyHash) : T?}` + `KeyHash.Make(ReadOnlySpan<char>)` | `KSA/SerializedCollection.cs:42,37`; `KSA/KeyHash.cs:15` | ✅ | new | `GetList()` returns the **live** backing list, which is what makes `.Remove(item)` a real unregister. `KeyHash.Make` lowercases → all parts-now id indexes are `OrdinalIgnoreCase`. |
| 10 | Direct API (mesh budget) | `Runtime/MeshBudget.cs:93-96,141-145,187-188,240-241` | `DeviceMeshInterleaved.Shared.{RunningVertexBufferSize, RunningIndexBufferSize} : public static uint` | `KSA/DeviceMeshInterleaved.cs:25,27` | ✅ | new | Written directly (inflate at reserve, rewind on the first frame, rewind again on rollback). Must stay **public static settable `uint`**. |
| 11 | Direct API (mesh budget) | `Runtime/MeshBudget.cs:87,90` | `DeviceMeshInterleaved.Shared.{VertexAllocation, IndexAllocation} : public static BufferEx` → `BufferEx.BufferSize` | `KSA/DeviceMeshInterleaved.cs:19,21`; `Brutal.VulkanApi.Abstractions/BufferEx.cs:90` | ✅ | new | Authoritative allocated size (as opposed to the running cursor). Sized from the running counters inside `BuildBuffers` (`KSA/DeviceMeshInterleaved.cs:55,63`). |
| 12 | Direct API (tripwire) | `Runtime/MeshBudget.cs:131,180` | `DeviceMeshInterleaved.Shared.IsBuilt : public static bool` | `KSA/DeviceMeshInterleaved.cs:31` | ✅ | new | Read as a **tripwire for U1**: must be `false` at `Reserve()` and `true` on the first frame. Both mismatches log a WARNING and keep going. |
| 13 | Behavioral (ordering invariant) | `parts-now/Mod.cs:40-46`; `parts-now.lib/PartsNowSubmod.cs:52-57` | `[StarMapAllModsLoaded]` = Harmony postfix on `ModLibrary.LoadAll()` (`Program.cs:942`), which runs **before** `ModLibrary.Bind(_renderer)` (`Program.cs:978`) → `IBinder.Bind` → `DeviceMeshInterleaved.Bind()` → `Shared.Build()` | `KSA/Program.cs:942,978`; `KSA/ModLibrary.cs:1824`; `KSA/DeviceMeshInterleaved.cs:192,33` | ✅ | new | 🔶 **U1 — the standing invariant.** `Build()` is one-shot and sizes both buffers from the counters as they stand at that instant. Reserve must land in between. Fails **silently**. |
| 14 | Direct API (mesh sizing) | `Runtime/MeshBudget.cs:265,277,286-287` | `MeshReference.DeviceMeshesInterleaved : DeviceMeshInterleaved[]` → `.VerticesSize` / `.IndicesSize : ByteSize` | `KSA/MeshReference.cs:32`; `KSA/DeviceMeshInterleaved.cs:115,125` | ✅ | new | Measured **before** `MeshReference.Dispose()` in purge step 6, for leak accounting. |
| 15 | Direct API (XML) | `Runtime/BundleParser.cs:89-93,102` | `XmlHelper.Serializers : public static Dictionary<Type, XmlSerializer>` → `[typeof(AssetBundle)]` | `KSA/XmlHelper.cs:13,46` | ✅ | new | **Must** use the game's instance: it carries the `XmlAttributeOverrides` mapping `<PartModel>`/`<Tank>`/`<Collider>`/`<Light>`… onto `PartTemplate.Components`. A hand-built `new XmlSerializer(typeof(AssetBundle))` silently drops every component. A missing entry is reported, not thrown. @5402: the overrides enumerate every `TemplateDataBase` subclass by reflection (`XmlHelper.cs:34-44`, file identical), so the new `<Parachute>` component (`Parachute.cs:12`) maps with no serializer change. |
| 16 | Direct API (registration) | `Runtime/RuntimeModLoaderStates.cs:200`; `Runtime/BundleParserQueries.cs:38` | `AssetBundle.OnDataLoad(Mod) : override void`; `AssetBundle.Assets : List<SerializedId>` (field); `[XmlRoot("Assets")]` | `KSA/AssetBundle.cs:81,74,9` | ✅ | new | The single call that registers everything a bundle declares. Parsing stays side-effect free until this runs. |
| 17 | Direct API (mod object) | `Runtime/RuntimeModLoaderStates.cs:148,153,161-168` | `ModLibrary.MOD_TOML`, `ModLibrary.Find(string) : Mod?`, `Mod.MakeUsing(string id, string manifestPath) : static Mod`, `Mod.{DirectoryPath, Preload, Id}` | `KSA/ModLibrary.cs:146,185`; `KSA/Mod.cs:103,91,78,82` | ✅ | new | The `Mod` is deliberately **not** registered into `ModLibrary.Lookup` (only the boot path does that, `KSA/ModLibrary.cs:430`), so `ModLibrary.Find` stays a reliable "was this loaded at boot?" test (row 21). `Preload` is forced false — `FileReference.OnDataLoad` only calls `RegisterLoader` while it is false. |
| 18 | Direct API (loader/binder queues) | `Runtime/RuntimeModLoaderDeltas.cs:33,36,80,93`; `Runtime/RuntimeModPurgeSteps.cs:285-286` | `ModLibrary.Loaders : public static List<ILoader>`; `ModLibrary.Binders : public static List<IBinder>`; `ModLibrary.RegisterLoader/RegisterBinder` (indirect) | `KSA/ModLibrary.cs:154,156,190,219` | ✅ | new | Mark/delta bookkeeping, then `RemoveAll` on purge. KSA never clears either list, so leaving entries behind would make a later full re-run re-load freed objects. |
| 19 | Direct API (worker step) | `Runtime/RuntimeModLoaderStates.cs:256`; `Runtime/RuntimeModLoaderGpuStates.cs:93-94` | `ILoader.Load() : void`; `IBinder.Bind(Renderer, StagingPool) : void` | `KSA/ILoader.cs:7`; `KSA/IBinder.cs:8` | ✅ | new | `Load()` is the **only** thing parts-now runs off the game thread. `Bind()` mirrors `ModLibrary.Bind`'s per-binder body (`KSA/ModLibrary.cs:1824`) minus its `Parallel.ForEachAsync` — the stock method would re-bind *every* binder ever registered. |
| 20 | Behavioral (thread gate) | `Runtime/RuntimeModLoaderStates.cs:233-237` (design note) | `Loading.OnFrame()` early-returns on `!Program.IsMainThread()`; `Loading.{Task, PushTask, Current}` | `KSA/Loading.cs:92,50,36,23`; `KSA/Program.cs:602` | ✅ | new | 🔶 **U7.** `FileReference.Load()` → `Loading.Task()` → `PushTask()` → `Current.OnFrame()` renders and submits a whole ImGui frame. On a worker that whole chain is a no-op *only* because of the `IsMainThread()` guard. If it is removed, `RunLoaders` renders a second ImGui frame inside the game's own frame. |
| 21 | Direct API (boot-mod test) | `Runtime/RuntimeModLoaderApi.cs:280`; `Io/ModFolderScanner.cs:251`; `Io/ModIdValidator.cs:166` | `ModLibrary.Find(string) : Mod?` → `ModLibrary.Lookup` (internal `SerializedCollection<Mod>`) | `KSA/ModLibrary.cs:185,66` | ✅ | new | Refuses to load/reload a mod KSA loaded at boot — parts-now cannot account for what KSA registered on its behalf. Fails **closed** (an exception means "treat as boot-loaded"). |
| 22 | Direct API (file loading post-conditions) | `Runtime/RuntimeModLoaderDeltas.cs:194,196,203,217,220,223,232` | `FileReference.{LocalPath (field), IsReference() : override bool, Load() : void, Id, ModPath}`; `MeshAtlasFileReference.Meshes : List<MeshReference>`; `MeshFileReference.Mesh : MeshReference?`; `MeshReference.IsReference()` | `KSA/FileReference.cs:13,57,67,24`; `KSA/MeshAtlasFileReference.cs:12`; `KSA/MeshFileReference.cs:15`; `KSA/MeshReference.cs:65` | ✅ | new | `FileReference.Load()` **catches and logs its own exceptions instead of throwing**, so `VerifyLoadersProduced` re-derives each `DoLoad()` post-condition by hand. Every one of these is a silent-failure detector; if any changes shape, a half-loaded mod becomes invisible again. |
| 23 | Direct API (mesh atlas ids) | `Runtime/GlbMeshNames.cs:48-79`; `Runtime/BundleValidatorContext.cs:157` | Reproduces `MeshAtlasFileReference.DoLoad()`'s naming rule: one `MeshReference` per `GltfLoader.GltfJson.Meshes[i].Name`, skipping names starting with `'_'` | `KSA/MeshAtlasFileReference.cs:31-44` | ✅ | new | ⚠ **Duplicated game logic, not a call.** parts-now reads only the GLB's JSON chunk itself (no `Brutal.Gltf` reference) because V6 must know the mesh ids *before* anything loads. If KSA changes the skip rule or the id source, V6 silently mis-reports. |
| 24 | Direct API (GPU texture teardown) | `Runtime/RuntimeModPurgeSteps.cs:146-154` | `TextureReference.{BindlessHandle : int (get; private set), Texture : SimpleVkTexture, TextureAsset : TextureAsset, Dispose(Device)}` | `KSA/TextureReference.cs:70,64,61,77` | ✅ | new | `Dispose(Device)` calls `Program.Instance.BindlessTextures.FreeTexture(BindlessHandle)` then `Texture.Dispose()`/`TextureAsset.Dispose()` **with no null checks**, and handle `0` is the bindless library's shared *empty* texture. Hence the triple guard (`>0` + both objects non-null). The `Device` argument is ignored by the game; the type does **not** implement `IDisposable`. |
| 25 | Direct API (materials) | `Runtime/BundleParserQueries.cs:178-201`; `Runtime/RuntimeModLoaderGpuStates.cs:226-235`; `Runtime/BundleValidatorRulesSchema.cs:273-308` | `PbrMaterialReference.{DiffuseReference, NormalReference : TexturePowerReference?, PBRMap, EmissiveMap, ThinFilmMap}`; `_isReference = Diffuse==null && Normal==null && PBRMap==null` | `KSA/PbrMaterialReference.cs:10,13,16,19,22,68` | ✅ | new | V9 mirrors the `_isReference` test to tell a material *definition* from a *pointer*. See U3. |
| 26 | Direct API (part model) | `Runtime/RuntimeModLoaderGpuStates.cs:255,257,287,308`; `Runtime/RuntimeModPurgeSteps.cs:43-48`; `Runtime/PartThumbnailGenerator.cs:262,279,311-320` | `PartTemplate.{ApplyGameData(PartGameDataReference), ResolveConsumerFeedPoints(), Dispose(), Thumbnail : ThumbnailReference?, IsSubPart : bool, Components : List<ModuleBase.TemplateDataBase>, SubPartInstances : List<PartInstance>, EditorTagsStrings : List<StringReference>}` | `KSA/PartTemplate.cs:255,405,250,111,119,113,21,30` | ✅ | ⚠️ additive schema @5402 (see notes) | `ApplyGameData` is **additive** (`AddRange` on connectors/masses/rockets/components), which is why parts-now attaches incrementally instead of calling `ModLibrary.AttachGameData()` (`KSA/ModLibrary.cs:1838`) — the stock method walks *every* registered entry and would double every part attached at boot. `ResolveConsumerFeedPoints()` starts with `ConsumerFeeds.Clear()`, so re-running it **is** idempotent. `Dispose()` disposes only `Thumbnail`. **@5402 additive schema:** `PartTemplate.CrashTolerance` (`[XmlAttribute] double = NaN`, `:17-18`, feeds `Part.CrashTolerancePascals` / `PartStructuralLimits`), `<SubPartGroup>` → `SubPartGroups` (`:107-108`, merged by `ApplyGameData :327`), and the `<Parachute>` component (`Parachute.TemplateData`, `ModuleList.cs:128`) — all flow through the real `ApplyGameData` + game serializer, so parts-now needs no change. |
| 27 | Direct API (model warm) | `Runtime/RuntimeModLoaderGpuStates.cs:347,351,355,176-178` | `PartModel.Get(PartModelModule.Template)`, `PartModelGlass.Get(PartModelGlassModule.Template)`, `PartModelDynamic.Get(PartModelDynamicModule.Template)` | `KSA/PartModel.cs:366`; `KSA/PartModelGlass.cs:460`; `KSA/PartModelDynamic.cs:374` | ✅ | ⚠️ @5402 new gate elsewhere: `PartModel/PartModelGlass/PartModelDynamic.AddInstance(…, IViewport, int)` early-return unless `viewport.HasAny(ViewportOptionFlags.RenderPartModels)` (`PartModel.cs:410-413`). Irrelevant to parts-now — thumbnails go through `ThumbnailCreator.CollectDraws`, not `AddInstance`, and every boot viewport carries the flag (`Program.cs:948-956`). | Warming turns an unresolvable `<Mesh Id>` into a catchable load-time exception instead of a crash when the player first clicks the part. Note `Get` resolves by scanning `Instances` for a matching `Template.Id` (`KSA/PartModelGlass.cs:460` ff.) — which is exactly why row 28 must prune those lists. |
| 28 | Direct API (static instance caches) | `Runtime/RuntimeModPurgeSteps.cs:109-120` | `PartModel.{Instances, InstancesRayTrace} : static List<PartModel>`; `PartModelGlass.{Instances, InstancesRayTrace}`; `PartModelDynamic.Instances`; `PartModelModule.Template.RayTracers : static List<Template>`; `PartModelGlassModule.Template.RayTracers` | `KSA/PartModel.cs:358,360`; `KSA/PartModelGlass.cs:452,454`; `KSA/PartModelDynamic.cs:368`; `KSA/PartModelModule.cs:22`; `KSA/PartModelGlassModule.cs:15` | ✅ | new | KSA **never** prunes these. `PartModelDynamic` has no `InstancesRayTrace` (dynamic models are never ray traced) and `PartModelDynamicModule.Template` has no `RayTracers` — both asymmetries are load-bearing. Matched by **object identity**, never by id (U5). |
| 29 | Direct API (component identity) | `Runtime/RuntimeModLoaderDeltas.cs:130-147`; `Runtime/LoadedModRecord.cs:91-105` | `ModuleBase.TemplateDataBase.Id : [XmlAttribute] public string = ""` | `KSA/ModuleBase.cs:8-11` | ✅ | new | 🔶 **U5.** Optional and not required to be unique → the purge collects the template **objects**. `ModelTemplateIds` exists for logging only. |
| 30 | Render/GPU (thumbnail framing) | `Runtime/PartThumbnailGenerator.cs:268,269,279,290,318` | `ThumbnailCreator.{ResetRootPart(ThumbnailPart), AddPart(ThumbnailPart, PartTemplate), MoveRootPart(ThumbnailPart, ThumbnailReference?, Camera), CollectDraws(ThumbnailPart, ThumbnailRenderResources), CreateThumbnailReference(Renderer, string) : ThumbnailReference}` | `KSA.Rendering/ThumbnailCreator.cs:216,179,192,126,153` | ✅ | none @5402 (lines +3) | Same framing the game's own `PreparePartThumbnails` uses (`:57`). `MoveRootPart(…, Camera)` forwards to the `(double fov, double nearPlane)` overload (`:197`) via `Camera.GetFieldOfView()` / `Camera.NearPlane`. `AddPart` only walks `SubPartInstances`, so a SubPart collects no draws — hence the explicit skip. |
| 31 | Render/GPU (thumbnail pipeline) | `Runtime/PartThumbnailGenerator.cs:131,281-286,322,339,350,514` | `ThumbnailRenderer(Renderer)` ctor; `.SIZE : static int` (= `GameSettings.Current.Graphics.PartThumbnailSize`); `.ColorFormat : static readonly VkFormat`; `.{PerInstanceDataDescriptorSetLayout, PerDrawDataDescriptorSetLayout, Sampler}`; `.RecordPartRender(CommandBuffer, ThumbnailReference, ThumbnailRenderResources, IViewport, string)`; `ThumbnailRenderResources(Renderer, DescriptorSetLayoutEx, DescriptorSetLayoutEx, VkSampler, int)`, `.DrawCommandVector.ElementCount`, `.UpdateDescriptorSets()`, `.Dispose()` | `KSA.Rendering.Thumbnails/ThumbnailRenderer.cs:34,32,14,26,28,30,117`; `KSA.Rendering.Thumbnails/ThumbnailRenderResources.cs:34,18,90` (file identical) | ✅ | ⚠️ @5402: `RecordPartRender` takes `IViewport` and binds the camera UBO slice with `GlobalShaderBindings.DynamicOffset(viewport.ShaderSlot)` (`:179`, was `viewport.Index`). `SIZE`/`ColorFormat` unchanged. | The three descriptor-set layouts/sampler are forwarded straight from `PartModelRenderer.ColorData` (`ThumbnailRenderer.cs:38-40`), so a change to the Part color pipeline reaches parts-now here. `ColorFormat` is consumed indirectly (image creation inside `CreateThumbnailReference`). **Rev 5401 "incorrect data stride" fix is not in the Thumbnail files** — it is `KSA/GlobalShaderBindings.cs:94,217`: the per-viewport uniform buffer is now sized for a fixed **8** slots (`ViewportRegistry.MAX_VIEWPORTS`, `ViewportRegistry.cs:18`) instead of the deleted `Program.ViewportCount`, so whichever `ShaderSlot` the thumbnail viewport was allocated has a slice (`_frameStride` itself is unchanged). parts-now inherits the fix by passing the viewport object; it never computes an offset. |
| 32 | Render/GPU (thumbnail scene) | `Runtime/PartThumbnailGenerator.cs:143,270,456` | `ThumbnailPart(Camera inParent, PartInstance? = null)` ctor; `.Children : List<ThumbnailPart>?`; `.Dispose()` | `KSA.Rendering.Thumbnails/ThumbnailPart.cs:72,22,78` | ✅ | new | Root part is parented to the thumbnail viewport's camera, mirroring `ThumbnailCreator.PreparePartThumbnails`. |
| 33 | Render/GPU (thumbnail image) | `Runtime/PartThumbnailGenerator.cs:312,319`; `Runtime/RuntimeModPurgeSteps.cs:43-46`; `Ui/ResultsPanel.cs:125,133`; `Runtime/ThumbnailReadback.cs:52` | `ThumbnailReference.{ImageView : ImageViewEx (get; private set), ModelTransform : TransformReference?, GetOrCreateImGuiTexture(VkSampler) : ImTextureRef, Dispose(), CreateImageView(...)}` | `KSA.Rendering.Thumbnails/ThumbnailReference.cs:16,13,36,54,31`; `KSA/TransformReference.cs:6` | ✅ | new | ⚠ **`ImageView.IsNull()` is a load-bearing guard everywhere.** A `<Thumbnail>` that came from XML has a `ModelTransform` but **never had `CreateImageView` called**, so `Dispose()` NREs on a null captured `Device` and `GetOrCreateImGuiTexture` would hand ImGui a null view. parts-now also *preserves* a declared `ModelTransform` across regeneration, which the game's own `CreateThumbnailImage` (`ThumbnailCreator.cs:146`) drops. |
| 34 | Render/GPU (shared viewport) | `Runtime/PartThumbnailGenerator.cs:141,142,195`; `Runtime/RuntimeModUnloader.cs:110-116` | `Program.ThumbnailViewport : static IViewport` (throwing property; a `PartThumbnailViewport` created with `ViewportRegistry.CreatePartThumbnailViewport(_renderer, ViewportOptionFlags.RenderPartModels, sampler)`); `ThumbnailDynamic.UpdateGlobalCameraData(IViewport, Camera) : static`; `ThumbnailDynamic.SetSelectedPart(PartTemplate?)`; `VehicleEditor.DynamicThumbnail : ThumbnailDynamic?` | `KSA/Program.cs:497,949`; `KSA/PartThumbnailViewport.cs:16-25`; `KSA.Rendering.Thumbnails/ThumbnailDynamic.cs:272,89`; `KSA/VehicleEditor.cs:707` | ✅ | ⚠️ @5402: `Viewport` → `IViewport` (parts-now `PartThumbnailGenerator.cs:61,141` fixed); `UpdateGlobalCameraData` now writes `GlobalShaderBindings.CameraData(inViewport.ShaderSlot)` (`:278`, was `.Index`); `ThumbnailCreator.Viewport` became a throwing property (`ThumbnailCreator.cs:33`), unused by parts-now. `IsOffscreen`/`ShouldRenderGizmos`/`EViewportLightMode` no longer exist — the doc comment at `PartThumbnailGenerator.cs:34-35` ("viewport index 1", `IsOffscreen`, `ShouldRenderGizmos`) is **stale** (comment only). | 🔶 **U6.** parts-now shares this viewport + camera with the part browser's hover preview. Safe only because parts-now submits in `Program.OnDrawUiFrame` and `ThumbnailDynamic.Render` (`ThumbnailDynamic.cs:167`) runs later in the **same** frame from `Editor.OnPreRender` (`KSA/VehicleEditor.cs:5409,5413` ← `KSA/Program.cs:2346`, after `OnDrawUiFrame` at `:2193`), each writing the camera UBO immediately before its own submit. **Never defer parts-now's submit to another frame phase.** |
| 35 | Render/GPU (camera) | `Runtime/PartThumbnailGenerator.cs:187-191` | `Camera.{Unfollow(bool changeControl = true), OnFrame(double), LocalPosition, LocalRotation, LocalScale}` (last three inherited from `Transform3D`); `Camera.{GetFieldOfView() : float, NearPlane : float}` (via `ThumbnailCreator.MoveRootPart`) | `KSA/Camera.cs:615,482,785,67`; `KSA/Transform3D.cs:9,13,11` | ✅ | new | ⚠ `Unfollow` **must** be called as `changeControl: false` — the defaulted overload nulls `Program.ControlledVehicle` and would drop the player's vessel mid-flight. INVARIANT: the camera is only ever re-asserted to origin/identity; the *part* is moved, never the camera. |
| 36 | Direct API (viewport) | `Runtime/PartThumbnailGenerator.cs:142,515` | `IViewport.GetCamera() : Camera`; `IViewport.Size : int2`; `IViewport.ShaderSlot : int` (replaces `Viewport.Index`; consumed indirectly by `UpdateGlobalCameraData`'s / `RecordPartRender`'s UBO slice) | `KSA/IViewport.cs:51,41,13` | ✅ | ⚠️ @5402 type replaced (`KSA/Viewport.cs` deleted; `ViewportBase`/`PartThumbnailViewport` implement it). parts-now never reads `Index`/`ShaderSlot` itself. | `Size` is only compared against `ThumbnailRenderer.SIZE` to warn when `PartThumbnailSize` changed since boot (both stay square, so framing is unaffected). |
| 37 | Render/GPU (Vulkan) | `Runtime/RuntimeModLoaderGpuStates.cs:85,93`; `Runtime/PartThumbnailGenerator.cs:129,135,138,341,356,361,368,372,390,493`; `Runtime/RuntimeModUnloader.cs:123-124`; `Runtime/ThumbnailReadback.cs:156` | `Program.GetRenderer() : Renderer`; `Renderer.{Allocator : KsaVmaAllocator, Graphics : Queue, Device : DeviceEx}`; `IBufferAllocator.CreateStagingPool(Queue, int)`; `Queue.Family`; `Queue.Submit(Span<VkSemaphore>, Span<VkPipelineStageFlags>, Span<CommandBuffer>, Span<VkSemaphore>, VkFence)`; `Device.{CreateCommandPool, AllocateCommandBuffer, CreateFence, WaitForFence, DestroyFence, FreeCommandBuffers, DestroyCommandPool, WaitIdle}` | `KSA/Program.cs:558`; `Core/Renderer.cs:15`; `Core/KSADeviceContextEx.cs:58,60,56`; `KSA/KsaVmaAllocator.cs:12`; `Brutal.VulkanApi.Abstractions/StagingPoolExtensions.cs:5`; `Brutal.VulkanApi/Queue.cs:10`; `Brutal.VulkanApi.Abstractions/QueueExtensions.cs:7`; `Brutal.VulkanApi.Abstractions/DeviceExtensions.cs:193,281,291,297`; `Brutal.VulkanApi/VkDevice.cs` | ✅ | new | parts-now owns a private **transient** command pool and one fence per thumbnail; the whole render is submit-and-wait on the game thread. `WaitIdle` gates purge step 1. Highest churn surface (Brutal Vulkan bumps) — but all compile-checked. |
| 38 | Direct API (editor refresh) | `Runtime/EditorRefresh.cs:41` (called `…GpuStates.cs:402`, `RuntimeModUnloader.cs:148`) | `VehicleEditor.ResetPartDiameterCache() : public static void` → clears `PartWindow._diameterCache` | `KSA/VehicleEditor.cs:7195,56` | ✅ | none @5402 | The **only** nudge the editor needs: `PartWindow.OnDrawUi` re-reads `ModLibrary.AllParts.GetList()` every frame, but `_diameterCache` is built lazily and reused. Never throws. |
| 39 | Direct API (unload safety gate) | `Runtime/RuntimeModUnloadGate.cs:74-79,98,105-110,119-124,148,154` | `VehicleProvider.GetAllVehicles()` + `PartHelpers.GetAllParts(Vehicle)` (abstractions); `Part.Template : PartTemplate`; `Part.SubParts : ReadOnlySpan<Part>`; `Program.Editor : static VehicleEditor?`; `VehicleEditor.{EditingSpace : VehicleEditingSpace, UnattachedPartTrees : List<PartTree>}`; `VehicleEditingSpace.AllParts => Parts?.Parts ?? default`; `PartTree.Parts` | `ksa-abstractions.lib`; `KSA/Part.cs:576,1079`; `KSA/Program.cs:226`; `KSA/VehicleEditor.cs:545,689`; `KSA/VehicleEditingSpace.cs:40,16`; `KSA/PartTree.cs:95` | ✅ | new | Refuses to purge while any live vehicle or the open editor still holds one of the mod's parts. **Fails closed** — any exception while inspecting becomes a refusal. `VehicleEditingSpace.AllParts` is null-safe by construction. |
| 40 | Direct API (validation-only lookups) | `Runtime/BundleValidatorRulesReferences.cs:194,195,205,209,213,295` | `SubstanceLibrary.{AllReactions() : ReadOnlySpan<Reaction>, TryGetReaction(KeyHash) : Reaction?}`; `GrainGeometryLibrary.{All(), TryGet(KeyHash) : GrainGeometry?}`; `VolumetricExhaustTemplate.Get(string) : static VolumetricExhaustTemplate?`; `ModLibrary.Get<SoundBehavior>(string)` | `KSA/SubstanceLibrary.cs:62,218`; `KSA/GrainGeometryLibrary.cs:25,41`; `KSA/VolumetricExhaustTemplate.cs:50`; `KSA/ModLibrary.cs:1042`; `KSA/SoundBehavior.cs:6` | ✅ | new | **Read-only.** V10 rejects `<Reaction Id>` / `<Grain Id>` / `<VolumetricExhaust Id>` / `<SoundEvent SoundId>` that name nothing, because parts-now cannot extend those libraries at runtime. `Get<SoundBehavior>` throws `NullReferenceException` on a miss and is the only public path (`AllSoundBehaviours` is internal, `TryGet<T>` takes the strict `IsSubclassOf` branch). The `AllReactions()/All()` probes downgrade to a warning when a library is empty. |
| 41 | Direct API (bindless budget) | `Runtime/BundleValidatorRulesIdentity.cs:221-222,231-232`; `Ui/StatusPanel.cs:202-210` | `Program.Instance : static Program`; `Program.BindlessTextures : BindlessTextureLibrary` (public field); `BindlessTextureLibrary.{TextureCount : int, MaxTextures : readonly int}` | `KSA/Program.cs:453,110,831`; `RenderCore.Systems/BindlessTextureLibrary.cs:42,20,55` | ✅ | new | V15 rule. The pool is `new FreeListIndexPool(maxTextures, allowResize: false)` with `maxTextures = 1024` (`Program.cs:831`), so exhausting it is **fatal, not slow** — parts-now holds 16 slots in reserve and refuses a load that would overrun. Not reflection: both members are public. |
| 42 | Direct API (asset classification) | `Runtime/BundleParserQueries.cs:40,51,73,81,92,108,116,216,242`; `Runtime/BundleValidatorRulesIdentity.cs:298-310` | Type hierarchy: `SubPartGameDataReference : PartGameDataReference : PartTemplate`; `SubPartTemplate : PartTemplate`; `MeshAtlasFileReference`/`MeshFileReference`/`TextureReference : FileReference`; `TexturePowerReference : TextureReference`; `PartInstance.InstanceOf`; `StringReference.Value`; `MeshViewModule.Template`; `SerializedId.Mod : Mod? { get; private set; }` | `KSA/SubPartGameDataReference.cs:3`; `KSA/PartGameDataReference.cs:5`; `KSA/SubPartTemplate.cs:3`; `KSA/PartInstance.cs:16,113`; `KSA/StringReference.cs:9`; `KSA/MeshViewModule.cs:9`; `KSA/SerializedId.cs:16` | ✅ | new | Every classifier tests **most-derived first** — a bare `is PartTemplate` matches all four part-shaped types. `SerializedId.Mod` names the owning mod in V3/V14 collision messages. Before `OnDataLoad`, `Hash` is `KeyHash.Zero` and `EditorTags` is empty, so validation reads `Id` strings and `EditorTagsStrings`. |
| 43 | Direct API (mod folder + manifest) | `Io/ModIdValidator.cs:158,175,181,214`; `Io/ModFolderWriter.cs:110,146,155,172-174`; `Runtime/PartsNowSettings.cs:65`; `Io/ModFolderScanner.cs:135` | `ModLibrary.{MOD_TOML, CONTENT_FOLDER, LocalModsFolderPath, LocalManifestPath, Manifest : public static ModManifest}`; `ModManifest.{Mods : List<ModEntry>, Save()}`; `ModEntry.{Id, Enabled, New}` | `KSA/ModLibrary.cs:146,148,176,178,158`; `KSA/ModManifest.cs:12,27`; `KSA/ModEntry.cs:24,9,21,40` | ✅ | new | `Manifest` is a public static field initialised to `null` and only filled by `PrepareManifest()`, so a null manifest is treated as "cannot prove the id is free" → **fail closed**. `new ModEntry { Id, Enabled = true, New = false }` is used deliberately instead of `new ModEntry(id, count)` (`ModEntry.cs:40`), which sets `Enabled=false, New=true`. |
| 44 | Lifecycle | `parts-now/Mod.cs:32-108`; `parts-now/Patcher.cs:23,36`; `parts-now.lib/PartsNowSubmod.cs` | StarMap `[StarMapMod]`/`[StarMapImmediateLoad]`/`[StarMapAllModsLoaded]`/`[StarMapBeforeGui]`/`[StarMapAfterGui]`/`[StarMapUnload]`; `ISubmod`; `HotkeyGuard` | `StarMap.API`; `MeowSci.KsaAbstractions` | ✅ | new | `ImmediateUnload => false` (parts-now holds GPU resources). `Dispose()` calls `RuntimeModLoader.AbandonForShutdown()`, which releases the in-flight job's `ThumbnailRenderer`, command pool and readback buffer **without** purging (a purge during shutdown would `WaitIdle` and free images while the game tears down). HotkeyGuard applied per the CLAUDE.md rule. |

### New game DLL references
Two game assemblies are referenced by `parts-now.lib.csproj` that **no other project in this repo
used before**, both purely to make typed access compile:

- **`Brutal.Vulkan.Vma.dll`** — `Renderer.Allocator` is a `KSA.KsaVmaAllocator`
  (`KSA/KsaVmaAllocator.cs:12`) which implements `Brutal.VulkanApi.Vma.IVmaAllocator`
  (`Brutal.VulkanApi.Vma/IVmaAllocator.cs:3`). Without the reference, `renderer.Allocator.…` does
  not bind.
- **`Planet.Render.Core.dll`** — `BindlessTextureLibrary` (`RenderCore.Systems/BindlessTextureLibrary.cs:11`),
  needed for the V15 texture-budget rule and the Status panel gauge.

Both are `<Private>false</Private>` HintPath references gated on `Exists('$(KSAFolder)…')`, exactly
like every other game DLL reference in the repo.

### Game assets referenced
- **None by id.** parts-now ships no asset and hard-codes no template/mesh/material/shader id.
- It *writes* a mod folder (`mod.toml` + up to three `<Assets>` XML documents) under
  `ModLibrary.LocalModsFolderPath`, and appends a `ModEntry` to `ModLibrary.Manifest`
  (`<user>/manifest.toml`).
- The XML **schema** it consumes is the game's own `<Assets>` bundle schema, parsed with the game's
  own serializer (row 15), so schema drift is handled by KSA rather than by parts-now — with the
  exception of the element names V8/V10/V11 match by string:
  `<Substance>`, `<MixtureReaction>`, `<FixedReaction>`, `<ThermalReaction>`, `<GrainGeometry>`,
  `<Situation>`, `<EditorTagDef>` (rejected as out of scope); `<Reaction Id>`, `<Grain Id>`,
  `<VolumetricExhaust Id>`, `<SoundEvent SoundId>`, `<Mesh Id>`, `<EditorTag Value>` and any
  `Path=` attribute (reference checks).

### Update-risk findings (5117 → 5261)

- **CONFIRMED COMPILE BREAK — `ImageBarrierInfo.Presets.SampledReadFragment` renamed to
  `SampledReadF`.** `parts-now.lib/Runtime/ThumbnailReadback.cs:56,84` → **2× CS0117**. The presets
  were swept for abbreviated names (`SampledReadVertex`→`SampledReadV`,
  `SampledReadFragment`→`SampledReadF`, `SampledReadCompute`→`SampledReadC`, and likewise for the
  `DepthSampledRead*` family). The replacement is **semantically identical** —
  `ShaderReadOnlyOptimal` / `ShaderReadBit` / `FragmentShaderBit`, the same layout, access mask and
  pipeline stage — so the transition to `TransferSrc` and back for the thumbnail readback is
  unchanged. → Fixed by rename.
- ⚠️ **Originated in the unvalidated 5118–5168 window**, not in 5261: `SampledReadFragment` exists at
  tag `2026.8.3.5117` and is absent from both `5168` and `5261`, and the `Presets` list is
  **byte-identical between OLD (5168) and NEW (5261)**. Not a regression from this build.
- ✅ **No other parts-now break.** All seven `ModLibrary.All*` reflection targets still resolve
  (`AllParts`, `AllCharacters`, `AllMeshes`, `AllFiles`, `AllMaterials`,
  `AllPartGameDataReferences`, `AllEditorTagDefinitions`), as do
  `SerializedCollection<T>._collection` (the only reason unload/reload exists) and
  `VehicleEditor._editorTagLookup`. `Part.ResetCachedPosMatrixValues()` and
  `PartTree.RecomputeStaticMass` are intact; `PartModelRenderer.UpdateRenderData(Viewport,int)` is
  signature-identical.
- ⚠️ **Editor/connector watch items (compile-clean, need a live pass):** bendable fuel-line hoses
  (rev 5171), roll (Q/E) while snapped to a connector (rev 5258), aeroshroud surface-attach connector
  flags (rev 5202), flipped-connector fixes across CoreElectricalA/FuelTankA/PassageA/PropulsionA
  (revs 5238/5239), a GLB→XML importer warning for suspicious connector orientations (rev 5225), and
  new `MeshColliderTemplate` / `ConvexHullColliderTemplate` types (rev 5185).

### Update-risk findings

> These are the standing invariants to re-verify on **every** game update. Each one fails **silently
> at runtime** — the mod compiles clean and the failure only shows up as corrupted geometry, a
> crash in someone else's code, or a mod that will not unload.

- 🔶 **U1 (fatal, silent) — `[StarMapAllModsLoaded]` must keep firing before `ModLibrary.Bind()`.**
  StarMap implements that attribute as a Harmony **postfix on `ModLibrary.LoadAll()`**
  (`KSA/Program.cs:942` @5402); `ModLibrary.Bind(_renderer)` runs later at `KSA/Program.cs:978` and is
  where the first `IBinder.Bind` → `DeviceMeshInterleaved.Bind()` (`:192`) → `Shared.Build()`
  (`:33`) allocates the two shared buffers **exactly once**, sized from
  `RunningVertexBufferSize`/`RunningIndexBufferSize` as they stand at that instant. parts-now
  inflates those counters in between (`MeshBudget.Reserve`) and rewinds them on the first UI frame
  (`MeshBudget.OnFirstFrame`), which is what leaves the headroom free. **If that order ever
  changes, the reservation silently stops working and every runtime-created mesh writes past the
  end of the shared vertex buffer** — the tripwire (`Shared.IsBuilt`, table row 12) only *warns*.
  Re-check: (a) `Program.cs` still calls `LoadAll()` before `Bind()`; (b) StarMap still hooks
  `LoadAll` for `AllModsLoaded`; (c) the loading screen still never runs `Program.OnDrawUiFrame`
  (which is what guarantees the first `Update(dt)` lands after `Bind`).
- 🔶 **U2 (fatal, silent) — `Shared.Build()` must stay one-shot, and `Rebuild()` must not be usable
  to grow the buffers.** `Build()` is `Interlocked.CompareExchange`-guarded (`:33-39`) and
  `Rebuild()` (`:69`) only reacts to a raytracing usage-flag mismatch — and it copies
  `VertexAllocation.BufferSize` bytes out of the **old** buffer (`:82-83`), so it can never enlarge
  anything. If a future build makes the shared allocator growable or adds a free list, the entire
  headroom trick (and the leak accounting in purge step 6) becomes unnecessary and should be
  deleted rather than left running.
- 🔶 **U3 (crash, immediate) — `Material.DiffuseReference` / `.NormalReference` / `.PBRMap` must
  keep being dereferenced unguarded.** `ThumbnailRenderResources.AddDraw`
  (`KSA.Rendering.Thumbnails/ThumbnailRenderResources.cs:138-140`) and
  `PartModel(.Glass/.Dynamic).WriteInstancesToGpu` (`KSA/PartModel.cs:464`,
  `KSA/PartModelGlass.cs:544`, `KSA/PartModelDynamic.cs:450`) read
  `.BindlessHandle` off all three with **no null check** (only `EmissiveMap` is `?.`-guarded).
  Validation rule **V9** exists solely to stop the player authoring a part that takes the whole game
  down at the first thumbnail. **If KSA ever null-guards them, V9 becomes an unnecessary
  restriction worth relaxing** — check `AddDraw` and `WriteInstancesToGpu` on every update.
- 🔶 **U4 (blocks unload, silent) — `SerializedCollection<T>` must keep having no removal API.** It
  exposes `Register`/`Find`/`GetList` only (`KSA/SerializedCollection.cs:20,37,42`), so
  `GameRegistry.Unregister` removes from the live `GetList()` list **and** reflects into the private
  `_collection` `ConcurrentDictionary<KeyHash,T>` (`:14`) that backs `Find`. Removing from only one
  leaves `Find` resolving a purged item. **If KSA ever adds a real removal API, replace the
  reflection with it** and delete the `"_collection"` string. Also note parts-now deliberately does
  **not** take the collection's private `Lock` (`:12`) — single-threaded, game-thread-only access is
  what makes that safe.
- 🔶 **U5 (silent corruption) — `ModuleBase.TemplateDataBase.Id` stays optional and non-unique.** It
  is a plain `[XmlAttribute] public string Id = ""` (`KSA/ModuleBase.cs:10-11`). The purge therefore
  matches model templates by **object identity**, never by id: an id match would miss every id-less
  template (leaving a stale `PartModel` that `PartModel.Get` — which scans `Instances` for a
  matching `Template.Id` — would hand to the reloaded part, complete with the purged mesh's old
  shared-buffer offsets) and would evict *another* mod's instances on a collision. If KSA ever makes
  the id required and unique, the identity `HashSet<object>` can be simplified; until then it must
  not be.
- 🔶 **U6 (crash out of the render loop) — `ThumbnailDynamic.Render`'s framing block sits OUTSIDE its
  try/catch.** `ResetRootPart`/`AddPart`/`MoveRootPart` are at
  `KSA.Rendering.Thumbnails/ThumbnailDynamic.cs:184-186`; the `try` only opens at `:197`. `AddPart`
  reaches `PartInstance.GetTemplate()` → `ModLibrary.Get<PartTemplate>` (`KSA/PartInstance.cs:96`),
  which throws `NullReferenceException` on a miss — i.e. straight out of `Editor.OnPreRender`
  (`KSA/VehicleEditor.cs:5413`). **This is why purge step 0 calls
  `Program.Editor.DynamicThumbnail.SetSelectedPart(null)` first** (`RuntimeModUnloader.cs:110-116`),
  before anything is unregistered. If the game ever widens that try/catch the step becomes belt and
  braces; if it *narrows* further, re-audit.
- 🔶 **U7 (frame corruption) — `Loading.OnFrame()` must keep its `!Program.IsMainThread()`
  early-return** (`KSA/Loading.cs:92`). `FileReference.Load()` calls `Loading.Task()` →
  `Loading.PushTask()` → `Current.OnFrame()`, which renders and submits a complete ImGui frame.
  parts-now runs `ILoader.Load()` on a worker precisely because that guard makes the whole chain a
  no-op there. Never "fix" this by nulling `Loading.Current` instead — `LoadTask`'s field
  initialiser throws when it is null, and that throw escapes `FileReference.Load`'s try block.
- ⚠ **U8 (silent mis-validation) — `MeshAtlasFileReference.DoLoad`'s mesh-naming rule is duplicated,
  not called** (table row 23). `GlbMeshNames` reproduces "one `MeshReference` per glTF mesh node,
  named by the node, skipping names starting with `'_'`" (`KSA/MeshAtlasFileReference.cs:31-44`) by
  reading only the GLB JSON chunk. If the skip rule or the id source changes, V6 starts reporting
  the wrong mesh ids (it degrades its errors to warnings when an atlas is unreadable, but not when
  it reads it *and gets different names*).
- ⚠ **U9 (silent partial load) — `FileReference.Load()` still swallows its own exceptions**
  (`KSA/FileReference.cs:67-148`). Every check in
  `RuntimeModLoaderDeltas.VerifyLoadersProduced` is a hand-written post-condition of a successful
  `DoLoad()` (`_isReference` cleared, atlas `Meshes` non-empty, `MeshFileReference.Mesh` non-null,
  `TextureReference` registered as a binder, `MeshReference` no longer a reference). If any of those
  post-conditions changes shape, a half-loaded mod goes back to being invisible.
- ⚠ **U10 (leak, by design) — the shared interleaved buffer is a monotonic bump pointer with no free
  list.** An unload or a reload orphans its meshes' bytes until the game restarts;
  `MeshBudget.RecordLeak` tracks them and the Status panel warns past 50% of the reserved headroom.
  A rollback (nothing bound yet) rewinds the cursors instead, and `MeshBudget.RestoreCursors`
  refuses to rewind below the startup watermark — a `(0,0)` snapshot would otherwise hand the next
  runtime mesh offset 0 and its `vkCmdCopyBuffer` would overwrite the whole game's geometry.
- ⚠ **U11 (behavioral) — editor tags cannot be registered after boot.**
  `VehicleEditor.MarkEditorTagDefinitionsLoaded()` locks the list; `RegisterTag` then logs a warning
  and adds nothing, so a part carrying a new tag sits in a category button that does not exist.
  Rule V7 rejects such tags up front. If the
  registered tag set changes again (e.g. another category removal like "Interstage"), V7's messages
  change with it automatically, but bundles that used to validate will start failing.
- ⚠ **U12 (behavioral, cosmetic) — `ThumbnailRenderer.SIZE` reads
  `GameSettings.Current.Graphics.PartThumbnailSize` live**, while the thumbnail viewport was sized at
  boot (`PartThumbnailViewport.cs:20`, from the same setting). parts-now warns on a mismatch and carries on (both are square, so framing is unaffected) and
  **never** mutates the game setting.

---

## Quick re-verification checklist (run on each new game build)

dont-stifle-me / shared part surface:

1. `Universe.ExecuteNextVehicleSolvers` still a **single overload** — eternal-flame, kiwis-marbles, kitchen-sink and the unscience supermod all resolve it with `AccessTools.Method(typeof(Universe), nameof(…))` and **no param array**, so a second overload would make resolution ambiguous.
2. `PartTree.RecomputeStaticMass` still present and still **private** — kitchen-sink `Traverse`s it by string. A public `PartTree.RefreshStaticMass()` wrapper exists as of 5348 (available simplification).
3. `GenericGizmo` ctor / `PerSegmentData` / `Static.GenericGizmoRenderData`, and `VehicleEditor.ScaleGizmo.GetSegmentDataByViewport(IViewport)` (`GenericGizmo.cs:277`, keyed by `ViewportId` since 5402) (dont-stifle-me per-axis drag).
4. Editor scaling is **uniform and clamped 0.5×–2×** as of rev 5329 (was triaxial), and modules implement `IRescale.SetScale(in ScaleFactors)` — dont-stifle-me exists to undo exactly this, so re-check `MINIMUM_SCALE`/`MAXIMUM_SCALE`/`ScaleBoundsFor`/`UpdateSelectedScale`/`QuantizeScale` on every build, and that each of the five by-name `VehicleEditor` targets still has **exactly one** declaration (a second overload turns the by-name `AccessTools.Method` into `AmbiguousMatchException` at `Apply()`). `UpdateSelectedScale`/`UpdateScaleGizmo` take `IViewport` since 5402.

> The old **flexo** block was retired with flexo at 5348. `PartModelRenderer.UpdateRenderData(Viewport,int)`
> and `OrbitLinePass.AddLineVertex/AddLineEnd` are now **unowned** and need no re-verification;
> `PartTree.UpdateRenderData(ref readonly double4x4,bool,IViewport,int)` (`IViewport` since 5402) is still live but is i-feel-seen's
> (see [`vehicle-physics.md`](vehicle-physics.md) table row 8); the hinge rotation
> surface (`Part.Asmb2ParentAsmb`, `PositionParentAsmb`, `BoundingBoxVehicleAsmb`, `TreeChildren`,
> `SubParts`, `Vehicle.UpdateAfterPartTreeModification`) is gone from this repo.

parts-now (all silent at runtime — see *Update-risk findings* above for the full reasoning):

8. **U1** — `Program.cs` still calls `ModLibrary.LoadAll()` **before** `ModLibrary.Bind(_renderer)` (`LoadAll` at `Program.cs:942`, `Bind` at `:978` @5402), and StarMap still implements `[StarMapAllModsLoaded]` as a postfix on `LoadAll` (`StarMap.Core/Patches/ModLibraryPatches.cs:17`). As of rev 5340 a `Loading.Task("Part Validation")` pass (`:1256-1258`) instantiates **every** registered part after `Bind` — watch its warnings for parts-now-generated parts.
12. **U2** — `DeviceMeshInterleaved.Shared.Build()` still one-shot; `Rebuild()` still cannot grow; `RunningVertex/IndexBufferSize` still public static settable `uint`; `IsBuilt` still readable.
13. Reflection names: `ModLibrary.{AllParts, AllMeshes, AllFiles, AllMaterials, AllPartGameDataReferences, AllEditorTagDefinitions}`, `SerializedCollection<T>._collection`, `VehicleEditor._editorTagLookup` — plus **U4** (still no removal API on `SerializedCollection<T>`).
14. **U3** — `ThumbnailRenderResources.AddDraw` + `PartModel(.Glass/.Dynamic).WriteInstancesToGpu` still dereference `Material.DiffuseReference`/`.NormalReference`/`.PBRMap` unguarded (if not, relax V9).
15. **U6** — `ThumbnailDynamic.Render`'s `ResetRootPart`/`AddPart`/`MoveRootPart` block is still outside its try/catch; **U7** — `Loading.OnFrame()` still early-returns on `!Program.IsMainThread()`.
16. Thumbnail surface: `ThumbnailCreator.{ResetRootPart,AddPart,MoveRootPart,CollectDraws,CreateThumbnailReference}`, `ThumbnailRenderer.{SIZE,ColorFormat,RecordPartRender,*DescriptorSetLayout,Sampler}`, `ThumbnailReference.ImageView`, `ThumbnailDynamic.{UpdateGlobalCameraData(IViewport,Camera),SetSelectedPart}`, `Program.ThumbnailViewport : IViewport`, `IViewport.{GetCamera,Size,ShaderSlot}`, `Camera.Unfollow(bool)`.
19. Camera-UBO sizing: `GlobalShaderBindings` still allocates `_frameStride * frameCount * 8` (`:217`, 8 = `ViewportRegistry.MAX_VIEWPORTS`) and every `DynamicOffset`/`CameraData` caller indexes by `viewport.ShaderSlot` — the rev-5401 thumbnail stride fix. If a build goes back to a dynamic viewport count, re-verify that the thumbnail viewport's slot is inside the buffer.
17. Asset-pipeline surface: `XmlHelper.Serializers[typeof(AssetBundle)]`, `Mod.MakeUsing`/`Preload`, `ModLibrary.{Loaders,Binders,Manifest,LocalModsFolderPath,MOD_TOML,CONTENT_FOLDER}`, `ModManifest.Save`, `ModEntry`, `FileReference.{LocalPath,IsReference,Load}`, `TextureReference.Dispose(Device)` — and **U8/U9** (GLB mesh-naming rule, `Load()`'s swallowed exceptions).
18. `BindlessTextureLibrary.{TextureCount,MaxTextures}` (`Planet.Render.Core`) + `Renderer.Allocator : KsaVmaAllocator` (`Brutal.Vulkan.Vma`) still resolve — the two new game DLL references.

---

## Area summary — Update-risk findings (5261 → 5348)

- ⚠️ **parts-now — every part is now instantiated at load** (rev 5340). `Program.cs:1212-1215` runs
  `PartArchetypes.WarnOnMalformedParts()` inside `Loading.Task("Part Validation")`, constructing a real
  `Part` from every non-subpart template in `ModLibrary.AllParts` and calling
  `Tree.ReinitializeDerivedValues()`. Rev 5329 added `PartTemplate.WarnOnDuplicateModuleIds()`.
  **Expect new load-time warnings for generated parts.** Not an error path for the mod.
- ✅ **parts-now's ordering invariant holds.** `ModLibrary.Bind(_renderer)` is at `KSA/Program.cs:942`
  (was `:985` at 5261) — still before the validation pass at `:1214`, and `[StarMapAllModsLoaded]`
  still fires before it. `MeshBudget.cs:23,177` cite the stale `:985`; comment-only.
- ⚠️ **Editor scaling changed triaxial → uniform, clamped 0.5×–2×** (rev 5329), and many modules gained
  the new `IRescale` interface (`SetScale(in ScaleFactors)`). This is what dont-stifle-me undoes.
- ❌ **flexo removed** (after this pass). It was **not** a compile break — `flexo.lib` built clean
  against 5348 — but the robotics approach never worked in-game and will not be reattempted this way,
  so the mod, its `.lib` and all of its wiring were deleted. See the stub section above. Its 5348
  verification results are recorded in the two bullets below and need no re-checking, since nothing in
  the repo depends on them any more.
- ✅ *(now unowned)* `PartModelRenderer.UpdateRenderData(Viewport, int)` was still a single `static void`
  on `PartModelRenderer` at 5348, and the explicit `[Viewport, int]` overload array still resolved
  uniquely (the new 3-arg `(Viewport, int, ref readonly double4x4)` overload lives on other types).
  `GenericGizmo` and `OrbitLinePass` were unchanged. flexo was the last consumer of the
  `PartModelRenderer` hook and of `OrbitLinePass`; `GenericGizmo` lives on in dont-stifle-me.
- ✅ **`PartTree.RecomputeStaticMass` still present and still private**, so kitchen-sink's
  `Traverse.Method("RecomputeStaticMass")` still works. `PartTree` **gained a public
  `RefreshStaticMass()`** wrapper — an available simplification, not a fix.
- ✅ **`Part`'s API churned but missed both mods.** Rev 5329 removed `Part.Sequence`, `SetSequence(int)`,
  `ActivateInStage`, `DeactivateInStage` and `ScaleTotal`; neither flexo (then still present) nor
  parts-now referenced any of them. `Part.Asmb2ParentAsmb`, `PositionParentAsmb` and `Scale` are
  unchanged.
- ✅ **`ModuleBase.Parent` became a property** (rev 5329, `IPartParent` split out of `Module`). Neither
  mod reflects on it. parts-now's seven `ModLibrary.All*` lookups and
  `SerializedCollection<T>._collection` / `VehicleEditor._editorTagLookup` all still resolve.

---

## Area summary — Update-risk findings (5348 → 5402)

> Span `2026.8.22.5348` → `2026.9.7.5402`. Revisions **5349–5400 are unlogged** in any changelog; the only
> logged commit is rev 5401 "Fixed crash for incorrect data stride for thumbnail rendering", so the decomp
> diff (197 KSA files, 20 Content files) is the sole evidence for everything below.

- ⚠️ **`KSA.Viewport` was deleted and replaced by `IViewport` / `IGameViewport` / `ViewportBase` /
  `GameViewport` / `PartThumbnailViewport` / `ViewportRegistry`** (rev unknown). Two **compile breaks** in
  this area, both fixed: `dont-stifle-me.lib/EditorScalePatches.cs:124` + `PerAxisScaleDrag.cs:28`
  (`UpdateSelectedScale(ref readonly double4x4, IViewport)` prefix and its drag helper) and
  `parts-now.lib/Runtime/PartThumbnailGenerator.cs:61,141` (`Program.ThumbnailViewport : IViewport`).
  Every by-name target keeps its **parameter names** and has **exactly one** declaration
  (`UpdateSelectedScale :3959`, `UpdateScaleGizmo :3732`, `ScaleBoundsFor :3995`, `ForEachPartWithSymmetry
  :4000`, `QuantizeScale :4025`), and every body is line-identical to 5348 — only the `Viewport` parameter
  type changed. `GenericGizmo.GetSegmentDataByViewport(IViewport)` (`:277`) now keys by `ViewportId`;
  `Program.ThumbnailViewport` (`Program.cs:497`) and `ThumbnailCreator.Viewport` (`:33`) became throwing
  properties; `IsOffscreen`/`ShouldRenderGizmos`/`EViewportLightMode`/`Program.Viewports`/`ViewportCount`
  are gone (the thumbnail viewport is now built with `ViewportOptionFlags.RenderPartModels`,
  `Program.cs:949`). Solution builds clean against 5402 (52/52 projects, 0 warnings, 0 errors).
- ✅ **Rev 5401 "data stride" fix — parts-now inherits it, no change needed.** The Thumbnail files
  (`ThumbnailRenderer.cs`, `ThumbnailDynamic.cs`, `ThumbnailPart.cs`) changed only `Viewport`→`IViewport`
  and `viewport.Index`→`viewport.ShaderSlot` (`ThumbnailRenderer.cs:179`, `ThumbnailDynamic.cs:278`);
  `SIZE`, `ColorFormat`, `ThumbnailRenderResources.cs` and `ThumbnailReference.cs` are unchanged. The
  actual fix is `KSA/GlobalShaderBindings.cs:94,217` (and `AtmosphereRenderer.cs`): the per-viewport
  uniform buffer is sized for a fixed **8** slots (`ViewportRegistry.MAX_VIEWPORTS`) instead of the
  deleted `Program.ViewportCount`, so the `ShaderSlot` handed to the thumbnail viewport by
  `ViewportRegistry.Allocate()` (`:71-82`) always has a camera-UBO slice. parts-now only ever passes the
  viewport object to `UpdateGlobalCameraData` / `RecordPartRender` (`PartThumbnailGenerator.cs:195,299,350`).
- ⚠️ **New gate in `PartModel/PartModelGlass/PartModelDynamic.AddInstance`** — early return unless
  `viewport.HasAny(ViewportOptionFlags.RenderPartModels)` (`PartModel.cs:410-413`); the IVA ray-trace branch
  now tests `viewport.HasAll(UseRaytracing) && viewport.Mode == IVA` instead of `== Program.MainViewport`.
  No effect here: parts-now thumbnails use `ThumbnailCreator.CollectDraws`, every boot viewport carries the
  flag (`Program.cs:948-956`), and the kitchen-sink `IvaForceRender` postfix (now
  `ksa-abstractions.lib/IvaForceRender.cs:98`, already `IViewport`) behaves as before.
- ✅ **Parachutes + structural limits: additive schema; parachutes now consumed by dont-stifle-me.** `PartTemplate` gained
  `CrashTolerance` (`[XmlAttribute] double = NaN`, `:17-18`) and `<SubPartGroup>`/`SubPartGroups`
  (`:107-108`, merged in `ApplyGameData :327`); `Parachute.TemplateData` (`[XmlType("Parachute")]`,
  `Parachute.cs:12`, `ModuleList.cs:128`) is picked up by `XmlHelper`'s reflection-built overrides (file
  identical), so parts-now's game-serializer path and its real `PartTemplate.ApplyGameData` call
  (`RuntimeModLoaderGpuStates.cs:255,287`) need nothing. New `PartStructuralLimits` / `PartFailure` /
  `PartContactLoad` types and `Part.{CrashTolerancePascals, InertMassKg, StructuralPart,
  IsAttachedInternal}` are not referenced by any unscience mod. Dont-stifle-me now consumes
  `Parachute`, `ChuteTuning`, `VehicleEditor.DrawParachuteSection`, and `Parachute.SetDiameter` only;
  it does not touch the cloth/physics implementation. Content: new `ParachuteAssets.xml`
  (GltfFile/PbrMaterial only, listed in `mod.toml`), four new radial parachute Parts + 14 SubParts in
  `CoreUtilityA*`, `CrashTolerance="3e6"` on `CorePropulsionA_Prefab_EngineA2..A6`, one new shader id in
  `DefaultAssets.xml` (`StaticObjectPrePassIndirectFrag`).
- ✅ **`CoreUtilityA_Prefab_ParachuteBayA` removed** (renamed `…ParachuteBayB` in `CoreUtilityAAssets.xml`
  / `…GameData.xml`). No unscience mod references it (repo-wide grep for `ParachuteBayA|CoreUtilityA_` is
  empty). All §5 ids used by this area's neighbours (`LightPart`, `CorePropulsionA_Prefab_EngineA2..A6`,
  `_connector3` BulkFluid/FeedsFrom wiring, `KittenBackPackPart`) still resolve.
- ✅ **Editor tags identical.** The `<EditorTagDef>` set and the set of `<EditorTag Value>` strings are
  byte-for-byte the same between 5348 and 5402 (sorted diff empty), so V7 / U11 are unaffected.
- ⚠️ **Semantic drift with no symbol change (not reached by these mods, recorded for the next pass):**
  `Part.DisplayName` now prefers `Template.DisplayName` when it differs from the id (`Part.cs:1391`, was
  `DisplayName = Id`); `Part.ResetModuleProperties()` now nulls `LightSwitch` (`:1807`) and the
  `PartModel*Module` light-off flag reads the new `Part.IsLightSwitchedOff()` (`:1357`);
  `VehicleEditor.HandleConnectorConnections` only connects when `connector.CanConnect()` and prefers a
  coincident connector (`:5092-5097`); the editor's right-click gate reads `Program.InputViewport`
  (`:3586`, was `HoveredViewport`); `Part.CountEnabledSubtreeSequencedModules` was removed;
  `GizmosRenderer.MAX_GIZMO_INSTANCES` 131072 → 655360. `Part.RefreshScale`/`RefreshScaleAndReposition`
  /`Scale` and `ScaleFactors`/`IRescale` are untouched, so dont-stifle-me row 8's limitation stands as is.
- ✅ **Verified clean (kind + type unchanged, lines refreshed in the tables above):** all six
  `ModLibrary.All*` registries are still `internal static readonly SerializedCollection<…>` fields;
  `SerializedCollection<T>._collection` is still a private `ConcurrentDictionary<KeyHash,T>` with no
  removal API (file identical); `VehicleEditor._editorTagLookup` is still `private static
  Dictionary<uint,string>` (`:537`); `PartTree.RecomputeStaticMass` is still **private** (`:778`) so
  kitchen-sink's `Traverse` string works, and `PartTree.RefreshStaticMass` (`:773`) is still public;
  `Universe.ExecuteNextVehicleSolvers` is still a single overload with an identical body (`:1834`);
  `DeviceMeshInterleaved`, `Loading`, `FileReference`, `MeshAtlasFileReference`, `TextureReference`,
  `PbrMaterialReference`, `XmlHelper`, `AssetBundle`, `Mod`, `ModManifest`, `ModEntry`,
  `ThumbnailRenderResources`, `ThumbnailReference`, `BindlessTextureLibrary`, `Renderer`,
  `KSADeviceContextEx`, `KsaVmaAllocator`, `PartArchetypes` are byte-identical between the trees.
  U1–U12 all hold (`LoadAll :942` → `Bind :978` → Part Validation `:1256`; `ThumbnailDynamic.cs:184-186`
  still outside the `try` at `:197`; `Editor.OnPreRender` still after `OnDrawUiFrame` in the same frame,
  `Program.cs:2193/2346`).
- 🔍 **Live checks still owed** (compile-clean, but the 5349–5400 window is unlogged): (1) runtime-load a
  part with parts-now and confirm the generated thumbnail renders in the part browser (exercises the
  `ShaderSlot`-indexed UBO sizing from rev 5401); (2) per-axis-scale a part with dont-stifle-me, then
  attach it to a connector (exercises the new `CanConnect()` / coincident-connector path in
  `HandleConnectorConnections`).
