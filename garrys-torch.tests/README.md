# Garry's Torch managed timing regression

Run from the repository root:

```sh
dotnet run --project garrys-torch.tests/garrys-torch.tests.csproj
```

The executable links the production `GarrysTorchPatches.cs` and uses real Harmony 2.4.2 to patch
and unpatch an already-executed managed frame-loop fixture. It needs no KSA assemblies or native
game runtime. The small fixture models bubble membership, staged actuator progress and result
application; it is not a physics simulation.

Checks cover the old after-UI result-loss regression, accumulating progress with the new hook,
one weld update per frame, result-before-weld-before-snapshot order, `PreviousTime` timestamps,
unchanged simulation steps, player-time interpolation pacing during pause/warp, absent system or
submod, exception isolation and unload. Transpiler checks reject missing, duplicate and reordered
seams and preserve branch/exception metadata. The intentional exception-isolation check prints
one `fixture weld failure` log before the final PASS line. Any failed assertion exits nonzero.

This validates the hook and timing contract, not the actual game's native physics, light rendering,
part-anchor math or scale behavior. See the [library README](../garrys-torch.lib/README.md) for
the required in-game checks and [scope](../scope/vehicle-physics.md) for the 5402 source trace.
