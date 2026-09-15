# ksa-mod-experiments

Silly Kitten Space Agency features, distributed together as the single `unscience` mod. Start with [`REPOSITORY_INDEX.md`](REPOSITORY_INDEX.md) for the complete catalog and
[`scope/FULL_SCOPE.md`](scope/FULL_SCOPE.md) for the game-integration map.

`dent-wizard` fires any vessel or EVA kitten from the camera at a clicked vessel or terrain,
with target-relative speed and orbital-velocity inheritance. Existing Kitchen Sink G-load protection
stays attached to the launched vessel; each feature retains its own save/reset policy.
See [Dent Wizard](dent-wizard/README.md).

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

`humble-arteest` includes **Hide visor glass** under Kitten Color. It hides all kitten visors
independently of material alpha; **Show visor glass**, deactivation, or unload restores drawing.
See [Humble Arteest](humble-arteest.lib/README.md) for the separate glass-rendering path.

`garrys-torch` updates welded vehicles after simulation results are applied and before the next
physics snapshots, preserving light-part actuation progress. Its shared frame hook also runs while
the HUD is hidden. Weld sources default to **Collisions off**, retaining part animations while
passing through vehicles and scenery; the per-weld checkbox and saved presets can opt in.
The red **Delete All Welds** button beside **Create Weld** removes all welds and is disabled when none exist.
See [Garry's Torch](garrys-torch/README.md) for timing and validation details.

Garry's Torch and Godzilla use 0.05–20 as a mouse-drag range only. Double-click or Ctrl-click
their scale fields to type values outside that range; APIs, presets and animations accept them too.

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

- push to `main` → prerelease `tip-<UTC stamp>-<run ID>-<attempt>`; the 5 newest tip builds are kept, older ones pruned
- push to `feature/*` (including nested branch names) → prerelease `feature-<UTC stamp>-<run ID>-<attempt>` with an `unscience-feature-…zip` asset; all feature branches share one pool of 5 builds, separate from tip builds
- push to `release/<version>` → release `v<version>` (re-pushing the branch rebuilds/moves it)
- `fix/**`, `chore/**` branches and PRs into `main` → build only

Manual workflow runs on branches follow the same policy. Rolling prerelease cleanup removes
older releases and their tags within that channel; stable releases are preserved.

The private assemblies come from `meow-sci/ksa-game-assemblies` via the
`KSA_GAME_ASSEMBLIES_PAT` repo secret (fine-grained PAT, read-only Contents on
that repo).

Godzilla adds session-based Smart vessel resizing, raw XYZ Basic scaling, independent **Scale physics** and **Scale colliders** runtime toggles, and restore controls.
See [Godzilla](godzilla/README.md).

BYO Music imports OGG/WAV/MP3 files into the common sounds library and attaches independently
controllable 3D playback to vessels, with continuous repeat or gaps. See [BYO Music](byo-music/README.md).

Pyro supports independent runtime On/Off cycles with simulation-second durations and manual/bulk
cancellation. See [Pyro](pyro/README.md#runtime-onoff-cycles).

Pebbles copies imported GLBs into a shared persistent library and discovers them automatically in
mesh pickers. See [shared GLBs](pebbles.lib/README.md#shared-glb-library).

Sphinx places shared imported GLBs as body-fixed models with automatic box/mesh colliders, with terrain alignment,
live XYZ transforms, optional common PNG overrides and live per-static UV scale/offset controls
with a mapping reset. See [Sphinx](sphinx/README.md) for controls
and model support. Placements are session-only; collider modes include Auto, Mesh, Fitted box and Off. No new shadow casters are added.

Iron Man adds opt-in editing and rocket flight for existing EVA kittens, with configurable body
attachment nodes, upright editor/rocket controls, native flight-computer gauges and save restoration.
Full-width EVA/Iron Man mode buttons switch native kitten and rocket behavior; editing works in
either mode. Surface debug teleports place Iron Man kittens upright with equipment clearance;
newly configured kittens start in EVA mode; scene saves can restore Iron Man mode disarmed. See [Iron Man](iron-man/README.md)
and its [source research](plans/iron-man/RESEARCH.md). Managed checks run with
`dotnet run --project iron-man.tests`, `dotnet run --project iron-man-flight.tests` and
`dotnet run --project iron-man-mode.tests`.

Garry's Torch weld scaling preserves custom authored SubPart scales (including Flexo parts).
XYZ controls multiply captured full-part scales; unweld/unload restore the original instance
proportions. See [scaling behavior](garrys-torch/README.md#scaling) for inheritance and restoration.

## Saving Unscience setups

Ordinary KSA Save/Load now includes Unscience scene setups through a versioned `unscience.json`
file in the native save folder. The existing toolbox shows capture/restore diagnostics. Window
layout autosave remains separate. Keep imported asset libraries and runtime part mods alongside
your installation; saves reference those dependencies. See [scene-save usage and limits](unscience/README.md#scene-saves),
the [research and implementation plan](plans/SAVES.md), [implemented coverage and acceptance](plans/saves-acceptance.md),
and [integration scope](scope/saves.md).

## KSA 5438 compatibility

This branch targets **2026.9.10.5438**. The upgrade migrates plume rendering and saved template IDs,
paint/emissive/IVA submissions, canopy materials/restoration, final-depth decals, resource-order
diagnostics and Vulkan bindings. Parts Now rejects new explosion definitions it cannot unload.
See [the full impact review and validation record](plans/KSA_5438_UPGRADE.md). Native flight/render
acceptance remains pending; compilation and managed tests are recorded separately.

Kitchen Sink adds a filtered vehicle picker and a scene-saved **G-load Invincibility** list with
per-vehicle removal. Collisions and other damage checks remain active; the defunct Flexo test
panels are removed. See [Kitchen Sink](kitchen-sink/README.md).
These local changes are reconciled with the 5438 upgrade: the G-load decision is unchanged and
the shared IVA rendering fix is retained. [Validation record](plans/KSA_5438_RECONCILIATION.md):
71 projects build cleanly and all 14 managed suites pass; native acceptance remains pending.

Save/restore maintenance is mandatory for feature changes in [AGENTS.md](AGENTS.md#scene-saverestore-maintenance-required-for-every-feature)
and [CLAUDE.md](CLAUDE.md#scene-saverestore-maintenance-required-for-every-feature). Durable runtime
registrations are saved by default; native/global/transient exclusions must be explicitly documented.
