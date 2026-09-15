# Dent Wizard and upstream Kitchen Sink integration

## Result

Merged upstream `9fcd42d` (`feat(kitchen-sink): add scene-saved G-load protection for KSA 5438`)
into local `main` containing `3778fb7` (`feat(dent-wizard): add target-relative vessel launcher`).
Both original commits remain in history. Reference assemblies remain KSA **2026.9.10.5438**
from `../ksa-game-assemblies/current/dll`; this does not introduce another game-version upgrade.

Four append-at-end documentation conflicts retained both features' sections:
`scope/00-architecture-and-abstractions.md`, `scope/saves.md`, `scope/vehicle-physics.md`,
and `unscience/README.md`. Production code merged without conflicts. The merged host retains
Dent Wizard's shared physics handoff and Kitchen Sink's G-load patch installation/removal,
its backward-compatible save records, shared dent-aware IVA integration and obsolete Flexo removal.

## Feature interaction and saves

Dent Wizard teleports the existing vehicle object. Kitchen Sink's protection registry retains
that exact reference; launching neither transfers protection to the target nor changes protection
on a failed shot. Dent Wizard reset/disposal owns only its own form, gesture and pending shot.
The G-load transpiler still changes only the whole-vehicle G-load destruction decision; ordinary
contacts, telemetry, pressure and part damage retain upstream behavior.

The combined save inventory is **29 submods and 31 feature records**. Dent Wizard's launched
vessel state is native-owned. Its empty v1 record exists for lifecycle cleanup; selection and
speed are input for the next one-shot action, not an applied registration, reusable animation
recipe or continuing force. Armed and pending shots never replay on load. Kitchen Sink separately
persists its protection registry and rebinds reconstructed vehicles. Existing record IDs/versions
and payloads are unchanged. See [save coverage](saves-acceptance.md).

## Validation

On macOS, `dotnet build ksa-mod-experiments.slnx --no-incremental
-p:UNSCIENCE_DIST_DIR=/private/tmp/dent-wizard-upstream-dist` passed for **74 projects**, with
**0 warnings and 0 errors**. Output was isolated from the live game installation.

All **15 managed suites passed** with `dotnet run --project <project> --no-build`:
`garrys-torch.tests`, `kitchen-sink.tests`, `pebbles.tests`, `godzilla.tests`, `byo-music.tests`,
`pyro.tests`, `sphinx.tests`, `iron-man.tests`, `iron-man-flight.tests`, `iron-man-mode.tests`,
`saves.tests`, `world-saves.tests`, `camera-saves.tests`, `ksa-upgrade.tests`, `dent-wizard.tests`.
Dent Wizard now links the production G-load registry too, verifying protection across successful
and rejected launches, target isolation and independent reset/unload ownership. Existing Kitchen
Sink suites exercise its production damage transpiler and real save adapter/coordinator.

Build log: `/private/tmp/dent-wizard-upstream-build.log`.
Suite logs: `/private/tmp/dent-wizard-upstream-tests/`.

No native KSA session was launched. Live ray picking, EVA flight, impacts/dents, protection under
contacts and actual save/load remain native acceptance items; see the feature READMEs and the
combined checklist in [save coverage](saves-acceptance.md). Passing fixtures does not establish
native physics/render behavior.
