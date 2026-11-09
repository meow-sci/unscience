# DOH native kitten roster integration

DOH previously constructed KittenEva vehicles without adding KittenRosterEntryData. Native KSA
finds EVA crew by their name hash, so the roster UI, health/condition, radiation, mission/travel
statistics and death history had no record to use. Native starting crew already demonstrate the
correct sequence in Universe.AssignStartingCrew: MarkLaunched and StartMission("EVA").

## Implementation

- KittenSpawner.Roster.cs creates an exact-name/actual-character roster row, starts its EVA mission
  and marks the vehicle launched. Existing valid rows keep mission count/start, condition, dose and
  career history. Duplicate/hash-conflicting names, character mismatches, KIA/retired crew and crew
  assigned elsewhere are rejected. Shared VehicleProvider requires the exact unique current object.
- Construction, roster registration and pose setup form one transaction. Failure cleans up only the
  provisional vehicle/row. Boarded cleanup avoids ending an unrelated same-name crew mission. A
  non-kitten lookup is never deregistered; earlier successful batch members/crew history remain.
- The real doh v1/order60 adapter integrates exact native-restored targets before paint migration
  and tracking. Missing old rows are backfilled; no extra vehicles are spawned. Incompatibilities
  report save diagnostics and preserve the original sidecar record. Old unrostered kittens begin
  accumulating history at repair time; past statistics cannot be recovered.
- Live maintenance repairs only tracked current EVA targets once per second of wall time, also
  while paused. Repeated errors are logged once per entry until recovery. Error/timing state is
  transient; old-world registry references clear through the existing reset/unload lifecycle.

Native UniverseData.KittenRoster owns roster persistence in universe.xml. No new DOH save field,
version or participant is added, and native crew data are never duplicated in unscience.json.
KSA's boarding/recovery/death paths transfer/end the same mission and retain KIA history. Prune
and unload forget DOH ownership and release private paint as before; they do not erase crew rows.
No new Harmony patch, custom death path or live synchronous deletion is introduced.

## Validation

The DOH suite links the production roster helper, rollback and real scene adapter. 102 checks cover
native-style registration, repeat repair without lost history, native kill/boarding fixtures,
creation rollback, identity safety, old-save migration, separately reconstructed native-roster
fixtures, repeated loads and incompatible-record retention, alongside the existing paint/name/
smite/default-offset checks. Native objects, physics, roster serialization and UI are managed
stand-ins; these checks do not prove the real game rendering or XML loader.

The full solution build passed against CURRENT 5801 with zero warnings/errors. Distribution
validation passed with one Unscience package and 32 referenced feature assemblies.

## Native acceptance

- [ ] Spawn one named kitten, blank-name kittens and custom-character batches. The built-in roster
  shows each once, the correct model, active EVA mission and a working double-click follow link.
- [ ] Advance simulation; mission time, distance, speed, radiation and life-support condition update.
  Native crew outside DOH keep their own state. Boarding and EVA again preserve identity/history.
- [ ] Save/load repeatedly and A-B-A; inspect universe.xml and verify no duplicate rows/missions,
  lost dose/history or extra vehicles. Load an older unrostered DOH save and verify exact repair.
- [ ] Recover, naturally kill and Smite kittens. Native home/KIA/cause/mission history is correct;
  pruning and unload do not remove those records or affect surviving crew/presets.
- [ ] Force constructor/pose/roster failure partway through a batch. Failed targets leave no new row
  or orphan vehicle; earlier successful spawns and all prior crew history remain.
- [ ] Load copies with missing targets, different characters, duplicate/KIA/retired/elsewhere-assigned
  identities. Expect diagnostics and retained sidecar data, never crew theft or resurrection.
