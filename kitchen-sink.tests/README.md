# Kitchen Sink managed checks

Run `dotnet run --project kitchen-sink.tests` from the repository root.

Links the production G-load registry and Harmony transpiler into a native-free
fixture of KSA 5438's structural-failure decision (source-identical to 5402). Checks multiple targets,
duplicate adds, same-name identity isolation, contact situations, unchanged load
telemetry, independent G/pressure thresholds and pressure cause classification,
pending part/event preservation, removal, disposed/missing targets, concurrent
UI/worker access, session reset, actual Harmony removal, and rejected detector layouts.

The fixture does not run Bepu, render ImGui, or reproduce the cart. Native acceptance:
add two vehicles through Kitchen Sink; collide them with verified high part crash
tolerances; remove one row and repeat; save, edit and reload to confirm protection returns on
reconstructed vehicles; load a vanilla/legacy save to confirm cleanup; verify the picker filter
does not activate game hotkeys.

Save checks also link the real Kitchen Sink adapter, VehicleProvider, JSON helpers and scene
coordinator. They cover JSON round-trip, reset/rebind, deletion capture, A-B-A/repeated loads,
legacy boolean-only and vanilla saves, missing/ambiguous/disposed identities, invalid lists,
retained records, unavailable-patch recovery and capture failures. Only native UI/model access
is replaced by fixtures. Native sidecar/load timing is covered separately by saves.tests.
