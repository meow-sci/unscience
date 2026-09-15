> **Historical plan:** current compatibility is tracked in [KSA_5438_UPGRADE.md](KSA_5438_UPGRADE.md).
> The 4750 findings below are not a current broken-feature list; later migrations retired several
> targets and rebuilt Vehicle Paint. Native acceptance remains separate from managed checks.

# Fix Plan — Current Gaps vs KSA `2026.6.9.4750`

> ⚠ **Superseded as the current baseline.** The suite is now verified against
> **`2026.7.9.5018`** — see [`KSA_5018_UPGRADE.md`](KSA_5018_UPGRADE.md). This document remains the
> history for the `4680 → 4750` hop. Note that three items recorded here as open
> (`Controller.___Transform`, zippo `"Color"`, supermod `IvaForceRender` wiring) have since been
> **fixed**; the 5018 review re-verified each fix against the new build.


Remediation plan for breakage found by auditing the unscience suite against the current KSA build
**`2026.6.9.4750`** (previous: **`2026.6.8.4680`**). Findings come from three signals, all cross-checked:

1. **`dotnet build`** against the live `4750` DLLs → typed/compile breaks (definitive).
2. **decomp diff** of every integration touchpoint (`scope/`) NEW vs OLD → string/reflection breaks the
   compiler can't see.
3. **`version.json` changelog** (revs 4681–4748) → behavioral changes that don't move a symbol.

Authoritative per-touchpoint detail lives in [`../scope/`](../scope/FULL_SCOPE.md); each item below links
to its area file.

### Framing — what this update actually changed

The "major update" 4680→4750 introduced **two** breaks directly: the electrical **energy/power
`float`→`double`** refactor (rev 4681) and the **Brutal-package nullable** tightening (rev 4729). The
audit also revealed that **space-tape was already broken before 4680** — most of its compile errors are
accumulated KSA API drift from when it was last built (against a pre-4680 KSA), only surfaced now — and
that **humble-arteest Vehicle Paint / mesh-deform shader hacks were already inert** and are now *harder*
to fix because of the rev 4693 MeshIndirect shader merge. All of it must be addressed for the suite to
build and run correctly against the live game.

### Severity legend

- 🔴 **P1 build-blocking** — solution does not compile against `4750`.
- 🟠 **P2 runtime break** — compiles, but the feature is dead/incorrect in-game.
- 🟡 **P3 behavioral** — changed game semantics; feature may misbehave or mislead.
- 🔵 **P4 latent** — pre-existing bug surfaced by the audit (not caused by `4750`).
- ⚪ **P5 hygiene** — stale docs/scope.

### Definition of done

- `dotnet build` of `ksa-mod-experiments.slnx` against the live install: **0 errors, 0 warnings**
  (`TreatWarningsAsErrors=true`).
- Runtime smoke tests pass (per item below).
- `scope/` touchpoint statuses + this plan updated to reflect fixes; `REPOSITORY_INDEX.md` / READMEs
  reconciled where touched.

---

## Phase 1 — Restore compilation (🔴 P1) — *required first*

