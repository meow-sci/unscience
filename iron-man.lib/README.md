# IronManLib

`IronManSubmod : ISubmod` supplies the opt-in EVA editor/rocket feature to Unscience and the thin
Iron Man development host. See [player controls](../iron-man/README.md) and the source-backed
[design research](../plans/iron-man/RESEARCH.md).

- `IronManSubmod` separates configured editor support from active rocket mode by actual `KittenEva`
  reference. Full-width EVA/Iron Man buttons queue transitions through `PhysicsFrameHook`; both
  modes can edit equipment. `IronManEvaSettings` restores the pre-entry flight-computer settings,
  valid control part/port and native View/Direct preference. Each rocket entry is manual/disarmed.
  Existing MMU worker bookkeeping is preserved alongside its matching EVA settings snapshot.
- `IronManPatches` coordinates transactional installation. An installation failure disables the
  feature rather than leaving the UI capable of activating a partial integration.
- `IronManConnectors` owns private connector templates per instance. It maintains authored
  transforms when stock scaling refreshes connectors; attached nodes cannot be edited/deleted.
- `IronManConnectorData` stores bounded versioned JSON/base64 in serialized `PartInstance.Id`.
  `IronManConnectorPatches` restores runtime Id and connectors in the four-argument Part constructor
  before native index regeneration. Ordinary parts have no metadata or changed construction.
- `IronManConnectorUnload` finds weakly retained authored roots, including inactive loaded roots
  and editor copies, and converts attachment endpoints before removing hooks. Failed conversion
  retains passive hooks instead of creating invalid saved indices or dropping fuel capabilities.
- `IronManEditorPatches` adds the existing character renderable at the editor's character-bucket
  seam, protects the body/root and preserves blueprint Character metadata for authored live EVA.
- `IronManFlightPatches` changes only opted-in worker snapshots' `IsKitten` classification and
  routes keyboard/actions through nonvirtual base Vehicle implementations. Stock modules calculate
  mass, fuel, forces, RCS and collision responses. Character simulation is restored on disable.
  Its key prefix runs at `Priority.First`, so active rocket mode owns vessel input before optional
  EVA actions such as Purr2D2's Space somersault. Inactive kittens pass through to those actions.
  Purr2D2's matching worker guard releases a queued/active somersault when native preparation
  selects `IsKitten = false`, preserving the vessel mode's prepared physics and inputs.
- `IronManFlightComputerPatches` changes only enabled kittens' native EVA/Vehicle canvas
  classification and both gauge button policy call sites. A nonvirtual base-policy call
  preserves stock target/burn/engine restrictions and avoids patching shared generic JIT code.
  `IronManFlightSettings` restores mode/frame/target/roll/tuning without rewinding burn progress
  or solver telemetry. The panel displays native per-axis control-system assignments. Composition
  with other mods patching the closed generic base policy is unsupported; see the
  [follow-up research](../plans/iron-man/FLIGHT_COMPUTER.md).
- `IronManRenderPatches` submits authored equipment before the normal part-batch upload while
  leaving the avatar in its original later phase. Rendering remains enabled for authored parts
  after flight activation is switched off.
- `IronManTeleportPatches` replaces only the surface-placement helper call inside
  `Vehicle.TeleportToLocation`. Active Iron Man kittens use headward proxy bounds for native terrain/
  launchpad clearance, then convert orientation/rates back into unchanged body coordinates.
  EVA mode, ordinary vessels, general teleports and other placement-helper callers remain stock.
  See [surface teleport](../plans/iron-man/SURFACE_TELEPORT.md).
- `IronManControlFramePatches` supplies headward default rocket controls through `Vehicle.Helm2Body`
  (named `Ctrl2Body` before KSA 5638); explicit control parts/ports keep their native frame. The
  worker flight computer derives its thrust-aligned and active control frames from that helm.
  `IronManNavballPatches` corrects the navball thrust marker for enabled kittens only: stock
  divides by a main-thread `Helm2Asmb` that `FlightComputer.CopyFrom` never copies, so it would
  be drawn about 90 degrees off the rocket nose. A missing target logs and keeps the stock marker.
  `IronManEditorOrientationPatches` changes the orbit camera basis and vertical pan/bounds for the
  complete editor scene, preserving geometry.
  `IronManRcsOrientationPatches` lets native geometry remap enabled root-backpack jets without
  changing authored maps. Membership is published as immutable snapshots for worker reads;
  mode changes invalidate thruster authority at the joined handoff with the all-zero global state,
  which always trips KSA 5638's control-frame change gate (`Zero` caches Identity and may not).
  Editor hooks use `IsConfigured`; physics/input/HUD/control-frame/navball/RCS hooks use `IsEnabled`
  (active Iron Man mode).
  See [orientation](../plans/iron-man/ORIENTATION.md) and [mode transitions](../plans/iron-man/FLIGHT_MODES.md).

`Apply(Harmony)` / `Remove(Harmony)` use the caller's Harmony owner. Unscience supplies the shared
frame handoff through Garry's Torch's existing installation; the development host installs it
itself and applies mandatory `HotkeyGuard`. No persistent activation setting, global template
mutation, shader replacement, GPU allocation or direct force injection is introduced.

Typed APIs compile against current KSA. String-resolved private methods and behavioral ordering
remain runtime risks; the complete inventory and live acceptance checklist are in
[scope/iron-man.md](../scope/iron-man.md). Managed checks are separate projects so they can run
without native KSA graphics/physics initialization.

## Scene saves

Saves configured kittens and active EVA/Iron Man mode by native vehicle ID, with original EVA preferences and current flight-computer settings. Restores active mode with engines disarmed. Native part reconstruction retains authored connector metadata and equipment; the adapter never spawns replacement kittens. Baseline control-part references use durable part paths.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).

Saved original EVA control part/connector references are validated against the loaded kitten
before enabling its restored mode. An unavailable reference produces a retained recovery record
instead of silently losing the original Disable baseline.

Input ownership and prepared-worker classification are runtime behavior rebuilt from the restored
mode. The Purr2D2 compatibility fixes change no scene-save ID, version, payload or replay policy.
