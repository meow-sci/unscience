# Godzilla managed regression checks

Run `dotnet run --project godzilla.tests`. This executable links the production snapshot and scale
ownership implementations to small managed KSA fixtures with real Brutal numerics and Harmony.
It checks Smart layout and authored scales, repeated edits, preservation of animated subpart state,
Basic/Smart transitions, exact restore,
invalid input, staging, non-default kitten scale and competing owners. It cannot validate native
physics, rendering or actual game animation; build the full solution against the current KSA DLLs too.

The production `WeldScale` validator is linked too. Checks exercise Smart and Basic sizes below
0.05 and above 20, preserving exact requested values and restoring originals afterward.

`VisualScaleChecks` applies production Harmony patches to representative draw methods. It checks
10,000,000× rendering without physical refreshes, render-only pixel culling, COM and XYZ transforms,
caller matrix restoration on success/exception, physical↔visual transitions, avatar scale isolation,
coexistence with an external GetWorldMatrix prefix, and unload/reapply. Physical bubble execution
and actual KSA render submissions still need in-game testing.

`ColliderScaleChecks` applies the production collider patches to native-shaped managed fixtures
with real Bepu sphere/box structs. Covers all four physics/collider combinations, independent XYZ
centers/max-axis dimensions, animated children, preserving nominal bounds and other physics fields,
restoring the caller's shared readonly scale (also on exceptions), native refresh interception,
worker broad-phase dirty flags, Basic/Smart switches, failure/restoration retry, collider membership
changes, unsupported fallback rejection and unload/reload. It does not execute KSA or Bepu contacts.
