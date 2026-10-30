# Iron Man — EVA editor, connectors and vessel physics

## KSA 5707 verification (5638 → 5707, 2026-10-29)

Verified against `2026.10.25.5707` (revs 5639–5703), diffed from `2026.10.19.5638`. Static/managed
only: the solution builds, the `iron-man.tests` suite passes, and `ksa-upgrade.tests/Ksa5707Checks.cs`
confirms on the real KSA.dll that every `Part` constructor reaches the private seam. No native run.
Paths are under `KSA/` in the new decomp tree.

- 🔴 **Saved anchors never restored on load (fixed; new in 5707).** Rev 5643 routes
  `Part(PartInstance, Part)` (`Part.cs:1532`, save load through `RegenerateFromPartInstance` `:2618`),
  the public 4-arg constructor (`:1551`), template sub-parts (`CreateTemplateSubPart` `:1536`) and the new
  copy constructor (`:1556`) through a private 10-arg constructor (`:1566`). Every remaining 4-arg caller
  passes no instance, so the old 4-arg patch never saw a marked save: the marker became `Part.Id`, no
  anchors were added, and `RegenerateConnectionsFromPartInstance` (`:2634-2648`) indexed past the
  backpack's empty connector array (IndexOutOfRange, so the save failed to load). Unconnected anchors were
  silently lost. `IronManConnectorPatches.cs:14-30,73-80` now patches the private constructor
  `(string?, PartTemplate, PartInstance?, Part?, TransformReference?, int, int, int, Part? boundsSource,
  bool isTemplateSubPart)` with the same `ref string? __0`, `PartTemplate __1`, `PartInstance? __2` prefix
  and `Restore` postfix. The 4-arg patch is **removed**: keeping both would decode the still-marked
  `__2.Id` twice and add the anchors twice.
- 🔴 **Copies dropped anchors and could crash (fixed; new in 5707).** New `PartCloner` (editor
  `DuplicateHighlightedPart` `VehicleEditor.cs:6086-6088`, part-menu Copy Tree `:6500-6502`, symmetry and
  radial clones `:4607,4770,4811,4873,4935`, engine clusters `:5329`) uses `Part(Part source, Part? parent)`.
  That constructor passes no name or instance and rebuilds `Connectors` from the template only
  (`Part.cs:1609-1616`). `RecordConnections` then records source connector indices, including owned anchors
  (`PartCloner.cs:162-185`), and `Connect` indexes the clone by them (`:258-280`). Cloning a kitten root
  with connected anchors therefore threw IndexOutOfRange. Before 5707, copies serialized and deserialized,
  so the marker restored their anchors. A new postfix on the public copy constructor
  (`IronManConnectorPatches.cs:34,42,82`) calls `IronManConnectors.CopyOwned` (`IronManConnectors.cs:58`),
  which re-adds the source's anchors in order (stock prefix, owned suffix), once per copy. Reachability is
  low: a configured body cannot be duplicated (`AllowDuplicate`), and its part menu (right-click on
  `Highlighted`, `VehicleEditor.cs:3746-3770`) is blocked by the root-selection guards. Equipment clones
  never involve anchors, and symmetry ghost previews are now transform-only (`:4318-4345`).
- 🔴 **`Part.Connectors` is `Connector[]` (fixed; compile break, rev 5643).** A part without template
  connectors, which includes `KittenBackPackPart`, starts on the shared `Array.Empty` (`Part.cs:652`).
  Anchors grow and shrink the array by replacement and never mutate it in place
  (`IronManConnectors.cs:49,78`; also `IronManConnectorData.cs:31`, `IronManEvaSettings.Saves.cs:24,37,39`).
  No game code keeps the array reference: `GetConnectorInstance` scans it (`Part.cs:3665`), and the editor's
  snap target stores connector indices that it re-resolves every frame.
- ℹ️ **`Part.Connections` returns a shared empty list** (`Part.cs:1183`) until `EnsureConnections`
  (`:1494`) allocates one. Iron Man only reads it after `Connect` (`IronManConnectorUnload.cs:46`).
  `Part.Connection` is no longer `IDisposable`; Iron Man never disposed connections.
- ℹ️ **Kitten radiation death (rev 5669) applies to enabled kittens.** `Vehicle.KillLethallyDosedCrew`
  (`Vehicle.cs:3041-3080`) tests `this is KittenEva`, not `IsKitten`, and calls
  `Universe.DestroyVehicle(Kill)`. Iron Man already skips disposed kittens. EVA dose is unshielded in
  either mode. MMU leg animation sign flip (rev 5671, `KittenRenderable.cs:721`) is cosmetic; the
  editor avatar uses zero acceleration.
