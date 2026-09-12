# Saves managed checks

Run `dotnet run --project saves.tests/saves.tests.csproj` (use `-p:NuGetAudit=false` for offline
restores when the dependency vulnerability feeds are unavailable).

The executable links the actual NativeSaveHooks implementation to small native-signature fixtures
and applies real Harmony patches. It verifies transaction ordering, worker joins before cleanup,
queued-action invalidation, editor refusal, capture/write/read/reconstruction errors, direct and
new-system loads, callback isolation, repeatability and targeted unpatching. These fixtures do not
initialize KSA native graphics or physics and do not replace an in-game save/load smoke test.

Whole-load requests are held until the frame dispatcher replays them; the hook fixtures verify
there is no preflight/reset at UI dispatch and only the latest request runs. Production dispatcher
ordering is separately exercised by `garrys-torch.tests`, including computing the next simulation
time only after the loaded world replaces the old one. Storage/coordinator checks also run from
this executable through `StorageTests.Run()`.
