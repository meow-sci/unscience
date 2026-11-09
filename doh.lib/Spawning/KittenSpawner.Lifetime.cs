using System;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.WarPaintLib;

namespace MeowSci.DohLib.Spawning;

public sealed partial class KittenSpawner
{
    /// <summary>Requests the game's normal kill action, before it rebuilds the next frame's vehicle list.</summary>
    public bool RequestSmite(string kittenId)
    {
        if (_registry.Get(kittenId)?.Vehicle is not KittenEva kitten || kitten.IsDisposed
            || !ReferenceEquals(VehicleProvider.FindVehicle(kitten.Id), kitten)) return false;

        // Keep the exact object, including after a rename. Native input owns dispatch and teardown;
        // never release paint or dispose an avatar while it is still in the current render list.
        foreach (var pending in InputEvents.VehicleDestroyBuffer.Get())
            if (ReferenceEquals(pending.Vehicle, kitten)) return true;
        InputEvents.VehicleDestroyBuffer.Add(new InputEvents.VehicleDestroyData
        {
            Vehicle = kitten,
            Recovered = false
        });
        Console.WriteLine($"doh: Smite requested for '{kitten.Id}'");
        return true;
    }

    public int RequestSmiteAll()
    {
        int count = 0;
        foreach (var id in _registry.KittenIds)
            if (RequestSmite(id)) count++;
        return count;
    }

    /// <summary>Forgets kittens removed by native KSA and releases their private War Paint materials.</summary>
    public int PruneDisposed()
    {
        var disposed = _registry.GetDisposed();
        if (disposed == null) return 0;
        foreach (var entry in disposed)
        {
            _registry.Unregister(entry.KittenId);
            if (entry.Vehicle is KittenEva kitten) WarPaintService.Shared.Unset(kitten);
        }
        return disposed.Count;
    }
}