The build currently fails with **11 errors** (`recon` task #7). All are in two libs.

### 1a. space-tape.lib (10 errors + 1 runtime corollary) → [`part-editor-and-robotics.md`](../scope/part-editor-and-robotics.md)

| # | Site(s) | Old API (≤ pre-4680) | New API in 4750 | Exact fix |
|---|---|---|---|---|
| 1 | `Thumbnails/SubpartViewerWindow.cs:365,407`; `SubPartsWindow.cs:221,227` | `ThumbnailReference.CreateImGuiThumbnail(VkSampler)` | `ThumbnailReference.GetOrCreateImGuiTexture(VkSampler) : ImTextureRef` (`KSA/.../ThumbnailReference.cs:36`) | Rename the 4 calls: `…CreateImGuiThumbnail(Program.LinearClampedSampler)` → `…GetOrCreateImGuiTexture(Program.LinearClampedSampler)`. Existing `.ImGuiImageRef` reads still work. |
| 2 | `PartImporter.cs:95,104,113` | energy/power were `float` | `BatteryTemplate.MaximumCapacity = EnergyReference.KWh` (**double**); `Generator.Produced` / `PowerConsumer.Consumed = PowerReference.W` (**double**) — rev 4681 split `JoulesReference`→`EnergyReference`+`PowerReference` | Change `float.IsNaN(` → `double.IsNaN(` on all three lines (and ensure locals are `double`). **This is the one genuine 4680→4750 regression here.** |
| 3 | `PartImporter.cs:135` | `DockingPortTemplate.Force` | `DockingPortTemplate.PushoffImpulse : ImpulseReference` (N·s) + `LatchingKineticEnergy : EnergyReference`; `ConnectorId` is now an `[XmlElement] StringReference` — rev 4683 | Replace with `template.DockingPort.PushoffImpulse.GetNewtonSeconds()` (semantics change N→N·s; carry the unit through the importer's field). |
| 4 | `Thumbnails/SingleSubpartGenerator.cs:207`; `SubpartThumbnailGenerator.cs:244` | `ThumbnailPart.ComputeBoundingSphereRadius()` | `ThumbnailPart.ComputeBoundingSphereRadius(out float3 outCenter) : float` (`KSA/.../ThumbnailPart.cs:150`) | `root.ComputeBoundingSphereRadius(out _)` (discard the center, or use it to recenter the thumbnail camera). |

**Corollary R1 (🟠 P2, fix in the same change):** `space-tape.lib` `GameDataXmlSerializer.SerializeDockingPort`
(~`:89-92`) **writes** `<DockingPort ConnectorId="…" Force="…">` as attributes. In 4750 `Force` no longer
exists and `ConnectorId` is a `StringReference` **element**, so even after the read-side fix, saved
docking ports will **deserialize empty**. Update the writer to emit `<ConnectorId Value="…"/>` and
`<PushoffImpulse Ns="…"/>` (+ `<LatchingKineticEnergy …/>` if authored), matching the new template schema.

**Verify:** build clean; in-game, open Space Tape → Load SubParts → generate thumbnails (item 1/4),
import a battery/generator/engine part (item 2) and a docking port (item 3), **save and re-load** a part
with a docking port (R1) and confirm the connector/impulse survive the round-trip.

### 1b. garrys-torch.lib (1 error) → [`vehicle-physics.md`](../scope/vehicle-physics.md)

| # | Site | Cause | Exact fix |
|---|---|---|---|
| 5 | `GarrysTorchSubmod.cs:457` (CS8604) | `_deleteConfirmName` is `string?` (decl `:52`) interpolated into `ImGui.Text($"…'{_deleteConfirmName}'?")`; the rev 4729 Brutal bump made `ImString.AppendFormatted(string value,…)` **non-nullable** | Null-coalesce at the interpolation: `…'{_deleteConfirmName ?? string.Empty}'?` (or guard the whole confirm block on non-null, consistent with the `IsNullOrEmpty`-guarded `_weldError`/`_savePresetError`). |

**Verify:** build clean; in-game, trigger a weld delete-confirmation popup and confirm the name renders.

---

## Phase 2 — Runtime/asset breaks (🟠 P2)

These compile but are dead in-game. Both were runtime-recompiled-GLSL hacks invalidated by the rev 4693
**MeshIndirect** merge — a `dotnet build` cannot catch them.

> **Status (2026-07-25): 2a Vehicle Paint is FIXED** — rebuilt on a different transport + interception
> point (see 2a). **2b mesh-deform is still guarded-off**; it needs per-instance *floats*, which the
> free-bit trick cannot carry, so it would need a different transport of its own.

> **Status (2026-06-27): short-term guards DONE.** Both features now self-detect the unsupported build
> and disable cleanly (clear UI notice; activation short-circuits; the per-instance write prefixes
> no-op unless shaders are active, so the `EmissiveColor` clobber is gone). Solution builds **0 errors /
> 0 warnings**. The **proper shader rework remains open** — and is *larger* than this plan originally
> assumed (see the architectural finding below).
>
> **New finding (decomp-verified, NEW `PartModelRenderer.cs` + `ShaderReference.cs`).** Reviving paint is
> not a string re-anchor. Rev 4693 moved part-color compilation to
> `ShaderReference.CompileVariantWithCustomOptions(options)`, which `PartModelRenderer.ColorData.Build*`
> calls per pipeline variant; it **reads the GLSL fresh from disk and destroys the module immediately**,
> and **never consults `ShaderReference.Shader`**. So the mods' whole mechanism (swap `.Shader` →
> `ColorData.Rebuild()`) is inert even with perfect anchors. A real fix must **Harmony-patch
> `CompileVariantWithCustomOptions` (or the four `Build*` pipeline methods) for `MeshIndirectVert/Frag`**
> — which changes the shader used to render *every* part — and therefore **requires in-game GPU iteration**
> (a bad pipeline makes all parts vanish or aborts on a validation layer). Not safe to land blind.

### 2a. humble-arteest — Vehicle Paint ✅ **DONE (2026-07-25) — rebuilt, not patched**

Vehicle Paint was reimplemented from scratch against 5018 and works again. The design below (kept for
history) was abandoned in favour of a simpler transport that sidesteps both blockers:

- **Transport:** the color rides in the **free high bits (11..31) of `PerInstanceData.StateBitFlag`**
  (the game writes only bits 0..10) as a 7:7:7 sRGB value. That field exists at offset 64 in every
  `PerInstanceData` variant and is already forwarded to every part fragment shader as
  `inStateFlags`@loc4 — so there is **no vertex-shader edit, no varying-location conflict (5..10 stay
  free for emissive/tfi/temperature/wetness), no struct/stride change, and no clobbering of
  `EmissiveColor`@68 / `Temperature`@68 / `TfiThickness`@72 / `Wetness`@76.** This also closes the P4
  `EmissiveColor` clobber item below.
- **Injection:** a Harmony prefix on **`RenderCore.ShaderModuleUtils.FromFile`** (not
  `CompileVariantWithCustomOptions`). Every variant compile funnels through it; the prefix compiles a
  patched source *string* with `FromString`, passing the caller's own `CompileOptions` and the original
  path as the compiler input-file name so `#include`s and all `ENABLE_*` variants behave stock. Any
  failure falls back to the unmodified file, so the worst case is stock rendering, never a broken
  pipeline. Nothing on disk is written.
- **Applying it:** sets `Program.RendererRebuildNeeded` (the game's deferred, `WaitIdle`-guarded
  rebuild) instead of calling `ColorData.Rebuild()` mid-frame.
- **Targeting:** per **part instance**, per part type, or global; flight and vehicle editor.
- **Standing invariant to re-audit each game update:** `StateBitFlag` bits 11..31 must stay unused by
  KSA. Secondary anchor: the `vec3 sampledColor …;` line in `MeshIndirect.frag` /
  `MeshIndirectRaytraced.frag` (a move there fails loudly at "Enable", it cannot half-apply).

Details: [`../scope/character-and-materials.md`](../scope/character-and-materials.md) rows A1–A11 and
[`../humble-arteest.lib/README.md`](../humble-arteest.lib/README.md).

<details>
<summary>Original (superseded) analysis of the break</summary>

Root cause (verified against both `Content` shader trees):
- The mod's `ModifyVertexShader` anchor strings (`"    int Highlighted;\n};"`, `out flat int outHighlighted`,
  `outHighlighted = instanceData.Highlighted;`) are **absent in both 4680 and 4750** → `ActivateShaders()`
  returns false → paint never renders. (Already broken before this update.)
