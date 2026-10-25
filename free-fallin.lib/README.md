# free-fallin.lib

Reusable core for [`../free-fallin`](../free-fallin) and the unscience umbrella mod.

| File | Responsibility |
|---|---|
| `FreeFallinSubmod.cs` | `ISubmod` lifecycle and ImGui appearance/PBR editor |
| `FreeFallinPatches.cs` | Prefixes `ChuteRenderable.Draw`, substitutes material handle 0, and restores observed canopies |
| `CanopyProjectionShaders.cs` | Game seams for Full Canopy: resolves the stock shader files, forces their compile through `FromFile`, and installs the in-memory patch only after every check passes |
| `CanopyShaderSource.cs` | Game-independent shader text transforms: derives `SET_TEXTURE`/`SET_MATERIAL` from the live `ModelPbr.frag`, checks anchors and varying location 3, and builds the patched sources |
| `CanopyMaterialController.cs` | Transcodes the stock BC7 KTX2 to RGBA8 when compositing a decal, then builds GPU albedo/PBR textures and `MaterialData` objects |
| `CanopyMaterialSettings.cs` / `CanopyTextureMode.cs` | Public settings model and Stock/Replace/FullCanopy/CenterDecal modes |
| Shared PNG dependency | Uses `ksa-abstractions.lib`'s `.unscience/pngs` catalog and `PngFileBrowser` |

The lib owns generated KSA texture/material registrations and retires the previous allocation
after switching observed canopies and waiting for the GPU. Allocations happen on Apply, not
per UI change or frame. KSA 5438 supplies a native material id to each `ChuteRenderable`; the
controller uses `ParachuteCanopy_Material_CheckerLongOrange` as its stable authored source for
normal/PBR data while preserving each canopy's own original slot for restoration.

Full Canopy stores projection scale, rotation, and a mode marker in `MaterialData.ExtraData`. The
patched skinned vertex shader derives a second UV from the canopy's bind-pose X/Z coordinates. The
patched PBR fragment shader uses that UV only for marked materials' albedo; the authored UV remains
in use for the stock normal and AO/roughness/metallic maps. Shader files on disk are never modified.

The skinned vertex shader reads `MaterialBuffer`, so it must declare the same descriptor sets as the
pipelines it joins. KSA 5530 renumbered the mesh-technique sets (texture library 1 → 5, materials
2 → 1), so the set indices are never hard-coded: `CanopyShaderSource.ParseDescriptorSets` reads them
from the game's `ModelPbr.frag` at activation and requires `ModelNormal.frag` and
`ModelTranslucent.frag` (the other fragment shaders paired with `Model_Skinned.vert`) to agree. A
shader can use LF, Windows CRLF or mixed line endings, with or without a final line terminator. A
missing, conflicting or non-numeric define, a changed anchor, or an occupied varying location 3
disables Full Canopy with a console line before any Harmony patch is installed, leaving every stock
shader untouched; the other texture modes keep working. `ksa-upgrade.tests` covers the parser on the
real 5638 header and the 5482 layout across these line endings, including malformed CRLF rejection.
When `KSAFolder` points at a game tree, it also patches the live shader files as read and with CRLF
and mixed endings. These are managed text checks; native shader compilation/rendering remains an
in-game acceptance check.

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
Each observed canopy records its native slot-zero handle before the first replacement; Restore
Stock, save/load reset, and unload restore that exact handle and clear tracking so a later apply
captures fresh native choices.

Shader parsing and generated shader text are transient renderer setup, recreated from the installed
game sources. The Windows line-ending fix changes no saved settings, feature ID, payload or version.
