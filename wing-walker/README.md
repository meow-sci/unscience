# Wing Walker

Wing Walker is bundled in **Unscience**, the only distributed release. This thin standalone
project is a development host; do not load it alongside Unscience. Both hosts load the same
`WingWalker.xml` assets and shared library patches. The standalone information panel opens with F11.

Both mounts are intended to show a helmeted occupant, with the Standing Mount standing upright.
The appearance fix awaits a native visual retest.
Standing Mount uses two simple brown leather straps over the boots, with subtle stitching;
External Seat is a dingy, heavily weathered rainbow webbed beach chair with a square gold-foil frame and textured white
plastic armrests. Editable Blender art, previews and the rebuild scripts are in
[`Art/`](Art/README.md). Development and release builds include both GLBs and the custom texture
sets; gold foil is referenced from Core.

The chair has **1,028 visible triangles** (872 webbing/plastic +156 frame) and **96 picking triangles**,
down from 12,100 each. It uses simplified woven strips and four-sided frame beams with flat faces. Three external
256px maps add stronger grey/brown grime, faded color, dirty edges and pale fiber wear; the frame still samples the native wrinkled
foil atlas. Picking uses coarse seat/back/arm panels and frame envelopes rather than duplicating
every visible strand. Physical colliders, crew fit points, life support and saved identities are
unchanged. Visual and frame-time effects still need native acceptance.
The geometry-only GLB is 84,160 bytes, down from 2,063,596; its three mesh IDs and native XML bindings
are preserved, with no embedded images/materials duplicating the external maps.

From the repository root, run `python3 scripts/check-chair-assets.py` after rebuilding the art;
`--json` reports actual mesh/texture measurements. Release-package validation runs the same check.

In the editor, find **External Seat** or **Standing Mount** under **Unscience External Crew** or
**Capsules**, then assign a kitten with the native Crew controls. In flight, a kitten can board near the
entry point and leave using the crew portrait's EVA action. Supplies are finite, the mounts have no
radiation shielding or Control module, and their surface connections are not habitat passages. Food
and scrubber supplies are local and isolated by default. There are no feature
toggles. Native crew, part and life-support state are saved by KSA without a new sidecar.

Place mounts and set symmetry before assigning crew. Selecting/picking up a mount or starting a
gizmo move unassigns its kitten; selecting a parent includes external mounts below it. Symmetry
and copying clear external-seat crew before cloning, so kitten names are never duplicated by
those operations. Reassign each kitten after placement. Stock seats and unrelated mounts are
unchanged; the native editor records the crew change for history and saves.

See the [library README](../wing-walker.lib/README.md) for details and the
[validation plan](../plans/WING_WALKER.md) for pending checks.
