# Kiwi's Marbles library

Reusable celestial-weld engine and Unscience submod. See [usage](../kiwis-marbles/README.md)
for target frames, offsets and per-step orbit control.

## Scene saves

Unscience persists source/target identities, celestial-versus-vehicle target type and offsets,
plus the original parent, orbital state vectors and epoch needed by Unweld. Native KSA saves
vehicles but does not capture celestial orbit edits. Scene reset restores original orbits before
native load reuses bodies; replay rebinds objects, validates cycles and parent identities, sorts
welds and applies them while the native solvers are quiescent. Missing targets remain reported
failures rather than being substituted. Existing solver patches continue the restored welds.
Native cross-parent/body-subtree and load/unload acceptance remains required.

### Star targets (KSA 5638)

`OrbitingStar` is a `StellarBody`, not a `Celestial`, but it is an `IOrbiter`, so the Target picker
offers it. Replay resolves a non-vehicle target by exact id from the same candidates: every
`IOrbiter` in the current system except vehicles. An absent or ambiguous id is reported, never
substituted. Before this fix, replay searched only `Celestial`s, so star-target welds were dropped on
load. The `kiwis-marbles` version-1 payload is unchanged: a star target is an ordinary non-vehicle id
(`TargetIsVehicle = false`). Original parents may be stars or barycenters, which are `IParentBody`.

The original orbit is rebuilt from saved state vectors with `Orbit.CreateFromStateCci`, which uses the
parent's own gravity. KSA 5638 orbits can also carry a central-mass override (`CentralMass`,
`TwoBody`, `Barycentric`). In stock Content only stars use one, and stars cannot be weld sources. A
modded planet or moon with such an orbit would, after a save and load, be unwelded onto a plain
parent-gravity orbit. Within a session, Unweld restores the captured orbit object exactly.
