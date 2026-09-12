# Pixel-Grid & Custom-Render Mods — Game Integration Scope

Permanent reference for detecting when KSA game updates break the pixel-grid and
custom-render mods (`blinky`, `its-so-shiny`, `thug-life`). Every game-facing member,
Harmony target, GPU/render API, shader, and part template these mods touch is
enumerated and verified against decompiled sources **and** the Content asset tree.

**Verified game versions**

- NEW decomp `2026.9.7.5402` root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- OLD decomp `2026.8.22.5348` root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`
- NEW Content root: `~/repos/meow-sci/ksa-game-assemblies/current/Content`
- OLD Content root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/Content`

Paths in the **Decomp/Content path (NEW)** column are relative to the NEW decomp root
(namespace-foldered, e.g. `KSA/Part.cs`) or the NEW Content root (e.g.
`Core/DefaultAssets.xml`). **Mod code** paths are relative to the repo root
`~/repos/meow-sci/unscience`. Line numbers were re-verified against 5402 on 2026-09-02; the
per-span "Δ vs OLD" cells are historical unless marked `@5402`.

**How these mods are hosted (all three)**

- All reusable game-facing logic lives in the `*.lib` project (`blinky.lib`,
  `its-so-shiny.lib`, `thug-life.lib`); each exposes an `ISubmod`
  (`MeowSci.KsaAbstractions.ISubmod`) consumed two ways:
  1. Standalone StarMap mod (`blinky/Mod.cs` F11, `its-so-shiny/Mod.cs` F11,
     `thug-life/Mod.cs` F12) — own ImGui window + own `Harmony` instance in its `Patcher.cs`.
  2. Embedded in the **unscience** supermod (`unscience/Mod.cs:69` `new BlinkySubmod()`,
     `:82` `new ItsSoShinySubmod()`, `:91` `new ThugLifeSubmod()`) as collapsible sections,
     with all three patch sets applied on the **single** supermod Harmony instance
     (`unscience/Patcher.cs:51` `ThugLifeRenderPatches.Apply`, `:57` `BlinkyPatches.Apply`,
     `:58` `ShinyPatches.Apply`, each wrapped in `TryApply`).
- `blinky` + `its-so-shiny` patch the **same three** render-data methods. Harmony allows
  multiple prefixes; `blinky` keys on `pixel_*` Ids and `its-so-shiny` on `shiny_*` Ids,
  so the prefixes never conflict (a part is skipped only if its own mod's prefix returns false).

**Summary of 4680 -> 4750 risk: NO breaking deltas detected.** Every patched method,
typed member, enum, shader id/path, and part-template id these mods use is
signature-identical between OLD and NEW; only source line numbers shifted. The
changelog's render-path churn (4693 MeshIndirect merge, 4745 ModelGlass+ModelEye merge,
4701/4747 MeshIndirect/ModelTranslucent `.frag` cleanups) touches `MeshIndirect.*` and
`Model*.*` shaders only — none of which these mods reference. The `dotnet build` against
the 4750 DLLs passes, which independently confirms the entire **direct typed + GPU** API
surface still compiles. Details per mod below.

---

## blinky

**Purpose** — Builds NxM LCD-style pixel grids out of real engine parts at runtime and
attaches them to a live vehicle. Each pixel is an a/b engine pair (net-zero thrust);
pixels are toggled by activating/deactivating their `EngineController`s. Supports
multiple named grids per vehicle, patterns, scrolling, static display, global scan, and
a render-skip performance toggle. Controllable via ImGui.

**Unscience integration** — `BlinkySubmod : ISubmod` (`blinky.lib/BlinkySubmod.cs:11`),
instantiated by the supermod (`unscience/Mod.cs:69`) and the standalone host
(`blinky/Mod.cs:27`). Static singleton `BlinkyGridManager` (`blinky.lib/BlinkyGridManager.cs:38`)
is the shared control surface for the UI and reusable callers. Render-skip patches applied via
`BlinkyPatches.Apply` (`blinky/Patcher.cs:14` standalone, `unscience/Patcher.cs:57` embedded).

**UI/hotkeys** — Standalone window "blinky", 480x640, `MenuBar`, toggled by **F11**
(`blinky/Mod.cs:52,79`). Create form (size/spacing/scale/offset/layout/engine/vehicle/
grid-name), per-grid pattern + destroy + diagnose sections, "Render engine meshes"
checkbox, Debug menu "Scan for blinky grids". All ImGui via `Brutal.ImGuiApi`.

