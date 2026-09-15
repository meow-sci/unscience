# Dent Wizard regression checks

Run `dotnet run --project dent-wizard.tests` from the repository root.

This managed executable links production `LaunchMath`, `LaunchRequest` and `DentWizardSubmod` against small KSA
boundary fixtures and the game-shipped `Brutal.Core.Numerics`/`BepuUtilities` assemblies. It checks:

- manual speed limits, nonfinite/zero direction rejection;
- a perpendicular orbital intercept and invariance under a shared velocity boost;
- target hit-point spin velocity;
- deferred click geometry, committed state timestamp, cross-parent launch and preserved source spin;
- native teleport rejection, disposed/reparented targets, editor use and stale world identities;
- terrain CCF-to-CCI conversion and surface rotation;
- automatic re-fire across multiple clicks, miss/failure retention, no timer-based shots,
  pending-shot preservation, toggle-off cancellation and automatic-mode cleanup;
- one-shot dispatch, repeated initialization, scene-reset cancellation and unload cleanup;
- the production Kitchen Sink G-load registry retains exact source protection across launch,
  rejection and Dent Wizard reset/unload without protecting an unregistered target.

Fixtures verify our boundary calls and arithmetic, not KSA's native flight-plan solver, physics
handoff implementation, ImGui interaction or collision behavior. The shared handoff has separate
production-hook fixtures in `garrys-torch.tests` and `saves.tests`.