- Rev 4693 makes a clean re-fix harder: the merged `MeshIndirect` shader now declares
  `outEmissiveColor`/`outTfiThickness`/`outTemperature` at **locations 5/6/7**, colliding with the mod's
  intended paint varyings at **6/7/8**.
- The Harmony write side (`PartModel.PerInstanceData` offset **68**) is no longer free padding — it's the
  game-used `uint EmissiveColor`. The current `PaintR` clobbers it. (Pre-existing; see also P4.)

Plan (Engine Emissive and Kitten Color are unaffected — do not touch them):
1. ✅ **Short term — DONE.** Vehicle Paint self-detects the unsupported build and disables. Implemented in
   `VehiclePaint.cs` (`IsSupported`/`UnsupportedReason` probe of on-disk `MeshIndirect.vert`;
   `ActivateShaders()` short-circuits), `VehiclePaintSubmod.cs` (UI shows an "unavailable on this build"
   notice instead of a dead Activate button), and `VehiclePaintPatches.cs` (the `AddInstance` prefix
   early-outs unless `ShadersActive` → **`EmissiveColor`@68 clobber eliminated**, also closing the P4 item).
2. **Proper fix (design task — OPEN, needs in-game GPU iteration).** Because the color pipeline ignores
   `ShaderReference.Shader` (see "New finding" above), the interception point must change:
   - **Harmony-patch `ShaderReference.CompileVariantWithCustomOptions`** (single seam, covers both the
     static `ENABLE_EMISSIVE`+`ENABLE_THIN_FILM` and dynamic `ENABLE_TEMPERATURE`+`ENABLE_THIN_FILM`
     color variants): when `this` is `MeshIndirectVert`/`MeshIndirectFrag`, compile a modified temp source
     with the *same* `options` plus an injected `ENABLE_PAINT` define, and return that module.
   - **Re-anchor to the `#ifdef`-gated struct.** Add the paint field *after* the conditional block (struct
     ends `…#endif\n};`), and a varying at **location 8** (5/6/7 are emissive/tfi/temperature).
   - **Use the one genuinely-free per-instance slot: offset 76.** In std430 the struct strides 80 B in
     every variant; bytes 76–79 are free in both the static (`packing2`) and dynamic (`packing1`) C#
     `PerInstanceData`. Pack RGB into a single `uint` there (mirror the game's `unpackRGB`/`EmissiveColor`
     packing) — this **stops clobbering `EmissiveColor`@68 and `TfiThickness`@72** that the old 68/72/76
     scheme hit. ⚠ verify the exact std430 offset on-GPU before trusting it.
   - Blast radius = every part renders through this shader, so iterate live (RenderDoc / visual check);
     keep the `IsSupported` guard as the fallback. Share the patched-compile machinery with mesh-deform (2b).

