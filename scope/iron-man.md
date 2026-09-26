# Iron Man — EVA editor, connectors and vessel physics

## KSA 5482 (5438 → 5482) verification

Verified 2026-09-25 against `2026.9.22.5482` (revs 5439–5481), diffed from `2026.9.10.5438`.
Static/managed only: the whole solution builds and the `iron-man`, `iron-man-flight` and
`iron-man-mode` suites pass. Those suites compile production sources against fixture mirrors, not
KSA.dll, so they cannot detect KSA signature drift. No native KSA run was possible. Evidence:
[KSA_5482_UPGRADE](../plans/KSA_5482_UPGRADE.md).

- 🔴 **`Part.Tree` nullable (fixed; 3× CS8602).** `Part.Tree` is `PartTree?` (`KSA/Part.cs:662`) and
  the `Part` ctor (`:1490`) no longer creates a tree. Trees now come from `CreateOwnTree()` (`:1456`),
  `PartTree.CreateFromNewPartTree` (`PartTree.cs:291`, used by `Deserialize`) or `Merge`.
  `IronManConnectorUnload.cs:51,65` filter the roots' and restored links' trees with
  `.OfType<PartTree>()`: owned roots are registered by the ctor postfix before `PartTree.Deserialize`
  builds the tree, so an aborted deserialization can leave a treeless root, which has no links, and
  skipping it preserves behaviour. `IronManRcsOrientationPatches.cs:53` uses
  `root.Tree?.OwningVehicle` on the worker (null → native authored map).
- ✅ **Editor avatar hook compatible.** `SuperMeshRenderSystem.ClearBuckets()` became
  `ClearBuckets(IViewport viewport)` (`SuperMeshRenderSystem.cs:517`; clears only that view's four
  colour passes, rev 5474). It is still one overload; `IronManEditorPatches.cs:22-23` resolves it by
  name and the postfix binds only `__instance`. `RenderEditor` sets `_renderedViewport = MainViewport`,
  calls `ClearBuckets(RenderedViewport)` (`Program.cs:4901`) and then the prepass, and
  `KittenRenderable.UpdateRenderData` draws into `ViewForViewport(viewport)` (`KittenRenderable.cs:356`),
  the same view that was just cleared. The postfix now also fires per viewport from `RenderViewport`
  (`:4419`) and `RenderGame` (`:4613`); `RenderCharacter` (`IronManEditorPatches.cs:133-142`) still
  returns unless an editor kitten exists and `RenderedViewport` is the main viewport.
- ✅ **Early equipment submission is now strictly required.** `Vehicle.UpdateRenderData(IViewport,int)`
  (`Vehicle.cs:3713`; pixel cull moved into `IsLargeEnoughToRender`, `:3707`) and
  `PartModelRenderer.UpdateRenderData(IViewport,int)` (`PartModelRenderer.cs:815`) keep their
  signatures. `PartTree.UpdateRenderData` now goes through the cached `PartTreeRenderData`
  (`EnsureBuilt` + `Compose*`). Frame order: `ClearFrameData` (`Program.cs:2334` →
  `PartModelRenderer.ClearFrameData`, `PartModelRenderer.cs:844`) → early loop skipping `KittenEva`
  (`Program.cs:4292-4299`) → upload (`:4317`) → late `KittenEva.UpdateRenderData` (`:4428`
  `RenderViewport`, `:4622` `RenderGame`). Instance lists are cleared at frame start rather than by
  `WriteInstancesToGpu`, and shadow culling re-reads them after upload. A late submission is therefore
  discarded rather than uploaded a frame late, so Iron Man's pre-upload submission is the only path by
  which equipment renders.
