using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.GarrysTorchLib;

/// <summary>Registers welding with the shared result/snapshot handoff.</summary>
public static class GarrysTorchPatches
{
    public static void Apply(Harmony harmony)
    {
        PhysicsFrameHook.Apply(harmony);
        PhysicsFrameHook.BeforePhysics += Update;
    }

    public static void Remove(Harmony harmony)
    {
        PhysicsFrameHook.BeforePhysics -= Update;
        PhysicsFrameHook.Remove(harmony);
    }

    private static void Update(double dt, UniverseTime stateTime) =>
        GarrysTorchSubmod.Instance?.UpdateBeforeVehicleSolvers(dt, stateTime);
}
