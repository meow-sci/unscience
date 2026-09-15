## Current triage — KSA 2026.9.10.5438

See [upgrade review](plans/KSA_5438_UPGRADE.md). Confirmed code migrations are implemented in the
working tree; native rendering/flight acceptance remains pending.

- **Vehicle Paint:** historical “dead since 4693” statements below are obsolete; the feature was
  rebuilt for 5018. This update added overloaded render targets, breaking patch selection. Paint,
  engine emissive and shared IVA targets now bind the common submission overloads explicitly.
- **Free Fallin:** new native canopy choices removed the old default ID. Custom material creation
  now uses a current baseline and restores each canopy's original material.
- **Graffiti:** skip the new early color-only resolve to prevent stale-depth/double compositing.
- **Pyro:** new renderer lifecycle and template aliases migrated; appearance controls are applied
  to final submitted data. The native renderer still never sets `_hasRefractionInstances` true in the supplied
  sources; the existing refraction-pass defect remains a separate limitation.
- **Blinky:** typed resource-order migration completed; feed diagnosis counts actual reachable
  tanks. The earlier propellant repair remains. Dense-grid ignition/rendering needs live acceptance.
- **Eternal Flame refill during burns:** still open; refill APIs and solver ordering are unchanged.
  Fuel remains UI-timed, unlike the solver-timed electrical refill. Do not attribute the old report
  to 5438 without reproducing it.
- **Garry's Torch errors:** prior actuator/collision fixes remain. New segmented bubble stepping,
  contact-local deformation and crash budgets require native reproduction; no new broken hook found.
- **Kitten animation repetition:** existing processor/clip fixes remain; reflection chain unchanged.
- **Zippo electricity:** remains a feature request, not a detected upgrade regression.

The dated notes below retain historical reports and hypotheses; this section supersedes their
current-status claims.

---

- blinky broken — **root-caused and fixed 2026-08-23 (propellant feed), needs a live pass** (see triage note below)
- eternal flame broken (seems like refill not working while engines are lit.. maybe race condition on data mutations since DMZ changes?)
- garry's torch - works but throws errors
- humble arteest vehicle paint broken
- kitten animations don't properly play each one, always the same — **root-caused and reworked 2026-08-23, needs a live pass** (see triage note below)
- 

- new zippo feature .. refill electricity

---

## Triage notes — KSA `2026.9.7.5402` upgrade (2026-09-02)

Full review: [`plans/KSA_5402_UPGRADE.md`](plans/KSA_5402_UPGRADE.md). Three compile breaks this pass
(`KSA.Viewport` → `IViewport` in five libs, `Cursor.InputRay` in graffiti, exhaust `AddInstance` air
state in pyro), all fixed; the build is green. Revisions
5349–5400 are unlogged, so the review came from the source diff. Everything below still needs a live pass.

- **garry's torch — works but throws errors** — a **new candidate** cause: 5402 adds part structural
  failure (`PartFailure.Detect`, no off-switch). Welded sources overlap the target every frame, so
  contacts between the two compounds can now shed debris or destroy a vehicle, and
  `WeldEngine.UpdateWeld` dereferences `Source.Parent` with no disposed guard. Watch for
  *"exceeded its crash tolerance"* log lines while welding. `Vehicle.Teleport` is byte-identical.
- **eternal flame refill during burns** — not explained by this span: `RefillConsumables` and
  `ExecuteNextVehicleSolvers` are byte-identical. Still open.
- **humble arteest vehicle paint** — still dead by design (rev 4693). Unchanged.
- **pyro refraction slider does nothing (new)** — game-side: 5402 never sets
  `_hasRefractionInstances`, so the refraction pass cannot run for stock engines either. Confirm live.
- **kitten animations / blinky** — no symbol they depend on moved; the 5348 fixes still await a live pass.

---

## Triage notes — KSA `2026.8.22.5348` upgrade (2026-08-23)

Full review: [`plans/KSA_5348_UPGRADE.md`](plans/KSA_5348_UPGRADE.md). The build was green after the
upgrade. Everything below still needs a live pass.

- **kitten animations always the same expression** — ✅ **ROOT-CAUSED AND FIXED (2026-08-23). The
  rev-5278 pose-guard theory below was wrong.** The rev-5278 guard is real
  (`AnimatedRenderable._lastPoseUpdateFrameNumber` replacing `if (!FreezeAnimation)`) but benign: the
  mod's `_expressionPose` cache-bust happens on trigger and the *next* frame re-samples normally.

  The actual defect was the **processor the mod was writing to**.
  `KittenRenderable` installs two `CatExpressionAnim` instances: `_catPersonalityExpressionAnim`
  (a permanent mood face from `CharacterAvatar.Personality`, weight pinned at 1) and
  `_catExpressionAnim` (a reactive scared face). The old `KittenAnimationController` located its
  target with `AnimProcessors.OfType<CatExpressionAnim>().LastOrDefault()` — which resolves to the
  **reactive** one, whose weight `KittenRenderable.UpdateRenderData` rewrites every frame:

  ```csharp
  _catExpressionAnim.ExpressionWeight =
      AnimationUtils.DampingExact(_catExpressionAnim.ExpressionWeight, accelDerivedTarget, 0.2f, dt);
  ```

  That line runs immediately before `UpdateAnimation` samples the pose, so the mod's eased weight
  never survived to render and the personality mood face was all that ever showed — *always the same
  expression*, regardless of which button was pressed.

  **Fix:** the mod now creates and appends **its own** `CatExpressionAnim` to
  `AnimatedRenderable.AnimProcessors` (last in the list, so it mixes over everything, and nothing
  else writes its weight). The game's reactive processor is now only *capped* via a UI slider, never
  written. Same pass exposed the full ground locomotion set and added a Harmony prefix on
  `AnimatedRenderable.UpdateAnimation` so a forced clip survives the game's per-frame clip selection.
  See [`scope/character-and-materials.md`](scope/character-and-materials.md) → kitten-animations.
  **Still needs a live in-game pass to confirm on screen.**