- ✅ **Lazy derived data (rev 5464).** `PartTree.RecomputeAllDerivedData` (`PartTree.cs:475-478`) now
  only marks derived data dirty. It is rebuilt on first read (`EnsureDerived`, `:521`) or by
  `PartTree.FlushDirtyDerived()` / `FlushDirtyResourceManagers()` in `PrepareFrame`
  (`Program.cs:2209-2210`). Those run after the `GetJobSimStep` handoff, where Iron Man's queued
  enable/disable/arm/connector edits execute, and before `ExecuteNextVehicleSolvers`, so worker
  snapshots still see recomputed data in the same frame; `UpdateVehicleConfiguration` reads mass
  through the lazy getters. Unload's recompute can no longer fail inside its transactional try/catch:
  a resource-graph rebuild error would surface later, at the flush. Optional hardening: call
  `EnsureDerived(DerivedData.All)` during unload for synchronous failure detection.
- ✅ **Input (rev 5449).** `Vehicle.OnKey(GlfwKeyEvent)` (`Vehicle.cs:3274`), `KittenEva.OnKey`
  (`KittenEva.cs:110`) and both `ProcessInput` methods keep their signatures; the bodies now match via
  `Input.Contains/Matches(in keyEvent, …)`. The reverse patch snapshots the new body, so enabled kittens
  inherit the new binding semantics. `GlfwKeyEvent` gained `Button`/`IsMouse`; user-bound mouse buttons
  now arrive as synthetic key events, which the prefixes forward unchanged.
- ⚠ **`Vehicle.Dispose(bool)` wake gating** (`Vehicle.cs:3775-3790`, revs 5452/5476) wakes on-rails
  vessels resting on the disposed vehicle only when it is not a `KittenEva` (a type test, not
  `IsKitten`). Disposing an enabled Iron Man kitten will not wake vessels stacked on it. Edge case;
  live check only.
- ✅ **Checked unchanged:** `TeleportToLocation` (`Vehicle.cs:4214`; one
  `GetInitialKinematicStateForLocation` call, `:4130`); `Ctrl2Body` (`:585`); the
  `OrbitController.GetFrame2Ecl` / `EditorOnScroll` bodies; `ThrusterController.cs`,
  `ThrusterControllerGlobalState.cs`, `Rocket.cs`; `GaugeCanvas.cs`, `GaugeButtonFlightComputer.cs`,
  `FlightComputer.cs`, `Gauges.xml`; `VehicleUpdateState.PrepareFromVehicle` and every worker
  `IsKitten` branch; the patched `VehicleEditor` bodies; `VehicleSaveData.Create`;
  `Part.GetReferenceWithChildren`. `PartTree.Deserialize` still runs the ctor per node, then
  `RegenerateConnectionsFromPartInstance`, then builds the tree, so the constructor-before-index
  invariant holds. `KittenBackPackPart` assets are unchanged.
- ℹ️ Test fidelity (not updated this pass): `iron-man-flight.tests/FlightFixture.cs` still models
  5438's "upload clears pending" in `PartModelRenderer`, and `iron-man.tests/Fixture.cs:63` keeps
  `Part.Tree` non-nullable, so the treeless-root path is not exercised.
- **Native acceptance pending** (in addition to the checklist below): equipment drawn exactly once per
  view in main and secondary viewports, with shadows; editor avatar aligned in prepass and main pass;
  same-frame mass/thrust/propellant after enable/arm/connector edits; mouse-bound vehicle actions on an
  enabled kitten; loading a save with marked anchors.

## Verification — 5402 → 5438 (historical)

No migration required in the opt-in flight/editor hooks. KittenEva, GaugeCanvas, GaugeButtonFlightComputer, VehicleSaveData, KittenRenderable and VehicleEditingSpace are byte-identical. Constructor/serializer, two EVA gauge checks, one generic gauge-policy call, one teleport-helper call, camera pan and single ManualControlMap read invariants remain. PartModelRenderer retains UpdateRenderData and supplies its new deformation descriptors internally. Native connector recomputation now also invalidates FlowTopology. Rocket/EVA mode, crashes, RCS/gauges and save reconstruction require native acceptance.