**Verify (proper fix):** apply paint to a part in-game and confirm tint renders without altering engine
emissive glow, thin-film, or other parts.

</details>

### 2b. mesh-deform (standalone, not bundled) — GLSL rewrite dead on 4750 → [`standalone-mods.md`](../scope/standalone-mods.md)

Same failure mode: its `MeshIndirect.vert` struct anchor (`uint EmissiveColor;\n};`) is now wrapped in
`#ifdef ENABLE_EMISSIVE` with added `Temperature`/`TfiThickness`, so injection misses; `ValidateStructModified`
false-positives and the shader reaches the compiler referencing non-existent members → `Activate()` fails.
The new trailing fields also collide with the slots it reuses via `packing1/packing2`. All C# touchpoints
are intact. **Lower priority (not in the supermod)**; fix alongside 2a using the same MeshIndirect rework,
or mark mesh-deform unsupported on `≥4693`.

✅ **DONE (guard).** Marked unsupported on `≥4693`: `MeshDeformShaders.cs` (`IsSupported` probe + `Activate()`
short-circuit), `MeshDeformSubmod.cs` (UI notice replaces the Active checkbox), `MeshDeformPatches.cs` (the
deform write prefix early-outs unless `ShadersActive`). The proper rework shares the 2a interception design.

---

## Phase 3 — Behavioral hardening (🟡 P3)

No symbol broke, but game semantics changed. Mostly space-tape; verify and adjust.

- **`Vehicle.IsControllable` gating (rev 4699)** → [`00-architecture-and-abstractions.md`](../scope/00-architecture-and-abstractions.md),
  [`vehicle-physics.md`](../scope/vehicle-physics.md), [`rpc.md`](../scope/rpc.md). Vehicles with no Control
  Module are now uncontrollable. Audited as **low risk**: doh kittens and capsules have a Control Module;
  garrys-torch welding doesn't strip modules and `KittenEva.IsControllable=>true`; unladen-swallow
  ignite/shutdown still writes inputs unconditionally. **Action:** no code change required; optionally
  surface controllability in vehicle pickers, and use `_overrideIsControllable` only if a feature needs a
  module-less vehicle to respond.
- **Editor tag/category schema (rev 4731/4732/4741)** → [`part-editor-and-robotics.md`](../scope/part-editor-and-robotics.md)
  (R2). "Interstage" category **removed** (→ Coupling/Structural); "Stages" **renamed** to "Resource
  Groups"; categories/tags moved to `CoreEditorTagsGameData.xml` and registered at startup with a
  `NotaCategory` flag. **Action:** ensure space-tape's authored/imported tag values match the live
  registered tags; remove/translate any hard-coded `"Interstage"`/`"Stages"` strings.
- **Face-snapping / connector semantics (rev 4687–4740)** → [`part-editor-and-robotics.md`](../scope/part-editor-and-robotics.md)
  (R4). `ToSurface`/`FromSurface`/`NoFaceSnapping` behavior and the approved face-snap target list
  changed. **Action:** re-validate that space-tape-authored connectors snap/attach as intended in the
  current editor.
- **Part-size data in XML (rev 4721)** → [`part-editor-and-robotics.md`](../scope/part-editor-and-robotics.md)
  (R3). `PartTemplate.Diameter` is now a `DistanceReference` and parts carry size data for the new editor
  size filter. space-tape doesn't author size → its parts may not appear under size filters. **Action
  (optional):** emit size data in saved Part XML.

---

## Phase 4 — Pre-existing latent bugs surfaced by the audit (🔵 P4)

Not caused by `4750`, but found while cataloging. Cheap, high-value fixes.

