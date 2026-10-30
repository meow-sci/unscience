# Iron Man managed checks

Run `dotnet run --project iron-man.tests` from the repository root.

The runner links the production connector component, metadata codec and Harmony patches unchanged.
It exercises actual Harmony constructor-prefix/postfix and serializer-postfix execution against small
managed fixtures, with the real Brutal numerics assembly. The fixtures match the researched KSA seams
as of 5707: a fixed `Connector[]`, a shared empty connection list, and constructor chaining in which
save load `Part(PartInstance, Part)`, the public 4-argument constructor and the copy constructor
`Part(Part, Part)` all reach the private constructor that Iron Man patches.

Checks cover per-instance isolation, up/down normals, scale-safe authoring, occupied connector guards,
XML metadata round-trips, reconstruction before saved connector-index access, independent copies,
unchanged stock saves, malformed/version-mismatched metadata, changed stock connector layouts and
removal.

5707 load and copy checks:
- Load through `Part(PartInstance, Part)` decodes the marker exactly once and accepts index reconnection.
- `PartCloner`-style copies re-create independent, unconnected nodes with the same order, poses and radii.
- Copies reconnect by source index.
- Copies of unmodified kittens and stock parts gain no nodes.

Unload checks cover weak-root discovery, solver wait, retaining attached equipment and bulk-fuel
capability, and rejecting conversions that would lose propellant flow without removing anchors.
These fixtures do not run KSA's native editor, renderer, resource solver or physics; a live
in-game edit/flight/save/reload/unload pass is still required.
