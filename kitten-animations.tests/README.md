# Kitten animation ownership and save checks

Run `dotnet run --project kitten-animations.tests`.

Source-links the production submod, target lifecycle, pose prefix, animation driver, expression
controller, avatar/processor accessors, global tuning and real v1 save adapter/JSON converters.
Native object/rendering seams, catalog discovery and the ImGui panel are managed fixtures;
vehicle identity lookup uses the production `VehicleProvider`.

Checks independent frozen bodies/rates and latched faces across control/target switches, background
pose dispatch, per-kitten release, detached multi-target capture, native-style reconstruction,
pre-first-frame re-save, repeated load, disposed/missing/ambiguous identities, avatar replacement,
old-shaped single-target JSON migration, invalid payloads, vanilla/new-scene cleanup, one-shot
exclusion and unload. No native rendering, animation culling or actual KSA load is exercised.
