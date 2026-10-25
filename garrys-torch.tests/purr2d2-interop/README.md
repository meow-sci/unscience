# Purr2D2 / Unscience production-hook compatibility checks

Optional managed cross-mod harness, outside the solution. It compiles the **complete actual**
`src/Runtime/GameHooks.cs` from a supplied Purr2D2 checkout together with Unscience's production
`PhysicsFrameHook.cs` and `NativeSaveHooks.cs`. It neither copies the foreign transformation nor
changes/builds the supplied checkout. Harmony 2.4.2 installs both real hook sets on the managed host.

From the Unscience root, run against a Purr2D2 checkout containing the compatibility correction:

```sh
dotnet run --project garrys-torch.tests/purr2d2-interop/Purr2D2.Interop.csproj \
  --configuration Release -p:Purr2D2SourceDir=/absolute/path/to/purr2d2 \
  -p:UseSharedCompilation=false
```

To audit original Purr2D2 revision `08b74bc` and deliberately assert its two demonstrated defects:

```sh
dotnet run --project garrys-torch.tests/purr2d2-interop/Purr2D2.Interop.csproj \
  --configuration Release -p:Purr2D2SourceDir=/absolute/path/to/original/purr2d2 \
  -p:UseSharedCompilation=false -- --audit-original
```

The original-source mode requires the old vehicle-anchor installation failure and premature
direct-load reset to be reproduced; default mode requires those cases to work correctly. There is
no silent skip when source is unavailable. The source path is printed, and all outputs stay in this
test project. After a source change, rebuild; `--no-build` reuses the last compiled foreign source.

The real Purr transformation replaces four calls: vehicle wait, derived-data flush, resource flush
and vehicle dispatch. It keeps Apply* and GetJobSimStep direct. The original default-order Unscience
installer therefore rejects **ExecuteNextVehicleSolvers**, rather than the ApplyOrbitSolvers seam
hidden by the earlier generic profiling fixture. Both historical-default and current Unscience
installers are checked with Purr installed first and last. Current production hooks retain exact
callback/step/snapshot ordering, player delta, PreviousTime, diagnostic finally blocks, native
exceptions and single dispatch through removal and reapplication of each owner.

The harness warms Purr's actual `PrepareAndDispatchVehicles` wrapper before installing a separate
prefix on its solver target. This confirms ordinary managed dispatch through the production
wrapper. The fixture target is explicitly `NoInlining` to keep that seam observable. This does
**not** prove that prewarming cannot inline the actual native KSA target: CURRENT's method has
94 IL bytes and no inlining override, and no real KSA method is executed here.

Save checks use the production Unscience deferral/reset/replay hooks and Purr's real reset prefix.
Direct DeserializeSave must defer without cleanup, then reset once after joining workers, before
native reconstruction and new-world step computation. Rejected preflight data must leave both
setups intact. File-load deferral, stale mutation disposal and fail-closed Purr worker joins are
also checked. Only the data-validation policy is substituted by a small preflight accept/reject
fixture; actual file parsing, full coordinator/sidecar persistence, renderer/worker execution and
Purr runtime ownership are covered elsewhere or require native acceptance. The fixture's native
load does not emit `Program.OnGameLoaded`, whose separate Purr postfix may perform another reset.

The host, SaberRuntime, diagnostic sinks, input types and worker-hook installer are managed
fixtures. They record the effects of the real frame/save hooks; this harness does not validate
lightsabers, physics, audio, GPU resources, native JIT decisions or in-game welding.
