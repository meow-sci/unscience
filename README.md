# ksa-mod-experiments

Silly Kitten Space Agency features, distributed together as the single `unscience` mod. Start with [`REPOSITORY_INDEX.md`](REPOSITORY_INDEX.md) for the complete catalog and
[`scope/FULL_SCOPE.md`](scope/FULL_SCOPE.md) for the game-integration map.

The current camera experiments include `hot-pursuit`: click a vehicle part to mount a live feed in
one of KSA's stock secondary viewports, then tune its part-local pose, FOV, and resolution.

The parachute experiments include `free-fallin`: globally tint the stock canopy, tile a PNG through
its panel UVs or project one cohesive image across the full canopy, composite a centered decal, and
tune its PBR response. `graffiti` can also raycast deployed canopy cloth and attach projected decals
that follow its inflation and motion. Graffiti and Free Fallin share one imported-image catalog at
`.unscience/pngs` and the same ImGui filesystem browser.

`pebbles` adds per-planet ground clutter replacement with built-in meshes or imported GLBs,
uniform scaling, and a textured collider editor. Its apply/restore controls live in the existing
Unscience collapsible panel; state is session-only. See [Pebbles](pebbles.lib/README.md).

`zippo` includes Disco party lights for one light or a whole vehicle, with independent repeating
color, moving-assembly actuation, and spotlight beam-spread cycles. Runtime light templates are
isolated per instance and restored when the effect stops. See [Zippo](zippo/README.md).

`kitten-animations` can drive any live EVA kitten without taking control of it. Its filterable target
picker follows the controlled kitten by default or stays pinned to an explicitly selected kitten id.
See [Kitten Animations](kitten-animations/README.md).

`garrys-torch` updates welded vehicles after simulation results are applied and before the next
physics snapshots, preserving light-part actuation progress. Its shared frame hook also runs while
the HUD is hidden. See [Garry's Torch](garrys-torch/README.md) for timing and validation details.

## building

Every project compiles against the proprietary KSA game assemblies, which are
never committed here. `Directory.Build.props` resolves them (first match wins):

1. `KSA_DLL_DIR` env var (or `-p:KSA_DLL_DIR=...`) — what CI uses.
2. A `ksa-game-assemblies` checkout cloned next to this repo (`../ksa-game-assemblies/current/dll/`).
3. Per-OS defaults (game install dir on Windows, `~/repos/meow-sci/ksa-game-assemblies/current/dll/` elsewhere).

If none resolve, the build fails with a single actionable error instead of a
wall of missing-type errors.

```bash
dotnet build ksa-mod-experiments.slnx
```

## distribution

Only `unscience` deploys to the KSA user mods directory or participates in `dotnet publish`.
Feature `.lib` projects retain their own code and explicit project references. Former standalone
hosts remain compile-checked development projects; they do not copy content to the mods directory.
The feature template follows the same rule. Add new feature libraries to Unscience's project and
submod/patch registration to ship them.

Set `UNSCIENCE_DIST_DIR` to redirect the single `<dir>/unscience` package. CI validates this output
before packaging it. Referenced feature assemblies are copied from MSBuild's resolved references,
so obsolete DLLs left in a build's `bin` folder cannot sneak into the package.

When migrating an existing game installation, remove the old standalone feature mod folders and
replace the old `unscience` folder. Builds deliberately do not delete existing user mod folders.

## releases (GitHub Actions)

`.github/workflows/release.yml` builds the whole solution and publishes ONLY the
`unscience` umbrella mod (which bundles every submod `.lib`) as a zip:

- push to `main` → prerelease `tip-<UTC stamp>`; the 5 newest tip builds are kept, older ones pruned
- push to `release/<version>` → release `v<version>` (re-pushing the branch rebuilds/moves it)
- `feature/**`, `fix/**`, `chore/**` branches and PRs into `main` → build only

The private assemblies come from `meow-sci/ksa-game-assemblies` via the
`KSA_GAME_ASSEMBLIES_PAT` repo secret (fine-grained PAT, read-only Contents on
that repo).
