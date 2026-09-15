# KSA 5438 managed upgrade checks

Run from the repository root:

```sh
dotnet run --project ksa-upgrade.tests/ksa-upgrade.tests.csproj
```

The executable is a small managed regression suite for the KSA 5438 integration fixes. It links
the production `PropellantFeedDiagnostics` helper and the production parts-now schema rule. The
feed fixture supplies the managed `ResourceManager.ConsumptionOrder` surface and exercises the
production loop across null, zero-level, empty-level, later-level, reversed selected views and
same-stage selected spans. Those arrays represent the view already selected by KSA; native flow
graph construction and the filtering/reversal decisions are outside this executable's boundary.
The supplied KSA DLL is Windows x64 and cannot be loaded by the macOS ARM64 test runner, so the
fixture intentionally models only this managed order surface; it does not copy decompiled game
sources or construct a native-backed resource graph.

The V8 fixture gives the linked production rule real `XDocument` instances with line information.
It verifies that all seven pre-5438 unsupported definition kinds remain rejected, that direct
`Explosion` and `ExplosionVolume` definitions are rejected, and that same-named nested references
remain allowed. A minimal `ValidationContext` and parser surface keep the test independent of KSA
asset deserialization, registry startup, ImGui, Vulkan and native game initialization.

The Free Fallin checks link the production Harmony patch and run it against a no-op `Draw` method.
They cover distinct original handles, repeated override changes, a late-spawn canopy, external
material changes that must survive restoration, re-enable capture of a new original, repeated
restore, and unpatch cleanup. The render fixture has no GPU or native behavior; it exists only to
exercise the production capture/replacement/restore ownership logic.

The test does not prove native resource graph construction, loader registration, renderer behavior
or in-game explosion playback. The upgrade workflow should pair it with a metadata-only check of
the supplied current KSA DLL's `ResourceManager`/`FlowOrder<Tank>` members and the live-game
acceptance checks documented in `scope/`.
