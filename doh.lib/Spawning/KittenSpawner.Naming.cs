using KSA;

namespace MeowSci.DohLib.Spawning;

public sealed partial class KittenSpawner
{
    // A runtime hint only: the native lookup is authoritative after reloads and renames.
    private int _nextKittenIndex;

    /// <summary>Returns null when an exact single-spawn name is already occupied.</summary>
    internal string? GenerateKittenName(CelestialSystem system, SpawnRequest request)
    {
        bool automatic = string.IsNullOrWhiteSpace(request.VehicleName);
        if (request.Count == 1 && !automatic)
            return system.All.TryGet(request.VehicleName!, out _) ? null : request.VehicleName;

        string prefix = automatic ? SpawnRequest.DefaultNamePrefix : request.VehicleName!;
        string name;
        do
        {
            name = $"{prefix}{_nextKittenIndex++}";
        } while (system.All.TryGet(name, out _));
        return name;
    }
}
