# KSA Abstractions Library

A foundational shared library providing common abstractions and utilities used across multiple KSA mods. It contains game-access helpers plus small cross-mod UI and data utilities.

## Overview

`ksa-abstractions.lib` serves as a dependency for many other mods, providing reusable patterns for:
- **Part Tree Traversal**: Recursive scanning of vehicle parts and sub-parts
- **Reflection-Based Field Access**: Safe access to private/internal KSA fields
- **Vehicle Lookup**: Game state queries for vehicles and controlled vehicle
- **Simulation Time**: Wrapper around KSA's universe time
- **Shared PNG Catalog**: One `.unscience/pngs` directory and reusable ImGui filesystem importer

## Key Classes & Methods

### PartHelpers
Static utility class for vehicle part operations.

- `GetAllParts(Vehicle vehicle)` - Recursively collects all parts in a vehicle, including nested SubParts
- `GetPartsWhere(Vehicle vehicle, Func<Part, bool> predicate)` - Filters parts by custom predicate

**Key Pattern**: Handles the recursive nature of KSA's part tree, where parts can contain other parts via the `SubParts` collection.

### ReflectionHelpers
Static helper methods for accessing private/internal KSA fields via reflection.

- `GetFieldValue<T>(object target, string fieldName)` - Type-safe field read with null guard
- `SetFieldValue(object target, string fieldName, object value)` - Type-safe field write
- `GetPropertyValue<T>(object target, string propertyName)` - Type-safe property read

**Key Pattern**: Isolates reflection calls for KSA internals (e.g., accessing `_controlledVehicle` from game state objects).

### VehicleProvider
Static helpers for querying vehicle state from the game.

- `GetControlledVehicle()` - Returns the currently player-controlled vehicle (or null if none)
- `GetAllVehicles(bool includeDebris = false)` - Returns the vehicles in the current solar system.
  KSA `2026.9.7.5402` added structural part failure, which sheds fragments as real `Vehicle` objects
  flagged `IsDebris` in the same system collection as crewed craft; they are filtered out by default
  so they don't fill every mod's vehicle picker. Pass `includeDebris: true` when debris is a
  legitimate target — a safety gate that must see everything, or a click/raycast that should hit
  whatever is visible.
- `FindVehicle(string id)` - Resolves an exact stable ID, including debris; returns null when absent or ambiguous.

**Key Pattern**: Provides safe wrappers around KSA's game state queries.

### SimTimeProvider
Wrapper around the KSA universe's simulation time.

- `GetElapsedTime()` - Returns elapsed simulation time in seconds
- `GetDeltaTime()` - Returns delta time since last frame

### HotkeyGuard
Mandatory Harmony prefix on `GameSettings.OnKeyAll` that swallows game hotkeys while any ImGui text input has focus (bypassed while the dev console is open). Every top-level mod applies it via `HotkeyGuard.Patch(harmony)` / `HotkeyGuard.Unpatch(harmony)`.

Since KSA 5482, game actions bound to a **mouse button** are dispatched from `Program.OnMouseButton` without passing `OnKeyAll`, so they are not blocked while typing. All default bindings are keys; only user-assigned mouse bindings are affected.

### PartRenderFilter
Hides selected parts' meshes while the parts stay in their vehicle and keep working (KSA 5482+).
Blinky and It's So Shiny register their render toggles here.

- `PartRenderFilter.Register(harmony, owner, Func<Part, bool> shouldHide)` adds or replaces an owner's predicate; `Unregister(harmony, owner)` removes it; `IsInstalled` reports the shared patches.
- A predicate receives the full part (`Part.FullPart`) once per model instance per viewport per frame, on the main thread. A part is hidden when any owner's predicate returns true.
- The first owner installs one shared prefix/postfix pair on each of `PartTreeRenderData.Compose`, `ComposeDynamic` and `ComposeGlass`; the last owner removes them. All owners share one patch set because two independent compactions would shift each other's ranges.
- The prefix records where each batch's range starts in `PartModel.ViewportData.InstanceList`. The postfix removes hidden slots from the range that `Compose*` just appended and removes the matching `DentInstanceList` entries. Cached render data is never modified, so predicate changes apply on the next frame. Hidden parts cast no shadows.
- Fails open. If the appended count does not match the batch (non-raster path) or the dent list is misaligned, the filter leaves that range unchanged. Any exception disables the filter for the session and logs it; the render loop continues. `Register` throws when the required members are missing.
- String reflection: private `PartTreeRenderData._batches`, `_dynamicBatches` and `_glassBatches`, plus the nested `Batch`/`DynamicBatch`/`GlassBatch` `.Model`, `.Parts` and `.Count`.
- **Known gap:** raytraced IVA submissions go to `RayTraceTransforms`, not the instance lists, so they are not filtered. The KSA 5438 per-module skip also hid those.
- Managed checks: `RenderFilterChecks` in [ksa-upgrade.tests](../ksa-upgrade.tests/README.md).

