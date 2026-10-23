# KSA 5438/5482 managed upgrade checks

Run from the repository root:

```sh
dotnet run --project ksa-upgrade.tests/ksa-upgrade.tests.csproj
```

The executable is a small managed regression suite for the KSA 5438 and 5482 integration fixes.
For 5438 it links the production `PropellantFeedDiagnostics` helper and the Free Fallin patch. For
5482 it links `PartRenderFilter` and the Vehicle Paint patches (sections below). The feed fixture
(`PropellantFeedFixture.cs`) supplies the managed `ResourceManager.ConsumptionOrder` surface and
exercises the production loop across null, zero-level, empty-level, later-level, reversed selected views and
same-stage selected spans. Those arrays represent the view already selected by KSA; native flow
graph construction and the filtering/reversal decisions are outside this executable's boundary.
The supplied KSA DLL is Windows x64 and cannot be loaded by the macOS ARM64 test runner, so the
fixture intentionally models only this managed order surface; it does not copy decompiled game
sources or construct a native-backed resource graph.

The parts-now bundle-validator (V8) checks were removed together with the parts-now mod at 5638.

The Free Fallin checks link the production Harmony patch and run it against a no-op `Draw` method.
They cover distinct original handles, repeated override changes, a late-spawn canopy, external
material changes that must survive restoration, re-enable capture of a new original, repeated
restore, and unpatch cleanup. The render fixture has no GPU or native behavior; it exists only to
exercise the production capture/replacement/restore ownership logic.

`FreeFallinShaderChecks` links production `free-fallin.lib/CanopyShaderSource.cs` (no game types). It
parses `SET_TEXTURE`/`SET_MATERIAL` from the verbatim 5638 `ModelPbr.frag` header (5, 1) and the 5482
ordering (1, 2), and requires fail-closed errors for missing, commented-out, conflicting, non-numeric,
shared or set-0 defines, a disagreeing partner fragment, a skinned vertex shader that already declares
sets or uses varying location 3, and missing anchors. When `KSAFolder` points beside a KSA `Content`
tree it also derives the sets from, and patches, the live `Model.vert`, `Model_Skinned.vert` and
`ModelPbr.frag`; otherwise it prints a SKIP line.

## KSA 5482 render-data checks

`RenderDataFixture.cs` models only the 5482 part render-data members (with the 5638 gates) that the
linked production code reflects, patches or calls: `PartTreeRenderData` with private nested `Batch`/`DynamicBatch`/`GlassBatch`
(`Model`, `Parts`, `Count`, `StateBitFlags`), the public `Compose`/`ComposeDynamic`/`ComposeGlass`
with the stock signature, `EnsureBuilt`, `InvalidateStates`, and the private
`WriteState`/`WriteDynamicState`. It also models `PartModel`, `PartModelDynamic` and `PartModelGlass`
per-viewport instance and dent lists, `Part.FullPart`, `IViewport` (`State`, `OptionFlags`, `Mode`),
`ViewportOptionFlags`, `ViewportStateFlags` and the 5638 `ViewportEx.RendersInterior()` interior gate
(IVA mode or `InsideVehicle`), which `Compose` applies to internal models.
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
- internal models stay hidden outside interior views, and inside-vehicle (crew portrait) and IVA views
  compose and compact them with aligned dents;
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

## Capsule glass experiment checks (5541)

`CapsuleGlassChecks` links production `CapsuleGlassExperiment`, `IvaForceRender` and
`PartRenderFilter`. Twenty-four checks apply actual Harmony constructor/compose patches against
managed fixtures: exact model IDs despite renamed part instances, both windows on multiple
capsules, sibling hull/similar ID isolation, native glass retained, instance/dent alignment,
independent IVA owner orders and preference changes, late-model capture, shadow-proxy isolation,
coexistence with full-part filter owners and actual unpatch restoration. Since 5638 they also cover
the editor reveal/restore (including an inside-vehicle editor view that keeps the native gate) and a
crew-portrait (`InsideVehicle`) view matching the IVA result, plus the native cabin there with the
experiment off. The fixture models native interior raster gates but has no Vulkan or ray tracing
renderer. Native window
alignment, depth, tint, exterior lighting and live save/load acceptance remain pending.

## Boundary

The test does not prove native resource graph construction or renderer behavior. The upgrade
workflow should pair it with a metadata-only check of the supplied current KSA DLL's `ResourceManager`/`FlowOrder<Tank>` members and the live-game
acceptance checks documented in `scope/`.