**Persistence** — Native saves retain the real engine parts, while Unscience saves named grid/cell
associations, ownership, render flags and scrolling. Replay rebinds native-loaded part paths; scan-only
recovery is insufficient because KSA omits ordinary `pixel_*` ids. No StarMap save hooks.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature, or asset path) | Decomp/Content path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Harmony prefix | `blinky.lib/BlinkyPatches.cs:26,30,60` | `PartModelModule.UpdateRenderData(in double4x4, bool, IViewport, int)` — return false skips submit | `KSA/PartModelModule.cs:87` | Yes | **retyped @5402** — 3rd param `Viewport` (class, removed) → `IViewport`; body now `FullPart.IsLightSwitchedOff()` (`:106`) | Patched by name only; no overload (1 method); prefix takes only `__instance`, so the retype is transparent. Game itself uses `Parent.FullPart.IsLightSwitchedOff()` here (`KSA/Part.cs:1357`). |
| 2 | Harmony prefix | `blinky.lib/BlinkyPatches.cs:27,31,66` | `PartModelDynamicModule.UpdateRenderData(in double4x4, bool, IViewport, int)` | `KSA/PartModelDynamicModule.cs:55` | Yes | **retyped @5402** (same as #1) | Same shape as #1. |
| 3 | Harmony prefix | `blinky.lib/BlinkyPatches.cs:28,32,72` | `PartModelGlassModule.UpdateRenderData(in double4x4, bool, IViewport, int)` | `KSA/PartModelGlassModule.cs:72` | Yes | **retyped @5402** (param only; body unchanged) | 4745 merged ModelGlass+ModelEye **shaders**; the C# module class is unchanged. Downstream `PartModel.AddInstance`/`PartModelGlass.AddInstance` now early-return unless `viewport.HasAny(ViewportOptionFlags.RenderPartModels)` (`KSA/PartModel.cs:410`, `KSA/PartModelGlass.cs:504`) — every preset carries that flag (`KSA/ViewportPresets.cs`), so no viewport gains or loses part models. |
| 4 | Direct (in prefix) | `blinky.lib/BlinkyPatches.cs:63,69,75` | `Module.Parent`→`Part`; `Part.FullPart`; `Part.Id` (string `StartsWith("pixel_")`) | `KSA/ModuleBase.cs:31`, `KSA/Part.cs:1123`, `:698` | Yes | None | `ModuleBase.Parent` is `required Part { get; set; }`; `FullPart => PartParent ?? this`. |
| 5 | Direct | `blinky.lib/LcdGridBuilder.cs:51` | `ModLibrary.Get<PartTemplate>(string id)` (string-keyed lookup) | `KSA/ModLibrary.cs:1042` | Yes | None | `Get<T>(string) where T:IKeyed`; throws if id missing. Runtime string id (see assets). |
| 6 | Direct | `blinky.lib/LcdGridBuilder.cs:360` | `new Part(string inName, PartTemplate inTemplate, PartInstance?=null, Part?=null)` | `KSA/Part.cs:1386` | Yes | None | |
| 7 | Direct | `blinky.lib/LcdGridBuilder.cs:148,238`; `:162,246` | `PartTree.CreateFromNewPartTree(Part rootPart)`; `Vehicle.UpdateVehicleConfiguration()` | `KSA/PartTree.cs:173`; `KSA/Vehicle.cs:1864` | Yes | None (bodies unchanged @5402) | Core build/destroy path; both unchanged. |
| 8 | Direct | `blinky.lib/LcdGridBuilder.cs:37,236,148` | `Vehicle.Parts` (PartTree, get+set); `PartTree.Root`; `PartTree.Parts` | `KSA/Vehicle.cs:604`; `KSA/PartTree.cs:97,95` | Yes | None | `Vehicle.Parts` is a public field. |
| 9 | Direct | `blinky.lib/LcdGridBuilder.cs:103-104,224-228` | `Part.TreeParent` (Part?); `Part.TreeChildren` (List<Part>) | `KSA/Part.cs:664,666` | Yes | None | Manual tree wiring. |
| 10 | Direct | `blinky.lib/LcdGridBuilder.cs:125,297` | `Part.SetStage(int)`; `Part.Stage` (get) | `KSA/Part.cs:1210`, `:835` | Yes | None | |
| 11 | Direct | `blinky.lib/LcdGridBuilder.cs:466` (connect); `:213-215` (disconnect) | `Part.Connection.Connect(IConnector, IConnector)`; `Part.Connections` (List<Connection>); `Connection.Disconnect()`; `Connector.CanConnect()`; `Connector.Connection` | `KSA/Part.cs:538,670,554,343` | Yes | None | 🔴 **Semantics matter, not just the signature.** The engine side MUST be the engine's own declared feed `Connector` — a bare `Part`↔`Part` connection is rejected by `ResourceManager.CanFlowAcross` (see #22). The fuel side stays a `Part` (`Part.CanConnect()` is always `true`, `KSA/Part.cs:1979`), so one tank anchors the whole grid. |
| 12 | Direct | `blinky.lib/LcdGridBuilder.cs:391,394,397` | `Part.PositionParentAsmb` (double3); `Part.Asmb2ParentAsmb` (doubleQuat); `Part.Scale` (double3) — all settable | `KSA/Part.cs:752,766,815` | Yes | None | |
| 13 | Direct | `blinky.lib/LcdGridBuilder.cs:419,428,494,624,656` | `Part.SubtreeModules` (ModuleList); `ModuleList.Get<TModule>()` for `Tank`, `RocketCore`, `EngineController` | `KSA/Part.cs:688`; `KSA/ModuleList.cs:178`; `KSA/Tank.cs`, `KSA/EngineController.cs` | Yes | None | `Get<TModule>()` returns `Span<TModule>` (`.Length`/index used). |
| 14 | Direct | `blinky.lib/LcdGridBuilder.cs:418,522,416` | `Part.IsSubPart`; `Part.Template` (PartTemplate); `Vehicle.Parts.Parts` | `KSA/Part.cs:1081,576` | Yes | None | |
| 15 | Direct | `blinky.lib/LcdGridBuilder.cs:659` | `EngineController.MinimumThrottle` (float, settable) | `KSA/EngineController.cs:38` | Yes | None (file byte-identical @5402) | Set **before** the PartTree rebuild — `PartTree.RecomputeRocketControls` (`KSA/PartTree.cs:747-770`) folds it into `PartTree.EngineThrottleMin`, which clamps the vehicle's manual throttle. |
| 16 | Direct | `blinky.lib/BlinkyGridManager.cs:223,252,266`; `NonLcdEngineCache.cs:46` | `EngineController.SetIsActive(Vehicle?, bool)` — pixel on/off | `KSA/EngineController.cs:77` | Yes | None (file byte-identical @5402) | Called with `null` vehicle arg. |
| 17 | Direct | `blinky.lib/NonLcdEngineCache.cs:35` | `EngineController.IsActive` (get) | `KSA/EngineController.cs:44` | Yes | None | |
| 18 | Direct | `blinky.lib/BlinkyGridManager.cs:258` | `Vehicle.SetEnum(Enum?)` with `VehicleEngine.MainIgnite` | `KSA/Vehicle.cs:6096`; `KSA/VehicleEngine.cs:5` | Yes | None | Ignites vehicle before lighting pixels. |
| 19 | Direct (diagnostics) | `blinky.lib/BlinkySubmod.cs:712,753,760` | `Combustor.ResourceManager` (field); `ResourceManagerBase.ConsumptionOrder` (`Tank[][]?` property); `ResourceManagerBase.FlowRule` | `KSA/Combustor.cs:13`; `KSA/ResourceManagerBase.cs:69,25` | Yes | None | **Replaced the old string-reflection probe** of `NearestToFurtherestNode*` (2026-08-23): `ConsumptionOrder` is public and already resolves the active `FlowRule`, so the diagnose path is fully typed — **no string reflection remains anywhere in `blinky.lib`** (re-verified 5402). |
| 20 | Direct (debug) | `blinky.lib/BlinkySubmod.cs:664-666,766` | `Vehicle.GetManualThrottle()`; `Vehicle.FlightComputer`; `Vehicle.IsSet<VehicleEngine>(T, bool)`; `EngineController.Cores` (RocketCore[]); `Connection.OtherPart(Part)` | `KSA/Vehicle.cs:1245,467,6206`; `KSA/EngineController.cs:36` (`Cores`); `KSA/Part.cs:501` | Yes | None | `IsSet(VehicleEngine.MainIgnite, false)` routes to the private `Vehicle.IsEngine` and reads `_manualControlInputs.EngineOn` — the only public read of the ignition flag. |
| 21 | Abstraction | `blinky.lib/BlinkyGridManager.cs:280`; `BlinkySubmod.cs` | `VehicleProvider.GetAllVehicles()` / `GetControlledVehicle()` (ksa-abstractions.lib) | `MeowSci.KsaAbstractions` (repo lib) | Yes | None | Game coupling lives in ksa-abstractions scope. |
| 22 | Direct | `blinky.lib/LcdGridBuilder.cs:491-497` | `RocketCore.FeedConnectors` (`Part.Connector[]`, bound in `RocketCore.OnFullPartCreated` → `BindFeedPoints` from the template's `ConsumerFeedWiring`/`FeedsFrom`) | `KSA/RocketCore.cs:20,24,26` | Yes | None (file byte-identical @5402) | 🔴 **The load-bearing dependency of the whole ignition path.** `ResourceManager.CanFlowAcross` (`KSA/ResourceManager.cs:274-282`) rejects the first hop out of the consumer part unless the connection sits on one of these connectors (`IsDeclaredFeedConnection`, `:305`). If the template wiring resolves to nothing, `FeedConnectors` is empty and the engine reaches no propellant. |
| 23 | Direct | `blinky.lib/LcdGridBuilder.cs:624-631`; `BlinkySubmod.cs:712` | `Combustor` type test on `RocketCore`; `Combustor.ResourceManager`; `ResourceManagerBase.ConsumptionOrder` | `KSA/Combustor.cs:7,13`; `KSA/ResourceManagerBase.cs:69` | Yes | None | Post-build propellant verification. `Combustor.ComputePropellantAvailable` (`KSA/Combustor.cs:60`) is `ResourceManager?.ResourceAvailable(...) ?? false`, so an empty `ConsumptionOrder` means the pixel can never light. `SolidMotor` cores legitimately have no `ResourceManager`. |
| 24 | Direct | `blinky.lib/LcdGridBuilder.cs:307` | `PartTree.ResourceGroupList` (public field); `ResourceGroupList.CalculateStages(bool = false)` | `KSA/PartTree.cs:27`; `KSA/ResourceGroupList.cs:100` | Yes | New this change | Public trigger for the **internal** `PartTree.RecreateResourceManagers` (`KSA/PartTree.cs:592`) — used by `RepairFuelFeeds` to rebuild the fuel graphs without rebuilding the part tree. If `CalculateStages` stops calling it, repair silently no-ops. |

**Game assets referenced**

| Asset | Kind | Referenced as | Content path (NEW) | In NEW? | Δ vs OLD |
|---|---|---|---|---|---|
| `CorePropulsionA_Prefab_EngineA1` | Engine part template (former default) | `ModLibrary.Get<PartTemplate>` id | — | **No** (gone since before 5117) | removed from blinky 2026-08-23; see note below |
| `CorePropulsionA_Prefab_EngineA2..A6` | Engine part templates (UI presets `BlinkySubmod.cs:51-57`; default A3 in `LcdGridConfig.cs:49`) | `ModLibrary.Get<PartTemplate>` id | `<Part Id=…>` in `Core/CorePropulsionAAssets.xml:446,531,616,648,770` + `<PartGameData>` in `Core/CorePropulsionAGameData.xml:118,182,246,291,373` | Yes | `@5402` each `<Part>` gained `CrashTolerance="3e6"` (new part-structural-limits attr); ids, wiring and GameData byte-identical |

Note (superseded): the 4750 pass recorded `EngineA1` as present-as-`<Part>` but absent from
the build-menu catalog. At **5348 it is gone from Content entirely** — neither
`CorePropulsionAAssets.xml` nor `CorePropulsionAGameData.xml` mentions it, and only A2–A6 exist.
`ModLibrary.Get` throws for it. Both the UI preset list and `LcdGridConfig.EnginePartId` now
default to **A3**, and `EngineA1` has been removed from `BlinkySubmod.EnginePresets`.

---

### 🔴 Root cause of "blinky broken" — the propellant feed (fixed 2026-08-23)

This is the long-standing `ISSUES.md` entry. The grid built and changed vehicle mass, but no
pixel ever lit, because **the pixel engines could not reach propellant**, so
`EngineControllerState.IsPropellantAvailable` stayed false and
`FlightComputer.CommandEngineThrottles` (`KSA/FlightComputer.cs:421-443`) never commanded a
throttle no matter how many times the engine was activated. With no plume there is nothing to
see — the meshes themselves are scaled to ~1% and are effectively invisible by design.

The 5018 fuel/resource rewrite added two gates that blinky's original wiring fails:

1. **`ResourceManager.CanFlowAcross` (`KSA/ResourceManager.cs:279-282`)** rejects the first hop
   out of the consumer part unless the connection sits on a connector declared in the part
   template's `ConsumerFeedWiring`/`FeedsFrom` (`IsDeclaredFeedConnection`, `:305`).
2. **`ResourceManagerBase.CanFlowAcross` (`KSA/ResourceManagerBase.cs:209-212`)** requires the
   connection to carry the combustor's `PlumbingCapability` — `BulkFluid` for these liquid engines.

blinky connected `Part`↔`Part` (`Part.Connection.Connect(pixelPart, fuelPart)`). `Part` implements
`Connection.IConnector`, but `Part.EndpointCapabilities` (`KSA/Part.cs:1066`) is `null` for anything
that is not a fuel-port part, and `ConnectorCapabilityExtensions.Intersect(null, null)` yields
`Electricity | ServiceFluid` — no `BulkFluid`. The connection also is not a declared feed connection.
Both gates fail, `PopulateGraph` never leaves the engine, `ConsumptionOrder` is empty, and
`Combustor.ComputePropellantAvailable` returns false forever.

**Fix** — connect the engine's own declared feed `Connector` (`RocketCore.FeedConnectors`, e.g.
EngineA3's `_connector3`, authored `<Capabilities>BulkFluid</Capabilities>` in
`Core/CorePropulsionAGameData.xml:189-193`) to the fuel `Part`. `Intersect(connectorCaps, null)`
returns the connector's own capabilities, so `BulkFluid` survives, and the connection is by
definition a declared feed connection. The fuel side stays a bare `Part` because
`Part.CanConnect()` is unconditionally `true` — one tank can anchor every pixel in the grid.
See integration points #11, #22–#24.

This was **not** a 5348 regression: the same two gates are present at 5261, so blinky has been
dark since the 5018 feed rewrite.

⚠️ **Second, softer requirement — the tank must hold the right propellant.** Every liquid
`CorePropulsionA_Prefab_EngineA2..A6` thrust chamber is authored `<Reaction Id="Hydrolox">`
(`Core/CorePropulsionAGameData.xml:10,39,68,137,201,313,392`), and `ResourceManager.CreateOrders`
(`KSA/ResourceManager.cs:335`) only admits tanks where `tank.ContainsAny(Mix)`. A vehicle whose tanks
carry anything else will still show a dark grid despite correct wiring, so
`VerifyPropellantFeeds` names the desired mix in its warning. blinky calls
`ResourceGroupList.CalculateStages()` with `reconfigureTankContents: false`, so it never rewrites the
vehicle's tank mixes out from under the real engines.

**Update-risk findings (4750 -> 5018)**

- 🔴 **BREAKING (fixed): `RocketCore.ResourceManager` moved.** 5018 rewrote the fuel/resource feed
  model: `RocketCore` no longer owns a `ResourceManager` — it now exposes `FeedConnectors`,
  `ResolvedConsumerFeeds`, `TryPrepareDrain`/`TryAccumulateDrain` and a `DrainContext`. The
  `ResourceManager` (and its `FlowRule`) live on the **`Combustor`** subclass; the other
  `RocketCore` subclass, the new **`SolidMotor`**, legitimately has none. `BlinkySubmod.DiagnoseGrid`
  now tests `core is Combustor` before reading it. Diagnostics-only path — no functional impact.
- ✅ **Engine activation is unaffected by the rev-4914 control-module lockout.**
  `EngineController.SetIsActive(Vehicle?, bool)` and `ThrusterController.SetIsActive` are
  **byte-identical** to 4750 — the new lockout ("no vehicle control module" disables the Active
  checkboxes, Decouple and the staging key) was implemented in the **UI layer only**. blinky drives
  engines through `SetIsActive(null, on)` and is not gated by it.
- ✅ **Staging reads survive the staging rewrite.** `StageList.cs` and `Staging.cs` were deleted in
  favour of `ResourceGroups`/`ResourceGroupList`/`SequencePerformance*`, but `Part.Stage` and
  `Part.SetStage(int)` are unchanged, so `LcdGridBuilder`'s stage alignment is still correct. Note
  rev 4873 changed `SetStage`'s internals (bulk-guarded rebuilds — previously every `SetStage` rebuilt
  every resource manager), so bulk stage assignment is now cheaper, not different.