- ✅ **Checked unchanged:**
  - Files byte-identical or differing only in log lines: `TeleportToLocation` (one placement call,
    `Vehicle.cs:4322`); `get_Helm2Body` (`:621`); `UpdateNavBallMarkers` (`:2833`) and
    `_navBallMarkerData`; `OrbitController` (`GetFrame2Ecl`, `EditorOnScroll`: two UnitX, two
    `CameraOffset.X`); `ThrusterController`/`ManualControlMap`; `Rocket`; `FlightComputer`;
    `GaugeCanvas` (two `isinst KittenEva`); `GaugeButtonFlightComputer`; `KittenEva`;
    `VehicleUpdateState`; `VehicleEditingSpace`; `VehicleSaveData`; `PartModelRenderer`; `PhysicsBubble`.
  - Same bodies: Vehicle `OnKey`, `ProcessInput`, `UpdateRenderData`, `Dispose(bool)`,
    `IsFlightComputerDisabled<T>`, `SetControlPart`.
  - Editor hooks: same single-overload signatures and parameter names (`part`, `newFocus`);
    `RequestNewVehicle`/`FinalizeNewVehicle` bodies unchanged; `DeletePart` only adds grab-preview cleanup.
  - Render: `ClearBuckets(IViewport)` is still one overload; `RenderEditor` still clears buckets before
    the prepass (`Program.cs:5296-5303`); the early, upload and late EVA render order is unchanged.
  - Serialization: the 3-arg serializer is unchanged; `PartTree.Deserialize` still constructs every
    part before indexing connections.
  - Assets: `KittenBackPackPart` XML and the `AutopilotSettings`/`KittenFlightControl` canvases.
- ℹ️ Test fidelity: `iron-man.tests/Fixture.cs` now models 5707 chaining (load, 4-arg and copy into the
  private constructor). `Program.cs` adds load-through-`Part(PartInstance, Part)` and copy checks
  (order, poses, radii, independent unconnected anchors, index reconnection, no anchors on plain copies).
- **Native acceptance pending:**
  - Load a world save and a blueprint with connected anchors; check node order, fuel flow and Character.
  - Copy Tree or duplicate an unconfigured kitten that has anchors.
  - Add or remove a node while a part is grabbed.
  - Let an enabled kitten die of radiation.

## KSA 5638 (5482 → 5638) verification

Verified 2026-10-22 against `2026.10.19.5638` (via 5554) from decomp and a real-IL dump of KSA.dll.
Static/managed only: the solution builds and all three Iron Man suites pass. KSA.dll is x64-only and
the arm64 build host has no Rosetta, so real-assembly reflection probes and native runs were impossible.

- 🔴 **`Vehicle.Ctrl2Body` → `Helm2Body` (fixed; compile break).** Same expression (`Vehicle.cs:613`), so
  the postfix retargets to `get_Helm2Body`. The helm frame now feeds `HelmRates` (`:661`),
  `GetHelm2Cce/Cci` (`:3180-3185`), navball `Navball2Helm`/`*DirHelm` markers (`:2837-2943`),
  OrbitController chase and worker `FlightComputerNavigation.Helm2Body` (`PhysicsBubble.cs:1372,1508,1888`).
  The worker FC then sets `Helm2Asmb = Ctrl2Asmb = Tnm2Asmb = nav.Helm2Body` (`FlightComputer.cs:505-507`),
  rotates `Tnm2Asmb` minimally so +X follows trimmed net gimbal thrust (`UpdateTnmFrame`, `:1494-1516`) and
  selects `Ctrl2Asmb = IsTnm ? Tnm2Asmb : Helm2Asmb` (`:530`, `GetDesiredControlFrame` `:1433-1452`) for
  attitude, TVC and RCS; module context `Ctrl2Body = FlightComputer.Ctrl2Asmb` (`VehicleUpdateState.cs:393`).
  Overriding only the helm is therefore sufficient: Tnm/Ctrl derive from it. Redesign landed in unlogged revs 5555–5602.
- 🔴 **Navball thrust marker 90° off (fixed; stock bug).** `UpdateNavBallMarkers` computes
  `ThrustDirHelm = UnitX·Tnm2Asmb·Helm2Asmb⁻¹` from the main-thread FC (`Vehicle.cs:2932-2943`), but
  `FlightComputer.CopyFrom` (`:265-317`, called `Vehicle.cs:2598`) copies `Tnm2Asmb` and never `Helm2Asmb`,
  which stays Identity. New `IronManNavballPatches` postfixes private `Vehicle.UpdateNavBallMarkers()` and,
  only for enabled kittens with a valid marker, recomputes it with the live `Helm2Body`. Ordinary vessels keep
  stock (their rotated control parts show the same stock offset). Missing method/field logs and skips.
