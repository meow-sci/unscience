# doh.lib — kitten creation and management

Headless spawning and tracking for DOH. All kitten paint, GPU material cloning and presets are
owned by [`war-paint.lib`](../war-paint.lib/README.md), which has no dependency on DOH.

## Spawning API

`KittenSpawner(SpawnedKittenRegistry)` creates EVA kittens with reference-vehicle or absolute-orbit
positioning. `SpawnRequest` supplies `Count`, `CharacterId`, `VehicleName`, positioning and optional
`PaintPresetName` / `PaintSettings`. `PaintSettings` clones on both assignment and retrieval: a
prepared request remains detached from later preset edits. A supplied snapshot takes precedence;
an unavailable named preset without a snapshot fails before creating kittens.

Names are exact for one kitten and collision-free numbered prefixes for batches. Null/blank input
uses `Kitten_doh_`. Both live vehicle IDs and rostered kitten names are reserved, case-insensitively;
KSA links EVA dose/sickness/death to a same-named roster entry. The naming counter is a runtime hint.
Default body-frame offset is **(1, 1, -2) m**. Reference objects retain target identity rather than
using an unstable list index. Orbit time follows the reference state's `StateTime`.

Every painted kitten is applied through `WarPaintService.Shared.Apply`; every batch member gets
private materials. GPU preflight budgets a set per kitten plus native avatar capacity and keeps
War Paint's reserve available. A partial batch reports all successfully created kittens.

Legacy `TintColor`, `PerKittenColors`, `UniqueMaterialsPerKitten` and `RecolorKitten` remain accepted
and forward to War Paint when no new preset recipe is supplied. A preset overrides all legacy color
fields. The old unique flag no longer shares mutable materials. Material types
moved from `MeowSci.DohLib.Materials` to `MeowSci.WarPaintLib.Materials`; callers should use War Paint's
public service rather than creating/releasing materials. The factory-taking constructor remains as
a compatibility overload accepting the moved type. `SpawnedKittenEntry.MaterialSet` is a legacy
view, not an owner; consult War Paint for current paint.

## Native lifecycle

Call on the game thread. Spawn joins `JobSystems.VehicleSolver` before constructing physics shapes,
creates the backpack's tree before configuring fuel, teleports, and registers the parent child.
Partially constructed kittens are removed, with a direct deregistration fallback if native teardown
throws.

UI callers use `RequestDespawn` / `RequestDespawnAll`: the shared physics handoff applies pending
results before removal. Missing handoff rejects requests. The exact registration is checked at
dispatch, so reset/rebind invalidates stale requests. Immediate `Despawn` / `DespawnAll` are for
that boundary or pre-renderer shutdown. Paint is unset before native
`Universe.DestroyVehicle(vehicle, CrewDisposition.Boarded)`. This native teardown hands off cameras,
control, targets and emitters without changing roster crew records. Prune removes already-disposed
entries through War Paint cleanup. DOH never cleans shared material systems or frees clone sets.

## Scene saves

The existing **`doh` v1**, order **60**, captures spawn name, offset/count/character, preset name and
detached preset recipe, and the IDs/characters of native-owned tracked kittens. Native reconstruction
owns actual vehicles; DOH never respawns on restore. The reference-vehicle picker and pending
requests are transient. Reset clears ownership and controls; War Paint owns old-world paint cleanup.

New capture leaves old per-kitten paint fields empty because War Paint saves active paint once in
its own record. Legacy `Tint`, `Materials` (`Source` / `Name` / `Color`) and optional `MaterialGroup`
remain decoded and validated. Replay applies their effective recipe through `ApplyLegacy`; each
kitten becomes independently paintable, preserving colors while migrating shared-group semantics.
Old enabled spawn tint imports a deterministically named global preset; unused tint and unique flags
remain round-tripped as compatibility metadata. Import failures retain the original record and a
detached spawn recipe, with diagnostics. Missing targets/materials and invalid recipes also report
and preserve earlier state. Later War Paint replay overrides old migration when both records exist.

Spawn names preserve blank/Unicode input and reject null, NUL or 256+ UTF-8 bytes. Native kitten names
remain native-owned. Explicit saved offsets survive; fresh/vanilla/missing fields use **(1, 1, -2) m**.

## Validation and dependencies

[`doh.tests`](../doh.tests/README.md) exercises production save/reset/replay, naming and deferred native
teardown in a native-free fixture. Native physics, material rendering, missing characters, pool
recovery and the Windows access-violation reproduction require in-game acceptance. See
[5801 follow-up](../plans/DOH_DESPAWN_5801_FOLLOWUP.md).

Dependencies: `war-paint.lib`, `ksa-abstractions.lib` and compile-only KSA/BRUTAL/Bepu game assemblies.
