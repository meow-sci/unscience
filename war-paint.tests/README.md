# War Paint managed checks

Run `dotnet run --project war-paint.tests/war-paint.tests.csproj` from the repository root.
The executable links production paint ownership, presets, scene adapter, material release/pool and
fur-reuse logic against a native-independent backend and asset-map fixtures.

61 checks cover independent kitten tint/material edits; detached snapshots; Unset restoring only
owned bindings before freeing slots; idempotent cleanup; disposed/stale/ambiguous targets; allocation
failure; global preset restart/replacement/deletion and corrupt/future file protection; compatible
character material-slot portability; legacy DOH/Humble imports; repeated scene replay and slot reuse;
vanilla cleanup; invalid and missing-target retention; exact native asset identity and recycled slot
protection; fixed-pool reserve accounting; and new native fur-source handle remapping.

The real shared `VehicleProvider`, serializer and save coordinator participate in the checks. The
backend records binding changes and GPU writes without loading KSA or Vulkan. Native source-key
release and fur remapping helpers are production code; native asset disposal is a managed fixture.
These tests do not prove actual shader output, GPU synchronization, portrait rendering or native
save reconstruction. See [implementation and native acceptance](../plans/WAR_PAINT_CONSOLIDATION.md).
