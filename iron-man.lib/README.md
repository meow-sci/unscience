# IronManLib

`IronManSubmod : ISubmod` supplies the opt-in EVA editor/rocket feature to Unscience and the thin
Iron Man development host. See [player controls](../iron-man/README.md) and the source-backed
[design research](../plans/iron-man/RESEARCH.md).

- `IronManSubmod` keeps activation by actual `KittenEva` reference, snapshots flight-computer modes,
  queues mutations through the shared `PhysicsFrameHook`, rejects ladder activation and restores
  modes on disable. UI code owns editor entry/exit, attachment forms and engine/RCS controls.
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

`Apply(Harmony)` / `Remove(Harmony)` use the caller's Harmony owner. Unscience supplies the shared
frame handoff through Garry's Torch's existing installation; the development host installs it
itself and applies mandatory `HotkeyGuard`. No persistent activation setting, global template
mutation, shader replacement, GPU allocation or direct force injection is introduced.

Typed APIs compile against current KSA. String-resolved private methods and behavioral ordering
remain runtime risks; the complete inventory and live acceptance checklist are in
[scope/iron-man.md](../scope/iron-man.md). Managed checks are separate projects so they can run
without native KSA graphics/physics initialization.
