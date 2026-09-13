# Camera / View Mods — Game Integration Scope

Permanent reference for detecting when KSA game updates break the camera/view mods
(`camera-controller-override`, `glass`, `hot-pursuit`). Every game-facing member these mods touch is
enumerated and verified against decompiled sources.

**Verified game versions**

- NEW decomp `2026.9.7.5402` root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- OLD decomp `2026.8.22.5348` root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`

Paths in the **Decomp path (NEW)** column are relative to the NEW decomp root
(namespace-foldered, e.g. `KSA/Camera.cs`). **Mod code** paths are relative to the
repo root `~/repos/meow-sci/unscience`.

**How these mods are hosted**

- Camera state + game reads/writes live in the `*.lib` projects (`camera-controller-override.lib`,
  `glass.lib`, `hot-pursuit.lib`). Each `.lib` exposes an `ISubmod`
  (`MeowSci.KsaAbstractions.ISubmod`) and a
  static patch helper (`CameraControllerOverridePatches`, `GlassPatches`) consumed two ways:
  1. **Standalone** StarMap mod (`camera-controller-override/Mod.cs`, `glass/Mod.cs`,
     `hot-pursuit/Mod.cs`) — own ImGui window; its own `Patcher.cs` applies the lib's patch helper.
  2. **Embedded** in the **unscience** supermod: `unscience/Mod.cs:62,66` adds
     `CameraControllerOverrideSubmod`, `unscience/Mod.cs:73` adds `GlassSubmod`; `unscience/Patcher.cs:59-63`
     calls `CameraControllerOverridePatches.Apply` and `unscience/Patcher.cs:70` calls `GlassPatches.Apply`.
- **Because both host paths route through the same patch helpers, every finding below applies
  identically to the standalone mod and to the unscience-embedded copy.**

**Summary of 4680 -> 4750 risk**

- **glass — NO breaking deltas.** All six game members (incl. the critical private field
  `Camera._fovRadians`) are signature-identical between OLD and NEW; only source line numbers shifted.
- **camera-controller-override — NO 4680->4750 deltas in the resolvable targets.** A long-standing
  defect — the Harmony field injector `___Transform` matched **no field** on the `KSA` controllers
  (the camera is the public field `Camera`, type `KSA.Camera : Transform3D`) — is now **FIXED (Phase 4)**:
  the prefix reads `__instance.Camera` directly. Because that injector threw at patch time (Harmony 2.4.2),
  it had also been aborting the rest of the supermod's patch chain; see the findings below.

---

## camera-controller-override

**Purpose** — Cinematic camera animation system (zoom / orbit / loopy-orbit / spiral / shake /
pan / rotate, keyframe sequences, animation groups, return-to-start). When a sequence is playing,
it intercepts the active camera controller's per-frame update and drives the camera transform
itself instead of letting the game position the camera.

**Unscience integration**

- `CameraControllerOverrideSubmod` (`camera-controller-override.lib/CameraControllerOverrideSubmod.cs`)
  owns all UI config + a `KeyframeSequencePlayer`, exposed via `SequencePlayer`.
- Patch wiring: the host sets `CameraControllerOverridePatches.SequencePlayer` then calls
  `Apply(Harmony)` (standalone: `camera-controller-override/Patcher.cs:18-20`; unscience:
  `unscience/Patcher.cs:59-63`, player wired at `unscience/Mod.cs:108`).
- `CameraControllerOverrideSubmod.Instance` (static, set in `Initialize()`) is the reusable entry point
  for driving sequences programmatically.

**UI/hotkeys** — Standalone window toggled with **F11** (`camera-controller-override/Mod.cs:51`),
rendered in `OnAfterUi`. Embedded copy renders as a collapsible section inside the unscience window.
No game hotkeys are rebound; `HotkeyGuard` is applied to block game keys while typing in ImGui.

**Persistence** — None. Keyframe sequences are built at runtime via the UI and are not saved/loaded
(README lists save/load as a future idea). The unscience supermod persists only generic submod
visibility/header state, not animation data.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | Reflection (AccessTools.Method) + Harmony prefix | `camera-controller-override.lib/CameraControllerOverridePatches.cs:25,29` | `KSA.OrbitController.OnFrame(IViewport inViewport, double inDeltaTime)` — `public override void` | `KSA/OrbitController.cs:487` | Yes | **Retyped @5402** — first param `Viewport` → `IViewport` (OLD `KSA/OrbitController.cs:468`); still the only `OnFrame` overload, so string resolution stays unambiguous | Method target resolves. Bound via `using KSA;` → `KSA.OrbitController` (NOT `RenderCore.Input.Controllers.OrbitController`). The prefix never names the viewport param, so the retype is invisible to it. |
| 2 | Reflection (AccessTools.Method) + Harmony prefix | `camera-controller-override.lib/CameraControllerOverridePatches.cs:26,31` | `KSA.FlyController.OnFrame(IViewport inViewport, double inDeltaTime)` — `public override void` | `KSA/FlyController.cs:653` | Yes | **Retyped @5402** — `Viewport` → `IViewport` (OLD `KSA/FlyController.cs:653`); only `OnFrame` overload (`OnFrameEditor` at `:742` is a different name) | Method target resolves. Bound to `KSA.FlyController`. |
| 3 | Harmony arg injection (`__instance`) | `camera-controller-override.lib/CameraControllerOverridePatches.cs:42` | `KSA.Controller` (base of both controllers) | `KSA/Controller.cs:8` | Yes | None (`Controller.OnFrame` virtual retyped to `IViewport` at `:62`; class otherwise identical) | Base type unchanged; `__instance` typing OK. |
| 4 | Harmony arg injection (by param NAME) | `camera-controller-override.lib/CameraControllerOverridePatches.cs:42` | `OnFrame` parameter `double inDeltaTime` | `KSA/OrbitController.cs:487`, `KSA/FlyController.cs:653` | Yes | None | Param name `inDeltaTime` matches in both controllers/versions. The prefix deliberately does not bind `inViewport`, which is why the 5402 `Viewport` → `IViewport` retype needed no mod change. |
| 5 | Direct field read (`__instance.Camera`) — **FIXED (Phase 4)** | `camera-controller-override.lib/CameraControllerOverridePatches.cs:42,54` | `KSA.Controller.Camera : Camera` (public field; `KSA.Camera : Transform3D`) | `KSA/Controller.cs:12` | Yes | None | Was a Harmony field injector `Transform3D ___Transform` binding to a **non-existent** field (the camera is `Camera`, not `Transform`; `KSA/Controller.cs` contains no `Transform` member in 5348 or 5402 — a `Transform` field exists only on the unrelated `RenderCore.Input.Controllers.CameraController`). Harmony 2.4.2 validates injected field names at patch time, so `harmony.Patch` **threw** → the prefix never attached AND the throw aborted the rest of the supermod chain. Now the prefix reads `__instance.Camera` (a `Transform3D`) directly and passes it to `Update`. |
| 6 | Direct typed API (read chain) | `camera-controller-override.lib/Animation/AnimationHelpers.cs:33` | `KSA.Controller.Camera` (field) → `KSA.Camera.Following` (`IFollowable?` prop) → `IFollowable.GetPositionEcl() : double3` | `KSA/Controller.cs:12`, `KSA/Camera.cs:158`, `KSA/IPosition.cs:7` | Yes | None (OLD `KSA/Camera.cs:158`; `Following`/`GetPositionEcl` unchanged; `IPosition.cs` byte-identical) | Target-tracking. `GetPositionEcl` is on `IPosition` (base of `IFollowable`). Live since the Phase 4 fix of #5. |
| 7 | Direct typed API (static) | `AnimationHelpers.cs:46`, `Animation/Animations/SpiralZoomOutAnimation.cs:127`, `Animation/Animations/SpiralZoomInAnimation.cs:136` | `KSA.Camera.LookAtRotation(double3 forwardEcl, double3 upEcl) : doubleQuat` — `public static` | `KSA/Camera.cs:198` | Yes | None (OLD `KSA/Camera.cs:198`) | Static helper, compile-time bound. Signature identical. |
| 8 | Direct typed API (read/write) | `camera-controller-override.lib/Animation/KeyframeSequencePlayer.cs:450,473` + all `Animation/Animations/*` | `KSA.Transform3D.PositionEcl { get; set; } : double3` — `public virtual` | `KSA/Transform3D.cs:15` | Yes | None (file byte-identical) | Mutates the controller's `Camera` (a `Transform3D`) by reference to move the camera. `Camera` overrides `PositionEcl` (`KSA/Camera.cs:110`). Live since the Phase 4 fix of #5. |
| 9 | Direct typed API (read/write) | `KeyframeSequencePlayer.cs:451,476,477` + `Animation/Animations/*` | `KSA.Transform3D.LocalRotation : doubleQuat` — `public` field | `KSA/Transform3D.cs:13` | Yes | None (file byte-identical) | Mutates the controller's `Camera` by reference to rotate the camera. Live since the Phase 4 fix of #5. |
| 10 | Harmony patch (shared, required) | `camera-controller-override/Patcher.cs:20` (+ `unscience/Patcher.cs` via HotkeyGuard) | `MeowSci.KsaAbstractions.HotkeyGuard.Patch(Harmony)` | n/a (abstraction lib; targets game input — see HotkeyGuard scope) | Yes | n/a | Blocks game hotkeys while typing in ImGui. Per-mod requirement. |
| 11 | Lifecycle (StarMap attributes) | `camera-controller-override/Mod.cs:19,22,38,45,60` | `StarMap.API` `[StarMapMod]`, `[StarMapImmediateLoad]`, `[StarMapAllModsLoaded]`, `[StarMapBeforeGui]`, `[StarMapAfterGui]`, `[StarMapUnload]` | StarMap library | Yes | n/a | Standalone load lifecycle. |

**Game assets referenced** — None. The mod only manipulates camera transforms via math (`Brutal.Numerics`); it loads no textures, models, shaders, or `Content/` files.

**Update-risk findings (5117 → 5261)**

- ✅ **No breaking deltas.** `RenderCore.Input.Controllers/Controller.cs` is **byte-identical** between
  OLD (5168) and NEW (5261), including the public `Camera` field the prefix reads.
  `OrbitController.OnFrame` and `FlyController.OnFrame` both still resolve by string.
- ✅ **glass** — `Camera._fovRadians` (the single most important glass check), `ChangeFieldOfView` and
  `UpdateProjection` all still resolve on `KSA/Camera.cs`.
- ✅ The `___Transform` field injector is **retired**, so the historic `Apply`-time throw (which used
  to abort the rest of the supermod's patch chain) cannot recur. The master index's §4/§6 entries
  still described it as broken; corrected this pass to match what `camera-controller-override.lib`
  actually does.
- ⚠️ **Camera behavioral watch items (compile-clean, need a live pass):** docking-camera orientation
  no longer baked at toggle time (rev 5191), docking camera un-flipped (5222), 0.1 m stand-off from
  the docking ring + reticle from actual viewport size (5255), portrait-camera FOV (5195) and a
  temporary side view for kittens on ladders (5245). These change what the camera *does* without
  moving a symbol glass or camera-controller-override binds to.

**Update-risk findings (4680 -> 4750)**

- **No 4680->4750 deltas in any resolvable target.** `OrbitController.OnFrame`, `FlyController.OnFrame`,
  base `Controller` (incl. the `Camera` field), `Camera.Following`, `Camera.LookAtRotation`,
  `IPosition.GetPositionEcl`, and `Transform3D.PositionEcl`/`LocalRotation` are all signature-identical
  between OLD and NEW; only line numbers shifted.
- **FIXED (Phase 4) — `___Transform` field injector (#5).** No `Transform` field exists on
  `KSA.Controller`/`OrbitController`/`FlyController` in **either** 4680 or 4750 (the camera is the public
  field `Camera`, `KSA.Camera : Transform3D`). The prefix now reads `__instance.Camera` directly, so the
  animation drives the live camera. **Secondary impact this also fixed:** Harmony 2.4.2 validates injected
  `___` field names at patch time and **throws** when none binds — so `CameraControllerOverridePatches.Apply`
  threw inside `unscience/Patcher.cs`, and because that call sat mid-chain, every feature applied *after* it
  (eternal-flame, glass, i-feel-seen, vehicle-paint, engine-emissive, and the since-removed flexo) silently failed to patch in the
  supermod. The supermod patch chain is now also hardened so any single feature's apply failure is isolated
  (logged + skipped) rather than aborting the rest. (`Transform` is the correct member name only on the
  unrelated `RenderCore.Input.Controllers.CameraController` family.)
- **Two controller families share class names — binding hazard.** `OrbitController`/`FlyController` exist
  in BOTH `KSA` (base `KSA.Controller`, `OnFrame(Viewport,double)`) and `RenderCore.Input.Controllers`
  (base `CameraController`, `OnFrame(double)`, and *does* have a `Transform` member). The mod's
  `using KSA;` selects the `KSA` family. If a future update moves the live camera controllers to (or
  resolves `using` toward) the `RenderCore` family, both the patch target signature and the `Transform`
  semantics change — re-verify which family the game actually instantiates for the flight camera.
- **Brutal package dependency (low).** Animations rely on `Brutal.Numerics` (`double3`, `doubleQuat`,
  and the `KSA.Transform3D` math). Changelog 4729 updated Brutal packages; the referenced types/members
  are still present and used by the game itself, so risk is low — but a `Transform3D`/numerics API change
  would surface here.

---

## hot-pursuit

**Purpose** — Click a rendered vehicle part and mount a live camera to the exact hit sub-part.
Each entry leases one of KSA's four preallocated secondary viewports and exposes part-local XYZ
translation, mount-relative pitch/yaw/roll, FOV, resolution, visibility and lease controls.

**Feasibility boundary** — KSA creates main + thumbnail + four secondary + two portrait viewports
at boot, exhausting `ViewportRegistry.MAX_VIEWPORTS == 8`, then seals allocation. Hot Pursuit does
not reflect into allocation or create GPU resources: it uses the supported public secondary-lease
API. Therefore at most four Hot Pursuit cameras can render, less any slots held by stock Add Camera,
docking cameras, or another mod.

**Unscience integration**

- `HotPursuitSubmod` (`hot-pursuit.lib/HotPursuitSubmod.cs` plus UI/placement partials) owns entries,
  stable target re-resolution, leases, and UI. `unscience/Mod.cs` creates it and calls the normal
  `ISubmod` lifecycle.
- `HotPursuitPatches.Apply/Remove` is used by both `hot-pursuit/Patcher.cs` and
  `unscience/Patcher.cs`.
- Standalone F11 and unscience both use the stock `GameViewport.DrawImGui` window for each feed.
  Closing that window releases the registry lease; the submod detects that through `TryGetOwned`
  and offers Reopen rather than retaining a stale reference.

**Placement and coordinate model**

- Placement is a one-shot world click using `Cursor.GetEgoRay(Program.MainViewport)`. It sweeps all
  live vehicles (including debris), calls the same mesh-precise `Part.RayCastEgo` used by KSA hover
  picking, and stores the returned closest sub-part, position, and normal. The latter two are in the
  hit sub-part's local assembly frame.
- The initial mount point is 0.15 m along the local hit normal. A tangent derived from local +Y (or
  +X fallback) completes the outward-facing basis.
- A `FixedController.OnFrame` prefix checks whether the passed viewport currently belongs to Hot
  Pursuit and skips stock fixed-camera math only for those views. It composes the mount through
  `Part.MatrixAsmb2Ego`, converts the point from
  the main camera's ego frame to ECL, transforms tangents normally and the surface normal by the
  inverse-transpose (then re-orthogonalizes the basis for non-uniform scale), then
  writes `Camera.PositionEcl` and `WorldRotation`. Immediately after writing `PositionEcl`, it
  applies the same public terrain clamp that `Camera.OnFrame` is about to apply, then
  `HotPursuitCelestialState.Synchronize` calls `Program.FindNearbyCelestial` for that secondary
  camera, mirrors KSA's 80,000 km surface-distance rejection, and fills the camera's public
  distance, terrain-height, and current-altitude fields. This prevents the nearby body from also
  entering `StaticCelestialDistanceRendering` as a distant sphere. The prefix runs before
  `GameViewport.OnFrame` calls `Camera.OnFrame`, so view/frustum data is baked from the current
  part pose without UI-hook lag.

**Integration points**

| # | Kind | Mod code | Game target (Type.Member + signature) | Decomp path (NEW) | Risk/notes |
|---|------|----------|----------------------------------------|-------------------|------------|
| 1 | Harmony selective prefix (explicit signature; typed arg) | `hot-pursuit.lib/HotPursuitPatches.cs` | `FixedController.OnFrame(IViewport,double)` | `KSA/FixedController.cs:22` | Keystone timing seam. Returns false only for a registry-confirmed owned viewport; missing/stale leases run stock. Caller still invokes `Camera.OnFrame` immediately afterward. |
| 2 | Direct viewport lease API | `HotPursuitSubmod.cs` | `ViewportRegistry.{AvailableSecondaryCount,TryClaimSecondaryViewport(IViewportOwner,out IGameViewport),TryGetOwned,ReleaseSecondaryViewport(IViewportOwner)}` | `KSA/ViewportRegistry.cs:54,181,213,246` | Four shared leases; reset-on-claim/release means all settings are reapplied. Allocation is sealed and capped at 8. |
| 3 | Direct viewport configuration | `HotPursuitSubmod.cs`, `.Ui.cs` | `IGameViewport.{SetName,SetCameraMode,SetResizeAllowed,RequestResize,SetVisible,BaseCamera}`; `CameraMode.Fixed` | `KSA/IGameViewport.cs`; `KSA/IViewport.cs`; `KSA/CameraMode.cs` | Stock render targets and ImGui window. Closing `DrawImGui` releases the lease. |
| 4 | Direct camera API | `HotPursuitSubmod.cs`, `HotPursuitPose.cs`, `HotPursuitCelestialState.cs` | `Camera.{SetFollow(...changeControl:false),SetFieldOfView(float),PositionEcl,WorldRotation,LookAtRotation,ClampCamera,NearbyCelestial,DistanceToNearbyCelestialKm,DistanceToNearbyCelestialSurfaceMeanKm,NearbyCelestialTerrainHeight,CurrentAltitudeKm}` | `KSA/Camera.cs:31-37,71,110,134,198,412,597,628` | `changeControl:false` is load-bearing: true would change the controlled vehicle. Hot Pursuit applies the public 0.5 m AGL terrain clamp before synchronizing celestial metrics; `Camera.OnFrame` repeats the now-idempotent clamp immediately afterward. |
| 5 | Direct picking API | `HotPursuitPicker.cs` | `Cursor.GetEgoRay(IViewport)`; `Part.RayCastEgo(...)`; `Vehicle.{BoundingSphereRadiusBody,GetMatrixAsmb2Ego(Camera)}` | `KSA/Cursor.cs:27`; `KSA/Part.cs:2398`; `KSA/Vehicle.cs` | Same-frame main cursor ray. Hit position/normal belong to the returned closest sub-part, not necessarily the top-level part. Terrain is deliberately unsupported. |
| 6 | Direct part transform/addressing | `HotPursuitCamera.cs`, `HotPursuitPose.cs`, `HotPursuitSubmod.cs` | `Part.{InstanceId,SubParts,MatrixAsmb2Ego}`; `Vehicle.Id`; `Universe.CurrentSystem` via `VehicleProvider` | `KSA/Part.cs:321,655,1165`; `KSA/Astronomical.cs:85` | Stable id re-resolution makes missing/failed/staged targets dormant rather than dereferencing destroyed objects. Full matrix includes scale and articulated sub-part parents. |
| 7 | Direct nearby-celestial lookup | `hot-pursuit.lib/HotPursuitCelestialState.cs` | `Program.FindNearbyCelestial(Camera)` | `KSA/Program.cs:5037` | KSA's `OnFrameCelestials` updates only `Program.GetCamera()` (the main/frame camera); Hot Pursuit explicitly performs the equivalent lookup for its owned secondary camera after `PositionEcl`. |
| 8 | Lifecycle/shared input guard | `hot-pursuit/Mod.cs`, `Patcher.cs`; unscience host | StarMap attributes; `HotkeyGuard` | StarMap / abstraction | No game assets or custom shaders. |

**Compatibility and live-test risks**

- Glass now scopes its reflection-based FOV injection and direct FOV application to the main
  viewport cameras. This preserves Hot Pursuit's per-camera FOV; re-check both together after camera
  changes.
- `Camera.OnFrame` calls terrain clamping, so a hull mount below 0.5 m AGL will be raised.
- Secondary viewports are substantial additional scene renders and expensive, but KSA 5402's
  `Program.RenderViewport` does not run `ParticleSystem`, `VolumetricExhaustRenderer`, the main
  planet/ocean/cloud pipeline, part-glass, or overall-bloom passes. Engine plumes and generic
  particles are therefore absent by design; those game-owned passes bind main-camera
  targets/resources and are not safe to inject from Hot Pursuit. Other effects/shadows are not
  guaranteed identical to the main view; verify the nearby-body artifact, vehicles, night lighting,
  scaled/robotic parts, target destruction/debris handoff, closing/reopening, and simultaneous
  docking cameras live.
- No reflection, assets, shaders, or custom Vulkan resources are used by Hot Pursuit itself.

---

## glass

**Purpose** — Camera field-of-view control: 8 photographic lens presets plus a manual FOV slider
(clamped 1°–179°). When the override is active it forces the camera FOV every frame and blocks the
game's own FOV input.

**Unscience integration**

- FOV state + logic live in `glass.lib/FovController.cs` (static), so other projects can drive FOV by
  referencing `glass.lib` without the `glass` mod. `GlassSubmod` (`glass.lib/GlassSubmod.cs`) is the
  `ISubmod` UI; its `Update(dt)` calls `FovController.ApplyFov()` each frame.
- Patch wiring: `GlassPatches.Apply(Harmony)` (standalone: `glass/Patcher.cs:14`; unscience:
  `unscience/Patcher.cs:70`). `GlassSubmod` added at `unscience/Mod.cs:73`.
- `glass.lib` exposes `FovController` as a reusable programmatic control surface.

**UI/hotkeys** — Standalone window toggled with **F9** (`glass/Mod.cs:51`). Embedded copy is a
collapsible section in the unscience window. `HotkeyGuard` applied. The game's own +/- FOV keys (which
call `Camera.ChangeFieldOfView`, `KSA/Camera.cs:769,774`) are suppressed by the prefix while override is active.

**Persistence** — None. `FovController` state (`IsOverrideActive`, `OverrideFovDegrees`) is runtime-only;
`GlassSubmod.Dispose()` calls `FovController.DisableOverride()` to hand control back to the game on unload.

**Integration points**

| # | Kind | Mod code (file:line) | Game target (Type.Member + signature) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|------|----------------------|----------------------------------------|-------------------|---------|----------|------------|
| 1 | **Reflection (AccessTools.Field — PRIVATE)** | `glass.lib/GlassPatches.cs:20` | `KSA.Camera._fovRadians` — `private float _fovRadians = 0.87266463f` | `KSA/Camera.cs:53` | **Yes** | **None** (OLD `KSA/Camera.cs:53`, identical declaration) | **Single most important check — PASSED (5402).** String-based private field; a rename would silently break all FOV control with no compile error. Name + type unchanged through every build since 4680. |
| 2 | Reflection (private field write, `SetValue`) | `glass.lib/GlassPatches.cs:57-66` | `KSA.Camera._fovRadians` (write target FOV in radians) | `KSA/Camera.cs:53` | Yes | behavior narrowed for Hot Pursuit | `UpdateProjectionPrefix` now first checks `ViewportRegistry.IsMainCamera(__instance)`, so secondary/portrait/thumbnail cameras retain their independent projection. |
| 3 | Reflection (AccessTools.Method) + Harmony prefix (skips original) | `glass.lib/GlassPatches.cs:25,50-53` | `KSA.Camera.ChangeFieldOfView(float change)` — `public void` | `KSA/Camera.cs:450` | Yes | behavior narrowed for Hot Pursuit | Prefix blocks stock FOV input only for a main viewport camera while override is active. |
| 4 | Reflection (AccessTools.Method) + Harmony prefix (`void`, runs-before) | `glass.lib/GlassPatches.cs:26,57-66` | `KSA.Camera.UpdateProjection()` — `public void` | `KSA/Camera.cs:466` | Yes | None (OLD declaration identical) | Main-camera-only prefix injects `_fovRadians`, then original rebuilds the projection matrix. |
| 5 | Direct typed API (static + instance) | `glass.lib/FovController.cs:40-55` | `KSA.Program.GetMainCamera() : Camera` + `KSA.Camera.GetFieldOfView() : float` (RADIANS) | `KSA/Program.cs:632`, `KSA/Camera.cs:785` | Yes | switched from frame camera | Reads/applies the player's main lens explicitly so a frame-scoped secondary camera cannot be stomped. |
| 6 | Direct typed API (instance) | `glass.lib/FovController.cs:55` | `KSA.Camera.SetFieldOfView(float fovDegrees)` — `public void` (param is DEGREES; converts to radians internally) | `KSA/Camera.cs:412` | Yes | None (OLD `KSA/Camera.cs:412`) | Called from `ApplyFov()` on the game thread. Note the asymmetry: setter takes **degrees**, getter (#5) returns **radians**. |
| 7 | Harmony patch (shared, required) | `glass/Patcher.cs:15` (+ unscience via HotkeyGuard) | `MeowSci.KsaAbstractions.HotkeyGuard.Patch(Harmony)` | n/a (abstraction lib) | Yes | n/a | Per-mod requirement. |
| 8 | Lifecycle (StarMap attributes) | `glass/Mod.cs:19,22,38,45,60` | `StarMap.API` load/gui/unload attributes | StarMap library | Yes | n/a | Standalone load lifecycle. |

**Game assets referenced** — None. Pure projection-math / field manipulation; no `Content/` assets.

**Update-risk findings (4680 -> 4750)**

- **No breaking deltas detected.** All six game members are signature-identical between 4680 and 4750;
  only line numbers shifted as `Camera.cs` grew.
- **Critical verification PASSED:** the private field `KSA.Camera._fovRadians` (`private float`) exists and
  is unchanged (OLD `KSA/Camera.cs:46` → NEW `KSA/Camera.cs:47`). A future rename of this field is the
  single highest-risk break (string-based, no compile error, FOV silently stops responding) — re-check
  this field name on every game update.
- **Standing string-resolution risk:** `ChangeFieldOfView` and `UpdateProjection` are resolved by name via
  `AccessTools.Method`; renames/overload changes would break at patch time (caught only at runtime, not
  compile time). `Program.GetCamera`, `Camera.GetFieldOfView`, and `Camera.SetFieldOfView` are typed
  (compile-time) and would fail the build if changed — lower risk.
- `KSA.Camera` remains in the `KSA` namespace (file `KSA/Camera.cs`) in both versions; note an unrelated
  `Brutal.GltfApi.Camera` also exists — the typed `using KSA;` keeps the binding unambiguous.

---

## Area summary — Update-risk findings (5261 → 5348)

- ✅ **`KSA/Camera.cs` is byte-identical between 5261 and 5348.** glass is completely clean this span:
  `Camera._fovRadians` (private field, by name), `Camera.ChangeFieldOfView(float)` and
  `Camera.UpdateProjection()` all resolve exactly as before.
- ✅ **camera-controller-override clean.** `AccessTools.Method(typeof(OrbitController), "OnFrame")` and
  the `FlyController` equivalent both still resolve; the `___Transform` field-injector bug stays
  **retired** — the prefix reads the public `__instance.Camera`
  (`CameraControllerOverridePatches.cs:42-54`).
- ✅ **Coordinate frames unchanged.** Rev 5280's `CelestialFrameMath` extraction preserved every public
  `Celestial` frame accessor, so camera-controller-override's ego-space math is unaffected.
- ℹ️ **New camera surface this span, none of it breaking:** per-kitten crew-portrait cameras with a FOV
  slider and Head/Neck/Chest bone targeting (revs 5270/5273), a resizable kitten-cam gauge (5297),
  portrait cameras skipped when not visible (5295), stars no longer rendered in kitten cams (5292), and
  `ViewportLightModes` so each viewport picks its own light path (5301). If glass's FOV override ever
  needs to apply per-viewport rather than globally, these are the seams to look at.

---

## Area summary — Update-risk findings (5348 → 5402)

Revisions 5349–5400 are **unlogged**; the only changelog entry in this span is rev 5401 *"Fixed crash
for incorrect data stride for thumbnail rendering"*, so the decomp diff is the sole evidence. Solution
builds clean against 5402.

- ✅ **glass clean.** `Camera._fovRadians` (`KSA/Camera.cs:53`, `private float`), `SetFieldOfView`
  (`:412`), `ChangeFieldOfView` (`:450`), `UpdateProjection` (`:466`) and `GetFieldOfView` (`:785`) all
  resolve with identical declarations; `ChangeFieldOfView`/`UpdateProjection` are still single-overload.
  The only `Camera.cs` hunks are `FollowWreckage` (`SetFollow(…, changeControl: false)`), the
  `ClampCamera` refactor into a new `TryGetSurfaceClampPositionEcl(double, out double3)` (`:648-664`) and
  `DeserializeSave` taking `IGameViewport` — none touch FOV or projection. `Program.GetCamera()`
  (`Program.cs:647`) is still `FrameViewport.GetCamera()`.
- ✅ **camera-controller-override clean; one signature retype absorbed.** The `Viewport` →
  `IViewport`/`IGameViewport` rework retyped `Controller.OnFrame` (`Controller.cs:62`) and both overrides
  (`OrbitController.cs:487`, `FlyController.cs:653`) to `OnFrame(IViewport, double)`. The prefix
  (`CameraControllerOverridePatches.cs:42`) binds only `Controller __instance` and `double inDeltaTime`,
  never the viewport, and both targets remain single-overload, so `AccessTools.Method(…, "OnFrame")`
  resolves exactly as before with no mod change. `Controller.Camera` (`Controller.cs:12`),
  `Camera.Following` (`:158`), `Camera.LookAtRotation` (`:198`), `Camera.PositionEcl` (`:110`) and the
  byte-identical `Transform3D.cs` / `IPosition.cs` are untouched. The `___Transform` injector stays
  **retired** (no `Transform` member on `KSA.Controller` in either tree).
- ℹ️ **OrbitController/FlyController behavior moved, none of it binding-relevant.** `OrbitController`
  gained an optional independent `OrbitView` (`independentView` ctor flag, `View`, `ClaimViewFor`,
  `ResetIndependentView`, `EnsureFollowTarget`, `CanChangeControl => ViewportRegistry.IsMainCamera(Camera)`)
  and seeds it at the top of `OnFrame` (`:495-500`); `FlyController.ClampCamera` now uses the controller's
  own `Camera` and the new surface-clamp helper (`FlyController.cs:845-862`). When a sequence is playing the
  prefix skips the whole body as before; on resume the seeding simply runs next frame.
- ✅ **glass secondary-camera isolation implemented with Hot Pursuit.** The viewport rework made
  the previous instance-agnostic prefix affect thumbnail, portrait, docking and extra cameras.
  `GlassPatches` now gates both interception paths with `ViewportRegistry.IsMainCamera`, and
  `FovController` reads/writes `Program.GetMainCamera()`. Each secondary camera retains its own FOV.
- ℹ️ **Follow-target handoffs are new** — `Universe.HandOffCameras` (`Universe.cs:1778`) and the
  debris/wreckage paths in `DestroyVehicleFromEvent` re-`SetFollow` any camera following a destroyed
  vehicle. camera-controller-override reads `Camera.Following` each frame (`AnimationHelpers.cs:33`),
  so a running sequence will retarget rather than throw.
- **Needs a live pass:** glass + Hot Pursuit with different FOVs, plus confirming a
  camera-controller-override sequence still drives the main camera after the `IViewport` retype.

## Hot Pursuit save/load adapter (feature/saves)

`HotPursuitSubmod.Saves` captures concrete `SavedPartReference` targets, part-local mount
basis, offsets/rotation, FOV/resolution, visibility and whether `ViewportRegistry.TryGetOwned`
reports an active lease. Reset disarms placement and releases all old owner leases before
native vehicle destruction. Restore resolves exact fresh targets and invokes the existing
`TryOpenViewport`/`ConfigureViewport` APIs. Slot exhaustion leaves a camera entry available
for manual reopening, with a Saves warning; closed viewports are not reopened automatically.

Acceptance: all four slots, another viewport owner, hidden/closed mounts, nested subparts,
changed/missing targets and repeated load. Viewport objects/owners are never serialized.

Camera Controller Override stores explicit tagged animation recipes for every built-in animation
and nested parallel groups, including pending group authoring state and return preferences. Playback
restores stopped. Glass reapplies enabled/degrees to the reconstructed main camera after native
load. These adapters reuse existing camera APIs and add no reflected game member.
