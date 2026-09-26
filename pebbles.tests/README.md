# Pebbles managed checks

Run `dotnet run --project pebbles.tests/pebbles.tests.csproj` from the repository root.
This executable links Pebbles' game-independent models, hull math and Workshop math/state, plus the
shared managed save serializer, directly; it requires neither game assemblies nor the newux
workspace/contracts libraries.

Ported checks cover detached recipe copying and validation, mesh assignment across variants
and LODs, collider scaling, untouched clutter types, camera projection and gizmos, undo/redo,
GLB parsing and scene transforms, material identity, compatibility fallbacks, texture mapping
and pixel conversion. `HullChecks` verify the hull volume/recentring math ported from KSA 5447 on
known polyhedra; `ClutterStateChecks` verify grid-memory carry-over (AND masks, authoritative vs
merged displaced records, per-spacing isolation, seed/export) and the `pebbles.clutter-state`
record's validation and round trip through the linked production `SaveParticipant`/`SaveJson`;
`PebblesChecks` covers the legacy-recipe `KeepBackfaceNormals` default. Failure throws and returns a nonzero exit code.

These checks cannot exercise Harmony, native image decoding, Vulkan rendering/resource
retirement or game physics. In-game smoke checks are listed in `../pebbles.lib/README.md`.

Shared catalog checks additionally link `SharedFileLibrary`/`GlbLibrary` against an isolated temporary
filesystem: actual GLB fixture bytes survive source deletion, recipe identities use the managed copy,
content changes alter the hash, lazy picker ids differ from frozen ids, and oversized copies fail early.
