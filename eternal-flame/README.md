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
- **Solver-timed electric refill loop** — runs from a Harmony prefix before vehicle solver preparation so battery changes are copied into the next electrical simulation step
- **Throttled electric diagnostics** — logs solver-loop vehicle, monitored, matched, and battery counts every few seconds while electric refill is enabled

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
