# Iron Man managed checks

Run `dotnet run --project iron-man.tests` from the repository root.

The runner links the production connector component, metadata codec and Harmony patches unchanged.
It exercises actual Harmony constructor-prefix/postfix and serializer-postfix execution against small
managed fixtures matching the researched KSA 5402 seams, with the real Brutal numerics assembly.

Checks cover per-instance isolation, up/down normals, scale-safe authoring, occupied connector guards,
XML metadata round-trips, reconstruction before saved connector-index access, independent copies,
unchanged stock saves, malformed/version-mismatched metadata, changed stock connector layouts and
removal.

Unload checks cover weak-root discovery, solver wait, retaining attached equipment and bulk-fuel
capability, and rejecting conversions that would lose propellant flow without removing anchors.
These fixtures do not run KSA's native editor, renderer, resource solver or physics; a live
in-game edit/flight/save/reload/unload pass is still required.
