# Godzilla library

Godzilla lives in the single Unscience package. Open **F11 → Godzilla**, choose a vessel
(or use the controlled vessel), set its size, and press **Apply**. The filter also finds EVA kittens.

- **Smart** uses a uniform multiplier (0.05–20), scales full-part positions about the original
  center of mass, and multiplies their authored scales. Subparts inherit the result; their animated
  local positions, rotations and scales remain owned by the game.
- **Basic** sets raw XYZ scales on all parts/subparts, like Garry's Torch, without changing full-part
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
vehicle mass/collision/aero/attachment data are refreshed. Kittens preserve their original avatar
scalar and share Garry's Torch's axis correction. Large size changes can intersect nearby terrain;
position the vessel with room to grow.

`godzilla.lib` implements `ISubmod` and exposes `RequestApply(Vehicle,bool,float3)` and
`RequestRestore(Vehicle)`. The host installs the shared handoff and `KittenScalePatches`; Unscience
already does both. The `godzilla` host remains a compile-checked development reference and is not
published separately.

Validation: full solution build plus `dotnet run --project godzilla.tests` and
`dotnet run --project garrys-torch.tests`. Managed fixtures cover transformations, restoration,
animation ownership, scale ownership and scheduling; native in-game collision/actuation/rendering
still require a live smoke test. See [integration scope](../scope/vehicle-physics.md).
