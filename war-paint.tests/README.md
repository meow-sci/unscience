# War Paint managed checks

Run `dotnet run --project war-paint.tests/war-paint.tests.csproj` from the repository root.
The executable links production paint ownership, preset editor/library, scene adapter, material
release/pool and fur reuse against a native-independent backend and asset-map fixtures.

93 checks cover isolated material edits; detached snapshots; restoration before slot release;
stale/disposed/ambiguous targets; allocation failure; global preset IO/corruption protection;
read-only material discovery; required selection and unique preset creation; dirty state;
live preview across assignments; save and discard; confirmed deletion and unaffected presets;
write failures/retry; compatible material roles; DOH/Humble migration; named and older nameless
v1 scene replay; effective unsaved scene colors; recovery of missing global presets; repeated
loads/reuse; vanilla reset; malformed/missing record retention; asset identity/recycled slots;
fixed-pool reserve accounting; and fur-source remapping.

The real `VehicleProvider`, serializer and save coordinator participate. Backend fixtures record
binding changes and GPU writes without loading KSA/Vulkan. These checks do not validate the native
ImGui layout, shader output, GPU synchronization or actual save reconstruction. See the
[editor acceptance checklist](../plans/WAR_PAINT_PRESET_EDITOR.md).