Verified against `2026.9.10.5438` using both supplied source/Content trees.
See [upgrade evidence and acceptance](../plans/KSA_5438_UPGRADE.md).
Older catalog tables below retain their explicitly cited build/line numbers; this section records the current delta.

Original baseline: **KSA 2026.9.7.5402**, `ksa-game-assemblies/current/decomp`. Added 2026-09-07.
Current verification: **2026.9.22.5482** (section above); watchlist rows marked `@5482` carry 5482 lines.
[Research evidence](../plans/iron-man/RESEARCH.md) explains the design;
[player instructions](../iron-man/README.md) describe activation and save dependencies.
The [flight-computer follow-up](../plans/iron-man/FLIGHT_COMPUTER.md) records the native HUD fix.

## Scope and activation

`iron-man.lib/IronManSubmod` is bundled through Unscience's `ISubmod` list, project reference and
`IronManPatches.Apply/Remove` on its shared Harmony instance. `iron-man/Mod.cs` is a compile-checked
StarMap development host with ImmediateLoad/AllModsLoaded/BeforeGui/AfterGui/Unload hooks and F11.
Its Patcher installs mandatory HotkeyGuard and PhysicsFrameHook; the bundled implementation uses
the existing shared physics handoff installed by Garry's Torch. No abstraction implementation changes.

Every kitten starts in EVA mode. Full-width `eva mode` / `iron man` buttons queue mode changes.
`IsConfigured` authorizes editor support after an explicit Edit or Iron Man action, including in
EVA mode; `IsEnabled` gates rocket worker/input/HUD/frame/RCS behavior and surface-teleport correction.
Authored-node restoration, data integrity and equipment rendering remain passive in EVA mode. No stock asset/template is mutated.
See [mode transitions](../plans/iron-man/FLIGHT_MODES.md).

## Harmony and reflection watchlist

Decomp paths below are under `KSA/`. By-name resolution (even with `nameof`) must be checked for
signature/overload changes; private string names must be re-grepped on every game update.

