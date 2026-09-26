# Dent Wizard Library

Reusable `ISubmod` implementation for the Unscience host and development `dent-wizard` host.
See [Dent Wizard usage](../dent-wizard/README.md) for controls, launch semantics and limitations.

- `DentWizardSubmod` owns source identity, form state, one-shot/automatic click mode, one pending shot and the transient
  `ISaveParticipant` (`dent-wizard`, v1). It subscribes/unsubscribes `PhysicsFrameHook.BeforePhysics`.
- `DentWizardSubmod.Ui` renders the standard padded/filterable form and floating firing gesture. Automatic mode retains arming across clicks; toggle-off,
  Cancel/Esc/right-click, editor entry, source loss, scene reset and disposal cancel it.
  Source entries and the combo preview read live `Vehicle.TotalMass` in kg; widget IDs stay
  stable while mass changes. No mass value is stored in the sidecar.
- `DentWizardPicker` mirrors Graffiti's part-mesh/EVA-sphere/terrain approach, excludes the source,
  and produces a click snapshot within 10 km. Terrain competes with vessel hits by distance.
- `LaunchRequest` validates live identities and unchanged target parent at execution, converts
  CCI/CCF click offsets at `SimStep.PreviousTime`, creates an orbit and calls `Vehicle.Teleport`.
- Public `LaunchMath.Velocity` implements target-relative launch velocity and finite-value checks;
  `IsValidSpeed` enforces only the finite 0.001 m/s minimum, not the UI drag interval.

The host must apply the shared `PhysicsFrameHook` and `HotkeyGuard`. Unscience already owns the
shared hook for saves/welds, and explicitly ensures it for Dent Wizard too. The feature does not
remove a host-owned hook on disposal. No new Harmony targets or string reflection are introduced.
All game operations run on the main thread at the existing handoff; no worker tasks are created.
`FirePending` calls `PhysicsFrameHook.JoinOrbitReaders()` before executing a shot. KSA 5482 queues the
nearest-orbit job just before the handoff, and the launch teleport disposes orbit points that the
job may be reading. Frames with no pending shot do not join.

Managed regression fixtures link production launch code against the game-shipped numeric types.
Build and tests do not verify native terrain picking, rendering, collision damage or EVA flight.