### IvaForceRender
Shared implementation of Kitchen Sink's **Always Render IVA Interiors**. Hosts call `IvaForceRender.Patch(harmony)` / `Unpatch(harmony)`.

- `Enabled = true` sets `Template.Internal = false` on loaded part-model templates. A `PartModel` constructor postfix catches models created later. Disabling or unpatching restores the changed flags.
- **Editor preview:** internal meshes stay visible in the vehicle editor outside IVA while the patch is installed, whether or not `Enabled` is set. KSA 5482 raster-composes static models in `PartTreeRenderData.Compose` and applies the internal/IVA gate there, without calling `PartModel.AddInstance`. The helper therefore uses a prefix on `Compose` for editor, non-IVA viewports that render part models. The prefix temporarily clears `Template.Internal` on internal, non-`ShadowProxy` templates, and a finalizer restores them even when `Compose` throws. Stock code then appends instances and dents consistently. The template list is rebuilt from `PartModel.Instances` after model creation or restoration.
- Change from 5438: internal meshes no longer appear in editor part thumbnails. The old `AddInstance` postfix also reached thumbnails.

### HiddenUiFrameHook
Keeps per-frame mod work alive while the game HUD is hidden (**F2** / `InputAction.ToggleUi`).

**Why it exists:** StarMap dispatches `[StarMapBeforeGui]` as a prefix of `Program.OnDrawUiFrame` and `[StarMapAfterGui]` as a postfix of `Program.OnDrawUiViewports`. Both sit inside `if (Program.DrawUI)` in `Program.OnFrame`, so while the HUD is hidden neither game method is called and neither StarMap hook fires — every `Update(dt)`-driven feature (weld physics, fuel refill, animations, RPC queue drain, …) silently freezes.

**What it does:** Harmony-prefixes `Program.OnDrawUiConsole(double dt)`, which the game calls unconditionally in the same frame phase (after `PrepareFrame`, inside the ImGui `NewFrame`…`Render` window, before `OnPreRender`). The prefix is a no-op while `Program.DrawUI` is true; when it is false it invokes the registered `BeforeGui` then `AfterGui` callbacks. `DrawUI` only changes during input polling in `PrepareFrame`, so a frame never fires both StarMap's hooks and this fallback.

- `HiddenUiFrameHook.BeforeGui` / `.AfterGui` (`Action<double>?`) — register the non-ImGui parts of your `[StarMapBeforeGui]` / `[StarMapAfterGui]` bodies **before** calling `Patch`
- `HiddenUiFrameHook.Patch(harmony)` / `.Unpatch(harmony)` — apply/remove alongside `HotkeyGuard`; `Unpatch` clears the callbacks
- `HiddenUiFrameHook.IsUiHidden` — `!Program.DrawUI`

ImGui *is* valid inside the callbacks, but hosts should keep window rendering out of them so mod windows honour the hidden HUD.

### PngLibrary and PngFileBrowser

`PngLibrary` owns the shared `KsaPaths.ModDataDir/pngs` catalog used by Graffiti and Free Fallin.
`Import(path, out error)` always copies into the catalog and auto-uniquifies collisions; `Scan()`
and `FullPath(name)` provide the common dropdown/file contract. `PngFileBrowser` is the reusable
ImGui picker and performs that import before returning the catalog file name to its consumer.

```csharp
// Mod.cs
[StarMapBeforeGui] public void OnBeforeUi(double dt) => UpdateSubmods(dt);
[StarMapAfterGui]  public void OnAfterUi(double dt)  { RenderWindows(); UpdateWelds(dt); }

// in [StarMapAllModsLoaded], before Patcher.Patch():
HiddenUiFrameHook.BeforeGui = UpdateSubmods;
HiddenUiFrameHook.AfterGui  = UpdateWelds;

// Patcher.cs
HiddenUiFrameHook.Patch(_harmony);    // in Patch(), next to HotkeyGuard.Patch
HiddenUiFrameHook.Unpatch(_harmony);  // in Unload()
```

