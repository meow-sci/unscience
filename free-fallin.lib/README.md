# free-fallin.lib

Reusable core for [`../free-fallin`](../free-fallin) and the unscience umbrella mod.

| File | Responsibility |
|---|---|
| `FreeFallinSubmod.cs` | `ISubmod` lifecycle and ImGui appearance/PBR editor |
| `FreeFallinPatches.cs` | Prefixes `ChuteRenderable.Draw`, substitutes material handle 0, and restores observed canopies |
| `CanopyProjectionShaders.cs` | Injects the material-gated Full Canopy varying/albedo projection into KSA's model PBR shaders in memory |
| `CanopyMaterialController.cs` | Transcodes the stock BC7 KTX2 to RGBA8 when compositing a decal, then builds GPU albedo/PBR textures and `MaterialData` objects |
| `CanopyMaterialSettings.cs` / `CanopyTextureMode.cs` | Public settings model and Stock/Replace/FullCanopy/CenterDecal modes |
| Shared PNG dependency | Uses `ksa-abstractions.lib`'s `.unscience/pngs` catalog and `PngFileBrowser` |

The lib owns generated KSA texture/material registrations and retires the previous allocation
after switching observed canopies and waiting for the GPU. Allocations happen on Apply, not
per UI change or frame.

Full Canopy stores projection scale, rotation, and a mode marker in `MaterialData.ExtraData`. The
patched skinned vertex shader derives a second UV from the canopy's bind-pose X/Z coordinates. The
patched PBR fragment shader uses that UV only for marked materials' albedo; the authored UV remains
in use for the stock normal and AO/roughness/metallic maps. Shader files on disk are never modified.

## Native save persistence

Ordinary KSA saves retain the **last successfully applied** global canopy settings: texture
mode and shared PNG name, tint/brightness, full-canopy rotation, decal size and PBR controls.
The effective material color is saved separately, so a later Humble Arteest recolor survives
load without changing the canopy's authored tint controls or depending on generated material names.
Unapplied controls remain editor state. Loading a native save without this feature restores
stock appearance. PNG files remain shared-library dependencies; missing files produce a
Saves warning and retain the original saved feature record for recovery.

`FreeFallinSubmod.Saves` provides the feature contract. `CanopyMaterialController` retains
a detached applied recipe; `CanopyGpuAssets` owns private material/texture allocations so
repeated loads retire prior resources. Observed canopy material indices are switched before
GPU retirement, with an idle-device wait. Shader hooks remain under the existing host.
The shared `MaterialColorState` tracker records each allocation's actual initial albedo and
forgets it on release, preventing a reused GPU slot from inheriting another material's color.
