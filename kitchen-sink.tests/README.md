# Kitchen Sink managed checks

Run `dotnet run --project kitchen-sink.tests` from the repository root.

Latest managed validation: 47 IVA ownership/persistence checks, 26 existing save checks and
54 G-load checks passed. The full solution compiles against local KSA 5541 with zero warnings
or errors; `saves.tests` also passes. Native acceptance below remains pending.

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