- **zippo — color control silently dead** → [`celestial-and-lights.md`](../scope/celestial-and-lights.md).
  `zippo.lib/LightController.cs:59,80` reflects field name `"Color"` on `KSA.LightModule+TemplateData`,
  but the C# field is `ColorRgb` (`[XmlElement("Color")]` is only the XML name). `GetField("Color")`
  returns null in both versions → reads return white, writes no-op. **Fix:** `"Color"` → `"ColorRgb"`
  (intensity + on/off already work). **Verify:** set a light color in-game and confirm it changes.
- **camera-controller-override — animation override likely inert** → [`camera.md`](../scope/camera.md).
  `CameraControllerOverridePatches.cs:42` injects `Transform3D ___Transform`, but `KSA.Controller`/
  `OrbitController`/`FlyController` expose the camera as the field **`Camera`** (no `Transform` field) in
  both versions; a `Transform` field exists only on the unrelated `RenderCore.Input.Controllers.CameraController`.
  **Action:** confirm at runtime whether the patch applies at all (the apply error is swallowed by
  `Patcher.cs` try/catch); if inert, inject `Camera ___Camera` / read `__instance.Camera`, and re-verify
  which controller family the flight camera actually uses. Fixing this also revives unladen-swallow's
  `POST /camera/animate`. **Verify:** run a camera sequence (UI and RPC) and observe motion.
- ✅ **humble-arteest — `PerInstanceData.EmissiveColor` clobber — CLOSED.** The Phase 2a rebuild moved
  paint into `StateBitFlag` bits 11..31; no per-instance *field* is written any more.
- **supermod doesn't wire `IvaForceRender.Patch`** → [`00-architecture-and-abstractions.md`](../scope/00-architecture-and-abstractions.md),
  [`ui-customization.md`](../scope/ui-customization.md). Inside the supermod, kitchen-sink's "Force IVA
  Rendering" only does the `Enabled`-setter mutation; the ctor/`AddInstance` postfixes (needed for parts
  spawned after toggle + editor preview) are unwired. **Action (if supermod IVA force-render is desired):**
  add `IvaForceRender.Patch(_harmony)` to `unscience/Patcher.cs:Patch()` and `Unpatch` in `Unload()`.

---

## Phase 5 — Doc & scope hygiene (⚪ P5)

- **Stale READMEs** (correct to match verified APIs):
  - `average-twr` README cites a nonexistent `vehicle.TotalThrust` → actual `Vehicle.FlightComputer.VehicleConfig.TotalEngineVacuumThrust` / `NavBallData.ThrustWeightRatio` ([`telemetry.md`](../scope/telemetry.md)).
  - `geeforce` README cites `Velocity.GetBodyFrameAcceleration()` + `Situation` + a `float` sample → actual `Vehicle.AccelerationBody` (`double3`) ([`telemetry.md`](../scope/telemetry.md)).
  - ✅ `humble-arteest` READMEs — rewritten with the 5018 Vehicle Paint design; the stale
    DynamicMeshIndirect / ModelEye / ModelGlass narrative is gone.
- **After each fix lands:** update the affected `scope/` touchpoint status (BROKEN→OK), the status summary
  in [`../scope/FULL_SCOPE.md`](../scope/FULL_SCOPE.md), and this plan. Keep `REPOSITORY_INDEX.md` accurate
  if any feature's described behavior changes.

---

## Suggested execution order & rough effort

| Order | Item | Severity | Effort |
|---|---|---|---|
| 1 | Phase 1a space-tape compile fixes (#1–#4) | 🔴 | S–M (mechanical, exact fixes given) |
| 2 | Phase 1a R1 docking-port writer | 🟠 | S |
| 3 | Phase 1b garrys-torch nullable | 🔴 | XS |
| 4 | **Checkpoint: `dotnet build` clean** | — | — |
| 5 | Phase 4 zippo `ColorRgb` | 🔵 | XS |
| 6 | Phase 4 camera `___Transform` (confirm + fix) | 🔵 | S–M (needs runtime check) |
| 7 | Phase 2a Vehicle Paint — ✅ **rebuilt and working (2026-07-25)** | 🟠 | done |
| 8 | Phase 3 space-tape tag/connector/size behavior | 🟡 | M |
| 9 | Phase 2b mesh-deform — ✅ guard done; ⬜ rework shares 7 | 🟠 | guard XS · rework M |
| 10 | Phase 4 supermod IvaForceRender wiring (if wanted) | 🔵 | S |
| 11 | Phase 5 doc/scope hygiene | ⚪ | S |

Phases 1 (+the two XS P4/P1 wins) restore a building, mostly-correct suite quickly; the shader rework
(Phase 2) is the only large effort and is isolated to humble-arteest Vehicle Paint + mesh-deform.