## Architecture Notes

- **Reflection-Based**: Relies on reflection rather than HarmonyLib patching, making it stateless and non-invasive
- **Null-Safe**: All field/property reads protect against null references
- **Type-Safe Generics**: Methods use generics to avoid casting at call sites
- **No Side Effects**: All methods are pure utilities with no state mutation

## Usage Examples

### Iterating Vehicle Parts
```csharp
var vehicle = VehicleProvider.GetControlledVehicle();
if (vehicle != null)
{
    var allParts = PartHelpers.GetAllParts(vehicle);
    var enginesOnly = PartHelpers.GetPartsWhere(vehicle, p => p.PartTemplate.EngineModule != null);
}
```

### Reflection-Based Field Access
```csharp
// Access private field without knowing exact type
var value = ReflectionHelpers.GetFieldValue<float>(someObject, "_privateField");
ReflectionHelpers.SetFieldValue(someObject, "_internalState", newValue);
```

### Getting Vehicle State
```csharp
var player = VehicleProvider.GetControlledVehicle();
var allVehicles = VehicleProvider.GetAllVehicles();
var elapsedTime = SimTimeProvider.GetElapsedTime();
```

## Common Patterns

### Part Tree Traversal Pattern
Many mods use the same pattern:
```csharp
var parts = PartHelpers.GetAllParts(vehicle);
foreach (var part in parts)
{
    // Process each part
}
```

This is used by mods like:
- **blinky**: Scanning for pixel grid parts
- **zippo**: Finding light components
- **garrys-torch**: Vehicle inspection

### Reflection Pattern
Mods frequently access private KSA internals:
```csharp
var lightComponents = ReflectionHelpers.GetFieldValue<List<object>>(part, "_lightData");
```

This is used by:
- **zippo**: Reading/writing light intensity and color
- **glass**: Accessing camera FOV fields
- **kitten-animations**: Accessing avatar state

## Notes for Future Development

- When adding new reflection helpers, consider caching field/property lookups for performance
- Follow the null-guard pattern: field access should return default/null rather than throwing
- Keep methods generic and reusable—avoid mod-specific logic in this library
- Document the exact KSA class/field names being accessed for debugging purposes

## Shared physics handoff and scale ownership

`PhysicsFrameHook.Apply/Remove(Harmony)` owns the validated `Program.PrepareFrame(double,double)`
transpiler formerly in Garry's Torch. `Enqueue(Action)` schedules main-thread mutations after all
solver results and before `BeforePhysics(double playerDelta, UniverseTime committedTime)` listeners
and next cloth/vehicle/orbit snapshots. Actions queued while dispatching wait until the next frame;
actions are discarded when the system is absent or the hook is removed. Callers must ignore their
queued actions after disposal. Exceptions are isolated per action/listener. Garry's Torch installs
this hook for Unscience and subscribes its weld callback; Godzilla queues edits and restores.

`JoinOrbitReaders()` waits for `JobSystems.NearestOrbitAndPerformanceWorker`, at most once per frame.
In KSA 5482, `PrepareFrame` queues the nearest-orbit job on that worker just before this handoff. The
job reads flight plans and cached orbit points, and `Vehicle.Teleport` or other flight-plan edits
dispose those points. The hook joins automatically before a deferred world change and before
draining queued mutations. `BeforePhysics` listeners that move vessels must call it before mutating;
Garry's Torch and Dent Wizard do. Idle frames do not wait.

`VehicleScaleOwnership` keeps weak vehicle keys with tool names. `TryAcquire`, `GetOwner` and
owner-checked `Release` prevent Godzilla/Garry's Torch from replacing one another's scale state.

## Shared imported media

`SharedFileLibrary` supplies a flat copied catalog with case-insensitive extension filtering,
numbered collision names, no overwrites, sorted scans and catalog-only `FullPath` names.
`LibraryFileBrowser` supplies folder navigation, quick links, filtering, refresh, double-click or
explicit Import, and visible filesystem errors. `PngLibrary`/`PngFileBrowser` retain their public
APIs as facades; `SoundLibrary.Files` adds `.unscience/sounds` for OGG/WAV/MP3. Consumers poll scans
or use Refresh to discover externally added files; decoding remains the consumer's responsibility.
Managed filesystem and loop checks: `dotnet run --project byo-music.tests`.

