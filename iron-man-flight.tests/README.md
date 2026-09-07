# Iron Man flight and render checks

Managed Harmony checks linking the production flight/render patch files against small fixtures of
KSA's virtual input dispatch, worker snapshot and two-phase rendering. Run:

```sh
dotnet run --project iron-man-flight.tests
```

Covers default-off and per-instance activation, stock base input dispatch without recursive EVA
calls, disable/unload restoration, worker joining, repeated patch installation, early equipment
GPU submission with the avatar retained in the late phase, and visible equipment after flight
mode is disabled. A second render prefix installed before and after Iron Man tests coexistence
without skipped early overrides or duplicated late submissions.

These tests exercise actual Harmony patch installation and the production call adapters. They do
not simulate KSA physics or graphics. In-game acceptance remains necessary for thrust, collision,
character/part alignment, material effects and secondary viewport rendering.
