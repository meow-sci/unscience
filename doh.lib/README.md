# doh.lib — kitten creation and management

Headless spawning and tracking for DOH. All kitten paint, GPU material cloning and presets are
owned by [`war-paint.lib`](../war-paint.lib/README.md), which has no dependency on DOH.

## Spawning API

`KittenSpawner(SpawnedKittenRegistry)` creates EVA kittens with reference-vehicle or absolute-orbit
positioning. `SpawnRequest` supplies `Count`, `CharacterId`, `VehicleName`, positioning and optional
`PaintPresetName` / `PaintSettings`. `PaintSettings` clones on both assignment and retrieval: a
prepared request remains detached from later preset edits. A supplied snapshot takes precedence;
an unavailable named preset fails before creating kittens, even when a prepared snapshot remains.
This prevents a deleted preset from silently producing anonymous paint.

Names are exact for one kitten and collision-free numbered prefixes for batches. Null/blank input
uses `Kitten_doh_`. Both live vehicle IDs and rostered kitten names are reserved, case-insensitively;
KSA links EVA dose/sickness/death to a same-named roster entry. The naming counter is a runtime hint.
Default body-frame offset is **(1, 1, -2) m**. Reference objects retain target identity rather than
using an unstable list index. Orbit time follows the reference state's `StateTime`.

Every painted kitten is applied through `WarPaintService.Shared.Apply`, carrying the selected
preset name into its live assignment; every batch member gets private materials. War Paint previews
and confirmed preset deletion include these DOH assignments. Anonymous legacy recipe/tint requests
import reusable named presets. GPU preflight budgets a set per kitten plus native avatar capacity and keeps
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

`RequestSmite` / `RequestSmiteAll` submit `InputEvents.VehicleDestroyData` to KSA's
`VehicleDestroyBuffer`, with `Recovered = false`. KSA processes the action after physics results
and **before** `RefreshVehiclesInFrame`; the shared physics handoff runs after that refresh and is
too late to delete a renderable safely. KSA performs normal death, camera/control/target/emitter
handoff and disposal. Requests retain exact current kitten objects, reject missing/non-kitten/stale
targets, and avoid duplicate pending events. No custom despawn API, deferred closure, kill patch,
or unload-time vehicle destruction remains. Unload clears DOH tracking; KSA owns the vehicles.

The UI calls this **Smite**, but KSA's input action uses the fixed enum cause **Abandoned**. There
is no custom reason string; DOH does not invent an enum value or patch native death reporting.
Prune drops native-disposed entries and asks War Paint to release their private material sets
after death, without restoring disposed avatar bindings. Other kittens' materials and reusable
global presets remain. War Paint also handles deaths/boarding independently of DOH tracking.

## Scene saves

The existing **`doh` v1**, order **60**, captures spawn name, offset/count/character, preset name and
detached preset recipe, and the IDs/characters of native-owned tracked kittens. Native reconstruction
owns actual vehicles; DOH never respawns on restore. The reference-vehicle picker and pending
native kill inputs are transient. Native input retains the old object, so a same-name reconstructed
kitten cannot be retargeted. Reset clears ownership and controls; War Paint owns old-world paint cleanup.
Capture omits native-disposed kittens even before pruning. The save ID/version and payload stay unchanged.

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

[`doh.tests`](../doh.tests/README.md) exercises production save/reset/replay, naming, native kill
requests and post-death paint cleanup in a native-free fixture. Real-DLL upgrade checks verify
input dispatch precedes render-list refresh. Native physics, material rendering, missing characters,
pool recovery and the Windows animation crash require in-game acceptance. See
[native kill follow-up](../plans/DOH_NATIVE_KILL_5801.md).

Dependencies: `war-paint.lib`, `ksa-abstractions.lib` and compile-only KSA/BRUTAL/Bepu game assemblies.
