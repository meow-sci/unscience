# Wing Walker validation

Implementation adds native External Seat and Standing Mount parts to Unscience. The
standalone host is for development; only the combined package is released. [Surface inventory](../scope/wing-walker.md).

## Standing Mount leather art

Standing Mount replaces the chair visuals with two simple brown leather straps over the boots,
with small flat tabs and subtle edge stitching. The Blender source, rebuild script and preview are
in [`wing-walker/Art`](../wing-walker/Art/README.md). One GLB holds a visible and `_VM` picking mesh;
three 256px PNGs supply diffuse, normal and packed AO/roughness/metalness. The model was checked
around the installed 5707.1 kitten boots in Blender; no Core art is included in the source/export.

The existing standing eye point is retained. Tabs/soles and the surface connector share
Z=0.2901232; the chair collider is replaced with the strap envelope. Both part IDs and all five
subpart keys/order remain stable, with Chair holding the straps, Headrest/Runners/Base using an
empty template, and EvaDoor retained. Native life support and simulation mass are inherited.
The 5801 native parser check passes with 15 rows and zero content errors, including the original
subpart order, strap references, crew eye point, connector math and crew-save serialization.
The production Harmony fixture passes. Native editor/flight visuals, standing-idle fit and
repeated save/load still need in-game acceptance.

The full solution and final Unscience package builds pass with zero warnings/errors. The fresh
`/private/tmp/unscience-wing-walker-straps-dist` passes `scripts/check-dist.py`, including exact
source-byte comparisons of the GLB and all three textures. Offline GLB checks verify the mesh
names/picking twin, identity node transforms, normal/UV/tangent streams, finite bounds, the sole
plane and three embedded textures. Blender source textures are packed and the source contains
only the straps and preview camera/lights.

Chair placement and native schema come from CURRENT 5801 XML/source. Mesh names and bounds were
also checked against the installed macOS 5707.1 Core chair atlas; the reference snapshot contains
no mesh payload. Live 5801 visual acceptance therefore remains necessary.

## Appearance follow-up — checks in progress

The user tested the earlier parts in game: they were present, but Standing Mount occupants still
sat and both occupants lacked helmets. The previous fixture checked clip identity without checking
the sampled pose. A timed blend can retain the seated pose when `dt` is zero. The new scoped public
render patch enables helmet and helmet-compatible ears for both parts, and starts standing ground
idle immediately with zero blend on the first renderable/model/clip sample. It holds the seated clip
null throughout the render body, temporarily unfreezes the model, and restores temporary state in a
finalizer. Native head masking and War Paint visor gating remain in place; own IVA `HideHead` hides
helmet and visor meshes without changing the cached ear pose. The native seat still owns the MMU.
IDs, XML, save format and settings do not change. Six Harmony targets remain; the public render
target replaces the private selector, and five private fields are now checked.

The focused `wing-walker.lib` build passed with zero warnings and errors. The 5801 native assembly
run verified the public render signature, five injected fields and helmet surfaces, and passed the
XML and save checks. Its CPU `BoneAnimRuntime` regression sampled the actual pose, reproducing the
stuck timed blend at `dt=0` and confirming that zero blend produces the target pose. The full solution also built with zero warnings and errors, and `scripts/check-dist.py` passed
for `/private/tmp/unscience-wing-walker-fix-dist` (one mod, 34 feature dependencies); the packaged
Wing Walker library SHA-256 matches the latest focused build. The updated production Harmony fixture
passed: it exercises lazy avatar creation/reassignment, sampled standing at zero delta, both external
helmets, own-IVA and same-frame camera behavior, visibility preservation, unscoped avatars, nested
scopes, exceptions and freeze restoration, unload/reload markers, boarding and render filtering.
These checks do not establish in-game visual correctness; the native visual retest remains pending.

## Earlier verification status

Before the appearance follow-up, the full solution built against the 5801 references with zero warnings and errors. The production
Harmony fixture in `wing-walker.tests` passed, including repeated-seat boarding and failure paths.
The shared `saves.tests`, `world-saves.tests`, `ksa-upgrade.tests`, and eight release-policy cases
also passed. The fresh `/private/tmp/unscience-wing-walker-dist` passed `scripts/check-dist.py`:
one Unscience mod with 34 feature dependency assemblies.

`wing-walker.native-tests` exercised the 5801 KSA managed parser: eight flattened rows, two Part
rows and zero content errors. It verified the previous six Harmony targets and two fields, inherited part data,
the native surface connector normal, four meals and one scrubber via `Habitat.ComputeFullLoad`, and
rejection of an unknown attribute. Native `IVASeatData` / `PartInstance` XML round-tripped distinct
crew names and both part IDs. The parser does not resolve asset references, initialize a live Core
library, call `IVASeat.GetSaveData`, or initialize the renderer.

For the ARM64 local run, the test explicitly retargeted only the PE machine field in a temporary
copy of the original 5801 IL-only, non-ReadyToRun assembly, leaving the source DLL unchanged and
using cached matching third-party dependencies. See [native test instructions](../wing-walker.native-tests/README.md).
Graphics, physics, boarding and scene save/load still require the in-game acceptance below.

## Native acceptance (pending)

- Place both parts, assign crew in the editor, and check seated/standing alignment, IVA camera eye,
  portraits and animation, with helmets and ears in external views, including a paused frame (dt=0).
  Check own IVA head/helmet/visor hiding, external/portrait views in the same frame, and War Paint
  visor behavior. Test absent standing idle if a suitable character exists.
- Check brown leather straps over both boots, grain/stitching, editor picking/thumbnail, shadows,
  surface attachment and the small collider. Confirm no chair visuals remain on Standing Mount
  and load an earlier standing craft/scene with its original five subparts.
- Verify controlled, noncontrolled and no-controlled-vehicle views, multiple vehicles and secondary
  viewports; check for missing or duplicate crew and consistent ear pose. Test scaled parts,
  transitions between seated and standing occupants, and unload/reload.
- Board near the entry point and use portrait EVA on the ground and in space; check occupancy,
  roster and reboarding. Put two identical part IDs on the same craft, board each exact door, and
  check duplicate, occupied and stale targets as well as native suit return and control handoff.
- Save scene A, load B, return to A and repeat: posture, seat assignment, crew and consumables must
  restore through native data without duplication or a Wing Walker sidecar.
- Exercise editor undo, copy, symmetry, focused and unattached part trees and seat reassignment.
- Check finite oxygen, water, food and LiOH, refill/available fluid connectivity and habitat passage
  behavior, isolated local food/scrubber supplies, passenger-only behavior without a Control module,
  plus radiation dose without shielding. Verify cleanup and patch removal on unload.
