# eternal-flame.lib

Feature implementation used by Unscience and the development host. See [feature documentation](../eternal-flame/README.md) for controls and architecture.

`FuelManager.RefillBeforeVehicleSolvers()` refills fuel (`Vehicle.RefillConsumables`) and battery charge
for monitored vehicles from the `Universe.ExecuteNextVehicleSolvers` prefix installed by each host. Refills run
before the worker snapshots module state, so a burn no longer overwrites them.
`EternalFlameSubmod.UpdateBeforeVehicleSolvers` forwards to it; `Update(dt)` is intentionally empty.
Debris is not monitored, because `GetAllVehicles()` excludes it by default.

## Scene saves

Saves the monitored vehicle IDs, independent fuel/electricity switches and refill interval. Load resets old monitoring and starts fresh refill timers; native tank/battery quantities remain native save state.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).
