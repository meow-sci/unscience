
## Scene saves

Unscience native-save integration remembers all anchored sunglasses quads, exact part/subpart
addresses, local offsets/rotation, dimensions and visibility. Load removes old anchors, reuses
shared rendering resources, and reconstructs quads on the native-restored parts. A running entrance
slide is saved at its current position and remains stopped after load; it is not triggered again.
Missing anchors or renderer allocation failures appear in Saves status.
