# Garry's Torch managed timing regression

Run from the repository root:

```sh
dotnet run --project garrys-torch.tests/garrys-torch.tests.csproj
```

The executable links the production `GarrysTorchPatches.cs` and uses real Harmony 2.4.2 to patch
and unpatch an already-executed managed frame-loop fixture. It needs the game-version BepuPhysics, BepuUtilities and Brutal.Core.Numerics assemblies
resolved through `KSAFolder`, but does not load KSA.dll or the native game runtime. The small fixture models bubble membership, staged actuator progress and result
application; it is not a physics simulation.

Checks cover the old after-UI result-loss regression, accumulating progress with the new hook,
one weld update per frame, result-before-weld-before-snapshot order, `PreviousTime` timestamps,
unchanged simulation steps, player-time interpolation pacing during pause/warp, absent system or
submod, exception isolation and unload. Transpiler checks reject missing, duplicate and reordered
seams and preserve branch/exception metadata. The intentional exception-isolation check prints
one `fixture weld failure` log before the final PASS line. Any failed assertion exits nonzero.

This validates the hook and timing contract, not the actual game's native physics, light rendering,
part-anchor math or native scale rendering. See the [library README](../garrys-torch.lib/README.md) for
the required in-game checks and [scope](../scope/vehicle-physics.md) for the 5402 source trace.

The caller transpiler now lives in `ksa-abstractions.lib/PhysicsFrameHook`; Garry's Torch registers
its weld callback. Queued Godzilla edits run before this callback. Source scale ownership is exclusive:
restore Godzilla before welding, or unweld before applying Godzilla. Managed checks also cover queued
mutation ordering, reentrant deferral, exception isolation and stale-system queue disposal.
The fixture's `JobSystems.NearestOrbitAndPerformanceWorker` records joins. Checks confirm that queued
mutations join the orbit readers once, after the step and before editing, and that a second
`JoinOrbitReaders()` in the same frame does not wait again.

## Harmony composition and actual game IL

`FrameHookInteropChecks` first reproduces the old default-priority installer's
`TargetInvocationException` with a real Harmony call-profiling transpiler. It then applies the
production installer with that profiler already installed at Normal and First priorities, and with
the profiler installed afterward. Checks cover exact world-load-before-step and
results-before-edit-before-weld-before-snapshot ordering, `PreviousTime`, duplicate application,
foreign removal, own removal/reapplication, three failed-install retries, retained foreign patches,
and full exception/owner/order diagnostics. A failed unpatch must retain the actual installed state
while discarding queued old-world work; a later successful removal recovers normally.

The frame fixture includes a `ReadOnlySpan<int>` local and loop branches. Run the same executable
with `--configuration Release` to exercise optimized managed IL, matching an important 5638 shape.
`PhysicsFrameAssemblyChecks` reads the real KSA.dll through `PEReader`; it never loads or executes
the Windows assembly. It requires the reflected caller/solver signatures and all seven unique,
ordered direct calls. KSA.dll is required; its folder resolves from runtime `KSA_DLL_DIR`, runtime
`KSAFolder`, then the `KSAFolder` recorded at build time. This also permits checking an older DLL
with the built executable. Current 5638 has a 703-byte caller with `ReadOnlySpan<Vehicle>` locals.

This proves the ordering defect and correction for compatible call wrappers. It does not establish
which installed mod caused a reported native startup failure, execute the actual KSA caller, or
replace the Windows weld/save/load acceptance checks.

An optional [Purr2D2 cross-mod harness](purr2d2-interop/README.md) compiles Purr's real complete
`GameHooks.cs` from an external source checkout alongside production Unscience frame/save hooks.
It checks actual install-order and direct-load reset interactions, including the historical
default-priority installer. The generic profiler fixture above remains useful for broader ordering
coverage; Purr specifically replaces the `ExecuteNextVehicleSolvers` anchor and leaves Apply* and
`GetJobSimStep` direct. The cross-harness is outside the solution and needs its explicit source path.

## Collision and preset regression

`CollisionChecks` links production `WeldCollisionPatches` and uses actual Bepu rigid bodies and a
static obstacle inside a small KSA wrapper fixture. Both collision entry points are warmed before
Harmony installation. Checks assert source contact suppression, unaffected other pairs, continued
simulation, exact shape restoration, immutable snapshots, collision opt-in, suspended/removed/stale
welds, restoration after exceptions and unload. `PresetChecks` uses production TOML persistence in
a temporary directory to verify absent `collisions` defaults false and true/false values round-trip.
These complement the existing actuator result-retention regression; they do not replace an in-game
check of animated light parts, scaled compound colliders, weld chains, terrain and scenery.

## Scale preservation regression

Scale application, animation and preset round-trip checks cover values below 0.05 and above 20;
those bounds belong only to the mouse-drag widgets, not scale validation.

`ScaleChecks` links production `WeldEngine.Scaling`, `WeldScaleSnapshot`, `WeldAnimation`,
`WeldAnimationManager`, `ReflectionHelpers` and `KittenScalePatches` against managed part/avatar
fixtures and real Brutal numerics. Checks cover custom XYZ full-part/SubPart baselines, identity
unweld without writes, nested inheritance with warmed caches, repeated noncumulative edits,
identity reset, animation midpoints/completion/queued reset, ongoing child animation, full-part
spacing, detached/added parts, fresh baselines after restore, invalid inputs and disposed sources.
A warmed-up kitten matrix method is patched with actual Harmony to check nondefault avatar scale,
XYZ correction and complete restoration; failed avatar capture can be retried. The fixtures do
not run the production UI or native KSA rendering/physics.

`SaveChecks` links production baseline capture/import and `SaveJson`. It recreates native-loaded
parts at their saved effective XYZ sizes, verifies three save/load cycles do not compound scale,
and checks Unweld returns to the original size. A nondefault kitten avatar baseline is also
serialized and restored. Only native part resolution is substituted by a small fixture.

`AnimationSaveChecks` also links production weld/light animation classes and managers. It compares
save-restored continuation with uninterrupted playback through active completion and queued-step
promotion, including nonzero easing and elapsed state. Native light writes use a small fixture.