`GlbLibrary.Files` adds `.unscience/glbs` (case-insensitive .glb, 128 MiB copy limit). Its
`SelectionId/FileName/IsSelection/Label` helpers represent lazy picker choices only. A GLB consumer
must resolve these into content-version identities before retaining a live recipe. `SharedFileLibrary`
now accepts an optional pre-copy byte limit; PNG/sound defaults remain unrestricted. Shared GLB
content tests live in `pebbles.tests`.

## Scene persistence

`Persistence/` contains the shared `ISaveParticipant` / `ISaveParticipantSource` contract,
`SaveParticipant<T>` typed adapter, `SaveJson`, versioned `SaveDocument`, bounded atomic
`SaveStorage`, conservative `SavedPartReference`, `SceneSaveCoordinator`, and `NativeSaveHooks`.
Feature libraries own detached recipes and normal reset/apply APIs; the host wires callbacks.
`MaterialColorState` tracks successful GPU albedo writes for cooperative appearance capture.
Allocation owners record their authored initial color and call `Forget` when releasing the
material, because GPU buffer handles can be reused and are never persistent identities.

Restore runs in ascending `RestoreOrder`; cleanup reverses it. `PrepareRestore` must validate
without mutating native state. `SaveRestoreContext.Warn` marks a partial restore: the source feature
record is retained instead of silently dropping missing entries on the next save. `Info` is for
nonfailure notices. Failed capture keeps the last good record when available. Unsupported feature
versions remain opaque. Reset and replay errors are isolated and visible in the host.

Part references use vehicle IDs and full-part/subpart tree addresses with template and whole-tree
structural signature checks. `BeginOperation()` caches one bounded traversal per vehicle for a
synchronous capture/rebind transaction on the game thread; never hold it across frames. They use neither
runtime IDs nor name fallback. Their validity is scoped to the hash-bound native save; they are not
an arbitrary cross-craft matching API. The JSON serializer retains only primitive coordinate axes for Brutal vectors, excludes recursive
swizzles, and rejects nonfinite floating-point values including exponent overflow. It is for explicit DTOs only, not live game
objects. Sidecars are limited to 32 MiB/depth 64 and paired to SHA-256 of universe.xml.

`NativeSaveHooks` publishes `Written` only when KSA's `UncompressedSave.Write()` returns true.
Since KSA 5482 a failed native save returns false instead of throwing. This happens when
`SaveDirectory.TryReplace` cannot delete the old folder, or when writing metadata or the universe
fails. The previous save folder can then survive. The hook raises `WriteFailed` instead, so no sidecar
from this session is written into the old save. KSA 5482 also catches an unreadable `universe.xml`,
logs it and returns before `Universe.DeserializeSave`. When a file load never reaches reconstruction,
`FinishedLoading` sets `LastLoadError` to an `InvalidDataException` and raises `LoadFailed`; the
current scene is unchanged. The reset join waits for `JobSystems.NearestOrbitAndPerformanceWorker`
(renamed from `ConcurrentWorkers` in 5482).

See [save integration](../scope/saves.md), [plan](../plans/SAVES.md) and
[managed checks](../saves.tests/README.md). GPU/native acceptance is separate from managed tests.

## KSA 5482 compatibility

Verified against KSA `2026.9.22.5482` by build and managed checks only. There has been no native run.

- New `PartRenderFilter` replaces the removed per-module `*Module.UpdateRenderData` render-skip targets.
- `IvaForceRender` reveals editor internals through `PartTreeRenderData.Compose` instead of the `AddInstance` sink.
- `PhysicsFrameHook.JoinOrbitReaders` joins the nearest-orbit job before vessel moves at the handoff.
- `NativeSaveHooks` reports failed native writes (`WriteFailed`) and unreadable saves (`LoadFailed`), and joins the renamed `NearestOrbitAndPerformanceWorker`.

Still needs in-game acceptance: Blinky/Shiny hiding in main, portrait and shadow views; editor IVA
internals; a locked-folder save and a corrupted-`universe.xml` load.

## KSA 5438 compatibility

The KSA 5438 build adds an explicit compile-only Planet.Render.Core reference for native save identity hashes. IVA rendering binds the common dent-aware part submission overload. Shared hotkey, hidden-HUD and physics handoff contracts were checked against both game builds.
