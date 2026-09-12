# Kitten Animations library

`KittenAnimationsSubmod` implements the existing Unscience panel for selecting a live EVA kitten,
playing discovered body/MMU clips, triggering facial expressions, overriding processor strength and
editing global animation-facing locomotion tuning. `KittenAnimationPatches` routes the target pose
through `KittenAnimationDriver`; `KittenExpressionController` owns and releases a separate expression
processor. The catalog reads the existing documented native private animation fields.

The `kitten-animations` save participant retains exact pinned target (or follow-controlled mode),
selected clip by source/label, reusable playback/strength settings, expression envelope/variant/latch
configuration and the 20 exposed animation-tuning values. Reset unbinds old avatars/processors and
restores the initialization tuning baseline before native reconstruction. It does not overwrite
unrelated physics tuning. Forced looping clips resume, including the saved phase of frozen poses; inactive selected clips
remain ready for the one-click Playback checkbox. Latched expressions resume their saved clip
variant at held strength; transient unlatched expressions stay stopped. An in-progress body
cross-fade resumes the selected clip pose rather than serializing its previous blended bone buffers. Missing pinned
targets/clips are reported without silently redirecting to a different kitten.

Run the full solution build for typed API checks. Native pose/render and repeated-save-load
acceptance requires KSA; managed lifecycle tests cannot initialize its graphics pipeline.
