# doh.lib — kitten creation and management

Headless spawning and tracking for DOH. All kitten paint, GPU material cloning and presets are
owned by [`war-paint.lib`](../war-paint.lib/README.md), which has no dependency on DOH.

`DohSubmod` renders a War Paint-style **Name / Model / unlabeled action** table with red **Smite**
buttons. **Smite All** fills the panel width; spawn and refresh split an action row equally with
6 px cell padding (12 px between buttons). Confirmations/errors use KSA's
`Brutal.Logging.DefaultCategory.Log.Info/Error`, replacing status/count labels and preset helper
text. This presentation and transient logging add no scene state or save fields.

## Spawning API

`KittenSpawner(SpawnedKittenRegistry)` creates EVA kittens with reference-vehicle or absolute-orbit
positioning. `SpawnRequest` supplies `Count`, `CharacterId`, `VehicleName`, positioning and optional
`PaintPresetName` / `PaintSettings`. `PaintSettings` clones on both assignment and retrieval: a
prepared request remains detached from later preset edits. A supplied snapshot takes precedence;
an unavailable named preset fails before creating kittens, even when a prepared snapshot remains.
This prevents a deleted preset from silently producing anonymous paint.

Available single names are used verbatim. Occupied names automatically gain or advance a numeric
suffix above the highest existing match (`Crew_7` with `Crew_20` becomes `Crew_21`). Batches append
numbers to the literal prefix; null/blank input uses `Kitten_doh_`, starting at zero in an empty scene.
Number selection scans native system IDs and all roster names case-insensitively, including seated,
retired and KIA crew. KSA links EVA dose/sickness/death to the same-named roster entry, so earlier
identities stay reserved. No counter is cached or saved: each spawn derives the next suffix from
the current native scene/roster, including after loads, with no fixed-width integer overflow.
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

## Native kitten roster

Every successful spawn joins `Universe.KittenRoster.Kittens` through `KittenRosterEntryData` with
its exact native name and actual character. Like KSA's starting EVA crew, DOH calls
`StartMission(EvaAssignmentId)` and `MarkLaunched()`. The built-in roster can follow the EVA, and
native health, radiation, life support, distance/speed and mission time apply normally. Boarding
transfers the same crew member; recovery ends its mission; death retains a KIA row and native cause.
DOH pruning and unload never delete these native records.

The real save adapter backfills older unrostered tracked EVA kittens after reconstruction. A
once-per-second live maintenance pass also repairs owned targets, including while paused. Existing
valid records keep their mission count/start time, condition, dose and career history. An old kitten
with no record starts tracking at repair time; unavailable past statistics are not fabricated.
Conflicting, KIA, retired, ambiguous or differently assigned identities are rejected rather than
overwritten; unresolved save targets retain their original sidecar records. Maintenance ignores
untracked, stale and disposed objects.

Roster registration and pose initialization are one creation transaction. If either fails, native
constructor cleanup preserves earlier crew missions and removes only this spawn's provisional row;
earlier successful batch members remain registered. See [evidence/acceptance](../plans/DOH_ROSTER_INTEGRATION.md).

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
Crew records, health and statistics are already owned by KSA's `UniverseData.KittenRoster` inside
`universe.xml`; they are not duplicated in the DOH sidecar. Replay reuses the native-restored roster
entry without restarting its mission, creating rows only for older missing records.

New capture leaves old per-kitten paint fields empty because War Paint saves active paint once in
its own record. Legacy `Tint`, `Materials` (`Source` / `Name` / `Color`) and optional `MaterialGroup`
remain decoded and validated. Replay applies their effective recipe through `ApplyLegacy`; each
kitten becomes independently paintable, preserving colors while migrating shared-group semantics.
Old enabled spawn tint imports a deterministically named global preset; unused tint and unique flags
remain round-tripped as compatibility metadata. Import failures retain the original record and a
detached spawn recipe, with diagnostics. Missing targets/materials and invalid recipes also report
and preserve earlier state. Later War Paint replay overrides old migration when both records exist.

Spawn names preserve blank/Unicode input and reject null, NUL or 256+ UTF-8 bytes. Collision recovery
does not edit the saved input or rename existing kittens; actual names remain native-owned and
numbering is derived after native reconstruction. Explicit saved offsets survive;
fresh/vanilla/missing fields use **(1, 1, -2) m**.

## Validation and dependencies

[`doh.tests`](../doh.tests/README.md) exercises production save/reset/replay, naming, native kill
requests and post-death paint cleanup in a native-free fixture. Real-DLL upgrade checks verify
input dispatch precedes render-list refresh. Native physics, material rendering, missing characters,
pool recovery and the Windows animation crash require in-game acceptance. See
[native kill follow-up](../plans/DOH_NATIVE_KILL_5801.md).

Dependencies: `war-paint.lib`, `ksa-abstractions.lib` and compile-only KSA/BRUTAL/Bepu game assemblies.
