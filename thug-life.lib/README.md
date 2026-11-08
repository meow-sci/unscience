# Thug Life library

## KSA 5801 compatibility

Temporary ShaderStages and VertexInput allocations are now explicitly disposed. Stages borrow stock shader modules, which remain owned by KSA. The pipeline already derives R11G11B10/sample compatibility from OffscreenTarget; UnlitMesh shaders and saved quad recipes are unchanged. Native quad/HDR rendering remains pending. See [upgrade evidence](../plans/KSA_5801_UPGRADE.md).

## Scene saves

Unscience native-save integration remembers all anchored sunglasses quads, exact part/subpart
addresses, local offsets/rotation, dimensions and visibility. Load removes old anchors, reuses
shared rendering resources, and reconstructs quads on the native-restored parts. A running entrance
slide is saved at its current position and remains stopped after load; it is not triggered again.
Missing anchors or renderer allocation failures appear in Saves status.

## KSA 5438 compatibility

The quad index binding uses KSA 5438’s renamed UInt16 enum member. UnlitMesh shaders and the RenderMainPass hook are unchanged; native quad rendering remains an in-game acceptance check.
