
## Scene saves

Unscience native-save integration remembers all anchored sunglasses quads, exact part/subpart
addresses, local offsets/rotation, dimensions and visibility. Load removes old anchors, reuses
shared rendering resources, and reconstructs quads on the native-restored parts. A running entrance
slide is saved at its current position and remains stopped after load; it is not triggered again.
Missing anchors or renderer allocation failures appear in Saves status.

## KSA 5438 compatibility

The quad index binding uses KSA 5438’s renamed UInt16 enum member. UnlitMesh shaders and the RenderMainPass hook are unchanged; native quad rendering remains an in-game acceptance check.
