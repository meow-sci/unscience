# Iron Man: full EVA / rocket flight modes

Implemented option 1 from the mode/teleport investigation. **No teleport patch is added.**

## State and UI

The `flight mode` label sits above two equal-width adjacent buttons: `eva mode` then `iron man`.
Their combined width fills the available content area, less native inter-button spacing; the
selected mode uses the theme's active button color. Switching is queued through `PhysicsFrameHook`
and blocked during a pending operation or an open editor. Native EVA is the default after loading.

Configured editor support and active rocket flight are separate session state:

- `IsConfigured`: editing or entering Iron Man explicitly registers the actual kitten reference.
  Root protection, avatar rendering, upright editor camera/pan, nodes and editor teardown use this.
- `IsEnabled`: the kitten is currently in Iron Man flight mode. Existing worker `IsKitten`, base
  input routing, HUD/button policy, rocket frame and geometric backpack RCS hooks retain this gate.
  Membership is published as immutable arrays for worker reads.

Choosing Edit in EVA configures attachment nodes and enters the same existing-vehicle editor,
without changing flight-computer settings or flight mode. Returning from the editor keeps the mode.
Returning to EVA retains configured editor support and all equipment. Disposed/old-system kittens
are pruned from both registries; neither mode nor configuration authorization is serialized.

## Transitions

At each EVA → Iron Man transition, capture the current EVA settings, clear held input, explicitly
shut down and disarm all engines, and select manual attitude, manual burn and direct thrust.
Ladder attachment must be released before entering rocket mode or opening the editor. Re-entering
Iron Man deliberately starts manual/disarmed again; rocket presets and ignition are not resumed.

Iron Man → EVA shuts down/disarms engines, restores the matching pre-entry EVA snapshot, removes
rocket membership and invalidates native RCS authority. `IronManEvaSettings` wraps the existing
ten-field flight-settings snapshot plus control part/connector and `KittenControlMode`.
Only a control part still belonging to the same part tree is passed to native `SetControlPart`;
the native method further validates modules/ports. `KittenEva.SetControlMode` preserves the game's
restriction against Direct control in CCF. No pose, velocity, mass or attachment transforms change.

## Native invariants

- [Vehicle.ClearHeldPlayerInput](../../../ksa-game-assemblies/current/decomp/KSA/Vehicle.cs) (5869)
  clears movement/sprint/grab and throttle-key flags but not EngineOn. `MainShutdown` is required.
- [KittenEva.SetControlMode/PrepareWorker](../../../ksa-game-assemblies/current/decomp/KSA/KittenEva.cs)
  (209–215, 321–330) applies native restrictions and refreshes camera/direct-control inputs.
- [PhysicsBubble.ResetMmuAttitudeTarget](../../../ksa-game-assemblies/current/decomp/KSA/PhysicsBubble.cs)
  (1719–1736) consumes worker `LocomotionState.MmuAttitudeApplied` and `PreMmuCaptured/RateLimit/RollMode`.
  Preserve that bookkeeping alongside the matching restored EVA settings. Clearing it independently
  could strand a captured transient camera-follow target. Native locomotion re-evaluates the current
  surface/water/attitude conditions when `IsKitten` returns true; it may legitimately update modes.
- `Vehicle.TeleportToLocation` remains stock: it places body +X upright and ignores Ctrl2Body.
  EVA mode restores native righting afterward, not an immediate upright teleport pose.

## Validation

`iron-man-mode.tests` links the actual production submod and snapshot classes to check the queued
handoff, repeated fresh EVA snapshots, shutdown/manual entry, editor availability, guards, mode
isolation, removed-control fallback and teardown. Existing Harmony tests verify the independent
configured-EVA editor orientation gate while flight frame/RCS revert. Full solution compilation,
connector tests and flight/HUD/orientation tests remain required. Native movement/MMU transitions,
the button layout, and an equipped kitten's physical behavior still require in-game acceptance.

Completed against 5402: full solution build with zero warnings/errors; connector, flight/HUD/
orientation and production mode-lifecycle suites all pass. Build deployment was directed to
`/private/tmp/iron-man-dist`.
