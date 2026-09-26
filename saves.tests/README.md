# Saves managed checks

Run `dotnet run --project saves.tests/saves.tests.csproj` (use `-p:NuGetAudit=false` for offline
restores when the dependency vulnerability feeds are unavailable).

The executable links the actual NativeSaveHooks implementation to small native-signature fixtures
and applies real Harmony patches. It verifies transaction ordering, worker joins before cleanup,
queued-action invalidation, editor refusal, capture/write/read/reconstruction errors, direct and
new-system loads, callback isolation, repeatability and targeted unpatching. These fixtures do not
initialize KSA native graphics or physics and do not replace an in-game save/load smoke test.

KSA 5482 behavior is mirrored by the fixtures. `UncompressedSave.Write()` returns `bool`, where
`FailWrite` returns false and `ThrowWrite` throws. `Load()` returns without reconstruction on an
unreadable file (`FailRead`). The worker join is `JobSystems.NearestOrbitAndPerformanceWorker`, and
its trace label is `join nearest-orbit-and-performance`. The checks confirm:

- a native write that returns false keeps its result, raises `WriteFailed` and publishes no `Written`
  sidecar callback;
- a throwing capture or write still skips sidecar callbacks;
- an unreadable save raises `LoadFailed` once with an `InvalidDataException`, sets `LastLoadError`
  and neither resets nor restores;
- the next successful load clears that error, and callback isolation still reports it.

Whole-load requests are held until the frame dispatcher replays them; the hook fixtures verify
there is no preflight/reset at UI dispatch and only the latest request runs. Production dispatcher
ordering is separately exercised by `garrys-torch.tests`, including computing the next simulation
time only after the loaded world replaces the old one. Storage/coordinator checks also run from
this executable through `StorageTests.Run()`.

`KittenPhaseTests` links the production narrow playback-clock adapter against native-shaped managed
fixtures. It verifies matching-clip capture, rejection of another clip's clock, direct frozen-pose
sampling, repeatability and invalid-time rejection. In-progress native cross-fades remain outside
these checks and are documented as a selected-clip-pose restore.

Part identity fixtures exercise changing runtime IDs, duplicate templates, nested subparts,
whole-tree topology mismatch, missing/ambiguous vehicles and transaction-scoped capture/rebind.
The linked production serializer rejects float/double exponent overflow as well as invalid JSON.
Native prerequisite tests reject missing used part/subpart templates, characters, parent bodies,
duplicate vehicle IDs, mismatched celestial systems, and malformed time/camera/roster metadata
before reset. A concurrent-worker join failure also aborts before cleanup/native destruction and
unwinds the file transaction exactly once.
`MaterialOwnershipTests` links production Humble Arteest color persistence against a managed asset
map with non-string struct keys. It verifies stable-name capture, exact-asset original restoration,
recycled/moved slot rejection, and ownership of generated DOH/Free Fallin names.