- ✅ All three render-skip targets (#1–#3) are still byte-identical in signature; the
  `Parent.FullPart.LightSwitch.LightIsActive` chain is intact (`LightIsActive` is on `PowerConsumer`).
- ✅ `PartTree.CreateFromNewPartTree(Part)` and the part-tree build/destroy API are unchanged.

#### Carried over from the 4680 -> 4750 review

- No breaking deltas. All three render-skip targets (#1–#3) are byte-identical in
  signature and even line number; the `Parent.FullPart.LightSwitch.LightIsActive` chain
  the game uses inside those very methods is intact.
- 4693 (MeshIndirect merge) / 4745 (ModelGlass+ModelEye shader merge) do **not** affect
  blinky: it prefix-returns `false` to skip the *entire* `UpdateRenderData`, never
  touching MeshIndirect internals, shader layouts, or the glass module's render body.
- The whole part-tree build/destroy API (#5–#14, the actual functional core) is unchanged.
- Only latent fragility was the **Diagnose-only** string-field reflection (#19) — since
  replaced by the typed `ConsumptionOrder` read (2026-08-23); blinky.lib has no string reflection now.

---

## its-so-shiny

**Purpose** — Same grid concept as blinky but each pixel is a stock `LightPart` instead
of an engine. Pixels are toggled through the light's `PowerConsumer` light switch
(`LightIsActive`); color/intensity are applied per `PartTemplate` via Zippo's
`LightController`. Grids connect to battery-bearing parts for power.

**Unscience integration** — `ItsSoShinySubmod : ISubmod`
(`its-so-shiny.lib/ItsSoShinySubmod.cs:11`), instantiated by the supermod
(`unscience/Mod.cs:82`) and standalone host (`its-so-shiny/Mod.cs:27`). Static
`ShinyGridManager` (`its-so-shiny.lib/ShinyGridManager.cs:31`) is the control surface.
Render-skip patches via `ShinyPatches.Apply` (`its-so-shiny/Patcher.cs:15`,
`unscience/Patcher.cs:58`). Color/intensity reuse `MeowSci.ZippoLib.LightController`
(sibling lib — the actual light-template reflection lives in the zippo scope).

**UI/hotkeys** — Standalone window "its-so-shiny", 500x640, `MenuBar`, **F11**
(`its-so-shiny/Mod.cs:52,79`). Create form (size/spacing/scale/offset/layout/intensity/
color/vehicle/grid-name), per-grid appearance + pattern + destroy sections, "Render
light meshes" checkbox (default off → mesh renders only while the light is active),
Debug menu "Scan for shiny grids".

**Persistence** — Native saves retain real light parts and switch state; Unscience saves grid/cell
associations, ownership, appearance and scrolling. Replay rebinds paths because native saves omit
ordinary `shiny_*` ids.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature, or asset path) | Decomp/Content path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Harmony prefix | `its-so-shiny.lib/ShinyPatches.cs:25,29,66` | `PartModelModule.UpdateRenderData(in double4x4, bool, IViewport, int)` | `KSA/PartModelModule.cs:87` | Yes | **retyped @5402** (`Viewport` → `IViewport`; prefix takes only `__instance`) | Coexists with blinky #1 (different Id prefix). |
| 2 | Harmony prefix | `its-so-shiny.lib/ShinyPatches.cs:26,30,69` | `PartModelDynamicModule.UpdateRenderData(in double4x4, bool, IViewport, int)` | `KSA/PartModelDynamicModule.cs:55` | Yes | **retyped @5402** | |
| 3 | Harmony prefix | `its-so-shiny.lib/ShinyPatches.cs:27,31,72` | `PartModelGlassModule.UpdateRenderData(in double4x4, bool, IViewport, int)` | `KSA/PartModelGlassModule.cs:72` | Yes | **retyped @5402** | `PartModel(Glass).AddInstance` now gated on `ViewportOptionFlags.RenderPartModels` (see blinky #3) — every preset has it. |
| 4 | Direct (in prefix) | `its-so-shiny.lib/ShinyPatches.cs:58-66` | `Module.Parent`→`Part`; `Part.FullPart`; `Part.Id`; `Part.LightSwitch` (PowerConsumer?); `PowerConsumer.LightIsActive` (bool) | `KSA/ModuleBase.cs:31`; `KSA/Part.cs:1123,686`; `KSA/PowerConsumer.cs:30` | Yes | **game chain changed @5402** | Mesh shown iff `LightIsActive`. The game's own hide test in #1/#2 is now `Part.IsLightSwitchedOff()` (`KSA/Part.cs:1357`) = `!LightIsActive \|\| !lightSwitch.IsSwitchedOn()` (new `PowerConsumer.IsSwitchedOn()` `:50`, reads `Tree.PowerConsumers.States[StatesIdx].Active`), and returns false when `lightSwitch.Parent.Tree != Tree`. The mod deliberately ignores the powered state (a pixel the mod turned on shows its mesh even if the bus is dead); unchanged behaviour. `Part.ResetModuleProperties` (`:1803`) nulls and re-resolves `LightSwitch` — always read it live, never cache. |
| 5 | Direct | `its-so-shiny.lib/ShinyGridBuilder.cs:27` | `ModLibrary.Get<PartTemplate>("LightPart")` | `KSA/ModLibrary.cs:1042` | Yes | None | Runtime string id (see assets). |
| 6 | Direct | `its-so-shiny.lib/ShinyGridBuilder.cs:157` | `new Part(string, PartTemplate, ...)` | `KSA/Part.cs:1386` | Yes | None | |
| 7 | Direct | `its-so-shiny.lib/ShinyGridBuilder.cs:94,147`; `:98,148` | `PartTree.CreateFromNewPartTree(Part)`; `Vehicle.UpdateVehicleConfiguration()` | `KSA/PartTree.cs:173`; `KSA/Vehicle.cs:1864` | Yes | None | |
| 8 | Direct | `its-so-shiny.lib/ShinyGridBuilder.cs:17,146,76-77` | `Vehicle.Parts`/`PartTree.Root`; `Part.TreeParent`/`Part.TreeChildren` | `KSA/Vehicle.cs:604`; `KSA/PartTree.cs:97`; `KSA/Part.cs:664,666` | Yes | None | |
| 9 | Direct | `its-so-shiny.lib/ShinyGridBuilder.cs:205,209-210` | `PartTree.Modules.Get<Battery>()`; `Battery.Parent`→`Part`; `Part.FullPart` | `KSA/PartTree.cs:37`; `KSA/ModuleList.cs:178`; `KSA/Battery.cs:9`; `KSA/ModuleBase.cs:31`; `KSA/Part.cs:1123` | Yes | None | Battery anchors for power partitioning. |
| 10 | Direct | `its-so-shiny.lib/ShinyGridBuilder.cs:87,221,131,133` | `Part.SetStage(int)`; `Part.Connection.Connect(IConnector,IConnector)`; `Part.Connections`; `Connection.Disconnect()` | `KSA/Part.cs:1210,538,670,554` | Yes | None | |
| 11 | Direct | `its-so-shiny.lib/ShinyGridBuilder.cs:181,185,186` | `Part.PositionParentAsmb`; `Part.Asmb2ParentAsmb`; `Part.Scale` | `KSA/Part.cs:752,766,815` | Yes | None | |
| 12 | Direct | `its-so-shiny.lib/ShinyPixelCell.cs:24,27`; `ShinyGridBuilder.cs:234,236` | `Part.LightSwitch` (PowerConsumer?); `PowerConsumer.LightIsActive` (set) | `KSA/Part.cs:686`; `KSA/PowerConsumer.cs:30` | Yes | None | Primary pixel on/off path. |
| 13 | Direct | `its-so-shiny.lib/ShinyPixelGrid.cs:147,150` | `Part.Template`; `Part.SubParts` (ReadOnlySpan<Part>) | `KSA/Part.cs:576,1079` | Yes | None | Recursive light-part discovery. |
| 14 | Transitive (ZippoLib) | `ShinyPixelCell.cs:31,36-37`; `ShinyGridManager.cs:218-223` | `LightController.{ApplyColor,ApplyIntensity,GetLightComponents,WriteColor,WriteIntensity,HasLights}` (operates on light template render data) | `MeowSci.ZippoLib` (repo lib; game coupling catalogued in zippo scope) | Yes | None | Color/intensity writes; verify in zippo scope file. |
| 15 | Abstraction | `ShinyGridManager.cs:155`; `ItsSoShinySubmod.cs` | `VehicleProvider.GetAllVehicles()`; `PartHelpers.GetAllParts` (ksa-abstractions.lib) | `MeowSci.KsaAbstractions` | Yes | None | |

**Game assets referenced**

| Asset | Kind | Referenced as | Content path (NEW) | In NEW? | Δ vs OLD |
|---|---|---|---|---|---|
| `LightPart` | Light part template (default `ShinyGridConfig.cs:11`) | `ModLibrary.Get<PartTemplate>` id | `<Part Id="LightPart">` `Core/PartAssets.xml:21`; `<PartGameData Id="LightPart">` with `<PowerConsumer LightSwitch="true">` `Core/CoreElectricalAGameData.xml:165` | Yes | None (both files byte-identical 5348→5402) |

**Update-risk findings (4680 -> 4750)**

- No breaking deltas. Render-skip targets (#1–#3) identical to blinky's and unchanged.
  The `LightPart` template + its `PowerConsumer LightSwitch="true"` definition are
  identical in OLD and NEW Content.
- Part-tree / battery-power build path (#5–#13) unchanged.
- Color/intensity (#14) flows through Zippo's `LightController`; its game-side coupling
  is owned by the zippo scope file and should be checked there, but no signature change
  was observed in the `PartTemplate`/light render-data types it depends on.

---

## thug-life

**Purpose** — Draws the "thug life" sunglasses meme as a textured cut-out quad anchored
to any part/subpart of any vehicle, riding along in 3D. Pure custom GPU rendering: builds
its own Vulkan pipeline/descriptor/texture and injects draws into KSA's offscreen MSAA
main pass via a Harmony postfix. Does **not** create parts.

**Unscience integration** — `ThugLifeSubmod : ISubmod`
(`thug-life.lib/ThugLifeSubmod.cs:14`), instantiated by the supermod
(`unscience/Mod.cs:91`) and standalone host (`thug-life/Mod.cs:27`). The render postfix
is applied via `ThugLifeRenderPatches.Apply` (`thug-life/Patcher.cs:18`,
`unscience/Patcher.cs:51`) and dispatches to the static
`ThugLifeRenderManager.Instance`/`.Active` so each host (standalone vs supermod) drives
its own manager on its own assembly load context. GPU resources own pipeline + texture +
buffers; render disables itself on first error (`ThugLifeRenderManager.cs:106,134`).

**Init timing (load-order constraint)** — the GPU resources are built **lazily, on the
first entry** (`ThugLifeRenderManager.EnsureGpuResources`, `:91-115`), never in the
constructor. StarMap fires `[StarMapAllModsLoaded]` from a postfix on
`ModLibrary.LoadAll()` (`KSA/Program.cs:942`), but the game does not create
`Program.OffscreenTarget` until `BuildRenderTargets()` further down that same boot method
(`KSA/Program.cs:970`, decl `:1507`) — so building the pipeline at `Initialize()` dereferenced a null
`RenderTarget` and the submod reported *"init failed: Object reference not set to an
instance of an object"*. ⚠ Any future work that moves GPU allocation back into
`Initialize()` re-introduces this. Same discipline as the sibling gatOS port.

**UI/hotkeys** — Standalone window "Thug Life", 500x600, **F12**
(`thug-life/Mod.cs:51,78`). Create form (vehicle / part / optional subpart filtered
combos + position/rotation/width/height), per-entry transform + Visible + Remove sections.
An **animate thug** button (`ThugLifeSubmod.cs:204`) appears beside *Add Sunglasses*
only when the selected vehicle is a `KSA.KittenEva`; it applies the fixed pose in
`KittenGlassesPreset.cs` and slides the entry into place via `ThugLifeSlide`, driven from
`ThugLifeRenderManager.Update(dt)` (called from the submod's `Update`, i.e. `OnBeforeUi`
in both hosts). The slide is pure mod-side math — it touches no game API.

**Persistence** — None. Entries are in-memory only (`ThugLifeRenderManager._entries`);
lost on reload. No StarMap save hooks, no disk I/O.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature, or shader/asset path) | Decomp/Content path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Harmony postfix | `thug-life.lib/ThugLifeRenderPatches.cs:19-21,44` | `SuperMeshRenderSystem.RenderMainPass(CommandBuffer commandBuffer)` — postfix records quad draws into the active offscreen pass | `KSA/SuperMeshRenderSystem.cs:347` | Yes | `:338`→`:347` @5402; body gained only a profiler `TagRegion(SkinnedTwoSidedMeshes)` around the new two-sided skinned technique's draws (parachute canopies) | Single method, patched by name. Called 3x from `KSA/Program.cs`: `:4395` (`RenderViewport`, once per **non-main visible** viewport — secondary + crew-portrait), `:4656` (main flight scene), `:4856` (editor). The part-thumbnail viewport does **not** go through it. |
| 2 | Render asset (shader) | `thug-life.lib/ThugLifeQuadRenderer.cs:114` | `ModLibrary.Get<ShaderReference>("UnlitMeshVert")` | id→path in `Core/DefaultAssets.xml:53`; file `Core/Shaders/Mesh/UnlitMesh.vert` | Yes | None (file byte-identical 5348→5402; `DefaultAssets.xml` only gained `StaticObjectPrePassIndirectFrag` at `:62`) | Stock shader; **not** MeshIndirect/Model* — untouched by 4693/4745. |
| 3 | Render asset (shader) | `thug-life.lib/ThugLifeQuadRenderer.cs:115` | `ModLibrary.Get<ShaderReference>("UnlitMeshFrag")` | id→path in `Core/DefaultAssets.xml:54`; file `Core/Shaders/Mesh/UnlitMesh.frag` | Yes | None (byte-identical) | Frag hard-writes `alpha=1.0` (cut-out via geometry, per renderer comment). |
| 4 | Direct (render) | `thug-life.lib/ThugLifeQuadRenderer.cs:117` | `RenderTechnique.CreateShaderStages(Device, Span<ShaderReference>, Span<VkSpecializationInfo>=default)` | `RenderCore/RenderTechnique.cs:35` | Yes | None (file byte-identical) | `ShaderReference : FileReference, IKeyed, IComboable` (`KSA/ShaderReference.cs:21`). |
| 5 | Direct (render-pass) | `thug-life.lib/ThugLifeQuadRenderer.cs:152` | `Program.OffscreenTarget.SetupGraphicsPipeline(ref VkGraphicsPipelineCreateInfo)` — `RenderTarget : IRenderPassInfo` | `KSA/Program.cs:457`, `KSA.Rendering/RenderTarget.cs:356` | Yes | **REPLACED @5261** — was `Program.OffScreenPass` (`RenderPassState`) → `.SampleCount`, `.Pass`; unchanged @5402 (`RenderTarget.cs` byte-identical) | Game migrated the main scene pass to **dynamic rendering**; the old property no longer exists. **Null until `BuildRenderTargets()` (`Program.cs:970`), which runs AFTER `ModLibrary.LoadAll()` (`:942`) — i.e. after `[StarMapAllModsLoaded]`. Pipeline build must stay lazy (see *Init timing* above).** Non-main viewports own their targets but build them with the same `Program.Instance.ColorFormat` + `GameSettings.GetSampleCount()` (`KSA.Rendering/ViewportRenderSurface.cs:105-107`, `KSA/ViewportBase.cs:128`), so the main-target-built pipeline stays compatible in every `RenderMainPass` call. |
| 6 | Direct (render) | `thug-life.lib/ThugLifeQuadRenderer.cs:133,134,136` | `Presets.InputAssembly.TriangleList`; `Presets.Rasterization.Fill.CullNone`; `Presets.BlendState.BlendColorAlpha` | `RenderCore.Pipelines/SimplePipelineCreator.cs:15` (+ Brutal abstractions) | Yes | None | Pipeline state presets; compile-verified by build. |
| 7 | Direct (render) | `thug-life.lib/ThugLifeQuadRenderer.cs:135` | `RenderingPresets.ReverseZDepthStencil.DepthTestWrite` | `KSA/RenderingPresets.cs:14` (used widely, e.g. `KSA.Rendering.Water.Rendering/OceanRenderer.cs:311`) | Yes | None (file byte-identical) | Reverse-Z depth test+write; the game still clears depth to `0f`. 4730/4733 depth-prepass changes did not alter this preset. |
| 8 | Direct (render) | `thug-life.lib/ThugLifeQuadRenderer.cs:130,131`; `:50`,`TextureFactory.cs:31,35,53` | `Renderer.{Device,Allocator,Graphics,DynamicStateInfo,ViewportState}` | `Core/Renderer.cs` (byte-identical) | Yes | None | Compile-verified. |
| 9 | Direct (render) | `thug-life.lib/ThugLifeQuadRenderer.cs:252,256` | `Program.GetRenderCamera()` (= `RenderedViewport.GetCamera()`); `Camera.MVP.viewProjection` | `KSA/Program.cs:642`, `:491` (`RenderedViewport : IViewport` @5402); `KSA/Camera.cs:63`, `KSA/ViewProjection.cs:13` | Yes | **CHANGED (mod-side @5348)** — was `Program.GetMainCamera()` (`:632`); `RenderedViewport` retyped `Viewport`→`IViewport` @5402 (mod never names the type) | `RenderMainPass` runs once per **visible viewport** (main + the two always-visible 128² crew-portrait viewports since 5261; @5402 `RenderViewport` sets `_renderedViewport = viewport` at `Program.cs:4315` before the call), and ego space is camera-relative, so the main camera drew the portrait passes with the wrong clip transform. Now uses the camera of the viewport being rendered. Mod null-checks defensively. |
| 10 | Direct (render) | `thug-life.lib/ThugLifeQuadRenderer.cs:264` | `Program.SetViewport(CommandBuffer)` | `KSA/Program.cs:4293` | Yes | None (body identical; sizes to `RenderedViewport.Size`) | |
| 11 | Direct | `thug-life.lib/ThugLifeRenderManager.cs:98` | `Program.GetRenderer()` (Renderer) | `KSA/Program.cs:558` | Yes | None | Called from the lazy `EnsureGpuResources()`, not from the constructor. |
| 12 | Direct (ego transform) | `thug-life.lib/ThugLifeQuadRenderer.cs:281,282,283` | `Vehicle.GetMatrixAsmb2Ego(Camera)`; `Part.PositionEgo(ref readonly double4x4)`; `Part.Asmb2Ego(doubleQuat)`; `Vehicle.Asmb2Ego` (doubleQuat) | `KSA/Vehicle.cs:1256,501`; `KSA/Part.cs:1155,1160` | Yes | None | Per-frame model-ego matrix; caller passes `in` to the `ref readonly` param. |
| 13 | Direct (UI) | `thug-life.lib/ThugLifeSubmod.cs:123,129,138` | `Vehicle.Parts.Parts`; `Part.Template.Id`; `Part.Id`; `Part.SubParts` | `KSA/Part.cs:576,698,1079`; `KSA/PartTree.cs:95` | Yes | None | Combo population. |
| 14 | GPU lib (Brutal/RenderCore) | `ThugLifeTextureFactory.cs:33,64`; `ThugLifeQuadRenderer.cs` (pipeline/descriptor/buffers) | `SimpleVkTexture`; `VkUtils.UploadBufferToImage`/`StageAndUploadToBuffer`; `BufferEx`, `DescriptorSetLayoutEx`, `DescriptorPoolEx`, `VertexInput`, `ShaderStages`, `CommandBuffer` | `Brutal.VulkanApi(.Abstractions)`, `RenderCore`, `Core` | Yes | None (`SimpleVkTexture.cs`, `VkUtils.cs` byte-identical @5402) | **4729 bumped Brutal packages** — highest churn surface; compile against 4750 DLLs passes, so the used API is intact. |
| 15 | Abstraction | `thug-life.lib/ThugLifeSubmod.cs:104` | `VehicleProvider.GetAllVehicles()` (ksa-abstractions.lib) | `MeowSci.KsaAbstractions` | Yes | None | |
| 16 | Direct (type test) | `thug-life.lib/KittenGlassesPreset.cs:35` | `KittenEva` (`: Vehicle`) — `vehicle is KittenEva`, gates the **animate thug** button | `KSA/KittenEva.cs:13` | Yes | **NEW (mod-side, 2026-08-23)** | Type identity only, no members touched. A rename/reparent of `KittenEva` is a compile error, not silent drift. Seated kittens are not vehicles and are out of scope here. |
| 17 | Direct (UI) | `thug-life.lib/ThugLifeSubmod.cs:308` | `Vehicle.Parts.Parts` — first top-level part as the kitten fallback anchor | `KSA/PartTree.cs:95` | Yes | **NEW (mod-side, 2026-08-23)** | A `KittenEva` is constructed around its MMU backpack part as root (`KSA/EVADoor.cs:209`), so this is non-empty in practice; null-checked regardless. |

**Game assets referenced**

| Asset | Kind | Referenced as | Content path (NEW) | In NEW? | Δ vs OLD |
|---|---|---|---|---|---|
| `UnlitMeshVert` | Vertex shader | `ModLibrary.Get<ShaderReference>` id | `Core/DefaultAssets.xml:53` → `Core/Shaders/Mesh/UnlitMesh.vert` | Yes | None (id, path, file all identical; md5 `71cc48a5…` @5402) |
| `UnlitMeshFrag` | Fragment shader | `ModLibrary.Get<ShaderReference>` id | `Core/DefaultAssets.xml:54` → `Core/Shaders/Mesh/UnlitMesh.frag` | Yes | None (id, path, file all identical; md5 `4f4adaa1…` @5402) |

Texture is generated programmatically (`ThugLifeTexturePattern.cs`, `R8G8B8A8UNorm`) — no
external texture asset dependency.

**Update-risk findings (5117 → 5261)**

- **CONFIRMED COMPILE BREAK — `Program.OffScreenPass` removed.** `ThugLifeQuadRenderer.cs:127,133`
  read `Program.OffScreenPass.SampleCount` and `.Pass` → **2× CS0117**.

  **This is an architecture change, not a rename.** KSA migrated the main scene pass from classic
  Vulkan render passes to **dynamic rendering**. The offscreen target is now
  `Program.OffscreenTarget` (`RenderTarget : IRenderPassInfo`) — the same object assigned to
  `PassContext.MainOpaquePass` — and it has **no `.Pass` and no `.SampleCount`** (it exposes
  `Samples`). `IRenderPassInfo` now declares exactly one member:
  `SetupGraphicsPipeline(ref VkGraphicsPipelineCreateInfo)`, which:
  - chains a `VkPipelineRenderingCreateInfo` (colour/depth/stencil formats) onto `info.Next`,
  - sets `info.RenderPass = VkRenderPass.NullHandle`,
  - overwrites `MultisampleState` with the target's `Samples`,
  - supplies `ViewportState` when absent.

  → **Fix:** `BuildPipeline` no longer sets `RenderPass`, `Subpass` or a hand-rolled
  `MultisampleState`; it calls `Program.OffscreenTarget.SetupGraphicsPipeline(ref info)` immediately
  before `CreateGraphicsPipeline`. This mirrors the game's own main-pass pipelines
  (`KSA/GenericMeshRenderer.cs:305`, `KSA/PartModelRenderer.cs:184,269`, `KSA/PartModelGlass.cs:269`).
  **The call must stay immediately before pipeline creation** — the structures it points `pNext` at
  are owned by the `RenderTarget` and overwritten on every call.

- ⚠️ **Originated in the unvalidated 5118–5168 window**, not in 5261: `Program.OffScreenPass` exists
  at tag `2026.8.3.5117` (`Program.cs:411`) and is absent from both `5168` and `5261`. It is **not**
  a regression introduced by this build.
- ⚠️ **Needs a live pass (F12).** thug-life drives its own Vulkan pipeline, descriptor set, VB/IB and
  texture upload; only an in-game look confirms the quad still rasterizes. The mod's reverse-Z depth
  preset (`RenderingPresets.ReverseZDepthStencil.DepthTestWrite`) and blend state are unchanged.
- ✅ `SuperMeshRenderSystem.RenderMainPass(CommandBuffer)` — **signature-identical** (line shift only),
  so the postfix still attaches. `UnlitMesh.vert` / `UnlitMesh.frag` and both shader ids
  (`UnlitMeshVert`, `UnlitMeshFrag`) are **byte-identical** this span.
- ✅ `PartTree.CreateFromNewPartTree`, `EngineController.SetIsActive(Vehicle?, bool)`, the three
  `*Module.UpdateRenderData` prefixes and `PartModel.AddInstance` are all signature-identical.
  `PerInstanceData`'s byte layout is **identical**, so the padding-byte hijack remains safe.
- ⚠️ **blinky's default engine part id does not exist** — `"CorePropulsionA_Prefab_EngineA1"`
  (`blinky.lib/LcdGridConfig.cs:47`, `BlinkySubmod.cs:51`). It was removed from
  `Content/Core/CorePropulsionAAssets.xml` **between 5018 and 5117** (absent at 5117/5168/5261); only
  `EngineA2`–`EngineA6` exist (`A2` "LR91 Sea", `A3`/`A6` "LR91 Vac", `A4` "VTR-10", `A5` "LR91 Vac +
  Verniers"). `ModLibrary.Get` throws on a missing id, making this a concrete candidate explanation
  for the **"blinky broken"** entry in [`../ISSUES.md`](../ISSUES.md) — the 5117 pass checked blinky's
  patch targets (all byte-identical) but never its part id. **Pre-existing; recommend defaulting to
  `EngineA2`. Not changed here** (behavioral, outside the compile-blocking scope).

**Update-risk findings (4750 -> 5018)**

- ✅ **No breaking deltas.** `SuperMeshRenderSystem.RenderMainPass(CommandBuffer)` is
  signature-identical. `SuperMeshRenderSystem.cs` did change (+32 lines) but **only in the
  shadow/CSM path**: `RenderShadowPass` gained a `cascadeIndex` parameter and pushes it as a push
  constant, depth pipelines switched `SetPushConstant<InstanceData>` → `SetPushConstant<int>`, a
  `SetCsmFilterSpecConstant` helper was added, and several `AddMacroDefinition` calls were collapsed
  to the shorter overload. thug-life postfixes the **main** pass and touches none of it.
- ✅ **`UnlitMesh.vert` / `UnlitMesh.frag` are byte-identical 4750→5018** (they do not appear in the
  Content diff at all), and their `DefaultAssets.xml` ids are unchanged.
- ✅ **Pipeline assumptions are read dynamically, so render-state churn is absorbed.**
  `ThugLifeQuadRenderer` reads `Program.OffScreenPass.SampleCount`, `Program.OffScreenPass.Pass` and
  `RenderingPresets.ReverseZDepthStencil.DepthTestWrite` at build time rather than hard-coding an MSAA
  count or depth mode — an MSAA/format change would be picked up automatically.
- ⚠ **Watch (visual-only, needs a live pass):** this span added a lot of render work around the
  offscreen pass — screenspace particles (`ScreenspaceParticleRenderer` + new `Composite.frag`),
  `MilkyWayRenderer`, volumetric trails, extruded shadow-cascade frusta (rev 4982), and CSM filter
  spec constants. None of it moves an API thug-life binds to, but a depth/MSAA behavioral change here
  manifests as a mis-drawn quad rather than a crash. **Re-verify visually.**

#### Carried over from the 4680 -> 4750 review

- No breaking deltas. The single Harmony target (`SuperMeshRenderSystem.RenderMainPass`)
  is signature-identical and even same-line; the offscreen-pass + camera + ego-transform
  APIs all match.
- **Shader merges do not affect thug-life.** 4693 (MeshIndirect merge), 4745
  (ModelGlass+ModelEye), 4701/4747 (`MeshIndirect.frag` / `ModelTranslucent.frag`
  cleanups) all touch `MeshIndirect.*` / `Model*.*`; thug-life uses only
  `UnlitMesh.vert`/`UnlitMesh.frag`, whose ids, paths, and files are unchanged.
- **Watch items (low, currently green):** (a) 4729 Brutal package bump is the largest
  potential break surface for the Vulkan calls in #14, but the 4750 build passes; (b)
  4694 offscreen/thumbnail-viewport fixes and 4730/4733 depth-prepass changes share the
  offscreen pass thug-life renders into — signatures intact, but a *behavioral* depth/MSAA
  change here would manifest as a visual glitch rather than a crash. Re-verify visually
  after large render-system updates.

---

## Area summary — Update-risk findings (5261 → 5348)

- ⚠️ **thug-life — the render environment moved under it; two items are not statically clearable.**
  Every binding is intact: `SuperMeshRenderSystem.RenderMainPass(CommandBuffer)` signature unchanged,
  `Program.OffscreenTarget` unchanged (`KSA/Program.cs:438`) so the 5261 dynamic-rendering rebuild still
  applies, and `UnlitMesh.vert`/`UnlitMesh.frag` are **byte-identical** with their `UnlitMeshVert` /
  `UnlitMeshFrag` ids still in `Content/Core/DefaultAssets.xml`. What changed around it:
  1. **Rev 5315 — Vulkan 1.3 → 1.4.** thug-life builds its own pipeline, descriptor set, VB/IB and
     texture upload against the game's device. 1.4 is backward compatible; exercise it once in game.
  2. **Rev 5283 — UI coverage culling.** `Content/Core/Shaders/UiCoverage/*` (seven new ids in
     `DefaultAssets.xml`) plus `GaugeCanvas.RegisterOpaqueCoverage`; expensive shaders are skipped behind
     opaque UI. thug-life's quad is a **postfix on the main pass** and registers no coverage, so it should
     be unaffected — but a mis-culled or z-fighting quad only shows in game.
- ✅ **blinky / its-so-shiny — the O(N³) power DFS is gone, and this is favourable.** Rev 5326 reworked
  vehicle power onto `PartTree.ElectricalCircuits` and moved `PowerManager.PopulateGraph` out of the
  constructor (`KSA/PowerManager.cs:14` @5261) into `OnDrawUi`, behind
  `if (base.ShowFlow && !_displayGraphBuilt)` (`:130-138` @5348) — **the graph is now built only when
  "Draw Graph" is ticked in the part window.** The changelog reports a 4500-consumer craft going from
  3.3 s to ~0.3 ms per power rebuild.

  `blinky.lib/LcdGridBuilder.cs:319` splits grids specifically *"to reduce ResourceManager.PopulateGraph
  cost from O(N³) to O(N³/K²)"* (see also `:62`, `:114`), and `its-so-shiny.lib/ShinyGridBuilder.cs:42`
  places *"distinct battery anchors [for] the per-PowerConsumer DFS in PowerManager.PopulateGraph"*.
  Both now solve a problem the game no longer has. **No change made** — removing those optimisations is a
  behavioral change to grid construction and belongs in its own task.

  Counterweight: `Part.Modules` is now `new ModuleList(keepModuleIdIndex: true)` for **every** part, so
  blinky's thousands of pixel parts each carry an id index. Worth a perf glance in game.
- ✅ **blinky's diagnostics still resolve.** `ResourceManagerBase.PopulateGraph` remains and
  `ResourceManager` is still reached via the `core is Combustor` test. (The former reflective read of
  `NearestToFurtherestNode*` was replaced by the typed `ConsumptionOrder` property on 2026-08-23 — #19;
  no string reflection remains in `blinky.lib`.)
- ✅ **All three render-skip prefixes unchanged** —
  `PartModelModule`/`PartModelDynamicModule`/`PartModelGlassModule.UpdateRenderData(in double4x4, bool,
  Viewport, int)`. `PartTree.CreateFromNewPartTree(Part)` and `EngineController.SetIsActive(Vehicle?, bool)`
  are also unchanged (`EngineController` additionally gained `ISequenced` + a `Sequence` property, rev 5329
  — additive).
- ⚠️ **Lights now register for every viewport** (rev 5301 `ViewportLightModes`): `LightModule.cs:125,141`
  went from `else if (viewport == Program.MainViewport)` to a bare `else`. its-so-shiny's grids light more
  viewports than before (including crew-portrait cams) — check for double-lighting or a cost spike.
- ℹ️ Rev 5333 fixed *"deactivating an engine mid-burn would leave it stuck on forever"*, and rev 5318 fixed
  *"assigning a part to sequence 0 silently zeroing the vehicle's delta-v and TWR"* — both are game bug
  fixes in paths blinky drives.
- ✅ **Closed 2026-08-23 — `LcdGridConfig.EnginePartId` now defaults to `EngineA3`**, and `EngineA1`
  is gone from `BlinkySubmod.EnginePresets`. It was absent from Content since before 5117 and
  `ModLibrary.Get` throws for it.
- ✅ **Closed 2026-08-23 — the real *"blinky broken"* root cause was the propellant feed**, not the
  part id. See *Root cause of "blinky broken"* above and integration points #11, #22–#24. The part-id
  bug only bit callers that used the config default; the feed bug killed every grid regardless.

---

## Area summary — Update-risk findings (5348 → 5402)

Revisions 5349–5400 are **unlogged** in any changelog (the only recorded commit is rev 5401 *"Fixed
crash for incorrect data stride for thumbnail rendering"*); everything below comes from the source
diff. Headline: the `Viewport` class was replaced by `IViewport`/`IGameViewport`/`GameViewport`/
`ViewportBase`/`ViewportRegistry` with a fixed pool of 8 shader slots and per-viewport
`ViewportOptionFlags`. The solution builds clean against 5402; none of these three mods needed a
code change.

- ✅ **blinky / its-so-shiny — clean.** All three render-skip targets are still single, name-resolvable
  methods; only their 3rd parameter was retyped `Viewport` → `IViewport`
  (`KSA/PartModelModule.cs:87`, `PartModelDynamicModule.cs:55`, `PartModelGlassModule.cs:72`) and the
  prefixes bind nothing but `__instance`. `PartModel.AddInstance` / `PartModelGlass.AddInstance` gained
  an early-return on `!viewport.HasAny(ViewportOptionFlags.RenderPartModels)` (`PartModel.cs:410`,
  `PartModelGlass.cs:504`) and the raytracing gate became `viewport.HasAll(UseRaytracing)` — all four
  presets in `KSA/ViewportPresets.cs` (main, secondary, part-thumbnail, character-portrait) carry
  `RenderPartModels` and only the main one `UseRaytracing`, so nothing changes for pixel parts.
  `PartModel.PerInstanceData` is **byte-identical** (`float4x4 ModelMatrix; int StateBitFlag; uint
  EmissiveColor; int packing1; float Wetness`). `EngineController.cs`, `RocketCore.cs`,
  `ResourceGroupList.cs`, `Battery.cs`, `Module.cs` are byte-identical; `Part.cs`, `PartTree.cs`,
  `Vehicle.cs`, `ResourceManager(Base).cs` changed only by `IViewport` signatures on UI methods plus
  additive members (parachutes, `PartStructuralLimits`/`CrashTolerance`, `SubPartGroupRef`,
  `IsLightSwitchedOff`). `CreateFromNewPartTree`, `SetIsActive`, `Connection.Connect`, `SetStage`,
  `UpdateVehicleConfiguration` bodies are unchanged.
- ℹ️ **its-so-shiny — the game's light-hide chain moved.** `PartModelModule`/`PartModelDynamicModule`
  now call `Part.IsLightSwitchedOff()` (`KSA/Part.cs:1357`, also requires the new
  `PowerConsumer.IsSwitchedOn()` `:50` and same-tree ownership) instead of the inline
  `LightSwitch.LightIsActive` + power-state chain. `ShinyPatches.ShouldRenderShinyPart` reads
  `LightIsActive` only, exactly as before — unchanged behaviour, but it now diverges from the game's
  test when the bus is unpowered (mesh shown, light dark). `LightModule.UpdateRenderData` still
  registers a light instance for every viewport (`LightModule.cs:118,134`); `LightPart` content
  byte-identical.
- ✅ **thug-life — bindings intact, environment moved again; needs a live pass (F12).**
  `SuperMeshRenderSystem.RenderMainPass(CommandBuffer)` is signature-identical (`:338`→`:347`); the only
  body change wraps draws of the new `MeshRendererSkinnedPbrTwoSided` technique (parachute canopies —
  `ModelPbr.frag` now flips the normal on `gl_FrontFacing`) in a profiler tag. `Program.OffscreenTarget`
  (`:457`), `GetRenderCamera()` (`:642`), `SetViewport` (`:4293`), `RenderTarget.SetupGraphicsPipeline`
  (byte-identical) and `RenderingPresets` (byte-identical, reverse-Z) are unchanged.
  `UnlitMesh.vert`/`.frag` **and** `MeshIndirect.vert`/`.frag` are **byte-identical**; `DefaultAssets.xml`
  gained one `StaticObjectPrePassIndirectFrag` line, moving nothing thug-life reads. What moved:
  `RenderMainPass` is still called once per visible non-main viewport from `RenderViewport`
  (`Program.cs:4395`) with `_renderedViewport` set first (`:4315`), but those viewports now **own** their
  render surfaces (`KSA.Rendering/ViewportRenderSurface.cs`) instead of sharing `Viewport.OffscreenTarget`
  construction — built with the same `ColorFormat` and `GameSettings.GetSampleCount()` (`:105-107`,
  `ViewportBase.cs:128`), so the pipeline built against the main target remains compatible; the
  part-thumbnail viewport (rev 5401's stride fix) never runs `RenderMainPass`. Only an in-game look
  confirms the quad still rasterizes in the main view and the crew portraits.
- ✅ **Content.** `CorePropulsionAAssets.xml`'s only change is `CrashTolerance="3e6"` on
  `EngineA2..A6` (`:446,531,616,648,770`, new `PartTemplate.CrashTolerance` → `Part.CrashTolerancePascals`
  for the new `PartFailure` system); `CorePropulsionAGameData.xml`, `PartAssets.xml`,
  `CoreElectricalAGameData.xml` byte-identical. ⚠ Pixel engines now carry a crash tolerance — a grid
  scraped along the ground could shed parts under the new structural-limit rules; not statically
  clearable.
- ℹ️ Not this area's surface, for context: `Cursor.InputRay` was removed (graffiti fix, see
  `decals.md`), `GridPass`/`SingleToMultisamplePass`/`GlobalShaderBindings` went to a fixed 8 shader
  slots, `GizmosRenderer.MAX_GIZMO_INSTANCES` 131072 → 655360, and `GenericGizmo.GetSegmentDataByViewport`
  takes `IViewport` (dont-stifle-me, see `part-editor-and-robotics.md`).
- **Needs a live pass:** thug-life quad in main + portrait viewports (above); a blinky grid lit
  once (the render path is unchanged, but rev 5401 and the per-viewport render-surface rework are
  render-loop changes); a shiny grid toggled with "Render light meshes" off to confirm the mesh still
  follows `LightIsActive`.

### Thug Life scene persistence

`ThugLifeSubmod.Persistence` serializes stable part anchors, position/rotation, dimensions and
visibility. Restore uses existing `ThugLifeRenderManager.Add` and shared renderer initialization;
reset clears entries/entrance slides and stale UI part lists while retaining valid shared GPU
resources. Entrance animation is restored at its saved position, stopped. No new render seam or
shader dependency; native repeated-load quad rendering and missing-anchor acceptance remain open.

### Blinky / Shiny scene persistence

Both managers now expose typed save participants and native part-path cell rebinding. Their created
parts already exist in native `PartInstance` trees; replay must not build another grid. KSA omits
ordinary `Part.Id` and assigns new runtime ids, so `PixelGrid.BuildFromPartGroups` and Shiny's direct
cell constructor rebuild the associations. Registry HashSet membership supplements `pixel_*` /
`shiny_*` name recognition in existing render prefixes; Blinky's ordinary-engine cache uses the same
identity check. All memberships clear on native scene reset and update on register/unregister.

Saved engine/switch state stays native. Replay restores active-mask metadata without ignition side
effects, repairs Blinky declared feed connectors and resumes scrolling via source pixels/speed/phase.
No new render patch, asset or shader is introduced. Missing/duplicate cells fail visibly. Native
part-count, Destroy ownership, saved power/fuel wiring and post-load render suppression require a
live acceptance run; shared template color limitations remain unchanged.
