# Godzilla library

Godzilla lives in the single Unscience package. Open **F11 → Godzilla**, choose a vessel
(or use the controlled vessel), set its size, and press **Apply**. The filter also finds EVA kittens.

- **Visual only (keep original physics)** is a session-only boolean, default **off**. Toggle it
  at runtime to convert all current Godzilla sessions at the next physics handoff; subsequent Apply
  actions use the selected setting. Turning it on restores the captured physical sizes first, then
  scales only render transforms. Mass, colliders, physics-bubble bounds, locomotion, camera targeting
  and picking stay at the original size. The giant visible surface can pass through terrain.
  Smart uses a uniform whole-craft multiplier; with Smart off, XYZ multiplies the entire craft in
  assembly/body axes, including spacing (different from physical Basic's raw per-part scales).
  Render pixel culling uses the visual size without changing the game's physical radius.
- **Smart** uses a uniform multiplier (drag range 0.05–20; typed values may exceed it), scales full-part positions about the original
  center of mass, and multiplies their authored scales. Subparts inherit the result; their animated
  local positions, rotations and scales remain owned by the game.
  Typed scales may be any positive, finite value; drag bounds do not constrain Apply.
- **Basic** sets raw XYZ scales on all parts/subparts without changing full-part
  spacing. Child inheritance can exaggerate dimensions. KSA's `ScaleFactors` uses the largest axis
  for modules/colliders; anisotropic visuals do not imply anisotropic collision shapes.
- **Restore original**, each entry's **Restore**, and **Restore all** recover captured part scales
  and full-part positions. Edits are relative to the first capture, never cumulative. Changing modes
  removes Basic child overrides before applying Smart. Smart restore does not rewind child animation.
- Settings are runtime only; unload restores surviving vessels. Staging/docking/part loss restores
  the remaining original parts and releases the session. Detached pieces keep their current size.
- Garry's Torch and Godzilla cannot own the same source's scale concurrently. Unweld before scaling,
  or restore Godzilla before welding. A scaled vessel can still be a weld target.

Apply/Restore run through `PhysicsFrameHook.Enqueue` after worker results and before welding/new
physics snapshots. Descendant transform caches, scale-aware modules, bounds, derived part data and
vehicle mass/collision/aero/attachment data are refreshed for physical edits. Visual-only edits
leave those fields alone; changing from physical to visual restores them once. Kittens preserve their original avatar
scalar and share Garry's Torch's axis correction. Large size changes can intersect nearby terrain;
position the vessel with room to grow.

`godzilla.lib` implements `ISubmod` and exposes `RequestApply(Vehicle,bool,float3)` and
`RequestRestore(Vehicle)`, plus `VisualOnly` / `SetVisualOnly(bool)`. The host installs the
shared handoff, `KittenScalePatches` and `VisualScalePatches`; Unscience already does all three. The `godzilla` host remains a compile-checked development reference and is not
published separately.

Validation: full solution build plus `dotnet run --project godzilla.tests` and
`dotnet run --project garrys-torch.tests`. Managed fixtures cover transformations, restoration,
animation ownership, scale ownership and scheduling, plus managed Harmony render hooks, visual
culling, COM/XYZ transforms,
physical/visual transitions and reload; native in-game collision/actuation/rendering
still require a live smoke test. See [integration scope](../scope/vehicle-physics.md).

Visual-only scaling covers the part-tree draw transform and the EVA avatar/attachments. Separate
world-space effects such as exhaust and simulated parachute cloth retain their physical placement.
Planet-scale clipping, shadows, LOD, native bubble behavior and interaction with Iron Man equipment
still need in-game acceptance; this toggle does not remove renderer precision limits.
