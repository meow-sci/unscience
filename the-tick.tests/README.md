# The Tick managed checks

Run `dotnet run --project the-tick.tests` from the repository root.

Links the production registry, Harmony patches and save adapter into native-free fixtures of KSA
5482's `Part.CrashTolerancePascals` resolution, `PartFailure.Detect` decision and
`PhysicsBubble.DetectStructuralFailure` decision flow. Checks: native tolerance before and after
patching, unbreakable tolerance on every full part and sub-part of protected vehicles only,
ownership through `Part.Tree` and the full part's tree, orphan parts, same-name isolation, finite
dent ratios, part failure and fragment guard suppression, pending part-failure preservation, the
G-load / dynamic-pressure threshold matrix with preserved telemetry, native kitten/debris/aero
causes, pre-existing destruction events, delete/prune/dispose, concurrent worker reads, session
reset, idempotent apply and actual Harmony removal.

Save checks link the real adapter, `VehicleProvider`, JSON helpers and `SceneSaveCoordinator`:
round-trip, reset/rebind gating both patches, deletion capture, A-B-A and repeated loads, missing
record and vanilla loads, missing/ambiguous/disposed identities, invalid lists, retained records,
unavailable-patch recovery and capture failures. Only native UI state is replaced by a fixture.

Not covered: Bepu contact accumulation, dents, ImGui and native save timing. Native acceptance:
protect a vessel, ram it into terrain and another vessel at speed, dive it through dense atmosphere,
confirm no destruction and native telemetry; delete the row and repeat to confirm destruction
returns; save, reload and load a vanilla save to confirm rebind and cleanup.
