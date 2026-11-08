# War Paint kitten coloring consolidation

Implemented against KSA `2026.11.10.5801` using the supplied CURRENT assemblies. Three explicitly
requested GPT-6.1-sol/xhigh agents handled core ownership, UI/host/Humble integration and DOH;
the parent reviewed lifecycle/save compatibility, added regression tests and validated the suite.

## User workflow

War Paint is the central kitten painting submod in the Unscience Toolbox. Select any live EVA
kitten, apply a whole tint or saved preset, then edit its individual materials live. Ad-hoc edits
need no saved preset. The active paints list selects targets and exposes Unset, which restores
the kitten's original material bindings. A pending preset/draft survives changing the target;
**Load kitten colors** explicitly replaces that draft with the target's current state.

Named presets persist across scenes and restarts in `.unscience/war-paint-presets.json`. Presets
and active paints are detached copies: editing/deleting a preset does not recolor existing targets,
and live edits do not silently modify a preset. Source-renderable + material-slot identities let
compatible characters use the same recipe while retaining their own textures. Missing/incompatible
roles fail with a diagnostic. Source material names remain readable for legacy recipes.

DOH removes its custom tint, individual paint, unique/shared batch and lucky-color controls and
offers a War Paint preset selector. Selection captures a detached recipe; reselect to refresh after
changing the global preset. New spawns each get independent private paint. The default offset is
**(1, 1, -2) m**. Humble Arteest removes its global kitten color writer/editor; part paint, engine
emissive and Hide/Show visor-glass controls remain there.

## Architecture and ownership

`war-paint.lib` owns `WarPaintService`, the UI, global preset library and scene participant. Its
native backend extracts the existing working DOH character/attachment binding and cloning path.
`Materials/` contains the moved GPU helpers, split into small partials without changing texture
construction or fur ExtraData. DOH/Humble depend on War Paint; War Paint has no reverse dependency.
The development host follows existing StarMap/HotkeyGuard conventions. Only Unscience is packaged.

Every painted kitten owns private slots. Native source bindings are captured before replacement.
Unset restores still-owned bindings before release. Exact native asset reference identity protects
recycled names/handles during reuse and release. Scene reset detaches reusable slots while clearing
old target references; replay binds reconstructed targets by exact vehicle and character identity.
Native fur source handles may change on reconstruction, so compatible role/slot mappings rebind the
existing private fur clone. Non-fur or fur-layout mismatches reject reuse without mutating the map.
Unused detached resources retire after reconstruction; native boarding/deletion is pruned. KSA's
fixed pool still reserves 64 slots for the game and refuses insufficient material budgets.

## Saved data and migration

The new `war-paint` v1 participant restores at order85. Active recipes are scene state; native
vehicle/character construction belongs to KSA. Presets are global files, atomically replaced.
Invalid/future preset files are preserved. UI filters, selected target and unapplied brush edits are
transient. No GPU handles, native objects, process-local clone names or pending commands are saved.

Existing `doh` v1/order60 and `humble-arteest` v1/order80 retain IDs/versions and legacy fields:

- DOH's old tint/material/group records import effective colors into independent War Paint targets.
  Former shared batches keep their saved appearance and become independently editable. Enabled
  legacy spawn tint imports a deterministic named preset without overwriting a different recipe;
  inactive color/unique inputs remain round-trippable legacy fields.
- Humble's named MaterialColors map resolves source assets on explicit live kittens, cloning them
  independently. Unmatched assets/targets warn, so the existing coordinator retains unresolved data.
- New captures keep active colors once, in War Paint. DOH stores native registrations and selected
  preset name plus detached spawn recipe, preserving setup if the library later changes.
- Explicit new War Paint scene recipes replay last and override legacy colors on the same target.
  Missing/ambiguous/changed-character targets and malformed records warn or reject, with retained
  original data; there is no controlled-vehicle fallback or replacement kitten spawn.

## Validation

Final full solution build passed against CURRENT with **0 warnings, 0 errors**:

```sh
DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build ksa-mod-experiments.slnx \
  --no-restore --disable-build-servers -m:1 -nr:false -p:NuGetAudit=false \
  -p:UseSharedCompilation=false \
  -p:KSAFolder=/Users/asherwin/wip/unscience-upgrade-ksa/repos/ksa-game-assemblies/current/dll/ \
  -p:SelectedDistModDir=/private/tmp/unscience-war-paint-dist/
```

All **19 managed suites passed**, including **61 War Paint** and **74 DOH** checks. War Paint tests
link the production service, preset library, adapter, material pool/release and fur-reuse helper.
They cover independent and live edits, Unset/release ordering, detached presets and restart/delete,
portable material roles, legacy imports, repeated/vanilla restore, cache reuse/retirement, identity
failure and invalid/unresolved record retention. Native asset fixtures verify structured keys,
same-name/same-handle replacement protection, idempotent release and game-reserve accounting.
DOH links the real central service and adapters for migration, detached request/selector recipes,
new-preset precedence, changed-character retention and deferred despawn. Existing shared save,
shutdown and rendering regressions also pass.

Distribution validation passed: one Unscience manifest with **32 feature assemblies**, including
War Paint, with no proprietary KSA DLL. `git diff --check` passed. Build/test logs are under
`/private/tmp/unscience-war-paint-*` on this machine.

No native KSA run was available on this macOS ARM64 host. In-game acceptance remains: native and
DOH kittens with different paints, all attachment/fur/visor behavior, preset restart and spawn,
live unsaved edits, Unset, boarding/deletion, repeated scene/vanilla load, resource recovery and
shutdown. Humble's immediate white vessels on part-paint Enable remain a separate unresolved issue:
[investigation and native A/B](HUMBLE_ARTEEST_5801_PAINT_INVESTIGATION.md).
