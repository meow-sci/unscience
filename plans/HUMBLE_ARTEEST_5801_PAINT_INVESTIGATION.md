# Humble Arteest immediate white vessels (5801)

The player reports that whole vessels glow bright white immediately on **Enable**, before
applying a color. This remains an open native rendering defect. The working kitten material
coloring path is separate from Vehicle Paint's part shader interception.

Three independent agents audited the shader transform, pipeline interface, and renderer rebuild.
They compared CURRENT `2026.11.10.5801` with PREVIOUS `2026.10.25.5707`, using the supplied
game assemblies and shader sources as authoritative. No confirmed cause for the exterior white
glow was found, and no speculative rendering fix is claimed.

## Evidence

- Actual production `VehiclePaintShaderSource` generated Multiply, Tint and Replace shaders.
  `glslc` compiled 64 stock/painted binaries across both part fragment sources and 16 native
  variant groups (static emissive/dynamic temperature, thin film, wet/frost combinations), with
  zero compilation failures. SPIR-V locations, descriptor bindings/sets, member offsets,
  array/matrix strides and specialization interfaces matched the stock shader in every group.
- The current native per-instance flags remain at byte 64 of the 80-byte record; emissive or
  temperature starts at 68, thin film at 72 and wetness at 76. The earlier upgrade already moved
  paint to bits 13–30 to preserve the new native habitat highlight bits 11 and 12.
- Enable installs/intercepts compilation and rebuilds the renderer. It does not assign paint,
  change engine emissive settings, lighting or exposure. Multiply decodes normalized channels
  and cannot increase the sampled albedo. These facts narrow investigation but do not establish
  correct native GPU execution.
- `Program.RebuildRenderer(bool)` and the relevant part renderer rebuild behavior were compared
  across the two builds without a confirmed regression.
- A separate existing IVA limitation was found: `RaytracingRenderer.BuildRasterizationPipeline`
  takes its fragment module from cached `MeshIndirectRaytracedFrag.Shader.Value`, bypassing the
  file interception on rebuild. This exists in both builds and can explain missing raytraced
  IVA painting; it does not explain the reported exterior white glow.

## Added diagnostics and native A/B

Activation now logs the loaded library path/MVID, selected shader mode/blend, paint counts and
Harmony owners of each paint seam. Each intercepted source logs its path and byte count once.
This identifies stale assemblies, pre-existing paint state and competing patches in a native log.

For an isolated compiler-path comparison, start a fresh StarMap process with:

```powershell
$env:UNSCIENCE_PAINT_SHADER_MODE = 'stock'
```

Enable Vehicle Paint in this diagnostic process. The interception forwards the untouched stock
GLSL through the same `FromString` route, retaining native compile options and include/debug path.
It intentionally applies no part colors. Remove the variable and restart to return to painting:

```powershell
Remove-Item Env:UNSCIENCE_PAINT_SHADER_MODE
```

Native acceptance still needs: an ordinary stock renderer rebuild with paint disabled (for example
a graphics option that rebuilds it); Enable with cleared paint records; the stock-source diagnostic;
then ordinary Multiply/Tint/Replace. Record whether Disable restores normal rendering and attach the
activation/compile diagnostics. The diagnostic setting is process-local, not a scene-save option.

Production managed tests now exercise the actual Harmony compile prefix: exact source bytes,
native options, stage/result, terminated path, success count, disabled path and stock fallback on
compile failure. Pure-source checks also require byte-identical diagnostic input. These tests and
offline shader compilation do not run the game's Vulkan renderer.
