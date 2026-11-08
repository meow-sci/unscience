# DOH managed checks

Run `dotnet run --project doh.tests` from the repository root.

The suite links production DOH save/reset/replay, request DTO, registry, naming and deferred teardown,
plus production **War Paint** ownership, scene adapter, preset library, material-set and release code.
Only native spawning, native material access and the ImGui shell are substituted. Preset IO uses a
unique temporary directory removed after a successful run. These checks do not load KSA or Vulkan.

Current suite: **74 checks**.

Coverage includes:

- exact names, Unicode/blank saved names, batch prefixes, roster reservations, stale native IDs and
  malformed name retention;
- unchanged `doh` v1 replay, legacy shared `MaterialGroup` migration to independent private paints,
  old tint/source/name material colors, stock kittens, old enabled/disabled UI color flags, deterministic
  preset import with collision protection, and separate central paint capture;
- detached spawn preset capture, explicit new-preset precedence over legacy color fields, independent
  per-kitten recipes, source/deferred-request mutation protection and deleted-global-preset fallback;
- same-scene/repeated/cross-scene replay without duplicate kittens or extra GPU allocation, missing
  targets, changed characters and malformed group diagnostics/retention, vanilla cleanup and central slot release;
- production native teardown ordering: restore original paint bindings, release that kitten's private
  set, join readers and call `Universe.DestroyVehicle(..., Boarded)`. A failed paint restore prevents
  destruction and preserves ownership; already-dead kittens are not destroyed twice;
- eight-kitten deferred removal after pending physics results commit, exact registration checks against
  same-name restored kittens, and rejection when the handoff is unavailable;
- **(1, 1, -2) m** defaults for fresh UI/API, vanilla saves and legacy missing offsets, while explicit
  offsets round-trip unchanged.

The old allocation-sharing assertions were replaced because War Paint always owns a private set per
kitten. Formerly shared legacy groups preserve each kitten's effective colors without retaining their
shared editing behavior. DOH itself never releases or caches material sets now.

Native acceptance remains required for physics/render buckets, appearance across different character
assets, attachment/fur rendering, actual GPU pool recovery, load reconstruction and the reported
Windows shutdown/despawn access violation. See [5801 follow-up](../plans/DOH_DESPAWN_5801_FOLLOWUP.md)
and [scene acceptance](../plans/saves-acceptance.md).