- 🔴 **RCS authority dead after disable (fixed).** 5638 gates `UpdateRockets` on
  `RocketsNeedUpdate(..., ctrl2Body)` (`Rocket.cs:89,256-260`) using `IsCtrl2BodyCurrent`
  (`ThrusterControllerGlobalState.cs:49`, 0.0005 rad); `UpdateThrusterCache` is `IsCacheValid` + `ResetCache`
  (`Rocket.cs:195-213`). The old reset to `Zero` cached Identity, so disabling back to the Identity EVA frame (or
  toggling with an Identity explicit control part) skipped the recompute: zero authority, `ActiveControlSystem`
  None, no manual rotation. `InvalidateCache` now writes `default`, the all-zero state a fresh list starts with
  (`ModuleStateful.cs:264`); a zero quaternion is never current, forcing a recompute. `RecomputeDynamicData`
  now takes seven parameters (`ThrusterController.cs:84`); the transpiler still finds one map read (`:124`).
- ℹ️ **Kitten impact death is EVA-only.** New `PhysicsBubble.DetectKittenImpact` (`:1057-1078`, from
  `DetectStructuralFailure` `:1086`) kills a kitten that loses ≥20 m/s of downward speed on contact. It is
  gated on `IsKitten`, so enabled Iron Man kittens skip it and use ordinary vessel G-load limits (no 2.5× kitten
  allowance, `:1099`). Live check only.
- ⚠ **Optimized KSA.dll (rev 5630) inlining.** Small getters such as `get_Helm2Body` may be inlined by Tier-1 JIT.
  KSA.dll is IL-only (no ReadyToRun), Iron Man patches at AllModsLoaded before any flight caller runs, and
  Harmony 2.4.2/MonoMod disables inlining of a method when it is patched. Residual risk: callers already
  Tier-1-compiled with the getter inlined before the first patch keep stock behaviour; verify after a
  mid-session unload/reapply.
- ℹ️ Test fidelity: `iron-man-flight.tests` now mirrors the 5638 helm/Tnm/Ctrl chain, `CopyFrom` gap, navball
  thrust tail, global-state cache/gate and the seven-argument `RecomputeDynamicData` (`HelmFixture.cs`,
  `ThrusterCacheFixture.cs`). Reverting either fix fails the managed checks.
- **Native acceptance pending:** navball thrust marker on the rocket nose with and without gimbal offset;
  manual RCS rotation immediately after Iron Man → EVA and with an explicit control part; landing impacts.

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
Current verification: **2026.10.25.5707** (top section); watchlist rows marked `@5707` / `@5638` / `@5482` carry those builds' lines.
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

## Purr2D2 input and worker ownership (2026-10-25)

Both mods can prefix `KittenEva.OnKey` with an effectful bool prefix. Default ordering allowed a
Space somersault to consume input before active Iron Man's base-vehicle handler. Iron Man now
installs that key prefix at `Priority.First`: enabled kittens run the base vehicle handler once
and consume the event; inactive kittens continue through the other EVA prefixes. Production
Harmony checks cover both installation orders, enable/disable, exact other-kitten isolation and
unload (**14 checks** in `iron-man-flight.tests/InputCompatibilityChecks.cs`).

The matching Purr2D2 change checks the final prepared worker's `IsKitten` after native preparation
and Iron Man's `PrepareFromVehicle` postfix. If it is false, Purr2D2 releases queued/active
somersault ownership before changing velocity, locomotion, movement/grab inputs or pending failure
events. This catches a mode switch after a button/keyboard request without a direct assembly
dependency. Character workers keep the normal one-shot launch behavior. Co-loaded native input,
rocket controls and transitions during a jump remain in-game acceptance work.

Save review: the existing `iron-man` record still restores configured/active mode and flight
preferences with engines disarmed. Prefix ordering and prepared-worker ownership are transient
runtime state; no ID, version, payload, reset or replay behavior changes.

## Harmony and reflection watchlist

Decomp paths below are under `KSA/`. By-name resolution (even with `nameof`) must be checked for
signature/overload changes; private string names must be re-grepped on every game update.

