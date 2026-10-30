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
            return IsOccupied(system, request.VehicleName!) ? null : request.VehicleName;

        string prefix = automatic ? SpawnRequest.DefaultNamePrefix : request.VehicleName!;
        string name;
        do
        {
            name = $"{prefix}{_nextKittenIndex++}";
        } while (IsOccupied(system, name));
        return name;
    }

    /// <summary>
    /// KSA 5669+ binds an EVA kitten to the roster entry with the same (case-insensitive) name for
    /// radiation dose, sickness and death, so a name held by a rostered kitten at home is taken too.
    /// </summary>
    private static bool IsOccupied(CelestialSystem system, string name)
        => system.All.TryGet(name, out _) || Universe.KittenRoster.Find(name) != null;
}
