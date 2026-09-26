# Eternal Flame - Infinite Fuel and Power Hack

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Keeps selected vehicles topped up by periodically refilling fuel tanks and battery charge at a configurable interval. Toggle the mod window with **F11**.

## Features

- **Filterable vehicle selector** — searchable combo box listing all vehicles in the current system
- **Monitored vehicle table** — shows all tracked vehicles with per-vehicle **Fuel** and **Elec** checkboxes and a remove button
- **Fuel refill** — periodically calls `RefillConsumables()` to top up all resource tanks; toggle per vehicle with the **Fuel** checkbox
- **Electricity refill** — periodically sets all `Battery` module charges to `MaximumCapacity`; toggle per vehicle with the **Elec** checkbox
- **Refill interval slider** — drag slider (0–5000ms) controlling how often refills run
- **Solver-timed refill loop** — fuel and battery refills both run from the Harmony prefix on `Universe.ExecuteNextVehicleSolvers` (both hosts), on one wall-clock interval timer; `EternalFlameSubmod.Update` does no refill work

## Refill timing

The vehicle-solver worker snapshots fuel and battery module state when it starts and commits its
results back after the step. Earlier builds refilled fuel from the UI `Update` tick, which runs after
the worker has started. While engines were burning, that commit overwrote the refill, so tanks
drained during burns. Both refills now run in the solver prefix, after the previous results are
applied and before the next snapshot. KSA's own refill command runs at the same point. In KSA 5482,
`RefillConsumables` also flags the refilled tanks, so dry engines read their propellant again.
This root cause was found by source analysis; refilling during a burn still needs an in-game check.

## Files

| File | Purpose |
|------|---------|
| `Mod.cs` | StarMap mod class — UI rendering & game loop hook |
| `Patcher.cs` | Harmony patcher setup/teardown and vehicle solver hook |
| `eternal-flame.csproj` | Main mod project |
| `../eternal-flame.lib/EternalFlameLib.cs` | Core refill logic (`FuelManager`, `MonitoredVehicle`) |

## Usage

1. Press **F11** to open the Eternal Flame window
2. Select a vehicle from the filterable dropdown and click **Add**
3. The vehicle appears in the monitored table with **Fuel** and **Elec** checkboxes enabled
4. Adjust the refill interval slider as desired (lower = more frequent refills)
5. Uncheck **Fuel** or **Elec** to pause that refill type without removing the vehicle
6. Click **X** to remove a vehicle from monitoring entirely

## Harmony Patching Pattern

Basic patch structure:

```csharp
[HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]
public static class TargetMethodPatch
{
    public static bool Prefix(/* method parameters */)
    {
        // Prefix runs before original, return false to skip original
        Console.WriteLine("Before TargetMethod");
        return true;
    }
    
    public static void Postfix(/* method parameters */)
    {
        // Postfix runs after original
        Console.WriteLine("After TargetMethod");
    }
}
```

## Key Files for Reference

When developing from this template, refer to:

1. **[REPOSITORY_INDEX.md](../REPOSITORY_INDEX.md)** - All mods documentation
2. **sibling mod READMEs** - Similar mods for reference implementation
3. **HarmonyLib docs** - Runtime patching patterns
4. **ImGui API docs** - UI widget reference

## Next Steps

1. Copy this entire folder
2. Rename appropriately
3. Implement your feature logic
4. Test with `dotnet build`
5. Update this README with your mod's actual purpose and features

## Testing

Build the solution:
```bash
dotnet build
```

Check for compilation errors before continuing with implementation.

## Common Issues

- **Namespace mismatches**: Update everywhere (csproj, Mod.cs, Patcher.cs)
- **Project references**: Add library project reference to main mod
- **Harmony ID conflicts**: Each Harmony instance needs unique ID string
- **ImGui crashes**: Ensure ImGui calls only happen in OnAfterUi

## Notes for Developers

- Keep UI separate from logic (UI in Mod.cs, logic in Lib project)
- Use Console.WriteLine for debugging
- Test Harmony patches carefully—they affect game runtime
- Document your Harmony patches explaining what they do
- Consider performance impact of per-frame operations

## Related Mods

See similar template mods:
- [unscience](../unscience) - Minimal template without .lib
- Other mods for inspiration on complete implementations

## Scene saves

Saves the monitored vehicle IDs, independent fuel/electricity switches and refill interval. Load resets old monitoring and starts fresh refill timers; native tank/battery quantities remain native save state.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).