| Target | Patch / owner | Required behavior |
|---|---|---|
| `Vehicle.TeleportToLocation(Celestial,double,double)` | surface-teleport transpiler | Exactly one static `GetInitialKinematicStateForLocation(Celestial,UniverseTime,double,double,double3,double3,double3,byte4)` call receives the vehicle via an adapter. Only live active Iron Man kittens: permute full bounds/center to rocket axes, reuse native placement, convert returned orientation/body rates; preserve orbit and native event queue. |
| `Vehicle.get_Helm2Body()` (was `get_Ctrl2Body`, renamed @5638) | control-frame postfix | Enabled kitten without explicit control part/connector: helm X→body -Z, Y→Y, Z→X. Shared by navball, rates, chase camera and worker navigation; the worker FC derives `Helm2Asmb`, thrust-aligned `Tnm2Asmb` and active `Ctrl2Asmb` (attitude/TVC/RCS module context) from it (`FlightComputer.cs:505-530` @5638). Small getter in an optimized assembly: see inlining note. |
| private `Vehicle.UpdateNavBallMarkers()`; private field `Vehicle._navBallMarkerData` (Harmony `ref` injection) | navball postfix | @5638 `Vehicle.cs:2846-2943`. Enabled kitten with `ThrustValid` only: `ThrustDirHelm = UnitX·FlightComputer.Tnm2Asmb·Helm2Body⁻¹`, replacing the stale never-copied main-thread `Helm2Asmb`. Missing method/field or patch failure logs and skips (stock marker), without disabling Iron Man. |
| private `OrbitController.GetFrame2Ecl(IFollowable,CameraReferenceFrame)` | editor-orientation postfix | Own configured EditingSpace + Editor frame only, in either flight mode: camera +Z maps to body -Z; geometry unchanged. |
| private `OrbitController.EditorOnScroll(GlfwWindow,double2)` | editor-orientation transpiler | Exactly two UnitX getters and two CameraOffset.X field reads become scoped headward pan and projected bounds. |
| `ThrusterController.RecomputeDynamicData` | RCS orientation transpiler | Exactly one ManualControlMap field read; enabled actual root-backpack uses native geometric mapping, authored field untouched. |
| `GaugeCanvas.IsContextVisible()` | flight-computer transpiler | Exactly two `isinst KittenEva` sites become a session-gated EVA classifier. Preserve all other context predicates, AND behavior, empty/null cases and saved canvas settings. |
| `GaugeButtonFlightComputer.IsDisabled()` / `PackData()` | flight-computer transpilers | Exactly one `Vehicle.IsFlightComputerDisabled<Enum>` call per method routes through the same adapter: enabled kitten uses nonvirtual base policy, all others keep virtual dispatch. Packed disabled/selected state and click eligibility stay consistent. |
| `VehicleUpdateState.PrepareFromVehicle(bool,ManualControlInputs)` | flight postfix | Stock assigns `IsKitten` from `ReadOnlyVehicle` on main thread before workers; opted-in snapshots become ordinary vessels. |
| `KittenEva.OnKey(GlfwKeyEvent)` / `ProcessInput(InputAction,GlfwKeyAction,GlfwModifier)` | flight prefixes | Enabled kittens use original base Vehicle input; `OnKey` runs at `Priority.First` before optional effectful EVA action prefixes, while inactive kittens pass through. Reverse patches on `Vehicle.OnKey` / `ProcessInput` must retain nonvirtual dispatch. |
| `Vehicle.UpdateRenderData(IViewport,int)` | render prefix; nonvirtual early base dispatch | Suppress late equipment submission without suppressing KittenEva's character draw. Early dispatch must retain other mods' base-render patches. |
| `PartModelRenderer.UpdateRenderData(IViewport,int)` | render prefix | Submit authored EVA equipment before upload. @5482 `Program` skips EVA in the early loop (`Program.cs:4292-4299`), uploads at 4317, then draws the avatar at 4428/4622. Instance lists are cleared at frame start (`PartModelRenderer.ClearFrameData`), so equipment submitted after the upload is discarded, not drawn a frame late. |
| `SuperMeshRenderSystem.ClearBuckets(IViewport)` | editor postfix | **Signature @5482** (was parameterless; per-view buckets, rev 5474); still one overload, postfix binds only `__instance`. Only matching Program renderer/main viewport/editor: submit avatar after clearing and before prepass (`Program.cs:4901` @5482). Also invoked per viewport from `RenderViewport` (`:4419`) and `RenderGame` (`:4613`), where the editor-kitten gate returns early. |
| `VehicleEditor.OnFrame`, `OnMouseButton`, `OnKey`, `UpdateSelected` | editor root-selection guards | Clear body selection/grab without interfering with accessory parts. |
| `VehicleEditor.DeletePart(Part)`, `SetFocusedTree(PartTree)` | editor prefixes | Preserve existing body root and focused tree. |
| private `VehicleEditor.DuplicateHighlightedPart`, `RequestNewVehicle`, `FinalizeNewVehicle` | editor prefixes | Block body duplication/replacement/new-vessel actions during a configured kitten edit in either flight mode. @5707 duplication copies through `PartCloner.CloneSubtreeAsTree` (`VehicleEditor.cs:6088`) instead of serialize/deserialize; the copy-constructor row covers anchors on any permitted copy. |
| `VehicleSaveData.Create(string,PartTree)` | metadata postfix | Preserve Character for a live KittenEva with this exact authored root, including after disabling; no character guessed for unrelated trees. |
| internal `Part.GetReferenceWithChildren(ref uint,PartInstance,bool)` | connector serialization postfix | Set marked instance Id only for owned nodes, preserving original Id, stock prefix count and ordered owned suffix. |
| private `Part(string?,PartTemplate,PartInstance?,Part?,TransformReference?,int,int,int,Part?,bool)` (**@5707**, `Part.cs:1566`; was the public 4-arg ctor) | constructor prefix/postfix | Every construction path reaches this one constructor: load `Part(PartInstance,Part)`, the 4-arg ctor, template sub-parts and copies. Patch it, and only it: `__0` inName (rewritten to the original Id), `__1` template, `__2` instance (decoded); `__8` boundsSource, `__9` isTemplateSubPart unused. Decode valid marked data, restore runtime Id, append nodes BEFORE `RegenerateConnectionsFromPartInstance` indexes them. Also patching the 4-arg ctor would add the anchors twice. A root is registered while its `Tree` is still null (since 5482; unload filters those out). |
| `Part(Part source, Part? parent)` (**new @5707**, `Part.cs:1556`) | copy-constructor postfix | Used by `PartCloner` (duplicate, Copy Tree, symmetry/radial, clusters). Connectors are rebuilt from the template, then reconnected by source index (`PartCloner.cs:162-185,258-280`), so `CopyOwned` must re-add the source's anchors in order before `Connect`. Unmodified kittens and stock parts gain nothing. |

