# Decals (graffiti) — Game Integration Scope

Permanent reference for detecting when KSA game updates break **graffiti** (click-to-place
projected PNG decals on vehicles, deployed parachute canopies, and terrain). Every game-facing member the mod touches is
enumerated with its decompiled-source path.

**Verified game versions**

- NEW decomp `2026.9.7.5402` root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
  (Content: `~/repos/meow-sci/ksa-game-assemblies/current/Content`)
- OLD decomp `2026.8.22.5348` root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`
  (Content: `~/repos/meow-sci/ksa-game-assemblies_prev/current/Content`)

Decomp paths are namespace-foldered and relative to the NEW root. First written against 5348; the
implementation is a port of the gatOS sticker system, whose anchors were independently verified
against that build.

**How the mod is hosted:** all logic in `graffiti.lib` (`GraffitiSubmod : ISubmod`,
`GraffitiPatches.Apply/Remove`), consumed by the standalone host (`graffiti/Mod.cs`,
`graffiti/Patcher.cs`) and by unscience (`unscience/Mod.cs` adds `GraffitiSubmod`;
`unscience/Patcher.cs` → `TryApply("graffiti", …)`). Findings apply to both hosts identically.

**No string reflection anywhere** — every game API used is public, so a rename/reshape is a
**compile** break (loud), never a silent runtime miss.

---

## Integration model

1. **One Harmony postfix** on `KSA.Rendering.RenderTarget.ResolveAttachments(CommandBuffer)`
   (`KSA.Rendering/RenderTarget.cs:315`). `Program.RenderGame` calls it unconditionally per
   viewport; the method body is MSAA-gated but a postfix fires either way — a reliable seam at
   every MSAA setting, the same post-resolve window the game's own `GridPass` draws in (resolved
   single-sample depth + colour both current and unbound). The postfix (`GraffitiPatches.cs`)
   gates on: `GraffitiSubmod.RenderActive` (static volatile, false when nothing live),
   `!Program.EditorFlag` (the editor resolves the SAME target through the main viewport),
   `__instance == Program.OffscreenTarget` and `Program.RenderedViewport == Program.MainViewport`
   (crew-portrait/secondary viewports own their targets and cameras). Since 5402 viewports are
   `IViewport`/`IGameViewport` objects in `ViewportRegistry`; the main viewport's
   `OffscreenTarget` **is** `Program._offscreenTarget` via `AttachSharedTargets`
   (`KSA/Program.cs:1526`), non-main viewports build their own (`ViewportRenderSurface.BuildOwned`),
   and `_renderedViewport` is set per viewport in `RenderViewport` (`:4315`) — both identity checks
   still select exactly the main flight-scene resolve.
2. **Frame ordering** — decal matrices are composed in `GraffitiSubmod.Update`, dispatched by
   StarMap's `[StarMapBeforeGui]` = prefix on `Program.OnDrawUiFrame` (decl `KSA/Program.cs:3021`,
   called `:2193`), which runs AFTER `OnFrameViewports` (`:2189`, cameras updated) and BEFORE
   `RenderGame` — same-frame camera, no swim. Since 5402 the cursor ray is no longer cached: the
   desktop cursor position is captured in `PrepareImGui` (`:2091 Cursor.SetDesktopPosition`) before
   `OnFrameViewports`, and `Cursor.GetEgoRay(viewport)` builds the ray from that position and the
   viewport's current camera at call time — the click ray is now **same-frame**, not one frame
   stale (5348 and earlier cached `Cursor.InputRay` after the UI phase).
3. **Picking** (`DecalPicker.cs`) — `Cursor.GetEgoRay(Program.MainViewport)` sweeps vehicles and
   their deployed canopies before terrain. Ordinary parts use
   `Part.RayCastEgo` (the identical sweep KSA's flight-mode hover picking runs: bounding-sphere
   broad phase, then `Ray.RaycastWatertight` over the view mesh), else a 64-step march + 24
   bisections over `Celestial.GetTerrainHeightFromDirCcf` in CCF (the shape of
   `TerrainImpactFinder.TryFind`), **every sample `accurate: true`** — see #10. A vehicle hit
   anchors to the hit **sub-part's** `InstanceId` (RayCastEgo returns position/normal in
   `closestSubPart`'s local frame). A **KittenEva** has no raycastable part view mesh, so it gets
   the game's own `KittenEva.UpdateHighlight` treatment instead: `Ray.Raycast` against a
   `BoundingSphere3D` at the root part's ego position (radius × the root's largest scale
   element), anchoring to the root part at the chord midpoint with the normal facing the
   clicker; the placement then floors the box depth at the sphere diameter
   (`PickResult.SuggestedMinDepth`) so the projected box reaches the avatar inside.
   A deployed parachute canopy is not a part view mesh: `DecalPicker.Parachute.cs` transforms its
   public `Parachute.ClothPositionsFront` nodes through the same attachment-local matrix
   as `Parachute.DrawCanopy`, tessellated as the topology's apex fan plus adjacent ring quads, and
   tested with `Ray.RaycastWatertight`. The hit stores three node indices + barycentric weights and
   the clicked side, so the anchor follows the live two-sided cloth surface each frame.
4. **Per-frame composition** (`DecalAnchors.cs`) — decal-space cube → ego as S·R·T·parent in
   double, inverted in double, packed to float push constants. Vehicle anchors:
   `Vehicle.GetMatrixAsmb2Ego(Camera)` + `Part.MatrixAsmb2Ego` (includes scale + sub-part
   chain). Parachute anchors recompute the barycentric point and triangle normal from the current
   front cloth nodes, then use the canopy attachment-local matrix. Terrain anchors: ENU basis via
   `Vehicle.ComputeEnu2Cce` + terrain radius, positioned
   as body-ego + body-fixed offset (the terrain-debug-overlay idiom).
5. **Draw** (`DecalRenderer.cs`) — pipeline layout = set 0 `GlobalShaderBindings` (dynamic
   offset per viewport, `DynamicOffset(Program.MainViewport.ShaderSlot)` since 5402 — was
   `Viewport.Index`), set 1 own depth sampler, set 2 `Program.Instance.BindlessTextures`
   (UpdateAfterBind|PartiallyBound); 112-byte push block; two GLSL strings compiled at runtime
   with `ShaderModuleUtils.FromString` whose `#include`s resolve next to the shipped `GridFrag`
   asset (`ModLibrary.Get<ShaderReference>("GridFrag").ModPath`). CullFront (camera-inside-box),
   no depth test (occlusion from sampled reverse-Z depth), alpha-over blend, single-sample,
   colour format `Program.Instance.ColorFormat`. Depth is barriered to `DepthSampledReadF` and
   left there, exactly as GridPass leaves it.
