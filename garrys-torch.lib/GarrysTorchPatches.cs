using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.GarrysTorchLib;

/// <summary>Registers welding with the shared result/snapshot handoff.</summary>
public static class GarrysTorchPatches
{
    private static bool _isApplied;

    public static void Apply(Harmony harmony)
    {
        if (_isApplied)
        {
            PhysicsFrameHook.Apply(harmony);
            return;
        }
        WeldCollisionPatches.Apply(harmony);
        try
        {
            PhysicsFrameHook.Apply(harmony);
            PhysicsFrameHook.BeforePhysics += Update;
            _isApplied = true;
        }
        catch
        {
            WeldCollisionPatches.Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        PhysicsFrameHook.BeforePhysics -= Update;
        _isApplied = false;
        try { PhysicsFrameHook.Remove(harmony); }
        finally { WeldCollisionPatches.Remove(harmony); }
    }

    private static void Update(double dt, UniverseTime stateTime)
    {
        var submod = GarrysTorchSubmod.Instance;
        try
        {
            submod?.UpdateBeforeVehicleSolvers(dt, stateTime);
        }
        finally
        {
            if (submod == null) WeldCollisionPatches.Clear();
            else WeldCollisionPatches.Publish(submod.Welds);
        }
    }
}
