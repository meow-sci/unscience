# DOH managed checks

Run `dotnet run --project doh.tests` from the repository root.

The suite links production DOH save/reset/replay, request DTO, registry, naming and native kill requests,
plus production **War Paint** ownership, scene adapter, preset library, material-set and release code.
Only native spawning, native material access and the ImGui shell are substituted. Preset IO uses a
unique temporary directory removed after a successful run. These checks do not load KSA or Vulkan.

Current suite: **76 checks**.

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
- native kill input submission and deduplication without a mod physics hook; no avatar or paint
  teardown at request time, including when live binding restoration would fail;
- native Kill/Abandoned dispatch, post-death private-slot release, surviving kittens sharing a preset,
  retained global presets after the last user dies, idempotent cleanup and non-kitten/stale rejection;
- saves before/after native dispatch, exact-object safety for same-name reconstructed kittens, and
  isolated failed-constructor rollback;
- **(1, 1, -2) m** defaults for fresh UI/API, vanilla saves and legacy missing offsets, while explicit
  offsets round-trip unchanged.

The old allocation-sharing assertions were replaced because War Paint always owns a private set per
kitten. Formerly shared legacy groups preserve each kitten's effective colors without retaining their
shared editing behavior. DOH itself never releases or caches material sets now.

Native acceptance remains required for physics/render buckets, appearance across different character
assets, attachment/fur rendering, actual GPU pool recovery, load reconstruction and the reported
Windows animation crash. Real-DLL frame-order checks live in `ksa-upgrade.tests`.
See [native kill follow-up](../plans/DOH_NATIVE_KILL_5801.md)
and [scene acceptance](../plans/saves-acceptance.md).
