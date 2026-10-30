# DOH managed checks

Run `dotnet run --project doh.tests` from the repository root.

Current suite: 58 checks.

Links the production DOH save adapter (`DohSubmod.Persistence.cs`), `SpawnedKittenRegistry`,
`KittenMaterialSet`, the material-set release code (`MaterialFactory.Lifetime.cs`), the spawner
teardown (`KittenSpawner.Teardown.cs`), `VehicleProvider`, the JSON helpers and the scene
coordinator into a native-free fixture. Also links the production naming partial and `SpawnRequest`.
Only the ImGui half of `DohSubmod`, its text buffer, the Vulkan/reflection
material bridge, material cloning and spawning are substituted. The substituted spawner follows the
production reuse rule: a released set is never reused. The fixture `Universe.DestroyVehicle` records
its disposition, clears control and deregisters, as KSA 5638 does.

Checks:

- exact single names (including the prefilled value and significant whitespace), case-insensitive
  occupied-name refusal, numbered batches skipping occupied names, blank/default fallback and
  a fresh spawner avoiding names restored by KSA;
- KSA 5707 roster names: a single name held by a rostered kitten is refused case-insensitively, a
  batch numbering past a rostered name skips it, and a merely similar name stays available;
- real-adapter JSON round-trips of a Unicode name/prefix and blank input, detached capture,
  repeated/cross-scene loads, legacy records without `SpawnName`, vanilla reset, and diagnosed,
  retained null/NUL/over-capacity (UTF-8) names;
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
- a blank group is rejected before reset and the record is retained;
- despawn joins the vehicle solver and the nearest-orbit worker, then calls
  `Universe.DestroyVehicle(kitten, CrewDisposition.Boarded)`, so crew records are untouched and
  control is released;
- a shared batch set is kept until its last kitten is despawned, then released;
- a kitten the game already destroyed is not torn down twice, but its entry and slots are freed;
- a non-kitten vehicle with a registered name is never destroyed;
- a half-built kitten is removed through the same teardown, and is still deregistered when the
  teardown throws.

Not covered here (in-game only): real material cloning through `GpuMaterialSystem`, GPU slot
accounting against the 512-slot pool, real `KittenEva` construction, and the native
`DestroyVehicle` hand-offs (camera, portrait camera, targets, emitters) and native load.
