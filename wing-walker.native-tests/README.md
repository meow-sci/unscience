# Wing Walker native XML validation

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

`KSA_DLL_DIR` can select a different assembly directory; by default it loads
`../ksa-game-assemblies/current/dll/`. The runner reports the matching `current/version.json` build,
reflected KSA assembly identity, and file version. It invokes native `GameContentTags.Build`,
`ContentPrePass.Resolve`, and `ContentDeserializer.Deserialize`; checks the inherited part rows,
module values, enum values, connector normal via KSA math, and the six Harmony target methods plus
two injected fields; then rejects an unknown XML attribute as a strictness check. It also exercises
native `Habitat.ComputeFullLoad` and round-trips two distinct `IVASeatData` names inside actual
`PartInstance` components through KSA's XML overrides. It does not initialize a renderer or need GPU
access. The parser does not resolve asset references or load rows into the live Core library;
`IVASeat.GetSaveData` is not called because it requires the game's initialized crew roster.