6. **Textures** (`DecalTextures.cs`) — `TextureLoader.LoadFromMemory` (PNG, forced RGBA8 via
   `TextureAsset.LoadOptions(R8G8B8A8UNorm, Rgba32)`) → `SimpleVkTexture` (max edge 2048,
   downsample, full mip chain) → `BindlessTextureLibrary.AddTexture/FreeTexture`. Freed images
   ride a MaxFramesInFlight+1 retire queue; teardown drains
   `Program.GetRenderer().GraphicsAndCompute.WaitIdle()` first.

**Persistence** — none for placed decals (session-scoped). The shared PNG library is plain files at
`<MyDocuments>/My Games/Kitten Space Agency/.unscience/pngs/`, owned by
`ksa-abstractions.lib/PngLibrary.cs` and also consumed by free-fallin. Imports always copy into that
folder. It is scanned at startup and on demand via the Rescan button; there is no background watcher
or polling loop. These are mod-authored files, not game assets.

## Touchpoints

| # | Kind | Mod code | Game member | Decomp path (5402) | Status | Notes |
|---|---|---|---|---|---|---|
| 1 | **Harmony postfix** | `GraffitiPatches.cs:31,60` | `RenderTarget.ResolveAttachments(CommandBuffer inCmdBuffer)` | `KSA.Rendering/RenderTarget.cs:315` | ✅ (file byte-identical 5348→5402) | resolved with `nameof` — rename = compile break. Param name `inCmdBuffer` bound by Harmony: a rename silently unbinds → `Apply` throws → `TryApply` skips graffiti |
| 2 | Direct API | `GraffitiPatches.cs:54-60`; `DecalRenderer.cs:360,400,402` | `Program.{EditorFlag, OffscreenTarget, RenderedViewport : IViewport, MainViewport : IGameViewport, SetViewport(CommandBuffer), PointClampedSampler, Instance.ResourceFrameIndex, Instance.ColorFormat, Instance.BindlessTextures, GetRenderer(), GetMainCamera()}` | `KSA/Program.cs:224,457,491,485,4293,469,218,222,110,558,632` | ✅ retyped @5402 | `RenderedViewport`/`MainViewport` were `Viewport` (class, removed @5402); `ReferenceEquals` on the same `GameViewport` object still holds. Main-viewport `OffscreenTarget` == `Program.OffscreenTarget` (`:1526`). Viewport identity checks are load-bearing (editor + portrait/secondary exclusion) |
| 3 | Direct API | `DecalRenderer.cs:362-390` | `RenderTarget.{DepthImage, ColorImage, Extent}`; `BarrierBatch`; `ImageBarrierInfo.Presets.{DepthSampledReadF, ColorAttachmentReadWrite}` | `KSA.Rendering/RenderTarget.cs:38,36,48`; `KSA.Rendering/BarrierBatch.cs`; `KSA.Rendering/ImageBarrierInfo.cs` | ✅ (all three files byte-identical 5348→5402) | near-verbatim GridPass.Run port; depth left in sampled-read state as the game's own pass does. @5402 `GridPass` itself went per-viewport (`SceneDepthDescriptorSets[8]` indexed by `ShaderSlot`, `UpdateDescriptorSet(IViewport)`, `Run` reads `inViewport.OffscreenTarget` — `KSA/GridPass.cs:43,128,455,486`); graffiti rebuilds its own depth set from `Program.OffscreenTarget.DepthImage` per frame ring, main-only, so nothing changes |
| 4 | Direct API | `DecalRenderer.cs:402-404` | `GlobalShaderBindings.{DescriptorSetLayout, DescriptorSet, DynamicOffset(int) : ByteSize}` + `IViewport.ShaderSlot` | `KSA/GlobalShaderBindings.cs:55,57,64`; `KSA/IViewport.cs:14` | ✅ (mod-side `Viewport.Index` → `ShaderSlot` @5402) | set 0 — the game-wide Camera/Lighting UBO block; set order is baked into the GLSL. UBO stride/order unchanged; the buffer is now sized for a fixed 8 shader slots (`ViewportRegistry.MAX_VIEWPORTS`) instead of `Program.ViewportCount` (6) — slots are pool-allocated, so always look up `MainViewport.ShaderSlot`, never assume 0 |
| 5 | Direct API | `DecalRenderer.cs:161,409`; `DecalTextures.cs:156,164` | `BindlessTextureLibrary.{DescriptorSetLayout, DescriptorSet, AddTexture(VkImageView), FreeTexture(int)}` | `RenderCore.Systems/BindlessTextureLibrary.cs:38,40` | ✅ (file byte-identical 5348→5402) | 1024 shared slots; UpdateAfterBind\|PartiallyBound makes live slot writes legal; FreeTexture rewrites the slot to the empty texture. Sampler slot 0 = linear-clamped full-mip (the shader's `SAMPLE_TEXTURE(texId, 0, uv)`) |
| 6 | Direct API (runtime GLSL) | `DecalRenderer.cs`; `DecalShaders.cs` | `ShaderModuleUtils.FromString(...)`; `ModLibrary.Get<ShaderReference>("GridFrag").ModPath`; GLSL headers `Common/Camera.glsl`, `Common/TextureSet.glsl` | `RenderCore/ShaderModuleUtils.cs:79`; `Content/Core/Shaders/Common/*` | ✅ | debugName must be a NUL-terminated real path next to the game's shaders (relative `#include` root). Shader reads `global.camera.{viewProjection, inverseProjection, inverseView}` and `global.lighting.{sunPosition, sunColor, planetColor}` — GLSL struct drift breaks at shaderc compile (loud console line, feature self-disables) |
| 7 | Direct API (pipeline) | `DecalRenderer.cs` | `Presets.{InputAssembly.TriangleList, Rasterization.Fill.CullFront}`; `RenderingPresets.{ReverseZDepthStencil.NoDepthTest, BlendState.BlendColorAlphaOver}`; `Renderer.{Device, Allocator, Graphics, GraphicsAndCompute, MaxFramesInFlight, DynamicStateInfo, ViewportState}`; `VkUtils.StageAndUploadToBuffer` | `Brutal.VulkanApi.Abstractions/Presets.cs`; `KSA/RenderingPresets.cs`; `Core/Renderer.cs`; `RenderCore/VkUtils.cs` | ✅ | reverse-Z + CullFront semantics are load-bearing (see risk notes) |
| 8 | Direct API (textures) | `DecalTextures.cs` | `TextureLoader.LoadFromMemory`; `TextureAsset(.LoadOptions)`; `new SimpleVkTexture(Allocator, StagingPool, TextureAsset, CreateOptions)`; `Stb/Ktx/GliTexture.Destroy()`; `CreateStagingPool` ext | `Brutal.TextureApi/TextureLoader.cs:130`; `RenderCore/TextureAsset.cs:35`; `RenderCore/SimpleVkTexture.cs:245`; `Brutal.VulkanApi.Abstractions/StagingPoolExtensions.cs` | ✅ | R8G8B8A8UNorm forces 4 channels; `ITexture` has no IDisposable — `Destroy()` must be called or the decode buffer leaks |
| 9 | Direct API (pick) | `DecalPicker.cs:53-56,82-103` | `Cursor.GetEgoRay(IViewport)` (**replaced `Cursor.InputRay` @5402**); `Part.RayCastEgo(ref readonly double4x4, Ray, out …×8, out Part?, out Part?)`; `Vehicle.{BoundingSphereRadiusBody, GetMatrixAsmb2Ego(Camera)}`; `Camera.{GetPositionEgo(IPosition), NearbyCelestial}` | `KSA/Cursor.cs:27`; `KSA/Part.cs:2398`; `KSA/Vehicle.cs:1256`; `KSA/Camera.cs:231,71` | ✅ **fixed @5402** (compile break, `Cursor.InputRay`/`UpdateInputRay`/`ScreenPosition` removed) | `GetEgoRay(viewport)` = `viewport.GetCamera().ScreenToEgoRay(Cursor.GetPosition(viewport))` — ego-space, built on demand from the viewport-local cursor position (`DesktopPosition - viewport.Position`) and the **current** camera, so it is same-frame (was one frame stale). Passed `Program.MainViewport` (main-viewport-only feature). RayCastEgo only hits SUB-parts — a top-level part with no sub-parts returns false (the loop never runs); acceptable: stock parts all have sub-parts |
| 9b | Direct API (kitten pick) | `DecalPicker.cs` (`TryPickKitten`) | `KittenEva` (type, `is` check); `new BoundingSphere3D(double3, double)`; `Ray.Raycast(BoundingSphere3D, out double, out bool)`; `Double3Ex.GetAbsoluteLargestElement(double3)`; `Part.{PositionEgo(ref readonly double4x4), ScaleTotal, MatrixAsmb2Ego}`; `PartTree.Root` | `KSA/KittenEva.cs:13`; `KSA/BoundingSphere3D.cs`; `KSA/Ray.cs:38`; `KSA/Double3Ex.cs:165`; `KSA/Part.cs:1155,802,1165`; `KSA/PartTree.cs:97` | ✅ (`Ray.cs`, `BoundingSphere3D.cs`, `Double3Ex.cs` byte-identical) | mirrors the game's own `KittenEva.UpdateHighlight(IGameViewport)` sphere pick (`KittenEva.cs:1102-1125`; @5402 it takes `Cursor.GetEgoRay(inViewport)` and reports via `inViewport.PartPicker.TryOffer`, the sphere math is unchanged) — kittens render through `CharacterAvatar`, not part view meshes, so `RayCastEgo` can never hit them |
| 9c | Direct API (parachute pick + anchor) | `DecalPicker.Parachute.cs` (`TryPickParachute`); `DecalAnchors.cs` (`TryComposeParachute`); `GraffitiSubmod.cs` (`FindParachute`) | `PartTree.Modules.Get<Parachute>()`; `ModuleBase.InstanceId`; `Parachute.{ClothPositionsFront, AttachLocationPartAsmb, Parent, CanopyIndex}`; `ChuteClothSystem.Topology`; `ChuteClothTopology.{Rings, Spokes, ApexIndex, CanopyNodeCount, NodeIndex}`; `Ray.RaycastWatertight(double3,double3,double3,out double)`; `Part.MatrixAsmb2VehicleAsmb` | `KSA/Parachute.cs:182-210,1108-1153`; `KSA/ChuteClothSystem.cs:84-98`; `KSA/ChuteClothTopology.cs`; `KSA/Ray.cs:141`; `KSA/ModuleBase.cs:27`; `KSA/Part.cs` | ✅ added @5402 | Canopies bypass `Part.RayCastEgo`. The proxy triangles use the same current front cloth buffer and attachment-local→ego transform as canopy rendering. Re-resolution prefers the runtime module id and falls back to stable parent-part id + authored canopy index after reload; barycentric node weights follow inflation/flutter. The visible skinned GLB is bone-driven by these nodes rather than being the literal 240-triangle proxy, so projection depth absorbs small surface differences; requires a live placement check. |
| 10 | Direct API (terrain) | `DecalPicker.cs:180-245`; `DecalAnchors.cs:79-90` | `Celestial.{GetCce2Ccf, GetCcf2Cce, GetCci2Cce, MeanRadius, GetTerrainHeightFromDirCcf(dir,bool), GetDirCcfFromLatLon, GetLatitudeFromCcf, GetLongitudeFromCcf}`; `Vehicle.ComputeEnu2Cce(double3, doubleQuat)` | `KSA/Celestial.cs:540,534,522,91,791,669,707,742`; `KSA/Vehicle.cs:3143` | ⚠ signature-identical; **accurate-path drift @5402 — needs live check** | lat/lon statics return DEGREES; height is metres above MeanRadius (0 for no heightmap); ComputeEnu2Cce returns null on the spin axis (pole fallback basis in `DecalAnchors`). ⚠ **`accurate: true` is load-bearing** everywhere the surface radius matters: only accurate mode evaluates the procedural terrain modifiers (`Celestial.cs:877-880`, gated `if (accurate)` since the 5319–5325 terrain precision rework) — the metres-scale displacement the rendered surface includes. @5402 the modifier evaluation's radius inputs (`gradientWeight`, `CelestialRadiusKm`, `Celestial.cs:825-848`) switched from `RenderData.SurfaceRadius` to `(float)MeanRadius`; if the GPU terrain did not move identically, terrain decals float/sink again. The composed radius is cached per entry (`DecalEntry.TerrainRadius`; terrain is static) |
| 11 | Direct API (anchor re-resolve) | `GraffitiSubmod.cs:240-260` | `Universe.CurrentSystem.Get(string)`; `Vehicle.Parts.Parts`; `Part.{SubParts, InstanceId, MatrixAsmb2Ego(in double4x4), Id}` | `KSA/Universe.cs:94`; `KSA/CelestialSystem.cs:288`; `KSA/Part.cs:1079,574,1165,698` | ✅ | per-frame; a despawned anchor makes the decal dormant, never pruned |
| 12 | Build refs | `graffiti.lib.csproj` | `Brutal.Vulkan(.Abstractions/.Vma)`, `Brutal.ShaderC`, `Brutal.Texture(.Abstractions)`, `Brutal.Ktx`, `Brutal.Core.Memory`, `Planet.Core`, `Planet.Render.Core` | — | ✅ | Planet.Render.Core carries `RenderCore`/`RenderCore.Systems`; Planet.Core carries `Core.Renderer` |

## Update-risk findings

- **Loud breaks (compile):** any signature change in the touchpoint table — no reflection is
  used anywhere. `ResolveAttachments` rename (#1 via `nameof`), pipeline/preset reshapes (#7),
  `SimpleVkTexture`/`TextureLoader` churn (#8 — the historically highest-churn surface, shared
  with thug-life).
- **Loud breaks (runtime, one console line, feature self-disables):** GLSL header drift
  (`Common/Camera.glsl` / `Common/TextureSet.glsl` struct or macro changes) fails at shaderc
  compile inside `EnsureGpu`; a draw fault latches `_gpuFailed`.
- **Silent breaks (semantic drift, no symbol change):**
  - the post-resolve seam's *meaning* — if the game moves grid/overlay drawing elsewhere or
    starts resolving into a different image, decals draw into the wrong target (symptom: decals
    gone or double-exposed, no error). Re-check `GridPass.Run` against `RecordPass` on every bump.
  - reverse-Z / NDC conventions in the depth reconstruction (symptom: decals project to wrong
    positions). The debug-box checkbox (magenta checker) is the built-in diagnostic.
  - `Cursor.GetEgoRay(viewport)` space/timing (ego-space, same-frame since 5402; it subtracts
    `viewport.Position`, so a main viewport that stops sitting at the window origin would skew
    picks), and `Part.RayCastEgo` frame conventions (position/normal are in the SUB-part's local
    frame — if that changes, placements land skewed).
  - the `accurate` flag's meaning in `GetTerrainHeightFromDirCcf` (#10): if a future build starts
    evaluating (or stops gating) the procedural modifiers differently, terrain decals silently
    float above / sink below the rendered surface again — the exact 2026-08-30 bug. Symptom:
    terrain placement reports success but nothing draws (the box misses the rendered surface);
    the debug-box checker not appearing on flat ground is the tell.
  - bindless sampler slot 0 semantics (linear-clamped); a sampler-table reshuffle turns decals
    point-sampled or wrapped.
- **Harmony param binding:** #1's `inCmdBuffer` param name — a rename throws at `Apply`
  (logged + skipped), same failure mode as pyro's postfix.
- **Projection-depth geometry (not a game coupling, but the #1 user-visible surprise):** the
  visible decal is the surface ∩ projection box. A box too shallow for the surface's curvature
  crops a wide decal to its central region — which looks like the image "zoomed in" (footprint
  grows, image edges vanish; the matrix path itself is exact — verified numerically). Default
  depth therefore scales with the decal (`GraffitiSubmod.AutoDepth`: half the larger side,
  floored at 0.3 m hull / 2 m terrain), and Depth is a placement setting. Terrain boxes
  additionally deepen with camera distance at compose time (`DecalAnchors.TerrainDepthPerMetre`
  = 1% of distance): the rendered terrain is a screen-space-error LOD mesh whose surface drifts
  metres-to-tens-of-metres off the true height as the camera pulls back — without the distance
  term, terrain decals vanish around thousands of metres out. The draw cull itself
  (`DecalRenderer.MaxViewDistanceMetres`, default 50 km) is mutable and exposed in the panel
  ("Max draw dist"). Too much depth has the
  opposite failure: the parallel projection punches through thin geometry and paints the far
  side (the normal-cutoff fade does not stop it, since the flipped normal can still face the
  decal axis).
- **Not done / known limits:** flight scene only (editor excluded by design); main viewport only;
  placed decals are not persisted; a top-level part with zero sub-parts cannot be clicked (see
  #9); decals do not draw while `VolumetricExhaust`-style per-viewport secondary cameras render.
  KittenEva decals anchor to the ROOT part's frame (the avatar's animation pose is not part of
  the part matrix), so a decal sprayed on a kitten stays put in body space rather than following
  a waving limb.

### 5348 → 5402 (2026-09-02)

Revisions 5349–5400 are unlogged in any changelog (only rev 5401 "Fixed crash for incorrect data
stride for thumbnail rendering" is recorded); the source diff below is the only evidence.

- 🔴 **COMPILE BREAK (fixed): `Cursor.InputRay` removed.** `KSA/Cursor.cs` was rewritten around
  the desktop cursor: `InputRay`, `UpdateInputRay(Camera?)`, `ScreenPosition`, `SetScreenPosition`
  are gone; new API is `GetEgoRay(IViewport)` (`:27`), `GetPosition(IViewport)` (`:22`),
  `DesktopPosition : float2`, `SetDesktopPosition(float2)`. `DecalPicker.cs:55` (`var ray =
  Cursor.InputRay;`) → **`Cursor.GetEgoRay(Program.MainViewport)`** (`:56`). Semantic change is
  favourable: the ray is built at call time from the current camera and the cursor position
  captured in `PrepareImGui` (`Program.cs:2091`), i.e. **same-frame** — the "one frame stale"
  caveat in #2/#9 no longer applies. The game's own pickers moved the same way
  (`Vehicle.UpdateHighlight`, `KittenEva.UpdateHighlight:1115`).
- 🔴 **COMPILE BREAK (fixed): `Viewport` class replaced by `IViewport`/`IGameViewport`.**
  `Program.MainViewport.Index` → `Program.MainViewport.ShaderSlot` (`DecalRenderer.cs:402`);
  `GlobalShaderBindings.DynamicOffset(int)` itself is unchanged (`:64`).
- ✅ **Seam identity verified.** `RenderTarget.cs` is byte-identical (`ResolveAttachments :315`).
  Main-path order is unchanged: `GizmoPass.Run` (`Program.cs:4736`) → `RenderedViewport
  .OffscreenTarget.ResolveAttachments` (`:4737`) → `GridPass.Run` (`:4745`). The main viewport's
  `OffscreenTarget` is `Program._offscreenTarget` (`AttachSharedTargets`, `:1526`); secondary and
  crew-portrait viewports own their targets (`KSA.Rendering/ViewportRenderSurface.cs:107`) and
  `RenderViewport` sets `_renderedViewport = viewport` (`:4315`) before their resolve, so
  `__instance == Program.OffscreenTarget && RenderedViewport == MainViewport` still fires exactly
  once, on the main flight-scene resolve; the editor path (`:4864`) is still excluded by
  `EditorFlag`.
- ✅ **GridPass drift absorbed.** The game's grid now keeps one scene-depth descriptor set per
  shader slot (`GridPass.cs:43`, `UpdateDescriptorSet(IViewport) :128`, `Run` uses
  `inViewport.OffscreenTarget` `:455`). graffiti refreshes its own depth set from
  `Program.OffscreenTarget.DepthImage` every frame and only draws for the main viewport — no
  change. `GlobalShaderBindings` UBO layout unchanged; buffer sized for 8 fixed slots.
- ✅ `Common/Camera.glsl`, `Common/TextureSet.glsl`, `Grid.frag`/`Grid.vert` byte-identical;
  `DefaultAssets.xml` gained one line (`StaticObjectPrePassIndirectFrag`), `GridFrag` id now at
  `:374`. `BindlessTextureLibrary.cs`, `ImageBarrierInfo.cs`, `BarrierBatch.cs`,
  `RenderingPresets.cs`, `ShaderModuleUtils.cs`, `TextureLoader.cs`, `TextureAsset.cs`,
  `SimpleVkTexture.cs`, `Ray.cs`, `BoundingSphere3D.cs`, `Double3Ex.cs` byte-identical.
- ⚠ **Needs a live terrain-decal check.** `Celestial.GetTerrainHeightFromDirCcf` (`:791`) is
  signature-identical, but inside the accurate-only modifier evaluation (`:825-848`) the radius
  inputs changed from `base.RenderData.SurfaceRadius` to `(float)MeanRadius` (`gradientWeight`,
  `CelestialRadiusKm`). This is the exact input #10 warns about: place a decal on flat and on
  hilly terrain and confirm the debug-box checker sits on the rendered surface.
- ⚠ **Needs a live pass** for the custom pipeline in general (own descriptor sets, bindless
  slot writes, reverse-Z depth reconstruction) — nothing in the API moved, but rev 5401's
  thumbnail-stride fix and the per-viewport render-surface rework are render-loop changes only an
  in-game look confirms. Also re-check a decal on a `KittenEva`: the game's own kitten pick is now
  gated by `ViewportOptionFlags.AllowSelection` / `CursorTarget.IsHitTestViewport`; graffiti's
  mirror is not, by design.
- ⚠ **Needs a live canopy-decal check.** Place on the top and underside while reefed and fully
  inflated; confirm the cloth proxy selects the visible canopy, the clicked-side normal passes the
  grazing-angle test, and barycentric anchoring follows flutter without leaving the projection box.
- ℹ Not otherwise graffiti-facing: `RayIntersections.glsl` cylinder `quadraticA` fix, new
  `Mesh/StaticObjectNormalIndirect.frag`, `PartFailure` / `ExhaustPlumeDeformation`.

## Save/load adapter (feature/saves)

`GraffitiSubmod.Saves` serializes explicit decal recipe/anchor fields through
`ISaveParticipantSource`. Vehicle and canopy parent parts use shared `SavedPartReference`
tree/subpart addresses with template checks; saved module IDs are deliberately replaced
by newly resolved parent instance ID plus `Parachute.CanopyIndex`. `FindPart` now descends
all nested subparts. Exact terrain IDs and geodetic anchors are retained.

Reset cancels gestures, disables publication, drains `Program.GetRenderer().GraphicsAndCompute`,
disposes the decal renderer and image cache, and clears old records before native teardown.
Restore reconstructs native references/texture handles; cloth weights are retained but
render matrices are recomputed on the next Update. Missing canopy/images remain dormant
where possible and warn; the save coordinator retains the source feature record. Native
acceptance must include changed cloth topology, nested subparts and saved stowed canopies.