| Target | Patch / owner | Required behavior |
|---|---|---|
| `Vehicle.TeleportToLocation(Celestial,double,double)` | surface-teleport transpiler | Exactly one static `GetInitialKinematicStateForLocation(Celestial,UniverseTime,double,double,double3,double3,double3,byte4)` call receives the vehicle via an adapter. Only live active Iron Man kittens: permute full bounds/center to rocket axes, reuse native placement, convert returned orientation/body rates; preserve orbit and native event queue. |
| `Vehicle.get_Ctrl2Body()` | control-frame postfix | Enabled kitten without explicit control part/connector: control X→body -Z, Y→Y, Z→X. Shared by navball, rates, worker navigation and modules. |
| private `OrbitController.GetFrame2Ecl(IFollowable,CameraReferenceFrame)` | editor-orientation postfix | Own configured EditingSpace + Editor frame only, in either flight mode: camera +Z maps to body -Z; geometry unchanged. |
| private `OrbitController.EditorOnScroll(GlfwWindow,double2)` | editor-orientation transpiler | Exactly two UnitX getters and two CameraOffset.X field reads become scoped headward pan and projected bounds. |
| `ThrusterController.RecomputeDynamicData` | RCS orientation transpiler | Exactly one ManualControlMap field read; enabled actual root-backpack uses native geometric mapping, authored field untouched. |
| `GaugeCanvas.IsContextVisible()` | flight-computer transpiler | Exactly two `isinst KittenEva` sites become a session-gated EVA classifier. Preserve all other context predicates, AND behavior, empty/null cases and saved canvas settings. |
| `GaugeButtonFlightComputer.IsDisabled()` / `PackData()` | flight-computer transpilers | Exactly one `Vehicle.IsFlightComputerDisabled<Enum>` call per method routes through the same adapter: enabled kitten uses nonvirtual base policy, all others keep virtual dispatch. Packed disabled/selected state and click eligibility stay consistent. |
| `VehicleUpdateState.PrepareFromVehicle(bool,ManualControlInputs)` | flight postfix | Stock assigns `IsKitten` from `ReadOnlyVehicle` on main thread before workers; opted-in snapshots become ordinary vessels. |
| `KittenEva.OnKey(GlfwKeyEvent)` / `ProcessInput(InputAction,GlfwKeyAction,GlfwModifier)` | flight prefixes | Enabled kittens use original base Vehicle input; reverse patches on `Vehicle.OnKey` / `ProcessInput` must retain nonvirtual dispatch. |
| `Vehicle.UpdateRenderData(IViewport,int)` | render prefix; nonvirtual early base dispatch | Suppress late equipment submission without suppressing KittenEva's character draw. Early dispatch must retain other mods' base-render patches. |
| `PartModelRenderer.UpdateRenderData(IViewport,int)` | render prefix | Submit authored EVA equipment before upload. @5482 `Program` skips EVA in the early loop (`Program.cs:4292-4299`), uploads at 4317, then draws the avatar at 4428/4622. Instance lists are cleared at frame start (`PartModelRenderer.ClearFrameData`), so equipment submitted after the upload is discarded, not drawn a frame late. |
| `SuperMeshRenderSystem.ClearBuckets(IViewport)` | editor postfix | **Signature @5482** (was parameterless; per-view buckets, rev 5474); still one overload, postfix binds only `__instance`. Only matching Program renderer/main viewport/editor: submit avatar after clearing and before prepass (`Program.cs:4901` @5482). Also invoked per viewport from `RenderViewport` (`:4419`) and `RenderGame` (`:4613`), where the editor-kitten gate returns early. |
| `VehicleEditor.OnFrame`, `OnMouseButton`, `OnKey`, `UpdateSelected` | editor root-selection guards | Clear body selection/grab without interfering with accessory parts. |
| `VehicleEditor.DeletePart(Part)`, `SetFocusedTree(PartTree)` | editor prefixes | Preserve existing body root and focused tree. |
| private `VehicleEditor.DuplicateHighlightedPart`, `RequestNewVehicle`, `FinalizeNewVehicle` | editor prefixes | Block body duplication/replacement/new-vessel actions during a configured kitten edit in either flight mode. |
| `VehicleSaveData.Create(string,PartTree)` | metadata postfix | Preserve Character for a live KittenEva with this exact authored root, including after disabling; no character guessed for unrelated trees. |
| internal `Part.GetReferenceWithChildren(ref uint,PartInstance,bool)` | connector serialization postfix | Set marked instance Id only for owned nodes, preserving original Id, stock prefix count and ordered owned suffix. |
| `Part(string,PartTemplate,PartInstance,Part)` | constructor prefix/postfix | Decode valid marked data, restore runtime Id, append nodes BEFORE `RegenerateConnectionsFromPartInstance` indexes them. @5482 the ctor (`Part.cs:1490`) no longer creates a tree; the postfix touches only template/sub-part/scale/connectors, so a root is registered while its `Tree` is still null (unload filters those out). |

No private field reflection, shader strings, GPU byte offsets or game DLL modifications.
Reverse-patch behavior and emitted nonvirtual dispatch are also covered by managed Harmony checks.
The new HUD adapters do **not** patch generic JIT methods: they resolve and emit a call to the closed
`Vehicle.IsFlightComputerDisabled<Enum>(Enum)` only. Private `_enumValue` reflection is unnecessary.
Other mods' Harmony patches on that closed generic base method are not guaranteed to compose;
the managed runtime experiment bypassed a postfix. No current repository mod patches that method.
Unexpected IL match counts fail installation with rollback; removed hooks restore the original UI.

## Direct APIs and behavioral dependencies

