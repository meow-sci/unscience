# DOH managed checks

Run `dotnet run --project doh.tests` from the repository root.

Links the production DOH save adapter (`DohSubmod.Persistence.cs`), `SpawnedKittenRegistry`,
`KittenMaterialSet`, the material-set release code (`MaterialFactory.Lifetime.cs`),
`VehicleProvider`, the JSON helpers and the scene coordinator into a native-free fixture. Only the
ImGui half of `DohSubmod`, the Vulkan/reflection material bridge and material cloning are
substituted. The substituted spawner follows the production reuse rule: a released set is never
reused.

Checks:

- capture writes one `MaterialGroup` for a shared batch, a distinct group for a unique kitten and
  none for an untinted kitten;
- reloading the same save rebinds reconstructed kittens onto the detached sets without new
  allocations or releases, keeps the batch shared and restores per-material colors;
- a legacy record without `MaterialGroup` never merges kittens onto one set, even if they shared a
  set before the load;
- a missing kitten is reported, and every detached set nothing rebinds is released (its GPU slots
  are freed);
- a vanilla load keeps detached sets only until the next frame's `Update`, which releases them;
- released sets are never reused;
- releasing a set twice is harmless;
- a blank group is rejected before reset and the record is retained.

Not covered here (in-game only): real material cloning through `GpuMaterialSystem`, GPU slot
accounting against the 512-slot pool, `KittenEva` construction/orphan cleanup, and native load.
