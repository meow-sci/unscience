# Saves managed checks

Run `dotnet run --project saves.tests/saves.tests.csproj` (use `-p:NuGetAudit=false` for offline
restores when the dependency vulnerability feeds are unavailable).

The executable links the actual NativeSaveHooks implementation to small native-signature fixtures
and applies real Harmony patches. It verifies transaction ordering, worker joins before cleanup,
queued-action invalidation, editor refusal, capture/write/read/reconstruction errors, direct and
new-system loads, callback isolation, repeatability and targeted unpatching. These fixtures do not
initialize KSA native graphics or physics and do not replace an in-game save/load smoke test.

KSA 5638 behavior is mirrored by the fixtures. `UncompressedSave.Write()` returns `bool`, where
`FailWrite` returns false and `ThrowWrite` throws. `Load()` returns without reconstruction on an
unreadable file (`FailRead`). The reset join is `JobSystems.WaitAll()`, which traces
`join nearest-orbit-and-performance`, `join orbit`, `join vehicle` and `join cloth` in that order.
Native `DeserializeSave` runs the same join, then skips vehicles with a missing parent, missing part
templates or a missing character (`native skipped <id>`), as KSA 5632/5633 does. The checks confirm:

- a native write that returns false keeps its result, raises `WriteFailed` and publishes no `Written`
  sidecar callback;
- a throwing capture or write still skips sidecar callbacks;
- an unreadable save raises `LoadFailed` once with an `InvalidDataException`, sets `LastLoadError`
  and neither resets nor restores;
- the next successful load clears that error, and callback isolation still reports it.

KSA 5916 star-system switching is mirrored by `SystemSwapFixtures.cs`. `Load()` hands the save to
`SaveSystemSwap.RequestLoad(id, system, ApplyLoaded)`. A contained system applies in place, another
installed system is queued and applied by `SaveSystemSwap.OnFrame()` through `Program.SwapSystem`
(`native teardown`, `native new system <id>`), and an uninstalled one only alerts. `SystemSwapChecks.cs`
verifies:

- `LoadIntoRunning` loads in place, and other systems are still refused before reset for direct loads;
- a queued switch is no failure, resets once before teardown, deserializes immediately (no deferral
  past `OnGameLoaded`) without a second reset, and restores once;
- the sidecar survives the switch through the real coordinator, including a capture while the switch is
  queued that keeps previously retained records;
- an uninstalled system is reported by name;
- multiplayer join switches reset first and leave no stale marker;
- a join that replaces a queued save switch ends that save's transaction once;
- `WontFit` and a direct `SwapSystem` leave no continuation;
- a negative control unpatches the two 5916 seams and reproduces the 5901-hook symptoms.

Whole-load requests are held until the frame dispatcher replays them; the hook fixtures verify
there is no preflight/reset at UI dispatch and only the latest request runs. Production dispatcher
ordering is separately exercised by `garrys-torch.tests`, including computing the next simulation
time only after the loaded world replaces the old one. Storage/coordinator checks also run from
this executable through `StorageTests.Run()`.

`RetiredFeatureTests` writes a legacy sidecar through the production `SaveStorage` containing the
`parts-now` record that the removed Parts Now submod produced (version 1, `SavedDependency[]`, both
the common empty list and a populated one) next to a live feature. Loading it through the production
`SceneSaveCoordinator` must restore the live feature, report the unowned record once, retain it, and
carry it forward unchanged through a capture, **Use current setup** and a rewrite.

`KittenPhaseTests` links the production narrow playback-clock adapter against native-shaped managed
fixtures. It verifies matching-clip capture, rejection of another clip's clock, direct frozen-pose
sampling, repeatability and invalid-time rejection. In-progress native cross-fades remain outside
these checks and are documented as a selected-clip-pose restore.

Part identity fixtures exercise changing runtime IDs, duplicate templates, nested subparts,
whole-tree topology mismatch, missing/ambiguous vehicles and transaction-scoped capture/rebind.
The linked production serializer rejects float/double exponent overflow as well as invalid JSON.
Native prerequisite tests still reject, before reset, the data native reconstruction cannot
survive: malformed time/camera/roster metadata, a missing celestial system or one KSA would not load in
place (`SaveSystemSwap.PlanLoad` other than `Load`/`LoadIntoRunning`), blank or
duplicate (case-insensitive) vehicle IDs, a vehicle without a parent-body record, and null, cyclic or
deeper-than-256 part trees. Vehicles that KSA skips no longer refuse the load. A missing part/subpart
template, parent body or character produces one `LoadWarning` per vehicle, after reset and before
native destruction, for both file and direct loads. Each warning names every missing template once.
A round trip through the real `SceneSaveCoordinator` shows that the skipped vehicle is reported by
the preflight and by its participant, and that only the reconstructed vehicle is rebound. The
unresolved record is retained across repeated loads and into the next save. A concurrent-worker join
failure also aborts before cleanup/native destruction and unwinds the file transaction exactly once.
The retired Humble global material ownership fixture moved with the feature to
[War Paint checks](../war-paint.tests/README.md). War Paint owns per-kitten recipes, private material
bindings and migration; this suite retains the shared native lifecycle/storage contracts.