One private field (`Vehicle._navBallMarkerData`, type-checked before patching) is reached through Harmony's
`____navBallMarkerData` injection. No shader strings, GPU byte offsets or game DLL modifications.
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
- `Vehicle.ControlPart/ControlConnector/Helm2Body/FlightComputer`; `FlightComputer.Tnm2Asmb` (copied by
  `CopyFrom`) and `Helm2Asmb` (not copied); `NavBallMarkerData.ThrustDirHelm/ThrustValid`; `VehicleEditingSpace.Asmb2Ecl`,
  `VehicleEditor.CameraOffset`; `ThrusterController.Parent.FullPart/ManualControlMap`,
  `Part.Tree?.OwningVehicle` (nullable since 5482) / `Template.Id`; root `KittenBackPackPart` authored RCS mappings.
  `ModuleStateful<ThrusterController,ThrusterControllerState,ThrusterControllerGlobalState,EmptyStruct>`
  `InitializeHotPathList(Parts.States).GetMutableGlobalStateForInitialization()` resets authority to
  `default(ThrusterControllerGlobalState)` (all-zero, as a fresh list; never `Zero`, whose Identity frame passes the
  5638 `IsCtrl2BodyCurrent` gate) at joined enable/disable/disposal. Native cache and gate check the module context Ctrl2Body;
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
- `Part.Connectors` (`Connector[]` since 5707; starts on the shared `Array.Empty` and is replaced, never
  mutated in place), `Part.Connections` (shared empty list until first connect; read-only here),
  `Template.Id/Connectors`, `FullPart`, `Tree`, `Scale`, `PositionParentAsmb`,
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
  `PoseIntegratorCallbacks`, `NarrowPhaseCallbacks`, `PartFailure`, G-load detection (2.5× kitten allowance) and,
  @5638, `DetectKittenImpact` (≥20 m/s downward-speed loss on contact, `PhysicsBubble.cs:1057-1086`) are
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
- [ ] Backpack RCS follows rocket axes, including engines off; disable restores authored EVA maps with
      immediate manual rotation (5638 frame gate), also with an explicit control part. Navball thrust
      marker sits on the rocket nose (and follows gimbal-trimmed thrust) only for the enabled kitten.
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
deadband/rate limits. No new Harmony/reflection seam. See [saves](saves.md). The 5638 navball
thrust-marker correction (recomputed every frame) and thruster-cache invalidation (native transient solver
state, rebuilt on the next worker step) add nothing to persist.
