# doh.lib — DOH Library

Headless library providing programmatic kitten spawning and per-kitten GPU material customization for KSA. Designed for use by the `doh` mod UI and other mods.

## Modules

### Materials (`Materials/`)

- **`MaterialSystemAccessor`** — Static reflection bridge to `GpuMaterialSystem` and `GpuTextureSystem`. Discovers `Program.Instance` at runtime, caches reflection handles for `CreateObject`, `GetOrLoad`, `AssetMap`, `BigBuffer`, and `DeviceCtx`. Provides:
  - `Initialize()` — one-time discovery of GPU systems
  - `CreateMaterial(name, data)` — registers a new `MaterialData` in the GPU buffer
  - `GetMaterialHandle(name)` / `GetExistingMaterialHandle(name)` — resolve material handles
  - `GetTextureBindlessHandle(name)` — resolve texture bindless handles
  - `WriteAlbedoColor(handle, color)` — staged Vulkan upload to modify AlbedoColor on an existing material
  - `Cleanup()` — reset cached state on unload
  - Pool accounting (`MaterialSystemAccessor.Pool.cs`): `GetCapacity()`, `GetFreeSlotCount()` and
    `GetAvailableSlots()` (free minus the game's reserve); `DestroyMaterial(name)` unmaps a
    doh-created material and returns its slot to the pool. `CreateMaterial` refuses to allocate once
    only `ReservedSlots` (64) slots are left.

- **`MaterialFactory`** — Creates unique `KittenMaterialSet` instances per kitten. Resolves character texture references (`PbrMaterialReference` → `TextureReference` → bindless handles) via reflection, constructs `MaterialData` structs with custom `AlbedoColor`, and registers them in `GpuMaterialSystem`. `Release(set)` (`MaterialFactory.Lifetime.cs`) frees a set's GPU slots and is idempotent. `Cleanup()` releases every set the factory created.

- **`KittenMaterialSet`** — Holds per-kitten GPU material handles with a tint color, the old→new handle map, per-material entries, and the asset names it owns in the pool (`AssetNames`). `UpdateTint()` writes directly to the GPU buffer for live recoloring. `IsReleased` marks a set whose slots were returned; a released set is never reused.

### DohSubmod (`DohSubmod.cs`)

- **`DohSubmod`** — `ISubmod` implementation for unscience supermod integration. Encapsulates the full spawning UI (vehicle/character selection, editable vehicle name/batch prefix, offset, batch count, color picker, kitten list with live recoloring and despawn). Also used by standalone `doh/Mod.cs` to avoid code duplication.
  - The name field starts at `Kitten_doh_` and applies to both spawn buttons. A single spawn uses
    nonblank text verbatim, refusing an occupied name; a batch appends collision-free numbers.
    Blank text uses automatic default-prefix numbering, including for a single spawn.
    "Occupied" means a live vehicle in `CurrentSystem.All` **or** a `Universe.KittenRoster` entry
    (`KittenSpawner.Naming.cs:30`, case-insensitive `KeyHash`). KSA 5707 (rev 5669) binds an EVA
    kitten to the same-named roster entry for radiation dose, sickness, death (KIA) and retirement,
    so a DOH kitten with a rostered name would share that kitten's fate. Unrostered DOH kittens get
    no dose.
  - The spawn target is held as the selected `Vehicle` object, never as a list index. KSA's vehicle
    list swap-removes on deregister, and `VehicleProvider` filters debris, so an index silently moved
    onto another vehicle (often a spawned kitten) whenever something was removed. Spawns then ignored
    the selection and the XYZ offset. A selection the game removes is cleared, not replaced.
  - The status area shows GPU material slot usage and how many more tinted sets fit.
  - `Update()` prunes kittens the game destroyed itself and releases detached material sets left by a
    load with no DOH record. The standalone mod calls it from `OnBeforeUi`.

### Spawning (`Spawning/`)

- **`KittenSpawner`** — Core spawning engine replicating `EVADoor.CreateKittenEva()`. Split into
  partials: `KittenSpawner.cs` (spawn), `.Teardown.cs` (despawn, prune, orphan removal), `.Positioning.cs`,
  `.Materials.cs` (per-kitten cloning), `.Naming.cs` (exact names / unique prefixes) and `.Catalog.cs`
  (backpack part, propellant, characters). Supports:
  - Vehicle-relative positioning with body-frame offset. The anchor is `SpawnRequest.ReferenceVehicle`,
    or an unambiguous `ReferenceVehicleId`. The orbit epoch is the reference state's own `StateTime`,
    as `EVADoor` does.
  - Absolute orbital state positioning (position + velocity + parent body)
  - Batch spawning with chain offsets
  - Optional per-kitten material customization. A tinted batch shares one material set unless
    `UniqueMaterialsPerKitten` or `PerKittenColors` is set.
  - GPU budget preflight: a tinted spawn is refused up front when its sets (~10 slots each) would not
    fit above the reserve. Before each `KittenEva` is constructed, at least 2 free slots are required,
    because its avatar allocates a fur material.
  - If the `KittenEva` constructor throws after the vehicle registered itself, the half-built kitten
    is removed through the same teardown (and deregistered directly if that throws). The result
    reports how many kittens spawned.
  - `Despawn(id)` / `DespawnAll()` — remove spawned kittens and release material sets no other
    kitten uses. Also clears entries whose kitten the game already removed. See
    [kitten teardown](#kitten-teardown-ksa-5638).
  - `PruneDisposed()` — drop entries for kittens the game destroyed and release their materials
  - `RecolorKitten(id, color)` — live tint update
  - `GetAvailableCharacters()` — enumerate character IDs from `ModLibrary`
  - Backpack part: since KSA 5482 the `Part` constructor no longer creates a part tree. The spawner
    calls `part.CreateOwnTree()` before configuring the MMH/NTO tanks and refilling them, as
    `EVADoor.GetBackPackPart` does. Without it, every spawn threw a null reference.

- **`SpawnRequest`** — Input DTO for spawn operations. Key fields: `ReferenceVehicle` / `ReferenceVehicleId`, `OffsetBodyFrame`, `Count`, `VehicleName`, `CharacterId`, `TintColor`, `UniqueMaterialsPerKitten`, `PerKittenColors`. `VehicleName` is exact for a single spawn and a prefix for batches; absent/blank input preserves the existing automatic `DefaultNamePrefix` (`Kitten_doh_`) behavior.

- **`SpawnResult`** / **`SpawnedKittenInfo`** — Result DTOs with per-kitten spawn details. `Warning` reports kittens that spawned untinted. A batch that stops part-way still lists the kittens it spawned.

- **`SpawnedKittenRegistry`** — Dictionary-based tracker for all mod-spawned kittens. Supports register, unregister, get, get-all, clear and `IsMaterialSetInUse`.

## GPU material pool

KSA's `GpuMaterialSystem` is a fixed-size pool of 512 material slots (KSA 5482) that never grows.
When it is full, every allocation throws `Failed to allocate handle for GPU object.`, including the
fur material the game creates inside every `KittenEva` constructor. A tinted doh kitten clones about
9-10 materials. Before this fix, clones were never freed, so roughly 50 tinted spawns filled the pool.
The next `KittenEva` constructor then threw with the kitten already registered, and the game crashed.

doh now:

- keeps 64 slots free for the game, and refuses tinted spawns that would not fit;
- shares one set across a non-unique tinted batch;
- releases a set when its last kitten is despawned or pruned, on unload, and after a load for any
  detached set no restored kitten rebound;
- removes a half-constructed kitten if its constructor still throws.

## Thread Safety

All `KittenSpawner` methods MUST run on the game thread. Callers are responsible for invoking them from a game-thread lifecycle hook.

### Vehicle physics step vs. spawn/despawn

Since KSA build 5402 the game locks the shared BepuPhysics shapes registry while the background vehicle physics step (`VehicleUpdateTask`, queued from `Program.PrepareFrame` on the `JobSystems.VehicleSolver` scheduler) is running. That step overlaps the UI pass where doh's buttons fire. Constructing a `KittenEva` (`BepuHandles.Create`) or disposing one (`BepuHandles.Dispose`) calls `ConstraintSim.UnlockShapes`, which throws `InvalidOperationException: The shapes registry cannot be mutated while the vehicle update is stepping` if the step is still mid-flight. This showed up as a crash when spawning several kittens inside each other, because overlapping kittens make the step slow enough to still be running when the next click lands.

`KittenSpawner` guards both paths with `JobSystems.VehicleSolver.Wait()` (see `WaitForVehicleSolverIdle`) before touching the vehicle. Teardown also joins `JobSystems.NearestOrbitAndPerformanceWorker`, whose hover job walks `CurrentSystem.All`; KSA destroys vehicles before queuing that job each frame, while doh's buttons fire after it. `PrepareFrame` waits on the same scheduler at the start of every frame, so this only moves that wait earlier; nothing re-queues the solver until the next frame, so the rest of the spawn loop is safe. The game's own EVA button avoids the race differently, by staging the spawn in `InputEvents.EvaSpawnBuffer` and applying it at the frame sync point.


### Kitten teardown (KSA 5638)

Every removal goes through the game's own `Universe.DestroyVehicle(vehicle, CrewDisposition.Boarded)`,
as KSA itself now does when a kitten boards a vessel (rev 5607). Earlier builds of doh called
`CurrentSystem.Deregister` and `Vehicle.Dispose` directly, which skipped the game's hand-offs.
`DestroyVehicle` moves any camera following the kitten, including the character-portrait camera
(rev 5486). It clears `Program.ControlledVehicle` and every other vessel's target, and moves the
kitten's particle emitters to the parent body. Then it disposes the kitten, which also deregisters it.

`Boarded` is the only disposition that leaves crew records alone. `EndMission` and `Kill` end or kill a
roster entry named after the vehicle. doh kittens are not roster crew, so neither applies. Material-set
release, the solver-idle wait and the prune, unload and stale-restore order are unchanged. KSA 5707
did not change `DestroyVehicle` or the dispositions. Its new radiation death destroys rostered EVA
kittens only, which the roster-name rule above keeps doh kittens out of.

## Dependencies

- `ksa-abstractions.lib` — VehicleProvider, CelestialProvider
- KSA game DLLs: KSA.dll, Planet.Core.dll, Planet.Render.Core.dll, Brutal.Core.Common.dll, Brutal.Core.Numerics.dll, Brutal.Core.Strings.dll, Brutal.ImGui.dll, Brutal.ImGui.Abstractions.dll, Brutal.Vulkan.dll, Brutal.Vulkan.Abstractions.dll, BepuUtilities.dll
  - **Not** `CommunityToolkit.HighPerformance.dll` — the one call that needed it (`Span<float4>` → bytes)
    now uses the BCL `MemoryMarshal.AsBytes`. That DLL ships with the game but is **not** copied into
    `ksa-game-assemblies/current/dll/`, so referencing it broke any build pointed at that tree.

## Scene saves

The `doh` participant captures spawn controls, each tracked live kitten's native vehicle ID and
character, and private material colors by source/name. Native KSA serialization owns the kittens:
restore only rebinds registry ownership and cloned materials and never spawns duplicates. A runtime
vehicle reference makes capture follow renamed kittens. Shared successful color-write tracking
also preserves Humble Arteest edits to DOH's private material slots.

The additive version-1 `SpawnName` field preserves the editable name/prefix, including blank input.
Legacy records without it, vanilla saves and new scenes reset to `Kitten_doh_`. Null, embedded NUL
or names exceeding the input's 255 UTF-8 byte limit are rejected and retained with diagnostics.
Native KSA owns already-spawned vehicle names. The numeric naming counter is only a runtime hint:
the live native lookup skips occupied names after reload, so the counter needs no sidecar record.

Each kitten also saves an optional `MaterialGroup`, the ID of the set it rendered with, so a shared
batch restores onto one shared set. Records without it, from older builds, restore one set per kitten.

Reset detaches each live set into a cache keyed by kitten and character. Restore reuses a detached
set when its source material handles still match, so reloading the same setup allocates no new
slots. A detached set is claimed once, unless the saved group shares it, so kittens that were unique
in the save are never merged. After restore, detached sets nothing rebound are released. A load with
no DOH record resets without a restore; its detached sets are released on the next frame's `Update`.
Missing kittens and materials are reported rather than replaced. GPU handles and process-local
cloned-material names are never written as durable target identities. The spawn-target vehicle
selection is UI input and resets on load.

Managed checks: [`doh.tests`](../doh.tests/README.md).
