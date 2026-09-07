# Iron Man flight, flight computer and render checks

Managed Harmony checks linking the production flight, native flight-computer HUD, settings and
render patch files against small fixtures of KSA's dispatch and lifecycle seams. Run:

```sh
dotnet run --project iron-man-flight.tests
```

Covers default-off and per-instance activation, stock base input dispatch without recursive EVA
calls, disable/unload restoration, worker joining, repeated patch installation, early equipment
GPU submission with the avatar retained in the late phase, and visible equipment after flight
mode is disabled. A second render prefix installed before and after Iron Man tests coexistence
without skipped early overrides or duplicated late submissions.

The flight-computer checks run all three production transpilers and the emitted nonvirtual
`Vehicle.IsFlightComputerDisabled<Enum>` adapter. They cover default-off and per-kitten HUD
selection, every additional context condition and AND ordering, empty/null contexts, preservation
of player-hidden panels, ordinary vessels, inherited selected-button state, packed disabled/click
bits, deferred click commands, base burn/target/engine prerequisites, live base-policy changes,
disable/missing-state/unload/reapply behavior and rejection of unexpected IL. The settings snapshot
test changes every exposed setting and checks restoration without rewinding the current burn or
solver telemetry.

Orientation checks link the production control-frame, editor-frame and backpack-map patches.
Real Brutal quaternion math verifies headward rocket controls, inverse frame round trips, explicit
control part/connector precedence, and identity/nonidentity editor assembly frames. Actual Harmony
patches verify scoped whole-scene editor orientation and pan direction/bounds without modifying
assembly geometry; per-root backpack map bypass, unchanged authored maps and global cache reset;
default-off, disable, unload and reapply. IL checks preserve labels/exception boundaries and reject
missing/duplicate target patterns. The control getter fixture mirrors the game's nullable control
selection expression without a `NoInlining` attribute; Debug and Release exercise warmed callers.

Surface-teleport checks link the production placement transpiler and adapter. They verify one
native helper call and the unchanged deferred teleport queue, asymmetric bounds and all eight
corners, the four lowest-face terrain probes, exact feet/accessory clearance, center-of-mass
conversion, nonidentity orientation and physical angular-rate preservation. Celestial, time,
latitude/longitude, color and the native orbit object survive unchanged. Default/configured EVA,
disposed kittens, ordinary vehicles, direct shared-helper calls and unrelated teleport paths stay
native. Tests also cover explicit control-part independence, mode changes after enqueueing,
disable/unload/reapply, and receiver insertion with preserved branch/exception metadata.
Configured EVA-mode fixtures also retain upright editor/panning while restoring the native flight
frame and RCS map. [Mode lifecycle tests](../iron-man-mode.tests/README.md) additionally exercise the
actual production submod's transition/configuration logic.

A bounded compatibility experiment found that a Harmony postfix on the closed generic
`Vehicle.IsFlightComputerDisabled<Enum>` entry point affects normal virtual calls but can be
bypassed by the emitted nonvirtual call. `AccessTools.MethodDelegate` could not bind this patched
generic signature. The production adapter therefore promises native base policy, not compatibility
with arbitrary patches on generic entry points. The separate nongeneric rendering adapter's
before/after Harmony compatibility tests remain valid.

These tests exercise actual Harmony patch installation and the production call adapters. They do
not replace validation against the game assemblies: fixture predicates model the stock dispatch
shape and restrictions. They do not simulate KSA physics or graphics. In-game acceptance remains
necessary for native HUD layout, attitude target orientation, available RCS/gimbal authority,
thrust, collision, character/part alignment, material effects and secondary viewport rendering.
