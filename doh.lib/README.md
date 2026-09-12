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

- **`MaterialFactory`** — Creates unique `KittenMaterialSet` instances per kitten. Resolves character texture references (`PbrMaterialReference` → `TextureReference` → bindless handles) via reflection, constructs `MaterialData` structs with custom `AlbedoColor`, and registers them in `GpuMaterialSystem`.

- **`KittenMaterialSet`** — Holds per-kitten GPU material handles (body, head, eye, fur) with a tint color. `UpdateTint()` writes directly to the GPU buffer for live recoloring.

### DohSubmod (`DohSubmod.cs`)

- **`DohSubmod`** — `ISubmod` implementation for unscience supermod integration. Encapsulates the full spawning UI (vehicle/character selection, offset, batch count, color picker, kitten list with live recoloring and despawn). Also used by standalone `doh/Mod.cs` to avoid code duplication.

### Spawning (`Spawning/`)

- **`KittenSpawner`** — Core spawning engine replicating `EVADoor.CreateKittenEva()`. Supports:
  - Vehicle-relative positioning with body-frame offset
  - Absolute orbital state positioning (position + velocity + parent body)
  - Batch spawning with chain offsets
  - Optional per-kitten material customization
  - `Despawn(id)` / `DespawnAll()` — remove spawned kittens
  - `RecolorKitten(id, color)` — live tint update
  - `GetAvailableCharacters()` — enumerate character IDs from `ModLibrary`

- **`SpawnRequest`** — Input DTO for spawn operations. Key fields: `ReferenceVehicleId`, `OffsetBodyFrame`, `Count`, `CharacterId`, `TintColor`, `UniqueMaterialsPerKitten`, `PerKittenColors`.

- **`SpawnResult`** / **`SpawnedKittenInfo`** — Result DTOs with per-kitten spawn details.

- **`SpawnedKittenRegistry`** — Dictionary-based tracker for all mod-spawned kittens. Supports register, unregister, get, get-all, and clear operations.

## Thread Safety

All `KittenSpawner` methods MUST run on the game thread. Callers are responsible for invoking them from a game-thread lifecycle hook.

### Vehicle physics step vs. spawn/despawn

Since KSA build 5402 the game locks the shared BepuPhysics shapes registry while the background vehicle physics step (`VehicleUpdateTask`, queued from `Program.PrepareFrame` on the `JobSystems.VehicleSolver` scheduler) is running. That step overlaps the UI pass where doh's buttons fire. Constructing a `KittenEva` (`BepuHandles.Create`) or disposing one (`BepuHandles.Dispose`) calls `ConstraintSim.UnlockShapes`, which throws `InvalidOperationException: The shapes registry cannot be mutated while the vehicle update is stepping` if the step is still mid-flight. This showed up as a crash when spawning several kittens inside each other, because overlapping kittens make the step slow enough to still be running when the next click lands.

`KittenSpawner` guards both paths with `JobSystems.VehicleSolver.Wait()` (see `WaitForVehicleSolverIdle`) before touching the vehicle. `PrepareFrame` waits on the same scheduler at the start of every frame, so this only moves that wait earlier; nothing re-queues the solver until the next frame, so the rest of the spawn loop is safe. The game's own EVA button avoids the race differently, by staging the spawn in `InputEvents.EvaSpawnBuffer` and applying it at the frame sync point.


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

Private GPU slots are cached by kitten/character across normal loads and reused when shared source
material handles still match. This prevents repeated loading of the same setup from consuming new
slots each time; KSA still owns the global material allocator and unique, never-before-seen setups
can allocate new slots. Missing kittens/materials are reported rather than replaced. GPU handles and
process-local cloned-material names are never written as durable target identities.
