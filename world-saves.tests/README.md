# World save checks

Managed executable exercising production save DTOs and recipes for canopy appearance,
ring definitions/overlays, Sphinx statics, decals and mounted cameras. It verifies detached
collections, constructor-backed fields, Brutal/System.Numerics fields, durable target data,
explicit viewport/visibility state, nonfinite rejection and portable GLB library identity.
Canopy checks preserve effective Humble recolors independently of authored tint settings,
accept older records without that optional color, and verify released material handles
cannot retain color baselines when their GPU slots are reused.

Run `dotnet run --project world-saves.tests -p:UNSCIENCE_DIST_DIR=/private/tmp/unscience-saves-dist`.
The game assemblies are needed as type references; checks do not initialize KSA, ImGui,
Vulkan or physics. Native restoration, render timing/resource retirement and target
resolution against reconstructed vehicles still require in-game acceptance.
Nonzero Brutal float/double vectors and System.Numerics UV vectors are compared after
round-trip; floating-point exponent overflow is rejected on deserialization as well as
nonfinite values on capture. The native KSA executable cannot run in this macOS ARM test
environment; executable native-camera operations belong to separate fixture tests.
