# Pebbles managed checks

## KSA 5949 compatibility

- `SnapToMeshChecks` link `Models/SnapToMeshPolicy.cs` and use the native cell-width arithmetic.
  - **Refused:** a collider-less type on a body without the planet-mesh structure (Luna); one that reaches past
    the stock patch (an Earth tree type); and, as negative controls, a metre past the patch or a wider cell.
  - **Accepted:** disabled types, custom-collider and convex-hull types, and the stock Earth grass reach, even
    after the native patch-minus-snap-step rounding.
- The residency fixture models KSA 5919 per-planet resources: `AllocatePlanetResources`/`DisposePlanetResources`,
  both guarded by `HasPlanetResources`. Detaching frees them once and only after every cell is released;
  re-attached arrays free their new allocation on the next detach, and unallocated arrays free nothing.
  Negative controls: the fixture flags a retained allocation and a free while cells remain.
- `RetirementShapeChecks` now models the 5919 `GroundClutterGpuResources` fields (same six maps) and the
  CPU-only cell grid. The terrain-curvature cleanup in `ClutterRetirement` needs KSA types and is not linked.

## KSA 5901 compatibility

The residency fixture models KSA 5875 clutter slots: only mapped (Main/Secondary) viewports resolve, others throw like `GroundClutterRenderer.ResolveViewportSlot`. Registered thumbnail and portrait viewports must be skipped by the production release.

See [upgrade evidence and native acceptance](../plans/KSA_5901_UPGRADE.md).

## KSA 5801 compatibility

Adds optional curvature and terrain-fade persistence/legacy absence, detached curve copies, domain/spacing and nonfinite-data validation. These are production recipe/sidecar JSON checks; native capture, graph construction and GPU resource retirement remain outside this managed executable.

See [upgrade evidence and native acceptance](../plans/KSA_5801_UPGRADE.md).

Run `dotnet run --project pebbles.tests/pebbles.tests.csproj` from the repository root.
This executable links Pebbles' game-independent models, hull math and Workshop math/state, plus the
shared managed save serializer, directly; it requires neither game assemblies nor the newux
workspace/contracts libraries.

Ported checks cover detached recipe copying and validation, mesh assignment across variants
and LODs, collider scaling, untouched clutter types, camera projection and gizmos, undo/redo,
GLB parsing and scene transforms, material identity, compatibility fallbacks, texture mapping
and pixel conversion. `HullChecks` verify the hull volume/recentring math ported from KSA 5447 on
known polyhedra; `ClutterStateChecks` verify grid-memory carry-over (AND masks, authoritative vs
merged displaced records, per-spacing isolation, seed/export) and the `pebbles.clutter-state`
record's validation and round trip through the linked production `SaveParticipant`/`SaveJson`;
`PebblesChecks` covers the legacy-recipe `KeepBackfaceNormals` and (KSA 5707) `UsesCustomNormals`
defaults: stock inheritance, explicit override, clone and sidecar round trip of an unset flag. `RetirementShapeChecks` link
`Runtime/FieldShapes.cs` and verify that partial retirement selects every mapped-memory field before buffers,
including the jagged `MappedMemory[][]` shapes (KSA 5638 cell grid, KSA 5919 `GroundClutterGpuResources`). `ClutterResidencyChecks` link
`Runtime/ClutterResidency.cs` against `ClutterResidencyFixture.cs`, managed stand-ins of the 5638
`IViewport`/`ViewportRegistry.Views`/`ClutterEcotypeRenderData.ReleaseViewport`/`ResolvePendingReleases`
surface, and verify that detached arrays release every registered (including hidden) viewport, return all
pooled cell slots and drop their generation viewport. Failure throws and returns a nonzero exit code.

These checks cannot exercise Harmony, native image decoding, Vulkan rendering/resource
retirement or game physics. In-game smoke checks are listed in `../pebbles.lib/README.md`.

Shared catalog checks additionally link `SharedFileLibrary`/`GlbLibrary` against an isolated temporary
filesystem: actual GLB fixture bytes survive source deletion, recipe identities use the managed copy,
content changes alter the hash, lazy picker ids differ from frozen ids, and oversized copies fail early.
