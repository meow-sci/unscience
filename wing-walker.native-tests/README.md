# Wing Walker native XML validation

This harness also validates the bundled [Beverage Cooler](../beverage-cooler/README.md) assets.

Run from the unscience repository root on an x64 .NET host:

```sh
dotnet restore wing-walker.native-tests -p:NuGetAudit=false --ignore-failed-sources
dotnet run --no-restore --project wing-walker.native-tests
```

On ARM64, the Windows KSA reference `KSA.dll` is x64 and CoreCLR cannot load it directly. The
explicit `--allow-il-header-retarget` option accepts only an IL-only PE with no managed native
header, creates a temp copy, and changes only its two-byte PE machine field to ARM64. It never
changes the source DLL. Supply any managed dependencies omitted from `current/dll/` through
`KSA_EXTRA_DLL_DIRS` (use a path-separated list; only exact assembly versions are loaded):

```sh
KSA_EXTRA_DLL_DIRS=/path/to/matching-managed-dependencies \
  dotnet run --no-restore --project wing-walker.native-tests -- --allow-il-header-retarget
```

The `PartModel.Get` regression also needs the game's matching `System.IO.Hashing` package assembly
through `KSA_EXTRA_DLL_DIRS`. Both test load contexts allow that explicit package name while keeping
framework `System.*` assemblies on normal runtime resolution; exact package-version checks remain.

`KSA_DLL_DIR` can select a different assembly directory; by default it loads
`../ksa-game-assemblies/current/dll/`. The runner reports the matching `current/version.json` build,
reflected KSA assembly identity, and file version. It invokes native `GameContentTags.Build`,
`ContentPrePass.Resolve`, and `ContentDeserializer.Deserialize`; checks inherited part rows, module
values, enum values, connector normal via KSA math, and the eight Harmony target methods plus five
injected fields. It verifies the appearance patch's typed public surfaces, including animation
selection, helmet visibility, avatar attachments, and ear state, then rejects an unknown XML
attribute as a strictness check. A synthetic one-bone `Skeleton` and `IAnimation` run against the
native `BoneAnimRuntime` reproduces the constructor-selected seated clip's zero-delta crossfade and
confirms a zero-blend restart applies the standing pose immediately. The executable also exercises native `Habitat.ComputeFullLoad`
and round-trips two distinct `IVASeatData` names inside actual `PartInstance` components through
KSA's XML overrides. It does not initialize a renderer or need GPU access. The parser does not
resolve asset references or load rows into the live Core library. It calls the empty-seat branch of
`IVASeat.GetSaveData` to verify an unassigned seat emits no stale crew name or module record.
Occupied-seat `GetSaveData` and native unassignment require the game's initialized crew roster.
The editor checks verify the exact `VehicleEditor.OnMouseButton` and private
`PartCloner.Gather(Part,uint)` signatures, selection fields and native `Vehicle.SetSeatCrew` API.

`CoolerNativeChecks` invokes the actual native cosmetic variant-family validator before strict
deserialization. It checks inherited gameplay, stable Body/Lid/LidCollider slots, the shared
mechanical lid, native Actuate module, the 30 L capacity (12 meals+2 CO₂ canisters), and negative
variant metadata, mechanical-subpart and Habitat storage-capacity drift cases. The actual native GLB loader binds both lid slots
and samples the closed, halfway and 100° open transforms, with a fixed hinge. Native PartInstance XML overrides round-trip both variant IDs,
different depleted stocks, fractional consumption and partial opening/closing lid state. No live
renderer, habitat-zone reconstruction, whole-world load or physics/contact acceptance is claimed.

`CoolerRenderCacheChecks` calls the real `PartModel.Get` without GPU initialization. A negative
fixture reproduces the old repeated `Shell`/`Trim` keys: the native global cache returns red-body
geometry for the lid and steel variant. The decoded fixed XML must produce eight distinct models,
each retaining its exact requested template and expected body/lid mesh and red/steel/common
material. Repeated lookups must return the same correct model. The fixture snapshots and restores
the native first-model dictionary and both instance lists in `finally`, disposes only its own
models, and verifies that no test entries remain. GPU rendering still requires an in-game retest.
