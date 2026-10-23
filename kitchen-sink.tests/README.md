# Kitchen Sink managed checks

Run `dotnet run --project kitchen-sink.tests` from the repository root.

Latest managed validation (KSA 5638): 47 IVA ownership/persistence checks, 26 existing save
checks, 17 capsule glass checks and 108 G-load checks passed, with the real `KSA.dll` probe
verified. The full solution compiles against local KSA 5638 with zero warnings or errors.
Native acceptance below remains pending.

Capsule glass adds 17 real-adapter checks: detached boolean JSON round-trip, independent legacy
IVA preference, reset/replay, repeated and cross-scene loads, missing/legacy records, malformed
and future payloads, unavailable-patch diagnostics/retention/recovery and unload. This suite
substitutes only renderer access; production filter/IVA-template ownership is separately tested
in `ksa-upgrade.tests` (20 checks).

Capsule native acceptance: enable the experiment in an exterior view of the stock medium/Gemini
capsule, inspect both windows and cabin from several angles, then view from IVA with ray tracing
on/off. Check glass depth/tint, shadows, hull/door-frame isolation, late-spawn capsules, both
visibility-toggle orders and Blinky/Shiny coexistence. Save/reload each toggle combination, repeat,
load a vanilla scene and unload; verify exact restoration. Native acceptance remains pending.

IVA camera checks link `IvaCameraUnlock`, `UnlockedIvaController`, the real Kitchen Sink
save adapter/coordinator and shared vehicle/part identity resolvers. They exercise the actual
protected setter delegate and Harmony head patch against matching managed game fixtures:
main/secondary isolation, input-focus cleanup, body rotation, return to seat, detached JSON
capture, exact seat rebind, repeated/cross-scene loads, vanilla/legacy records, invalid/future
payloads, missing/ambiguous/disposed targets, topology/mode mismatches, retained recovery,
mode transitions and unload. Native FlyController input/motion and ImGui are substituted;
Brutal quaternion/vector arithmetic is real. No Vulkan, GLFW or audio context is created.

IVA native acceptance: enable game IVA ray tracing, enter a seat, unlock and fly through
the interior with keyboard/mouse/gamepad; confirm lighting, glass, head visibility and IVA
audio. Rotate/translate the vessel; adjust speed and sprint; focus text/modals; switch modes
with keys held; return to the same seat; save/reload the detached pose repeatedly; load a
vanilla scene and remove/change the followed vessel. Check the ordinary free cam and secondary
views, plus interaction with Camera Controller Override playback. Native acceptance is pending.

Links the production G-load registry and Harmony transpiler into a native-free
fixture of KSA 5638's `DetectStructuralFailure(VehicleUpdateState, SimStep)` decision, including the
new `DetectKittenImpact` branch (`PhysicsBubble.cs:1057-1095`). Checks multiple targets,
duplicate adds, same-name identity isolation, contact situations, unchanged load
telemetry, independent G/pressure thresholds and pressure cause classification,
pending part/event preservation, removal, disposed/missing targets, concurrent
UI/worker access, session reset, actual Harmony removal, and rejected detector layouts
(missing/duplicate G-load or kitten-impact anchors, plus correct filter placement in either order).
`KittenImpactChecks` covers native kitten ground/ocean deaths at the inclusive 20 m/s threshold,
the ladder and non-kitten exemptions, protected kittens surviving 25 m/s landings with and without
excess G while native telemetry and velocity sampling continue, fluid pressure still killing a
protected kitten, same-name isolation, removal and unpatch.

`KsaAssemblyProbe.cs` (also linked into `the-tick.tests`) reads the real `KSA.dll` the suite was
built against (`KSA_DLL_DIR`/`KSAFolder`, metadata only, never loaded). It requires the fixture
methods that the production lookups resolved to match the shipped static/return/parameter types
and names, and the shipped detector body to call `StructuralLoad.get_GLoadFraction` and
`DetectKittenImpact` exactly once. Without `KSA.dll` it prints WARN and the PASS line says
"SKIPPED"; against the 5482 `KSA.dll` it fails, which is the regression the 5638 upgrade exposed.

The fixture does not run Bepu, render ImGui, or reproduce the cart. Native acceptance (pending on
5638): add two vehicles through Kitchen Sink; collide them with verified high part crash
tolerances; protect an EVA kitten and drop it onto ground and water from > 20 m/s, then remove it
and confirm the hard landing kills; remove one row and repeat; save, edit and reload to confirm protection returns on
reconstructed vehicles; load a vanilla/legacy save to confirm cleanup; verify the picker filter
does not activate game hotkeys.

Save checks also link the real Kitchen Sink adapter, VehicleProvider, JSON helpers and scene
coordinator. They cover JSON round-trip, reset/rebind, deletion capture, A-B-A/repeated loads,
legacy boolean-only and vanilla saves, missing/ambiguous/disposed identities, invalid lists,
retained records, unavailable-patch recovery and capture failures. Only native UI/model access
is replaced by fixtures. Native sidecar/load timing is covered separately by saves.tests.
