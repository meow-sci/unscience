# Iron Man mode lifecycle checks

Runs the actual production `IronManSubmod.cs` core, `IronManEvaSettings.cs` and `IronManFlightSettings.cs` against small
managed KSA/service fixtures. The mode implementation is not copied into these tests.

```sh
dotnet run --project iron-man-mode.tests
```

Covers queued physics handoff, per-kitten configuration and activation, opening the editor while
remaining in EVA mode, repeated complete EVA/rocket transitions with fresh saved settings,
disarmed/manual rocket entry, shutdown on EVA return, retained editor eligibility, missing-patch,
restoration of native EVA control mode and surviving control-part/connector selections,
ladder and editor guards, deferred-action lifetime checks, stale state pruning, cache invalidation,
worker joining, and teardown of an editor opened for a configured kitten in EVA mode.

The fixtures record calls to game services, engines and the physics queue; they do not simulate
the game or render ImGui. The companion `iron-man-flight.tests` exercises the actual Harmony
input, solver, HUD, editor orientation, control-frame and RCS-map patches. Native keyboard mode
behavior, mode-button appearance and walking/rocket flight still require an in-game acceptance pass.
