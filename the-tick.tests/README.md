# The Tick managed checks

Run `dotnet run --project the-tick.tests` from the repository root.

Links the production registry, Harmony patches and save adapter into native-free fixtures of KSA
5638's `Part.CrashTolerancePascals` resolution, `PartFailure.Detect` decision and
`PhysicsBubble.DetectStructuralFailure(VehicleUpdateState, SimStep)` decision flow, including the
kitten-impact branch (`DetectKittenImpact`, `PhysicsBubble.cs:1057-1095`). Checks: native tolerance before and after
patching, unbreakable tolerance on every full part and sub-part of protected vehicles only,
ownership through `Part.Tree` and the full part's tree, orphan parts, same-name isolation, finite
dent ratios, part failure and fragment guard suppression, pending part-failure preservation, the
G-load / dynamic-pressure threshold matrix with preserved telemetry, native kitten/debris/aero
causes, registered kittens surviving 25 m/s ground/ocean landings while unregistered and
same-name kittens die (velocity sampling continues; that frame's telemetry stays unwritten as in
KSA), pre-existing destruction events, delete/prune/dispose, concurrent worker reads, session
reset, idempotent apply and actual Harmony removal.

`KsaAssemblyProbe` (linked from `kitchen-sink.tests`) reads the real `KSA.dll` the suite was built
against (`KSA_DLL_DIR`/`KSAFolder`, metadata only) and requires the fixture method that the
production lookup resolved to match the shipped static/return/parameter types and names, so a game
signature change can no longer pass silently (5638 added `SimStep`). It prints a WARN and the PASS
line says "SKIPPED" when `KSA.dll` is absent. Last run: 108 damage/lifecycle + 24 save checks pass
against 5638; pointing it at the 5482 `KSA.dll` fails as intended.

Save checks link the real adapter, `VehicleProvider`, JSON helpers and `SceneSaveCoordinator`:
round-trip, reset/rebind gating both patches, deletion capture, A-B-A and repeated loads, missing
record and vanilla loads, missing/ambiguous/disposed identities, invalid lists, retained records,
unavailable-patch recovery and capture failures. Only native UI state is replaced by a fixture.

Not covered: Bepu contact accumulation, dents, real kitten surface velocity, ImGui and native save
timing. Native acceptance (pending on 5638): protect a vessel, ram it into terrain and another
vessel at speed, dive it through dense atmosphere, confirm no destruction and native telemetry;
register an EVA kitten and drop it onto ground and water from > 20 m/s; delete the rows and repeat
to confirm destruction returns; save, reload and load a vanilla save to confirm rebind and cleanup.
