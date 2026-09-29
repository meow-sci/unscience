# the-tick.lib

Reusable core of [The Tick](../the-tick/README.md): per-vessel indestructibility for KSA.

- `TheTickSubmod` (`ISubmod`, `ISaveParticipantSource`): panel lifecycle, pruning, filtered picker,
  add buttons, active table, and the version-1 `the-tick` scene record (protected vehicle IDs, order 20).
- `TickProtection`: public concurrent registry keyed by exact `Vehicle` identity, safe for physics
  workers reading while the UI mutates. `UnbreakableTolerancePascals` is `double.MaxValue`.
- `TheTickPatches.Apply/Remove(Harmony)`: guarded installation of a prefix on the
  `Part.CrashTolerancePascals` getter and a prefix/postfix pair on the private static
  `PhysicsBubble.DetectStructuralFailure(VehicleUpdateState)`. Signatures are validated and a partial
  failure rolls back. `Remove` clears the registry so unload restores native behaviour immediately.

Other mods can call `TickProtection.Add/Remove` to make a vessel unbreakable while the patches are
applied by a host. Shared part templates are never mutated.