- **blinky broken** — ✅ **ROOT-CAUSED AND FIXED (2026-08-23). The engine-part-id theory was only a
  side bug; the real cause is the propellant feed.** Grids built and added mass, but no pixel could
  ever light because the pixel engines reached **no propellant**, so
  `EngineControllerState.IsPropellantAvailable` stayed false and `FlightComputer.CommandEngineThrottles`
  never commanded a throttle. With no plume there is nothing to see — the meshes are scaled to ~1% and
  are effectively invisible on their own, which is why the render checkbox appeared to do nothing.

  The 5018 fuel/resource rewrite added two gates blinky's wiring failed:
  1. `ResourceManager.CanFlowAcross` (`KSA/ResourceManager.cs:279-282`) rejects the first hop out of a
     consumer part unless the connection sits on a connector declared by the part template's
     `ConsumerFeedWiring`/`FeedsFrom` (`IsDeclaredFeedConnection`).
  2. `ResourceManagerBase.CanFlowAcross` requires the connection to carry the combustor's
     `PlumbingCapability` — `BulkFluid` here.

  blinky connected `Part`↔`Part`. `Part.EndpointCapabilities` is `null` for non-fuel-port parts and
  `Intersect(null, null)` yields `Electricity | ServiceFluid`, so neither gate passed: `PopulateGraph`
  never left the engine and `ConsumptionOrder` stayed empty.

  **Fixed** by connecting the engine's own declared feed connector (`RocketCore.FeedConnectors`, e.g.
  EngineA3's `_connector3`, authored `BulkFluid`) to the fuel part — `LcdGridBuilder.ConnectToFuel`.
  Also fixed: `LcdGridConfig.EnginePartId` now defaults to `EngineA3` (`EngineA1` is gone from
  Content and removed from the preset list); `SetMinimumThrottle` moved before the PartTree rebuild so
  `PartTree.EngineThrottleMin` picks it up; new post-build propellant verification logging; a
  **Repair Feed** button + `POST /blinky/grids/repair` for grids found by scanning or built by the old
  code; a UI warning when the vehicle is not ignited or the throttle is zero; and the diagnose path
  now reads `Combustor.ResourceManager.ConsumptionOrder` typed instead of by string reflection.

  Not a 5348 regression — both gates exist at 5261, so blinky has been dark since 5018.

  Still to verify live: grid build timing (rev 5326 moved `PowerManager.PopulateGraph` behind the part
  window's "Draw Graph" toggle, so rebuilds should be dramatically faster — re-measure), and that the
  a/b thrust cancellation still nets to zero with propellant actually flowing.
- **eternal flame refill not working while engines are lit** — no new evidence. `Battery.cs` is
  **byte-identical**, and `Vehicle.RefillConsumables()` / `Battery.Refill(ref BatteryState)` are
  unchanged. The rev-5326 power rework touched circuit construction and draw, not refill. The 5261 leads
  (rev 5227 ×10 battery capacity; revs 5252/5253 control lockout) still stand.
- **garry's torch throws errors** — no signature drift in its patch targets. New
  suspects this span: the **physics-bubble merge/split rewrite** (revs 5331/5339 — bubble ownership moved
  into `VehicleUpdateTask`, merge checks multithreaded off the render thread) and **ground-clutter
  collisions** (revs 5263/5303/5307, default off, destroy on >25 J/kg impact). garrys-torch teleports a
  vehicle every frame, so both are plausible interactions. **Re-test: the spam may have changed shape.**
  (This entry used to be paired with flexo; **flexo was removed 2026-08-23** — the mod never worked and
  the robotics approach will not be reattempted, so its half of the issue is closed by deletion.)
- **humble arteest vehicle paint broken** — unchanged: still dead by design since rev 4693, still
  self-disables. `MeshIndirect.frag` changed by one line (portrait-light rename) and the
  `vec3 sampledColor` anchor still resolves. Engine Emissive and Kitten Color unaffected.
- **new zippo feature — refill electricity** — unchanged; still a feature request, not a break.
  (Separately: zippo's long-recorded `GetField("Color")` bug is **closed** — the code reads `"ColorRgb"`,
  which is correct. The scope docs describing it as broken were stale and have been fixed.)

---

## Triage notes — KSA `2026.8.19.5261` upgrade (2026-08-11)

Full review: [`plans/KSA_5261_UPGRADE.md`](plans/KSA_5261_UPGRADE.md). Five compile breaks were fixed
this pass (build is green, 55/55 projects); everything below still needs a live pass.

- **blinky broken** — 🔍 **PROBABLE ROOT CAUSE FOUND.** blinky's default `EnginePartId` is
  `"CorePropulsionA_Prefab_EngineA1"` (`blinky.lib/LcdGridConfig.cs:47`, `BlinkySubmod.cs:51`), and
  **that part id no longer exists in the game.** It was removed from
  `Content/Core/CorePropulsionAAssets.xml` between builds 5018 and 5117, so it is absent at 5117,
  5168 and 5261. Only `EngineA2`–`EngineA6` remain (`A2` = "LR91 Sea", `A3`/`A6` = "LR91 Vac",
  `A4` = "VTR-10", `A5` = "LR91 Vac + Verniers"). `ModLibrary.Get` throws on a missing id.
  The 5117 triage missed this because it only checked blinky's *patch targets* (all byte-identical)
  and never its *asset ids*. **Suggested fix: default to `CorePropulsionA_Prefab_EngineA2`.**
  Not changed yet — it is a behavioral default, so confirm the preferred engine first.
- **garry's torch throws errors** — the vehicle threading model was **rewritten** this span (revs
  5208–5216: `DynamicWorkerPool`, `ParallelBatch`, per-vehicle parallel jobs, object-pooled
  `PhysicsBubble`/`ConstraintSim`; plus rev 5237's stale-resource-handle crash fix). The mod's
  solver drain was ported from `JobSystems.VehicleSolvers.Wait()` to `JobSystems.VehicleSolver.Wait()`
  and is provably still complete, but **re-test: the error spam may have changed shape or gone.**
- **kitten animations always the same expression** — `CatExpressionAnim._expressionPose` still
  resolves and the type is byte-identical, so the reflection is not the cause. New suspects this
  span: revs 5203/5233/5235/5244/5249 added ladder, jump, tumble and landing anims and changed
  blend/freeze behaviour ("anim frozen before the blend completed").
- **eternal flame refill not working while engines are lit** — `Vehicle.RefillConsumables()` and
  `Battery.Refill` are still signature-identical. Two new leads: rev 5227 made **all batteries ×10
  maximum capacity** (so a fixed-rate refill now looks far slower), and revs 5252/5253 tightened
  control lockout (`ControlsLockout`, engine shutdown blocked without a control module).
- **humble arteest vehicle paint broken** — unchanged: still dead by design since rev 4693. Both GLSL
  anchors still resolve. Engine Emissive and Kitten Color are **unaffected** by this build; the one
  `MeshIndirect.frag` change (rev 5196 portrait lights) does not touch the anchor.
- **flexo throws errors but works** — no signature drift in its patch targets this span. New
  editor-side suspects: bendable fuel-line hoses (5171), roll-while-snapped (5258), and the map grid
  moving out of screen space (5256/5257).

---

## Triage notes — KSA `2026.8.3.5117` upgrade (2026-08-01)

Nothing above was **confirmed** fixed or explained by this build; all entries still need a live pass.
Recorded here so the next triage doesn't re-derive it. Full review:
[`plans/KSA_5117_UPGRADE.md`](plans/KSA_5117_UPGRADE.md).

- **eternal flame refill** — the game changed refill behavior in rev 5021: *"Prevented tanks from
  being unexpectedly refilled when loading vehicles, saving vehicles, or exiting the editor… swapping
  engines, etc. will still cause refills"*, plus an explicit **"Refill Consumables"** button in the
  editor. `Vehicle.RefillConsumables()` and `Battery.Refill` are signature-identical, so this is not
  a break — but it is a plausible interaction with the reported symptom and worth testing first.
  Note also rev 5114 (*flight computer now aware when engines run out of propellant*) touches the
  same "while engines are lit" window.
- **humble arteest vehicle paint** — this entry predates the 2026-07-25 rebuild that made paint work
  again on 5018. Both GLSL anchors still resolve on 5117 (verified statically), so **re-test before
  assuming it is still broken**; if it is, the cause is new.
- **kitten animations always the same expression** — prime suspect remains the 5018-era animation
  pipeline rework, not this build: `CatExpressionAnim` and `AnimatedRenderable` are byte-identical
  5018→5117 and `_expressionPose` still resolves.
- **garry's torch / flexo error spam** — no signature drift in either mod's patch targets this span.
  New in 5117: rev 5115 vehicle destruction on structural g-limit / dynamic pressure, which torch's
  per-frame `Vehicle.Teleport` could now trip. Worth checking whether the errors changed shape.
- **blinky** — `EngineController.SetIsActive`, `PartTree.CreateFromNewPartTree` and the three
  `*Module.UpdateRenderData` patch targets are all byte-identical this span.
