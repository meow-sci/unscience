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
