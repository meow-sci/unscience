# thug-life

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Apply the classic "thug life" sunglasses meme as a 2D textured quad anchored to any
part or subpart of any vehicle in 3D space. Multiple sunglasses can be placed at once,
each with its own offset, rotation, and size.

## Toggle window

Press **F12** to open / close the standalone control panel. When loaded inside the
**unscience** supermod, the same UI appears as a collapsible section in the unscience
window (F11) — no separate hotkey needed there.

## Usage

1. Press F12 to open the **Thug Life** window.
2. Under **Anchor New Sunglasses**:
   - Pick a **Vehicle** from the dropdown
   - Pick a **Part** on that vehicle
   - Optionally pick a **SubPart** of that part — leave at `(use this part)` to
     anchor to the part itself
3. Tune the **Position** (meters), **Rotation** (degrees pitch/yaw/roll), and **Width / Height** (meters) in the anchor part's local frame.
4. Click **Add Sunglasses**.

The new sunglasses entry appears under **Active Sunglasses** and can be re-tuned in
place. Toggle the **Visible** checkbox to hide an entry without removing it, or click
the red **Remove** button to delete it.

### animate thug (kitten one-click)

When the selected **Vehicle** is a kitten on EVA, an **animate thug** button appears next
to *Add Sunglasses*. It ignores the form's position / rotation / size fields and drops a
pre-tuned pair of shades onto the cat's face, sliding them in from off-frame:

| | value |
|---|---|
| start position | `0.251, 0, -2` |
| end position | `0.251, 0, -0.761` |
| rotation | `-90, 0, 90` |
| width x height | `0.975` x `0.2` m |
| slide | 1.2 s, ease-out |

No **Part** selection is needed — if none is picked the entry anchors to the kitten's root
part (its MMU backpack, which is what `KittenEva` is built around). If a part or subpart
*is* picked, that anchor is used instead. Only EVA kittens are offered: a seated kitten is
not a vehicle and never appears in the target list.

The values live in [`KittenGlassesPreset.cs`](../thug-life.lib/KittenGlassesPreset.cs) and
the slide in [`ThugLifeSlide.cs`](../thug-life.lib/ThugLifeSlide.cs). The landed entry is a
perfectly ordinary entry — re-tune, hide or remove it like any other.

## How it works

### Texture
Generated programmatically in [ThugLifeTexturePattern.cs](../thug-life.lib/ThugLifeTexturePattern.cs) as a 15x4 R8G8B8A8UNorm bitmap matching the iconic
blocky sunglasses look — two lenses with a transparent bridge, stepped top/bottom
edges, and white "glare" highlights in the upper-left of each lens.

The texture is uploaded via [ThugLifeTextureFactory](../thug-life.lib/ThugLifeTextureFactory.cs) using
`SimpleVkTexture` + `VkUtils.UploadBufferToImage`. A nearest-neighbour sampler preserves the blocky pixel-art look at any size.

### Rendering
Each frame, a Harmony **postfix** on `SuperMeshRenderSystem.RenderMainPass` injects
draw commands for every entry into the active offscreen render pass:

- Pipeline uses KSA's stock `UnlitMeshVert` / `UnlitMeshFrag` shaders.
- Targets `Program.OffscreenTarget` (NOT `Program.MainPass`). Since KSA 5261 the
  offscreen scene pass is Vulkan *dynamic rendering*, so the target stamps the pipeline
  itself via `SetupGraphicsPipeline` — supplying the colour/depth formats and its own
  MSAA sample count. Reverse-Z depth test/write, and no face culling so the quad is
  visible from both sides.
- Per-entry model matrix is composed in ego-space using `part.PositionEgo` +
  `part.Asmb2Ego` so the quad rides along with whatever vehicle / subpart it is
  anchored to.
- The MVP uses `Program.GetRenderCamera()` — the camera of the viewport *currently
  being rendered*, not the main camera. `RenderMainPass` runs once per visible
  viewport, which includes the two always-on crew-portrait viewports, and ego space is
  camera-relative; the main camera would draw those passes with the wrong clip
  transform.

### GPU init is lazy — on purpose

The pipeline, texture and buffers are built on the **first** anchored entry, not at mod
load. StarMap fires `[StarMapAllModsLoaded]` from a postfix on `ModLibrary.LoadAll()`
(`KSA/Program.cs:897`), and the game does not create `Program.OffscreenTarget` until
`BuildRenderTargets()` further down the same boot method (`:934`). Building the pipeline
at load therefore dereferenced a null `RenderTarget` and the window showed *"init
failed: Object reference not set to an instance of an object"*. Do not move GPU
allocation back into `Initialize()`. A side benefit: a loaded-but-unused mod costs no
GPU memory.

Detailed approach lives in the project's [ksa/quad.md skill doc](../.claude/skills/ksa/quad.md).

## Files

### thug-life (mod entry assembly)

| File | Purpose |
|---|---|
| `Mod.cs` | StarMap lifecycle, F12 toggle, top-level ImGui window framing. |
| `Patcher.cs` | Applies `HotkeyGuard` plus calls `ThugLifeRenderPatches.Apply` to install the render postfix. |

### thug-life.lib (reusable core)

| File | Purpose |
|---|---|
| `ThugLifeSubmod.cs` | `ISubmod` implementation owning the UI and the render manager. Hosted by both the standalone Mod.cs and unscience. |
| `ThugLifeEntry.cs` | Per-anchor state: vehicle, part, position/rotation/size, visibility, optional slide. |
| `ThugLifeSlide.cs` | One-shot ease-out position animation, advanced once per frame by the manager. |
| `KittenGlassesPreset.cs` | The tuned kitten pose behind the **animate thug** button, plus the `IsKitten` test. |
| `ThugLifeRenderManager.cs` | Holds entries + GPU resources; brings the GPU resources up lazily on the first entry (`EnsureGpuResources`); advances slides in `Update(dt)`; static `Active`/`Instance` for the Harmony postfix; iterates entries and submits draws. |
| `ThugLifeQuadRenderer.cs` | Owns the pipeline, descriptor set, vertex/index buffers; computes the per-frame MVP and records one draw per entry. |
| `ThugLifeTextureFactory.cs` | Creates the `SimpleVkTexture` + `VkSampler` for the sunglasses pattern. |
| `ThugLifeRenderPatches.cs` | Shared `Apply` / `Remove` Harmony postfix on `SuperMeshRenderSystem.RenderMainPass` — used by both the standalone Patcher and the unscience Patcher. |
| `ThugLifeTexturePattern.cs` | The 15x4 ASCII pixel grid that defines the meme image. |

## Notes

- Width / Height are in meters and apply directly to the unit-square quad — the
  default 0.6 m × 0.16 m matches the texture's 15:4 aspect ratio. Tweak both to
  re-stretch.
- Rotation is applied in the **anchor part's local frame** (not the camera or
  vehicle), so a fixed rotation stays "stuck" to the part even as the vehicle
  rotates relative to the camera.
- The quad's `+Z` normal initially points along the anchor part's +Z; use the
  rotation fields to face it where you want.
- If rendering ever throws (e.g. shaders missing), the manager disables itself and
  the error appears in the UI; it never spams the render loop. A GPU-init failure
  surfaces on the **Add Sunglasses** button, since that is what brings the pipeline up.
