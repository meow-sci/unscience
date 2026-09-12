# Kiwi's Marbles library

Reusable celestial-weld engine and Unscience submod. See [usage](../kiwis-marbles/README.md)
for target frames, offsets and per-step orbit control.

## Scene saves

Unscience persists source/target identities, celestial-versus-vehicle target type and offsets,
plus the original parent, orbital state vectors and epoch needed by Unweld. Native KSA saves
vehicles but does not capture celestial orbit edits. Scene reset restores original orbits before
native load reuses bodies; replay rebinds objects, validates cycles and parent identities, sorts
welds and applies them while the native solvers are quiescent. Missing targets remain reported
failures rather than being substituted. Existing solver patches continue the restored welds.
Native cross-parent/body-subtree and load/unload acceptance remains required.
