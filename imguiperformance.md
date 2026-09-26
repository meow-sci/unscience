# ImGui Render-Path Performance Audit

This document catalogues every sub-mod whose `RenderContent()` / `Render()` ImGui code performs work that can be trivially cached. Each finding lists the exact file, line numbers, what the code does today, why it is expensive, and a concrete remediation plan a coding agent can execute without ambiguity.

> **Convention used throughout:**
> - **CRITICAL** – causes per-frame allocations, LINQ chains, or game API calls that scale with data size.
> - **HIGH** – per-frame string allocations or repeated lookups that are easy to cache.
> - **MEDIUM** – small per-frame cost that adds up across many sub-mods.
> - **LOW** – minor or conditional cost; fix opportunistically.

---

## Table of Contents

1. [Cross-Cutting Patterns](#1-cross-cutting-patterns)
2. [marque.lib](#2-marquelib)
3. [blinky.lib](#3-blinkylib)
4. [steely-eyed-missile-kitten.lib](#4-steely-eyed-missile-kittenlib)
5. [eternal-flame.lib](#5-eternal-flamelib)
6. [garys-torch.lib](#6-garys-torchlib)
7. [kiwis-marbles.lib](#7-kiwis-marbleslib)
8. [zippo.lib](#8-zippolib)
9. [con-man.lib](#9-con-manlib)
10. [geeforce.lib](#10-geeforcelib)
11. [average-twr.lib](#11-average-twrlib)
12. [humble-arteest.lib](#12-humble-arteestlib)
13. [skittles.lib](#13-skittleslib)
14. [camera-controller-override.lib](#14-camera-controller-overridelib)
15. [i-feel-seen.lib](#15-i-feel-seenlib)
16. [inanimate-carbon-rod.lib](#16-inanimate-carbon-rodlib)
17. [grant (supermod)](#17-grant-supermod)
18. [glass.lib](#18-glasslib)
19. [kitten-animations.lib](#19-kitten-animationslib)

---

## 1. Cross-Cutting Patterns

These anti-patterns recur across many sub-mods. Each per-mod section below references which of these patterns apply. Fix the pattern once in a shared helper or apply the same fix consistently across all affected mods.

### Pattern A – `VehicleProvider.GetAllVehicles()` called every frame in render code

**What happens:** The game API enumerates all active vehicles and returns a new collection each call. Multiple sub-mods call this inside `RenderContent()` every frame, sometimes more than once in the same frame.

**Why it is expensive:** Each call iterates the universe vehicle list and allocates a new `IReadOnlyList`. When multiple sub-mods (grant aggregates 15+) all do this in the same frame, the game API is hammered dozens of times per frame for the same data.

**Remediation:**
1. Add a `CachedVehicleList` (or similar) helper to `ksa-abstractions.lib` that caches the result for the current frame. The cache should store the list along with the frame number (`Time.frameCount` or similar) and invalidate when the frame advances.
2. Each sub-mod replaces `VehicleProvider.GetAllVehicles()` in render methods with the cached helper.
3. The same approach applies to `CelestialProvider.GetAllCelestials()` and `CelestialProvider.GetAllOrbiters()`.

**Affected mods:** eternal-flame.lib, garys-torch.lib, kiwis-marbles.lib, zippo.lib, i-feel-seen.lib, humble-arteest.lib (VehiclePaintSubmod), blinky.lib, marque.lib.

---

### Pattern B – LINQ chains (`.Select()`, `.OrderBy()`, `.Distinct()`, `.ToList()`, `.ToArray()`) in render code

**What happens:** Render methods build intermediate collections using LINQ every frame. Each LINQ operator allocates an enumerator, and terminal operators (`.ToList()`, `.ToArray()`) allocate a new collection.

**Why it is expensive:** These allocations happen at 60+ fps. The results are identical frame-to-frame unless the underlying data changes (vehicles dock, undock, etc.), which is rare.

**Remediation:**
1. Cache the result in a field and recompute only when the source data changes (e.g., vehicle count changes, a weld is added/removed, etc.).
2. Where sorting is needed, sort once on data change, not every frame.
3. Where `.ToList()` is only used to avoid concurrent-modification during iteration, use a pre-allocated `List<T>` field and call `.Clear()` + `.AddRange()` to reuse the buffer.

**Affected mods:** blinky.lib, marque.lib, eternal-flame.lib, garys-torch.lib, kiwis-marbles.lib, steely-eyed-missile-kitten.lib (MissionUI), zippo.lib.

---

### Pattern C – String interpolation / `ToString()` / `string.Format()` in render loops

**What happens:** Formatted strings (`$"{value:F2}"`, `$"label {count}"`) are created every frame inside `RenderContent()`. Each interpolation allocates a new string on the managed heap.

**Why it is expensive:** GC pressure. At 60 fps with 15 sub-mods each creating 5-10 strings, that is 300-600 short-lived string allocations per frame.

**Remediation:**
1. For values that change infrequently (vehicle counts, label text), cache the formatted string in a field and regenerate only when the underlying value changes.
2. For values that change every frame (e.g. telemetry numbers), accept the allocation cost but reduce string count by combining multiple values into a single formatted string where possible.
3. For ImGui IDs that use string concatenation (`"label" + "##id"`), use `ImGui.PushID(int)` / `ImGui.PopID()` with integer IDs instead of string concatenation.

**Affected mods:** Nearly all — average-twr.lib, geeforce.lib, eternal-flame.lib, garys-torch.lib, kiwis-marbles.lib, steely-eyed-missile-kitten.lib (all 3 UI files), camera-controller-override.lib, con-man.lib, blinky.lib, humble-arteest.lib, inanimate-carbon-rod.lib, glass.lib, marque.lib.

---

### Pattern D – `new float2()` / `new float4()` color structs in render code

**What happens:** Color constants and padding values are constructed as new struct instances every frame: `new float4(0.4f, 1.0f, 0.4f, 1.0f)`.

**Why it is expensive:** While struct allocations are stack-based in C# and cheaper than heap allocations, ImGui may box them. More importantly, these are constant values that should be `static readonly` fields.

**Remediation:**
1. Promote all constant color and padding values to `private static readonly` fields.
2. For colors that vary at runtime, cache the converted `uint` via `ImGui.GetColorU32()` in a field and update only when the color value changes.

**Affected mods:** average-twr.lib, geeforce.lib, con-man.lib.

---

## 2. marque.lib

**Severity: CRITICAL**

### Finding 2.1 – `GetAllOrbiters()` + `.OrderBy().ToList()` every frame the menu is open

**File:** `marque.lib/MarqueLib.cs`
**Lines:** 45, 63, 71, 101, 128-131, 195-197

**Current behavior:**
- `DrawVehiclesMenu()` (line 71): calls `GetAllVehicles()`, then `vehicles.OrderBy(v => v.Id).ToList()` — sorts and allocates a new list every frame.
- `DrawSolMenu()` (lines 45, 101, 128-131): calls `GetAllOrbiters()`, then chains `.OfType<Celestial>().ToList()` and `.OrderBy(c => c.Id).ToList()` multiple times per frame.
- `DrawEverythingMenu()` (lines 195-197): calls `GetAllOrbiters()` then `.OrderBy().ToList()`.

**Why it is expensive:** Each `.OrderBy()` allocates a sorted buffer, each `.ToList()` allocates a new list. Combined with the game API call, this is the most expensive render path in the entire mod suite.

**Remediation:**
1. Cache the sorted vehicle list in a `List<Vehicle>` field. Invalidate the cache when the vehicle count changes (compare `vehicles.Count` to a stored count).
2. Cache the celestial hierarchy (sun → planets → moons) in a tree structure. Rebuild only when the celestial count changes.
3. Cache the "everything" sorted list similarly.
4. Move all `GetAllVehicles()` / `GetAllOrbiters()` calls out of the draw methods and into an `Update(dt)` or a per-frame cache that is populated once before any ImGui rendering begins.

### Finding 2.2 – Per-item cast and string comparison in filter loop

**File:** `marque.lib/MarqueLib.cs`
**Lines:** 208-216

**Current behavior:**
```csharp
foreach (var orbiter in allOrbiters) {
    var id = (orbiter as Astronomical)?.Id ?? "";  // cast per item
    if (!_everythingFilter.PassFilter(id)) continue;
```

**Remediation:**
Cache the `(id, orbiter)` pairs when the orbiter list is rebuilt, avoiding the cast every frame.

---

## 3. blinky.lib

**Severity: CRITICAL**

### Finding 3.1 – `.Select().Distinct().Count()` LINQ chain every frame

**File:** `blinky.lib/BlinkySubmod.cs`
**Lines:** 96-98

**Current behavior:**
```csharp
int vehicleCount = grids.Count > 0
    ? grids.Values.Select(s => s.VehicleId).Distinct().Count()
    : 0;
```

**Why it is expensive:** Three LINQ operators allocate enumerators, a `HashSet<string>`, and iterate the entire grid collection every frame.

**Remediation:**
1. Maintain a `HashSet<string> _distinctVehicleIds` field. Update it when grids are added or removed (in `BlinkyGridManager`).
2. Replace the LINQ chain with `_distinctVehicleIds.Count`.

### Finding 3.2 – `.ToList()` on `grids.Values` every frame

**File:** `blinky.lib/BlinkySubmod.cs`
**Line:** 116

**Current behavior:**
```csharp
foreach (var gs in grids.Values.ToList())
```

**Why it is expensive:** Allocates a new `List<GridState>` every frame containing all grid values.

**Remediation:**
1. If the purpose is to avoid concurrent modification, use a reusable `List<GridState>` field: `_gridSnapshot.Clear(); _gridSnapshot.AddRange(grids.Values);`.
2. Better: iterate `grids.Values` directly if grids are not modified during rendering.

### Finding 3.3 – Linear search through all grids to check vehicle membership

**File:** `blinky.lib/BlinkySubmod.cs`
**Lines:** 146-151

**Current behavior:**
```csharp
bool hasGrid = false;
foreach (var gs in BlinkyGridManager.Grids.Values)
{
    if (gs.VehicleId == vehicle.Id) { hasGrid = true; break; }
}
```

**Remediation:**
Maintain a reverse lookup `Dictionary<string, List<string>>` (vehicleId → gridNames) in `BlinkyGridManager`, updated on grid add/remove.

### Finding 3.4 – String concatenation for grid IDs and vehicle selectables in render loop

**File:** `blinky.lib/BlinkySubmod.cs`
**Lines:** 271, 403

**Remediation:**
Use `ImGui.PushID(index)` / `ImGui.PopID()` instead of string concatenation for ImGui IDs.

---

## 4. steely-eyed-missile-kitten.lib

**Severity: CRITICAL**

### Finding 4.1 – `Enum.GetValues<FlightEventType>()` in render loop

**File:** `steely-eyed-missile-kitten.lib/UI/EventFeedUI.cs`
**Line:** 34

**Current behavior:**
```csharp
foreach (FlightEventType evtType in Enum.GetValues<FlightEventType>())
```

**Why it is expensive:** `Enum.GetValues<T>()` allocates a new array every call via reflection.

**Remediation:**
Cache as a `private static readonly FlightEventType[] AllEventTypes = Enum.GetValues<FlightEventType>();` field and iterate the cached array.

### Finding 4.2 – `evtType.ToString()` + string concatenation per enum value per frame

**File:** `steely-eyed-missile-kitten.lib/UI/EventFeedUI.cs`
**Line:** 37

**Current behavior:**
```csharp
evtType.ToString() + "##feed_filter_" + evtType
```

**Remediation:**
Pre-build a `Dictionary<FlightEventType, string>` mapping each enum value to its ImGui label string. Build once in a static initializer.

### Finding 4.3 – Complex string interpolation per event per frame in event feed

**File:** `steely-eyed-missile-kitten.lib/UI/EventFeedUI.cs`
**Line:** 63

**Current behavior:**
```csharp
$"[T+{evt.TimestampSec:F0}s] [{evt.Type}] {evt.VehicleName}: {evt.Description}"
```

**Remediation:**
Cache the formatted string on the `FlightEvent` object itself (compute once when the event is created, store as a `DisplayText` property).

### Finding 4.4 – `FormatDistance()` / `FormatSpeed()` helpers with math + string formatting per vehicle per frame

**File:** `steely-eyed-missile-kitten.lib/UI/MonitorUI.cs`
**Lines:** 63-78

**Current behavior:** For each vehicle row, calls `FormatDistance()` (lines 63, 66, 69) and `FormatSpeed()` (lines 72, 75) which each do conditional math (comparison + division) and string interpolation.

**Remediation:**
1. These values change on every telemetry sample (2 Hz), not every frame (60+ Hz).
2. Cache the formatted strings per vehicle per snapshot. When the snapshot is the same object as the previous frame, reuse the cached strings.
3. Add a `FormattedSnapshot` wrapper that lazily computes formatted strings on first access.

### Finding 4.5 – `FirstOrDefault()` LINQ searches in MissionUI render path

**File:** `steely-eyed-missile-kitten.lib/UI/MissionUI.cs`
**Lines:** 50, 95, 168

**Current behavior:**
```csharp
missionManager.Definitions.FirstOrDefault(d => d.Id == missionId)
```

**Remediation:**
Build a `Dictionary<string, MissionDefinition>` from `Definitions` on load. Use dictionary lookup instead of linear search.

### Finding 4.6 – `.Keys.ToArray()` allocation per frame

**File:** `steely-eyed-missile-kitten.lib/UI/MissionUI.cs`
**Line:** 95

**Remediation:**
Cache the keys array and invalidate when missions change.

### Finding 4.7 – `BuildDifficultyStars()` creates two `new string()` allocations per mission per frame

**File:** `steely-eyed-missile-kitten.lib/UI/MissionUI.cs`
**Line:** 141

**Current behavior:**
```csharp
new string('*', clamped) + new string('.', 5 - clamped)
```

**Remediation:**
Pre-build a `static readonly string[]` with all 6 possible difficulty strings (`"....."`, `"*...."`, `"**..."`, `"***..","****.", "*****"`). Index by difficulty value.

### Finding 4.8 – `new string(' ', depth * 3)` indent string per condition per frame

**File:** `steely-eyed-missile-kitten.lib/UI/MissionUI.cs`
**Line:** 187

**Remediation:**
Pre-build a `static readonly string[]` of indent strings for depths 0-10 (or whatever max is reasonable).

---

## 5. eternal-flame.lib

**Severity: HIGH**

### Finding 5.1 – `VehicleProvider.GetAllVehicles()` called twice per frame

**File:** `eternal-flame.lib/EternalFlameSubmod.cs`
**Lines:** 45 (RenderVehicleSelector), 97 (RenderAddButton)

**Remediation:**
Call `GetAllVehicles()` once at the top of `RenderContent()` and pass the result to both methods.

### Finding 5.2 – `.Select(v => v.Id).ToArray()` allocation every frame

**File:** `eternal-flame.lib/EternalFlameSubmod.cs`
**Line:** 52

**Remediation:**
Cache the vehicle names array. Invalidate when vehicle count changes.

### Finding 5.3 – String concatenation in vehicle selectable loop

**File:** `eternal-flame.lib/EternalFlameSubmod.cs`
**Lines:** 88, 178

**Current behavior:**
```csharp
ImGui.Selectable(vehicleNames[i] + "##ef", isSelected)
```

**Remediation:**
Use `ImGui.PushID(i)` / `ImGui.PopID()` and pass the plain name without `##` suffix.

### Finding 5.4 – `ImGui.CalcTextSize()` and `ImGui.GetColumnWidth()` per table row

**File:** `eternal-flame.lib/EternalFlameSubmod.cs`
**Lines:** 165-177

**Remediation:**
Compute `btnWidth` once outside the loop (the " X " text size is constant). Cache `ImGui.GetFrameHeight()` in a local before the loop.

---

## 6. garys-torch.lib

**Severity: HIGH**

### Finding 6.1 – `VehicleProvider.GetAllVehicles()` + `string[]` allocation every frame

**File:** `garys-torch.lib/GarysTorchSubmod.cs`
**Lines:** 109, 116-118

**Remediation:**
Cache vehicle list and ID array. Invalidate on vehicle count change.

### Finding 6.2 – String interpolation in collapsing header per weld per frame

**File:** `garys-torch.lib/GarysTorchSubmod.cs`
**Lines:** 67, 205, 219

**Remediation:**
Cache header strings per weld. Regenerate only when weld source/target changes.

### Finding 6.3 – `_savePresetName.ToString().Trim()` every frame the modal is open

**File:** `garys-torch.lib/GarysTorchSubmod.cs`
**Line:** 390

**Remediation:**
Only compute the trimmed string on button press, not every frame.

### Finding 6.4 – Reflection calls in `ApplyVehicleScale()`

**File:** `garys-torch.lib/WeldEngine.cs`
**Lines:** 127-133

**Current behavior:** Calls `GetType().GetField()` and `GetType().GetProperty()` with `BindingFlags` each time scale is applied.

**Remediation:**
Cache the `FieldInfo` / `PropertyInfo` in static fields on first successful lookup. Use the cached info for subsequent calls.

---

## 7. kiwis-marbles.lib

**Severity: HIGH**

### Finding 7.1 – `CelestialProvider.GetAllCelestials()` and `GetAllOrbiters()` + array rebuild every frame

**File:** `kiwis-marbles.lib/KiwisMarblesSubmod.cs`
**Lines:** 64-65, 79-82

**Remediation:**
Use the shared per-frame cache from Pattern A. Cache the ID arrays alongside.

### Finding 7.2 – Redundant `Dictionary.ContainsKey()` + indexer pattern (double lookup)

**File:** `kiwis-marbles.lib/KiwisMarblesSubmod.cs`
**Lines:** 242-250, 255-259, 261, 316-318, 373-375

**Current behavior:**
```csharp
if (!_weldEditState.ContainsKey(i)) { ... _weldEditState[i] = ...; }
var (proxy, scaleIdx) = _weldEditState[i];  // second lookup
```

And worse:
```csharp
float curLon = _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].lon : 0f;
float curLat = _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].lat : 0f;
float curRadialKm = _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].radialKm : 0f;
```

**Remediation:**
Use `TryGetValue()` which does a single lookup:
```csharp
if (!_weldSurfaceState.TryGetValue(i, out var surfState))
    surfState = (0f, 0f, 0f, false);
float curLon = surfState.lon;
float curLat = surfState.lat;
float curRadialKm = surfState.radialKm;
```

### Finding 7.3 – Trigonometric calculations in render path

**File:** `kiwis-marbles.lib/KiwisMarblesSubmod.cs`
**Lines:** 356-363, 474-480

**Current behavior:** `Math.Sin`, `Math.Cos`, `Math.Asin`, `Math.Atan2` called on `lonChanged || latChanged || radChanged` conditions.

**Remediation:**
These are conditional on slider changes and likely acceptable. However, `OffsetToLonLat()` (lines 474-480) is called unconditionally at lines 257 and 322. Cache the result and recompute only when `weld.Offset` changes.

### Finding 7.4 – String interpolation per weld per frame

**File:** `kiwis-marbles.lib/KiwisMarblesSubmod.cs`
**Lines:** 48, 198, 220, 238, 326, 382

**Remediation:**
Cache weld header strings. For numeric displays (G5 formatting), cache the formatted string and regenerate only when the underlying offset changes.

---

## 8. zippo.lib

**Severity: HIGH**

### Finding 8.1 – `VehicleProvider.GetAllVehicles()` called every frame via `RefreshVehicles()`

**File:** `zippo.lib/ZippoSubmod.cs`
**Lines:** 36 (caller), 218 (implementation)

**Remediation:**
Use per-frame vehicle cache (Pattern A). Only rebuild the name array when vehicle count changes.

### Finding 8.2 – Reflection-based `GetType().FullName` comparison in `GetLightComponents()`

**File:** `zippo.lib/LightController.cs`
**Lines:** 33-42

**Current behavior:**
```csharp
if (c?.GetType().FullName == "KSA.LightModule+TemplateData")
```

**Why it is expensive:** `GetType().FullName` allocates a string and involves reflection on every component in the list. Called indirectly from render path when parts are selected.

**Remediation:**
1. Cache the `Type` object: `private static readonly Type LightModuleType = Type.GetType("KSA.LightModule+TemplateData");`
2. Compare with `c?.GetType() == LightModuleType` (reference equality, no string allocation).

### Finding 8.3 – String array rebuild for vehicle names every frame

**File:** `zippo.lib/ZippoSubmod.cs`
**Lines:** 222-226

**Remediation:**
Cache the name array. Invalidate when vehicle count changes.

---

## 9. con-man.lib

**Severity: HIGH**

### Finding 9.1 – `ImGui.CalcTextSize()` called 3 times every frame

**File:** `con-man.lib/ConManSubmod.cs`
**Lines:** 68-70

**Current behavior:**
```csharp
ImGui.CalcTextSize("Startup default")
ImGui.CalcTextSize(" Apply ")
ImGui.CalcTextSize(" Delete ")
```

**Remediation:**
Cache the text sizes in `static float` fields. Compute once on first render (or when font/scale changes).

### Finding 9.2 – `GetLayoutNames()` called 4 times in single `RenderContent()`

**File:** `con-man.lib/ConManSubmod.cs`
**Lines:** 83, 150, 160, 251

**Remediation:**
Call once at the top of `RenderContent()`, store in a local variable, pass to all consumers.

### Finding 9.3 – Reflection calls per gauge in `RenderGaugeSummary()`

**File:** `con-man.lib/ConManSubmod.cs`
**Lines:** 284, 302-320

**Current behavior:** `GetCanvases()`, then for each canvas: `GetEnabled()`, `GetCustomOffset()`, `GetCustomScale()` — each potentially using reflection.

**Remediation:**
1. Cache the gauge summary data. Refresh only on explicit user action (e.g., after applying a layout).
2. If real-time display is needed, cache the `FieldInfo`/`PropertyInfo` objects in the `GaugeStateAccessor` so reflection lookups happen once.

### Finding 9.4 – `Array.IndexOf()` in render path

**File:** `con-man.lib/ConManSubmod.cs`
**Lines:** 151, 166

**Remediation:**
Build a `Dictionary<string, int>` mapping layout names to indices when layout list changes. Use dictionary lookup instead of linear search.

---

## 10. geeforce.lib

**Severity: HIGH**

### Finding 10.1 – 10+ string interpolations per frame in stats table

**File:** `geeforce.lib/GForceUI.cs`
**Lines:** 75-85

**Current behavior:** `$"{recorder.Latest.Magnitude:F2} g"` and similar for 10 fields.

**Remediation:**
1. The underlying data changes at 40 Hz (sampling rate). Cache the formatted strings and regenerate only when a new sample arrives (compare sample timestamp or count).
2. Alternatively, render raw floats using `ImGui.Text()` with a pre-formatted buffer pattern.

### Finding 10.2 – Grid label and time axis string allocations per frame

**File:** `geeforce.lib/GForceUI.cs`
**Lines:** 262, 276 (grid labels), 430-436 (`FormatTimeLabel()`)

**Remediation:**
1. Grid Y-axis labels depend on `yMax` / `jerkMax`. Cache label strings and regenerate only when these values change.
2. Time axis labels depend on `viewStart` / `viewEnd`. Cache label strings and regenerate only when the view range changes (scrub slider or live mode advance).

### Finding 10.3 – `new float4()` color allocations in render loop

**File:** `geeforce.lib/GForceUI.cs`
**Lines:** 362, 400, 493-494

**Current behavior:**
```csharp
ImGui.GetColorU32(new float4(color.X, color.Y, color.Z, 0.4f))
```

**Remediation:**
Pre-compute `uint` color values as `static readonly` fields for all constant colors. For the alpha-modified variant, cache the `uint` alongside the base color.

### Finding 10.4 – Circular buffer modulo arithmetic per sample in hot loop

**File:** `geeforce.lib/GForceRecorder.cs` (indexer), called from `GForceUI.cs` lines 224, 302, 356

**Current behavior:** Each `recorder[i]` call computes `(_head - _count + index + _buffer.Length) % _buffer.Length`.

**Remediation:**
1. Expose a `Span<GForceSample>` or direct array access method that returns a contiguous view of the visible range.
2. Alternatively, compute the base offset once outside the loop: `int baseIdx = (_head - _count + _buffer.Length) % _buffer.Length;` then index as `_buffer[(baseIdx + i) % _buffer.Length]`.

---

## 11. average-twr.lib

**Severity: HIGH**

### Finding 11.1 – 8 statistics recomputed every frame

**File:** `average-twr.lib/AverageTwrSubmod.cs`
**Lines:** 84-87 (TWR stats), 102-105 (Accel stats)

**Current behavior:** `ComputeMean()`, `ComputeStdDev()`, `ComputeHarmonicMean()`, `ComputeBrachiMean()` called every frame.

**Why it is expensive:** Data is sampled at 100 Hz. Between frames (~16ms at 60 fps), at most 1-2 new samples arrive. Recomputing all 8 statistics every frame is wasteful.

**Remediation:**
1. Cache the 8 computed values in fields.
2. Recompute only when `_accumulator.SampleCount` changes (i.e., a new sample arrived since last render).

### Finding 11.2 – String formatting for stat rows every frame

**File:** `average-twr.lib/AverageTwrSubmod.cs`
**Lines:** 138-141

**Remediation:**
Cache the formatted strings alongside the computed values (Finding 11.1). Regenerate when sample count changes.

### Finding 11.3 – `new float4()` / `new float2()` constant allocations

**File:** `average-twr.lib/AverageTwrSubmod.cs`
**Lines:** 48, 60, 62, 131

**Remediation:**
Promote to `private static readonly` fields.

---

## 12. humble-arteest.lib

**Severity: HIGH**

### Finding 12.1 – `VehicleProvider.GetAllVehicles()` + string array every frame in VehiclePaintSubmod

**File:** `humble-arteest.lib/VehiclePaintSubmod.cs`
**Lines:** 150-153

**Remediation:**
Use per-frame vehicle cache (Pattern A).

### Finding 12.2 – Dictionary allocations in `RefreshPartCache()`

**File:** `humble-arteest.lib/VehiclePaintSubmod.cs`
**Lines:** 328-365

**Current behavior:** Creates `new Dictionary<string, int>()` (labelCounts) and `new Dictionary<string, int>()` (seen) each time RefreshPartCache is called.

**Remediation:**
Reuse dictionary fields. Call `.Clear()` instead of allocating new instances. Only call `RefreshPartCache()` when the selected vehicle changes, not every frame.

### Finding 12.3 – Global state set every frame in EngineEmissiveSubmod

**File:** `humble-arteest.lib/EngineEmissiveSubmod.cs`
**Lines:** 143-148

**Current behavior:**
```csharp
if (_applyToAll) {
    EngineEmissive.GlobalEnabled = true;
    EngineEmissive.GlobalTemperature = _globalTemp;
    EngineEmissive.GlobalTfi = _globalTfi;
}
```

**Remediation:**
Only set global state when the values actually change (track previous values and compare).

---

## 13. skittles.lib

**Severity: MEDIUM**

### Finding 13.1 – `FirstOrDefault()` LINQ search in render path

**File:** `skittles.lib/SkittlesSubmod.cs`
**Lines:** 159-160

**Current behavior:**
```csharp
_themeManager.AvailableThemes.FirstOrDefault(t => t.Name == _themeManager.ActiveThemeName && !t.IsBuiltIn)
```

**Remediation:**
Cache the "active custom theme" reference. Update when theme changes.

### Finding 13.2 – `GetThemeNames()` called multiple times per frame

**File:** `skittles.lib/SkittlesSubmod.cs`
**Lines:** 36, 42, 123

**Remediation:**
Call once at top of `RenderContent()`, store in local.

### Finding 13.3 – `_filterInput.ToString()` every frame

**File:** `skittles.lib/SkittlesSubmod.cs`
**Line:** 73

**Remediation:**
Cache the string result. Only recompute when the input buffer changes (e.g., track a dirty flag or compare length).

---

## 14. camera-controller-override.lib

**Severity: MEDIUM**

### Finding 14.1 – String interpolation in playback status display every frame

**File:** `camera-controller-override.lib/UI/KeyframeSequencePanel.cs`
**Lines:** 63-69, 87-89

**Current behavior:**
```csharp
$"Playing [{player.CurrentKeyframeIndex + 1}/{player.Keyframes.Count}]"
$"{player.TotalElapsedTime:F1}s / {totalDuration:F1}s"
```

**Remediation:**
Cache the status text string. Regenerate when keyframe index changes or elapsed time crosses a 0.1s boundary.

### Finding 14.2 – Local string array allocated every frame

**File:** `camera-controller-override.lib/UI/KeyframeSequencePanel.cs`
**Line:** 278

**Current behavior:**
```csharp
string[] returnEasingNames = { "Linear", "Ease In", "Ease Out", "Ease In-Out" };
```

**Remediation:**
Promote to `private static readonly string[]`.

---

## 15. i-feel-seen.lib

**Severity: MEDIUM**

### Finding 15.1 – `VehicleProvider.GetAllVehicles()` every frame

**File:** `i-feel-seen.lib/IFeelSeenSubmod.cs`
**Line:** 28

**Remediation:**
Use per-frame vehicle cache (Pattern A).

### Finding 15.2 – Linear search in `VehicleTracker.IsTracked()`

**File:** `i-feel-seen.lib/VehicleTracker.cs`
**Lines:** 17-23

**Remediation:**
Maintain a `HashSet<Vehicle>` for O(1) tracked-vehicle lookups. Update on add/remove.

---

## 16. inanimate-carbon-rod.lib

**Severity: MEDIUM**

### Finding 16.1 – Case-insensitive string search on all items every frame in thumbnail grid

**File:** `inanimate-carbon-rod.lib/InanimeCarbonicRodSubmod.cs`
**Lines:** 264-269

**Current behavior:**
```csharp
foreach (var kvp in SubpartThumbnailCache.All) {
    if (filterText.Length > 0 && !kvp.Key.Contains(filterText, StringComparison.OrdinalIgnoreCase))
        continue;
    _filteredEntries.Add(kvp);
}
```

**Remediation:**
1. Cache the filtered list. Only refilter when `filterText` changes (compare to previous value).
2. This is especially valuable because the thumbnail cache is static and doesn't change after generation.

### Finding 16.2 – `SubpartThumbnailCache.All.Count` accessed multiple times

**File:** `inanimate-carbon-rod.lib/InanimeCarbonicRodSubmod.cs`
**Lines:** 74, 181

**Remediation:**
Store in a local variable at the top of the render method.

---

## 17. grant (supermod)

**Severity: MEDIUM**

### Finding 17.1 – Submod list sorted every frame in View menu

**File:** `grant/Mod.cs`
**Lines:** 194-196

**Current behavior:**
```csharp
var sorted = new List<ISubmod>(_submods);
sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
```

**Why it is expensive:** Allocates a new `List<ISubmod>` and sorts it every frame, even though the submod list never changes at runtime.

**Remediation:**
Sort the list once during initialization. Store the sorted list as a field.

### Finding 17.2 – Dictionary string-key lookups per submod per frame

**File:** `grant/Mod.cs`
**Lines:** 237, 244, 248

**Remediation:**
These are `Dictionary<string, bool>` lookups which are O(1) amortized. The cost is minor but could be avoided by using the submod index as the key (integer) instead of the name (string hash + equality).

---

## 18. glass.lib

**Severity: LOW**

### Finding 18.1 – Single string interpolation per frame

**File:** `glass.lib/GlassSubmod.cs`
**Line:** 41

**Current behavior:**
```csharp
ImGui.Text($"Current FOV: {currentFovDeg:F1}°");
```

**Remediation:**
Cache the formatted string. Regenerate only when FOV value changes.

---

## 19. kitten-animations.lib

**Severity: LOW**

### Finding 19.1 – `KittenAvatarAccessor.GetKittenAvatar()` called twice per frame

**File:** `kitten-animations.lib/KittenAnimationsSubmod.cs`
**Lines:** 25 (Update), 74+ (RenderContent)

**Remediation:**
Cache the avatar reference in a field during `Update()`. Use the cached reference in `RenderContent()`.

---

## Implementation Priority

The following order is recommended based on impact and effort:

### Phase 1 – Shared Infrastructure (do first, unblocks all mods)
1. **Add per-frame vehicle/celestial cache to `ksa-abstractions.lib`** (Pattern A). All 8+ affected mods benefit immediately.

### Phase 2 – Critical LINQ and Allocation Hotspots
2. **marque.lib** – Remove all `.OrderBy().ToList()` chains from draw methods (Finding 2.1).
3. **blinky.lib** – Remove `.Select().Distinct().Count()` and `.ToList()` (Findings 3.1, 3.2).
4. **steely-eyed-missile-kitten.lib** – Cache `Enum.GetValues`, pre-build label strings, cache mission lookups (Findings 4.1-4.8).

### Phase 3 – High-Impact Per-Mod Fixes
5. **eternal-flame.lib** – Deduplicate `GetAllVehicles()` calls, cache arrays (Findings 5.1-5.4).
6. **garys-torch.lib** – Cache vehicles, cache reflection FieldInfo (Findings 6.1-6.4).
7. **kiwis-marbles.lib** – Cache providers, fix `TryGetValue` pattern, cache strings (Findings 7.1-7.4).
8. **zippo.lib** – Cache vehicles, fix reflection type comparison (Findings 8.1-8.3).
9. **con-man.lib** – Cache text sizes, deduplicate `GetLayoutNames()`, cache reflection (Findings 9.1-9.4).
10. **geeforce.lib** – Cache stats strings, pre-compute colors, optimize buffer access (Findings 10.1-10.4).
11. **average-twr.lib** – Cache statistics and formatted strings (Findings 11.1-11.3).
12. **humble-arteest.lib** – Cache vehicles, reuse dictionaries, guard global state writes (Findings 12.1-12.3).

### Phase 4 – Medium and Low Priority
13. **skittles.lib** – Cache theme lookups, deduplicate `GetThemeNames()` (Findings 13.1-13.3).
14. **camera-controller-override.lib** – Cache status strings, static easing names array (Findings 14.1-14.2).
15. **i-feel-seen.lib** – Cache vehicles, add HashSet for tracked vehicles (Findings 15.1-15.2).
16. **inanimate-carbon-rod.lib** – Cache filtered list, local variable for count (Findings 16.1-16.2).
17. **grant** – Sort submod list once at init (Finding 17.1).
18. **glass.lib** – Cache FOV string (Finding 18.1).
19. **kitten-animations.lib** – Cache avatar reference (Finding 19.1).
