# DOH despawn / late shutdown follow-up (5801)

The DOH removal implementation below is **superseded** by
[the native kill-action follow-up](DOH_NATIVE_KILL_5801.md). The complete two-kitten log identifies
an earlier animation exception; the old handoff deleted after render-list refresh. Shutdown,
Graffiti and default-offset changes below remain applicable.

## Evidence and limits

The native playtest spawned eight individually tinted kittens under Earth's surface. Each set
cloned nine GPU materials. All eight material-release and Despawned messages were logged before
`Saved pipeline cache`, StarMap/purrTTY unload, then an access violation in:

`GraffitiSubmod.Dispose → WaitIdle → Queue.WaitIdle → VkDeviceExtensions.WaitIdleInternal`.

No earlier exception or device-lost message appears in the supplied extended log. This establishes
a fatal **shutdown cleanup** failure; it does not establish why the game first entered shutdown or
prove that the material release itself corrupted GPU memory. The BlankMaterial tinted-fallback
message is not an allocation failure; the log reports successful cloning and release.

Native-source evidence (supplied CURRENT build 2026.11.10.5801):

- `Core/KSADeviceContextEx.Dispose` saves the pipeline cache, then destroys allocator/device/instance.
  `Brutal.VulkanApi/Device.Dispose` frees the native dispatch table. The observed queue access after
  that cache message and loader disposal is consistent with a stale native device/dispatch table.
  `KSA.Program.Dispose` already tears down rendering, ImGui and its renderer before late loader unload.
- `KSA.PhysicsBubble.ApplyResultsToVehicles` still dereferences each completed worker's ReadOnlyVehicle.
  Waiting for VehicleSolver only joins work; it does **not** apply those results or clear render events.
  DOH previously destroyed kittens directly from its UI after that worker was dispatched.
- `KittenRenderable.Dispose → CharacterAvatar.Dispose → AnimatedRenderable.Dispose` now unregisters
  meshes and frees native animation/skeleton lists. UI-time removal therefore crosses both pending
  solver and per-frame render ownership. Despawn belongs at the existing mutation handoff.

## Changes

1. DOH UI uses `RequestDespawn` / `RequestDespawnAll`: queue at PhysicsFrameHook, after native result
   application and before new snapshots/render preparation. Requests retain the exact registry entry;
   reset/rebind or a prior request invalidates them. A missing hook refuses the operation, with a log/UI
   message, instead of performing unsafe synchronous destruction. Existing synchronous `Despawn` remains
   for handoff and shutdown callers. The legacy DOH host also installs/removes PhysicsFrameHook.
2. `GameShutdownPatches` prefixes native `KSA.Program.Dispose`, joins workers, waits for the live device,
   then calls the existing Unscience unload before KSA frees resources. The callback is consumed once;
   Mod.Unload is guarded against repeat/re-entrant/late calls. The native body still runs. The host's
   Planet.Render.Core/Brutal.Vulkan.Abstractions references are compile-only, like its other game references.
3. Graffiti cleanup lives in `GraffitiSubmod.Lifetime.cs`. It only obtains/waits on the renderer when
   it owns a pipeline or live/retired images; unused/repeated cleanup touches no Vulkan API. Textures
   uploaded without a pipeline still retire. Native-save reset uses this same ownership-aware path.
4. User-requested DOH default offset is **(1 m, 1 m, −2 m)** in the existing vehicle body frame.
   Public SpawnRequest, fresh UI, vanilla reset and a missing saved Offset use the same default.
   Explicit saved offsets remain unchanged. No terrain clamping or reinterpretation of axes is added.

## Persistence

No record IDs/versions or ownership payloads change. Pending despawns are transient user actions,
not durable scene configuration: load/reset replaces the registry, making the old closures inert.
Global handoff removal clears queued actions. The native-restored kitten with the same ID is protected.
A save made before the queued removal commits retains that kitten; a later save omits it normally.
Graphics ownership and the shutdown callback are runtime-only. Graffiti's persisted decals are unchanged;
scene reset still releases prior-world GPU ownership and replays the saved recipes.

## Managed validation

- Whole-solution build against CURRENT DLLs, redirected temporary distribution: **0 warnings/errors**.
- `doh.tests`: **69 checks**, including queued removal of eight uniquely tinted kittens, native impact
  destruction before dispatch, pending-results ordering, stale same-name registrations, unavailable
  hook refusal, default/reset/legacy offset behavior and explicit saved-offset round trips.
- `ksa-upgrade.tests`: production-Harmony Program.Dispose prefix checked against the real KSA.dll
  signature; workers/device idle → feature pipeline/images release → native disposal ordering;
  callback removes its own patch, late repeated cleanup, unused Graffiti, texture-only ownership,
  callback failure and removal. Existing constructor/reflection/shader checks remain enabled.
- All **18 managed suites pass**, and the distribution validator confirms one Unscience mod with
  31 feature assemblies and no proprietary game DLLs. `git diff --check` passes.

These fixtures execute production teardown/queue/serializer paths against native-independent mirrors.
They do not reproduce a Windows access violation, run Vulkan or prove the missing original exit cause.
Native acceptance remains required: repeat the eight-kitten below-ground Despawn All sequence, then
ordinary exit; repeat with Graffiti unused, with image-only/failed pipeline setup, and with active
placed decals. Verify `Releasing submods before native renderer disposal` appears before
`Saved pipeline cache`, late StarMap unload produces no second resource cleanup, and a scene load
between request and dispatch leaves the newly restored kittens untouched. Check the new default
and preservation of an explicitly saved offset. Preserve any earlier exception if shutdown still occurs.
