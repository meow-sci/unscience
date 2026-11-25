using KSA;

namespace MeowSci.BlinkyLib;

/// <summary>
/// Whether an engine can draw tank propellant, mirroring native
/// <c>Rocket.UpdatePropellantAvailability</c>. Since KSA 5925/5936 a rocket fires only when every one
/// of its cores has propellant: a <c>GasGeneratorRocket</c> (stock engines A2/A3/A5/A6) needs both its
/// combustor and its gas-generator core, so a single starved tank-fed core keeps the whole engine dark.
/// </summary>
internal static class RocketFeedCheck
{
    /// <summary>True when any rocket of the controller has tank-fed cores and every one reaches a tank.</summary>
    internal static bool AnyRocketFed(EngineController controller)
    {
        var rockets = controller.Rockets;
        if (rockets == null) return false;
        for (int i = 0; i < rockets.Length; i++)
            if (rockets[i] != null && IsFed(rockets[i]))
                return true;
        return false;
    }

    /// <summary>True when any engine controller on the part can draw tank propellant.</summary>
    internal static bool AnyRocketFed(Part part)
    {
        var controllers = part.SubtreeModules.Get<EngineController>();
        for (int i = 0; i < controllers.Length; i++)
            if (AnyRocketFed(controllers[i]))
                return true;
        return false;
    }

    // PlumbedCore (KSA 5487) is the tank-fed base of Combustor, ThermalCore, GasGeneratorCore and
    // AntimatterRamCore; SolidMotor cores draw no tank propellant and never starve a rocket here.
    private static bool IsFed(Rocket rocket)
    {
        bool anyTankFed = false;
        var cores = rocket.Cores;
        for (int i = 0; i < cores.Length; i++)
        {
            if (cores[i] is not PlumbedCore plumbed) continue;
            if (PropellantFeedDiagnostics.CountTanks(plumbed.ResourceManager) == 0) return false;
            anyTankFed = true;
        }
        return anyTankFed;
    }
}
