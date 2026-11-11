# Wing Walker

Wing Walker is bundled in **Unscience**, the only distributed release. This thin standalone
project is a development host; do not load it alongside Unscience. Both hosts load the same
`WingWalker.xml` assets and shared library patches. The standalone information panel opens with F11.

Both mounts are intended to show a helmeted occupant, with the Standing Mount standing upright.
The appearance fix awaits a native visual retest.
Standing Mount uses two simple brown leather straps over the boots, with subtle stitching;
External Seat uses the Core chair. Editable Blender art, a preview and the rebuild script are in
[`Art/`](Art/README.md). Both development and release builds include the GLB and leather textures.

In the editor, place **External Seat** or **Standing Mount** from **Unscience External
Crew**, then assign a kitten with the native Crew controls. In flight, a kitten can board near the
entry point and leave using the crew portrait's EVA action. Supplies are finite, the mounts have no
radiation shielding or Control module, and their surface connections are not habitat passages. Food
and scrubber supplies are local and isolated by default. There are no feature
toggles. Native crew, part and life-support state are saved by KSA without a new sidecar.

See the [library README](../wing-walker.lib/README.md) for details and the
[validation plan](../plans/WING_WALKER.md) for pending checks.
