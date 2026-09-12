# Iron Man — EVA editor, connectors and vessel physics

Baseline: **KSA 2026.9.7.5402**, `ksa-game-assemblies/current/decomp`. Added 2026-09-07.
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
| `PartModelRenderer.UpdateRenderData(IViewport,int)` | render prefix | Submit authored EVA equipment before upload. `Program` skips EVA in early loop (`Program.cs:4210`), uploads at 4227, then draws avatar at 4327/4522. |
| `SuperMeshRenderSystem.ClearBuckets()` | editor postfix | Only matching Program renderer/main viewport/editor: submit avatar after clearing and before prepass (`Program.cs:4793`). |
| `VehicleEditor.OnFrame`, `OnMouseButton`, `OnKey`, `UpdateSelected` | editor root-selection guards | Clear body selection/grab without interfering with accessory parts. |
| `VehicleEditor.DeletePart(Part)`, `SetFocusedTree(PartTree)` | editor prefixes | Preserve existing body root and focused tree. |
| private `VehicleEditor.DuplicateHighlightedPart`, `RequestNewVehicle`, `FinalizeNewVehicle` | editor prefixes | Block body duplication/replacement/new-vessel actions during a configured kitten edit in either flight mode. |
| `VehicleSaveData.Create(string,PartTree)` | metadata postfix | Preserve Character for a live KittenEva with this exact authored root, including after disabling; no character guessed for unrelated trees. |
| internal `Part.GetReferenceWithChildren(ref uint,PartInstance,bool)` | connector serialization postfix | Set marked instance Id only for owned nodes, preserving original Id, stock prefix count and ordered owned suffix. |
| `Part(string,PartTemplate,PartInstance,Part)` | constructor prefix/postfix | Decode valid marked data, restore runtime Id, append nodes BEFORE `RegenerateConnectionsFromPartInstance` indexes them. |

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
  `Part.Tree.OwningVehicle/Template.Id`; root `KittenBackPackPart` authored RCS mappings.
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
- `PartTree.RecomputeAllDerivedData`, `HasUnsavedChanges`, `PerformanceSequences.SetDirty`;
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
recomputes resource graphs before removing nodes. It preserves the other endpoint; if bulk flow
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
