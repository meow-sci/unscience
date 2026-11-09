# Kitten Animations library

`KittenAnimationsSubmod` implements the Unscience panel for selecting a live EVA kitten,
playing discovered body/MMU clips, triggering facial expressions, overriding processor strength and
editing global animation-facing locomotion tuning. Each visited kitten has independent settings,
`KittenAnimationDriver` and `KittenExpressionController`, keyed by exact `Vehicle.Id`.
The target selector chooses which kitten to edit; changing control, camera or selection never clears
another kitten's holds. Select that kitten again to disable body control, clear its expression or
change its strength overrides. Unlatched expressions continue their normal timed envelope offscreen.

`KittenAnimationsSubmod.Targets.cs` owns target lifetimes. The pose prefix uses a model-reference
lookup for every registered kitten, including uncontrolled kittens. Each expression controller owns
one appended `CatExpressionAnim`. Missing, disposed or ambiguous targets detach processors and release
model references, retaining detached settings until the exact ID returns. Avatar replacement rebinds
through the same catalog and ownership APIs. The catalog reads the existing documented private fields.
Native rendering/culling still controls when an offscreen body animation is sampled; no extra render
or physics work is forced. Seated IVA crew remain outside the EVA-vessel registry.

The existing `kitten-animations` v1 participant saves a `Kittens` array with exact vehicle IDs,
clip source/label, active/paused state and phase, reusable playback/strength settings, expression
settings/variant and latched clip identity. Panel selection and the 20 exposed global animation-tuning
values remain scene-level fields. An absent/null `Kittens` array explicitly migrates the original
single-target v1 fields; legacy follow-controlled mode resolves the native controlled kitten once,
because those old records contain no actual target ID. Empty arrays mean no saved targets.
Duplicate/blank IDs and malformed settings fail validation before replay. Missing targets and clips
report diagnostics; targets never fall back to another active or similarly named kitten.

Capture is detached. Reset releases every old avatar/processor and restores the initialization tuning
baseline before native reconstruction, including vanilla/missing-record loads, new scenes and unload.
Forced clips and frozen poses resume their saved phase, even if re-saved before the first visible
pose pass. Latched expressions resume their saved clip at held strength. Unlatched one-shots and
intermediate expression easing are intentionally transient. Cross-fades restore the selected pose;
native bone buffers, reflection caches and GPU handles are not serialized. KSA owns the actual
kitten vessels, camera and simulation; animation tuning remains global rather than per kitten.

Run `dotnet run --project kitten-animations.tests` for production ownership/prefix/save-adapter checks
against managed native seams, and `dotnet build` for typed game API checks. Native pose rendering,
culling and actual KSA save/load acceptance still require the game.
