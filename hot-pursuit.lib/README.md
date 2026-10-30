# Hot Pursuit Library

Reusable implementation for the Hot Pursuit KSA mod. `HotPursuitSubmod` implements
`MeowSci.KsaAbstractions.ISubmod`, so the same camera manager and ImGui panel can run in the
standalone `hot-pursuit` StarMap host or inside the `unscience` umbrella mod.

The library owns camera entries and `IViewportOwner` lease tokens, performs mesh-precise vehicle
part picking, re-resolves targets by vehicle ID and `Part.InstanceId`, and recomposes each camera's
part-relative pose from `FixedController.OnFrame`. It uses KSA's stock secondary viewport renderer
and window; it does not allocate Vulkan resources.

After writing each mounted ECL position, `HotPursuitCelestialState` mirrors KSA's
main-camera nearby-celestial selection and public distance/terrain/altitude fields, including
the 80,000 km surface-distance cutoff. Up to 5482 this kept the nearby body out of the
distant-sphere pass (the dark-grey secondary-view artifact). Since 5638 stock resolves
`Camera.GetEffectiveNearbyCelestial()` (`NearbyCelestial ?? FindNearbyCelestial`), which picks the
same body. The mirror keeps raw `NearbyCelestial` readers, such as the airless-body near/far flag,
consistent with that pick. Stock never clears a secondary camera's `NearbyCelestial`, so
`HotPursuitCelestialState.Clear` runs whenever an entry releases or loses its lease. A stale body
would otherwise override the next owner's lookup and render the wrong planet.

Since 5541/5638 the secondary `Program.RenderViewport` path also renders terrain and an
atmosphere pass without rings, reusing the main viewport's shadow maps. Since 5707 that pass also
renders clouds, without cloud self-shadowing, ground cloud shadows or plume/explosion volumes. Each
secondary slot owns full-resolution cloud targets of about 34 B/px, roughly 143 MB at the 2048×2048
maximum; they are allocated per slot, not per lease. It still omits the `ParticleSystem`,
`VolumetricExhaustRenderer`, ocean, part-glass and overall-bloom passes. Engine plumes and generic particles are therefore not available in these feeds; the game-owned
passes bind main-camera targets/resources and are intentionally not re-injected by this library.

See [`../hot-pursuit/README.md`](../hot-pursuit/README.md) for features, user controls, limitations,
and the four-slot shared-pool constraint. Game integration details and update risks are cataloged in
[`../scope/camera.md`](../scope/camera.md).

## Native save persistence

KSA saves retain exact part-tree targets, mount point/normal/tangent, translation/rotation,
FOV, resolution, visibility and whether a secondary viewport was open. Load releases old
leases and cancels placement before native objects are replaced, resolves fresh parts,
and reclaims requested slots. A closed camera stays closed. Slot exhaustion preserves
the mounted camera and reports that its viewport must be reopened; other owners' slots
are never displaced. Missing targets produce a recoverable Saves warning. Runtime
instance IDs and viewport handles are never stored.
