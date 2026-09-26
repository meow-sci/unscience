# KSA 5438/5482 managed upgrade checks

Run from the repository root:

```sh
dotnet run --project ksa-upgrade.tests/ksa-upgrade.tests.csproj
```

The executable is a small managed regression suite for the KSA 5438 and 5482 integration fixes.
For 5438 it links the production `PropellantFeedDiagnostics` helper, the production parts-now
schema rule and the Free Fallin patch. For 5482 it links `PartRenderFilter` and the Vehicle Paint
patches (sections below). The
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

## KSA 5482 render-data checks

`RenderDataFixture.cs` models only the 5482 part render-data members that the linked production code
reflects or patches: `PartTreeRenderData` with private nested `Batch`/`DynamicBatch`/`GlassBatch`
(`Model`, `Parts`, `Count`, `StateBitFlags`), the public `Compose`/`ComposeDynamic`/`ComposeGlass`
with the stock signature, `EnsureBuilt`, `InvalidateStates`, and the private
`WriteState`/`WriteDynamicState`. It also models `PartModel`, `PartModelDynamic` and `PartModelGlass`
per-viewport instance and dent lists, `Part.FullPart`, `IViewport` and `ViewportOptionFlags`.
Instance lists hold part tags and dent lists hold tag + 1000, so compaction order and alignment are
observable. Brutal numerics are minimal stand-ins; no decompiled game source is copied.

`RenderFilterChecks` links production `PartRenderFilter` and applies its real Harmony patches. It
covers:

- no owner leaves every instance rendering;
- two owners (blinky-style and shiny-style predicates) share one compaction in slot order;
- instances already queued for the same model by another vehicle are left untouched;
- sub-parts are judged by their full part;
- static and dynamic dent lists stay aligned;
- ranges with no hidden parts are unchanged, and glass compacts;
- non-raster submissions, misaligned dent lists and viewports without `RenderPartModels` are left
  unchanged (fail open);
- unregistering one owner keeps the other, and the last owner uninstalls the patches;
- a throwing predicate disables filtering instead of breaking composition.

`VehiclePaintChecks` links production `VehiclePaint` and `VehiclePaintPatches`. `VehiclePaintFixture.cs`
provides compile-only shader stubs: `ShaderModuleUtils`, `CompileOptions`, `Device` and a
`VehiclePaintShaders` stand-in whose `TryGetPatchedSource` returns null. No shader is compiled. The
checks cover:

- all four seams attach, including the private nested-batch writers bound as `object`;
- unpainted parts keep stock bits;
- per-part paint reaches static slots, and template paint reaches dynamic slots;
- an unchanged paint state never invalidates, and each change invalidates exactly once;
- clearing paint and uninstalling the shaders rewrite stock bits;
- a newly seen tree is built once;
- removal detaches every seam.

These fixtures exercise managed ordering, compaction and cache-invalidation logic only. They do not
run `PartTreeRenderData`'s real batching, GPU upload, shadow culling, raytraced IVA submission or
the patched GLSL. Hiding and painting in the main, portrait, shadow and IVA views still need an
in-game pass on 5482.

## Boundary

The test does not prove native resource graph construction, loader registration, renderer behavior
or in-game explosion playback. The upgrade workflow should pair it with a metadata-only check of
the supplied current KSA DLL's `ResourceManager`/`FlowOrder<Tank>` members and the live-game
acceptance checks documented in `scope/`.
