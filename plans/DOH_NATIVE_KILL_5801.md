# DOH native kill action (5801)

## Evidence

The complete two-kitten Windows session reports an `IndexOutOfRangeException` in
`SkeletalAnimClip.Sample → BoneAnimRuntime.Update → KittenEva.UpdateRenderData` after one DOH
despawn. The shutdown hook then disposes remaining submods before native renderer teardown.
This identifies the earlier animation/render failure; the subsequent shutdown logs are cleanup.

CURRENT is KSA **2026.11.10.5801**, matching the log. Native source and DLL metadata show:

- `Program.PrepareFrame` applies vehicle results, then `InputEvents.ApplyInputEvents`, then
  `RefreshVehiclesInFrame`. The old DOH `PhysicsFrameHook` callback runs later, at `GetJobSimStep`.
- `Program.RenderViewport` iterates `VehiclesInFrame`. Deleting an avatar at the old handoff can
  leave it in that frame's render list after `Vehicle.Dispose` frees its animation buffers.
  This is consistent with the supplied stack; native reproduction of the fix remains pending.
- Native `InputEvents.VehicleDestroyData.Apply` with `Recovered = false` calls
  `Universe.DestroyVehicle(vehicle, CrewDisposition.Kill, KittenDeathCause.Abandoned)`.
  Normal lethal radiation/life-support EVA deaths use the same `Universe.DestroyVehicle` teardown.
- Native kill inputs accept no reason string. `KittenDeathCause` is a fixed enum; "smote" would
  need custom patching or a non-native persisted value, so neither is introduced.

## Implementation

Removed the custom live-kitten despawn implementation, synchronous APIs, deferred physics closures,
pre-death material unsetting, and unload-time vehicle destruction. **Smite / Smite All** submit
native destroy inputs holding exact current kitten objects. Pending native requests are deduplicated.
The standalone development host no longer installs a physics hook just for DOH removal.

`PruneDisposed` only removes tracking and calls central War Paint cleanup after native disposal.
Private material slots retire per kitten; another kitten using the same saved preset is independent.
Reusable preset files remain even when their last user dies. War Paint already prunes any native-dead
target centrally, so its service and preset schema need no change. DOH unload clears tracking and
leaves vehicle destruction to KSA. Failed-constructor rollback remains isolated in Construction.cs:
an incomplete avatar must never be left registered for rendering; this is not a live-kitten action.

## Saves and validation

`doh` v1/order60 and `war-paint` v1/order85 stay unchanged. Kill requests are transient native
input, contain object references rather than name lookups, and are absent from scene payloads.
Save before dispatch retains the kitten; capture after native death omits it even before pruning.
Native scene replacement owns old-object destruction; pending input cannot retarget a newly
reconstructed same-name kitten. DOH reset and legacy-paint migration keep their existing contracts.

- `doh.tests`: **76 managed checks**, including native input/no early disposal, deduplication,
  two independent private sets using one preset, post-death cleanup, reusable preset retention,
  pre/post-dispatch save capture, stale-world identity and failed-constructor rollback.
- `ksa-upgrade.tests`: real CURRENT DLL call order and native input field/shared teardown checks,
  alongside existing production-Harmony shutdown/reflection/shader checks.
- Whole solution builds against explicit CURRENT DLLs with temporary deployment output: **0 warnings/errors**.
  Used `--no-restore -m:1` with the existing restored dependency assets.
  `war-paint.tests` passes all **107** ownership/preset/scene checks.

## Native acceptance

- Spawn two kittens using one paint preset; Smite one and keep the survivor visible/animated.
- Repeat with the controlled/followed kitten, stock appearance and Smite All. Verify cameras/control
  hand off through native KSA and private GPU slots recover without affecting the survivor.
- Save before/after Smite, reload, and verify dead kittens and assignments stay absent. Reload a
  same-name saved kitten around a queued action and confirm it survives.
- Reuse the preset after all assigned kittens die; exit normally and confirm clean shutdown.

Managed checks do not execute Windows KSA, Vulkan or the native animation renderer.