- `Vehicle.InitialKinematicState` mutable `Orbit/Body2Cce/BodyRates`; native surface helper assumes
  +X-up and samples minimum-X footprint. `TeleportToLocation` passes mass-centered bounds from
  `MassToGeometryAsmb` and `BoundingBoxAsmb`, with zero center. Preserve native terrain/launchpad
  placement and surface velocity. Eligibility is checked on the receiver at request time;
  `InputEvents.TeleportInputData.Apply` runs before the mod mode-change handoff in `Program.PrepareFrame`.
  No shared helper or general `Vehicle.Teleport` patch. See [surface teleport](../plans/iron-man/SURFACE_TELEPORT.md).
- `IronManEvaSettings` additionally captures `Vehicle.ControlPart/ControlConnector` and
  `KittenEva.ControlMode`; restores through `Vehicle.SetControlPart` (same-tree guard plus native
  validation) and `KittenEva.SetControlMode` (native CCF restrictions). Both transition directions
  clear held input, MainShutdown and disarm; rocket entry selects manual attitude/burn/direct thrust.
  Preserve worker MMU bookkeeping with its matching saved EVA FC state; no locomotion reset/teleport.
- `Vehicle.ControlPart/ControlConnector/Ctrl2Body`; `VehicleEditingSpace.Asmb2Ecl`,
  `VehicleEditor.CameraOffset`; `ThrusterController.Parent.FullPart/ManualControlMap`,
  `Part.Tree?.OwningVehicle` (nullable since 5482) / `Template.Id`; root `KittenBackPackPart` authored RCS mappings.
  `ModuleStateful<ThrusterController,ThrusterControllerState,ThrusterControllerGlobalState,EmptyStruct>`
  `InitializeHotPathList(Parts.States).GetMutableGlobalStateForInitialization()` resets authority to
  `ThrusterControllerGlobalState.Zero` at joined enable/disable/disposal. Native cache checks Ctrl2Body;
  membership reads use published arrays on workers. See [orientation evidence](../plans/iron-man/ORIENTATION.md).
- `Program.EditorFlag`, `IsEditorOpen`, `ControlledVehicle`, `Editor`, `MainViewport`,
  `RenderedViewport`, `Instance.ResourceFrameIndex`, `Instance.SuperMeshRenderSystem`, `VehiclesInFrame`.
- `VehicleEditor.ExistingVehicle`, `EditingSpace.Parts/GetMatrixAsmb2Ego`, `Highlighted`, `Selected`,
  `HighlightConnector`, `RequestExit`, `Dispose`, `IsChangeStartOrEnd`; stock existing-editor exit
  preserves the vehicle object. Empty-editor launch creates plain Vehicle and remains unsupported.
- `KittenEva.Renderable`, `Character.Id`, `LocomotionState.Mode`; `KittenRenderable.HideHead` and
  `UpdateRenderData(IViewport,int,double,float4x4,double3,double3,in LocomotionState,in KittenAnimInputs)`.
  Assembly-to-camera transform must not subtract flight CoM again. Avatar is not part-tree geometry.
- `Vehicle.Parts`, `IsDisposed`, `ClearHeldPlayerInput`, `SetEnum`, `UpdateVehicleConfiguration`;
  `VehicleProvider.GetAllVehicles(true)` finds live objects. `FlightComputer.AttitudeMode`, `BurnMode`,
  `RCSMode`, `SetManualThrustMode`, `RateHold`; `EngineController.SetIsActive` and module enumeration.
  `IronManFlightSettings` additionally snapshots/restores `AttitudeFrame`, `AttitudeTrackTarget`,
  `CustomAttitudeTarget:double3`, `RollMode`, `AngleDeadband`, `RateLimit`. No live burn/telemetry rollback.
  UI reads `FlightComputer.ActiveControlSystem.{X,Y,Z}` (`AttitudeControlSystem.None/Rcs/Tvc`).
