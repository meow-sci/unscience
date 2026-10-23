# Hot Pursuit

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Hot Pursuit places live, part-mounted cameras in KSA's stock secondary viewport
windows. Arm placement in the F11 window, click a visible vehicle part in the 3D
world, and the new camera starts 0.15 m outside the clicked surface looking along
its surface normal. Translation, pitch/yaw/roll, field of view, and resolution can
then be tuned per camera.

## Usage

1. Press **F11** to open the Hot Pursuit window.
2. Click **Arm camera placement**, then click a vehicle part in the 3D world.
   Escape or right-click cancels. UI clicks are ignored.
3. Use each camera's controls to tune its part-local translation, mount-relative
   rotation, FOV, and resolution. Click **Apply resize** after changing dimensions.
4. The **Visible** checkbox controls rendering. **Reopen viewport** claims a slot
   again after the stock viewport window has been closed. Remove deletes the camera
   and releases its lease.

Hot Pursuit targets vehicle parts only; terrain is intentionally ignored. The target
vehicle ID and `Part.InstanceId` are retained, so an unloaded or despawned target
becomes dormant and can resume when that vehicle/part is live again.

## Viewport and camera model

KSA 5402 preallocates a finite pool of secondary game viewports. Every camera owns
an `IViewportOwner` token and claims one slot through
`ViewportRegistry.TryClaimSecondaryViewport`; the current KSA 5402 build exposes **four
shared secondary slots** (also used by KSA's own Add Camera action and any other mod).
The pool size, including slots used by other game systems and mods, is the hard camera-count limit. Closing a stock camera
window makes KSA release the lease. Hot Pursuit checks ownership every frame and
shows a reopen affordance instead of using a stale viewport reference.

The claimed viewport is configured with the stock renderer: `CameraMode.Fixed`, a
500×500 default (or the requested size), no user resize, a vehicle follow target,
and the requested FOV. No custom Vulkan render path or texture registration is used;
KSA renders the normal secondary scene directly in its stock window.

Hot Pursuit also synchronizes the mounted camera's nearby-celestial state immediately
after each pose update. KSA only populates that state for the main camera. Before 5638 the
nearby body could therefore also be submitted as a distant sphere, producing a large
dark-grey plane/sphere across the secondary view. Since 5638 stock falls back to the same lookup
(`Camera.GetEffectiveNearbyCelestial()`), and the sync keeps the camera's raw state consistent. It
mirrors KSA's 80,000 km surface-distance cutoff and its distance, terrain-height and current-altitude
fields. Hot Pursuit clears the state when it releases or loses a viewport, so a stock camera opened in
that slot later is not pinned to a previous body.

## KSA secondary-renderer limitation

Since 5541/5638 secondary viewports render terrain and an atmosphere-only pass, without clouds
or rings. KSA's `Program.RenderViewport` path still does not run the `ParticleSystem`,
`VolumetricExhaustRenderer`, ocean/cloud, part-glass or overall-bloom passes for secondary
viewports. Engine plumes and generic particles therefore do not appear in
Hot Pursuit feeds. These are KSA-owned passes with main-camera/target/resource bindings,
and Hot Pursuit does not inject them into secondary rendering; camera settings cannot
enable them safely. The feed remains the stock secondary path rather than a second copy
of the complete main renderer.

A selective prefix on `FixedController.OnFrame(IViewport,double)` replaces the stock fixed-camera
logic only for Hot Pursuit-owned viewports, immediately before `GameViewport` advances
`Camera.OnFrame`. It recomputes the exact
part-local mount pose every frame using the reference main camera's ego frame and
converts the resulting point to ECL. This keeps cameras attached through vehicle
motion, floating-origin changes, subpart transforms, and non-uniform part scaling.

## Architecture

`hot-pursuit.lib` contains the reusable `HotPursuitSubmod`, camera entries, vehicle
ray picker, pose math, and shared `HotPursuitPatches`. The standalone `hot-pursuit`
assembly supplies StarMap lifecycle, the F11 window, HotkeyGuard, and patch setup.
The same library is embedded in the unscience toolbox.

There is no persistence yet. Camera state lasts for the current game session.

## Live-test caveat

This implementation is compiled against the current KSA 5402 decompiled/API surface,
but viewport behavior is engine-owned and KSA is under active development. In a live
game, verify that at least one secondary slot is available, that the stock viewport
window continues updating while its target moves, that the nearby body no longer produces the
dark-grey artifact, and that closing/reopening the window releases and reacquires the lease as
expected. Engine plumes and generic particles remaining absent is expected on KSA 5402. A future
KSA change to viewport pooling, nearby-celestial selection, the fixed-controller call order, or
part raycast coordinate conventions may require corresponding updates.