- `GaugeCanvas.VisibleInContext` / `GaugeVisibilityFlag` AND semantics; separate `_enabled` and
  HUD menu settings remain untouched. `Content/Core/Gauges.xml`: `AutopilotSettings` requires Vehicle,
  `KittenFlightControl` requires EVA. Neither asset is edited. `GaugeButtonFlightComputer.OnReleased`
  calls the patched `IsDisabled` before queuing native `InputEvents.FlightComputerInputData`; unchanged
  `Vehicle.SetEnum/ToggleEnum` applies action + navball frame and native input reset. `IsSet<Enum>`
  already delegates to base for ordinary actions. Missing target/burn/engine restrictions stay active.
- `Part.Connectors`, `Template.Id/Connectors`, `FullPart`, `Tree`, `Scale`, `PositionParentAsmb`,
  `Asmb2ParentAsmb`; `Part.Connector.TemplateBase`, mutable `TransformReference`, capabilities,
  `Connection`, owner, scale and transforms. `ScaleFactors` reduces scale to a single scalar.
- `Part.Connection.Connect/Disconnect`, `Connection.Connectors[0/1]`, `IConnector.ConnectionPart`;
  preserve resource capabilities on unload. Stock wildcard Part-to-Part links do not carry bulk fuel.
- `PartTree.RecomputeAllDerivedData` (marks derived data dirty since 5482; rebuilt on first read or at
  the `PrepareFrame` flush before the vehicle solvers), `HasUnsavedChanges`, `PerformanceSequences.SetDirty`;
  serialized `PartInstance.Id` and connection-index regeneration. No undo/redo API in 5402.
- `PhysicsFrameHook.Enqueue`: handoff after worker result application, before all next snapshots.
  Teardown waits `JobSystems.VehicleSolver` and `ClothSolvers`; closes owned editor before unpatching.
- Worker `IsKitten` branches in `PhysicsBubble.FlightComputerInputsFor/AdvanceKittenLocomotion`,
  `PoseIntegratorCallbacks`, `NarrowPhaseCallbacks`, `PartFailure`, and G-load detection are
  semantically load-bearing even without symbol changes. Normal vessel behavior replaces EVA servos,
  ground movement, swim/ladder behavior and special failure allowances while activated.

**Asset:** `KittenBackPackPart` template identity. Feet origin and `-Z` up convention in
`Content/Core/PartGameData.xml:24`; connector outward normal is local `+X`. No added asset files.

## Persistence and unload

Marker `iron-man:v1:` in serialized instance Id stores original Id, stock connector count and up to
16 nodes with name, position, direction and radius. Decoder enforces version, payload bounds,
finite vectors/radius, supported template and matching stock node count; invalid marked saves fail
before stock index resolution. Native-only saves do not preserve flight activation; Unscience scene
sidecars restore the chosen mode disarmed. Saved files require the mod while
connected runtime indices exist; removing a mod cannot retroactively make these files stock-safe.

Unload tracks weak roots, converts owned endpoints to stock surface links transactionally and
marks derived data for recomputation before removing nodes (since 5482 the resource graphs are rebuilt
lazily, on first read or at the next `PrepareFrame` flush, on the final stock-link topology and
outside the transaction). It preserves the other endpoint; if bulk flow
would be lost or a reconnect fails, it retains original links and passive hooks, logging the reason.
Existing equipment is never silently deleted. Native behavior after completely removing rendering
support follows the game's original EVA limitations.

## Validation and open native acceptance

Full solution compilation and managed tests check typed API compatibility, math/metadata bounds,
constructor-before-index restoration, connector isolation and connection/unload semantics, and
Harmony default-off/per-instance routing, base dispatch, render ordering and restoration.
Native-HUD checks add context combinations, default-off/per-instance/control switching,
both button call sites and native restrictions, deferred clicks, GPU bits, changed-IL
rejection, unpatch/reapply and control-settings restoration without telemetry rollback. Fixture
checks do not prove native rendering or physics. Surface-teleport checks exercise all corners of
asymmetric bounds, center/clearance, nonidentity orientation and world angular rates, native
argument/orbit identity, queued request timing, active-only isolation, other paths and IL boundaries/count guards.

The user reports that the initial mod works apart from the unavailable flight computer. The new
HUD correction addresses that reported gap; live autopilot response remains to be verified.

- [ ] Disabled startup: ordinary EVA walking, ladders, RCS, menu and save behavior unchanged.
- [ ] Flight-mode buttons fill one row with current selection highlighted. Edit in EVA without
      entering rocket mode; return preserves mode. Switch repeatedly through native walking/MMU and
      rocket controls; engines stay disarmed on every switch, EVA preferences restore, other kittens
      stay unchanged. Unload while editing a configured EVA closes the protected editor correctly.
- [ ] Enable a kitten: native Autopilot Settings replaces EVA-only controls, honoring HUD visibility.
      Select valid frame/attitude/roll/profile/RCS actions; selected and disabled appearance matches
      actual clicks. Missing target/burn/engine still disables the corresponding controls. Switch to
      another inactive kitten and back; disable restores EVA UI and original control settings.
- [ ] Verify actual attitude response with fueled RCS/gimbals and valid burn execution; confirm the
      native X/Y/Z actuator readout. Default Up points the kitten's head away from the surface;
      explicit control parts/ports retain their selected axes. Disable restores EVA controls.
- [ ] Editor opens upright with equipment/nodes/picking aligned; scroll moves vertically and bounds
      work for a rotated EditingSpace. Exit/re-enter leaves root/attachment geometry unchanged.
- [ ] In active Iron Man mode, map Apply and named Teleport To destinations place the head upward
      with boots/equipment clear of terrain and launchpads, including scaled/asymmetric builds.
      EVA-mode/configured kittens, other inactive kittens, ordinary vessels and other teleport paths
      retain native placement. Wait for queued mode selection before teleport; an existing FC
      attitude target may turn the kitten afterward.
- [ ] Backpack RCS follows rocket axes, including engines off; disable restores authored EVA maps.
- [ ] Enable one of two kittens; only that kitten gets vessel controls/physics. Reject ladder enable.
- [ ] Editor avatar aligns with body nodes, including a scaled kitten. Body/root actions are blocked;
      accessory move/rotate/scale/copy/delete and blueprint accessory loading still work.
- [ ] Up/down/custom nodes snap, fuel/electric graphs connect, occupied nodes remain locked;
      round-trip connected part trees and reload a world save with nodes in correct index order.
- [ ] Balanced tanks/engines consume correct propellant, produce thrust/torque and obey throttle;
      attached RCS responds to vessel inputs. Main and secondary views show hardware once per frame.
- [ ] Disable restores EVA movement and stops/disarms engines; equipment stays visible. Re-enable
      and return through editor exit. Native-only save/load starts flight mode off; scene sidecars restore the saved mode disarmed.
- [ ] Coexistence: I Feel Seen distance override, Humble Arteest paint, Godzilla scaling and Kitten
      Animations. Observe rigid equipment vs animated body; no foot-bone attachment is provided.
- [ ] Unload while editing/in flight with attached/disabled/loaded nodes; inspect preserved resource
      links and diagnostics. Previously saved marked files still require the mod.


## Scene save adapter

`IronManSubmod.Saves` records configured kitten IDs, saved flight mode, current flight preferences
and original EVA preferences. `IronManEvaSettings.Saves` binds the original `ControlPart` through
`SavedPartReference` and `Part.Connectors` index, validates ownership against the loaded kitten's
`Parts`, and restores `KittenControlMode`. Missing control references warn and retain the original
record instead of silently replacing its baseline. Existing constructor metadata still restores
custom connectors before native linking; late replay never authors replacement nodes. Engines
remain disarmed. `IronManFlightSettings.Saves` validates enum values and finite custom targets,
deadband/rate limits. No new Harmony/reflection seam. See [saves](saves.md).
