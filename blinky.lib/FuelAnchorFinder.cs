using System;
using System.Collections.Generic;
using System.Linq;
using KSA;

namespace MeowSci.BlinkyLib;

/// <summary>
/// Picks the vehicle parts that a grid's pixel engines are wired to for propellant (the
/// round-robin fuel anchors used by <see cref="LcdGridBuilder"/>).
///
/// An anchor must carry a <see cref="Tank"/> that can hold the pixel engine's propellant. Since
/// KSA 5638 a tank's containment is part of that test: a magnetic trap
/// (<c>Tank.IsMagneticTrap</c>, e.g. <c>CoreFuelTankA_Prefab_MagneticTrapA</c>) holds only
/// substances that require magnetic containment (antimatter), and an ordinary tank holds none of
/// them (<c>Tank.CanHold</c>). The game's own <c>Tank.HoldableSubsetOf</c> is used for the check,
/// so a methalox pixel engine is never anchored to a magnetic trap and starved.
/// </summary>
internal static class FuelAnchorFinder
{
    /// <summary>
    /// Collects every part whose subtree carries a tank able to hold at least one reactant of the
    /// pixel engine's propellant, preferring non-sub-parts. When the pixel part has no tank-fed
    /// (<see cref="PlumbedCore"/>) core, any tank-carrying part qualifies.
    /// </summary>
    internal static List<Part> Find(Vehicle vehicle, Part? pixelPart)
    {
        var mixes = PropellantMixesOf(pixelPart);
        var result = Collect(vehicle, mixes, includeSubParts: false, out int rejected);
        // Fallback: accept sub-parts if nothing qualified at top level.
        if (result.Count == 0)
            result = Collect(vehicle, mixes, includeSubParts: true, out rejected);

        if (rejected > 0)
            Console.WriteLine($"blinky: skipped {rejected} tank part(s) that cannot hold '{string.Join("', '", mixes.Select(m => m.Name))}' (e.g. magnetic traps)");
        return result;
    }

    /// <summary>The distinct propellant mixes drawn by the part's tank-fed rocket cores.</summary>
    private static List<ReactantMix> PropellantMixesOf(Part? part)
    {
        var mixes = new List<ReactantMix>();
        if (part == null) return mixes;

        var cores = part.SubtreeModules.Get<RocketCore>();
        for (int i = 0; i < cores.Length; i++)
        {
            if (cores[i] is PlumbedCore plumbed && plumbed.DesiredMix is { } mix && !mixes.Contains(mix))
                mixes.Add(mix);
        }
        return mixes;
    }

    private static List<Part> Collect(Vehicle vehicle, List<ReactantMix> mixes, bool includeSubParts, out int rejected)
    {
        var result = new List<Part>();
        rejected = 0;
        foreach (var part in vehicle.Parts.Parts)
        {
            if (!includeSubParts && part.IsSubPart) continue;

            var tanks = part.SubtreeModules.Get<Tank>();
            if (tanks.Length == 0) continue;

            bool canFeed = mixes.Count == 0;
            for (int i = 0; i < tanks.Length && !canFeed; i++)
                canFeed = CanHoldAny(tanks[i], mixes);

            if (canFeed) result.Add(part);
            else rejected++;
        }
        return result;
    }

    private static bool CanHoldAny(Tank tank, List<ReactantMix> mixes)
    {
        foreach (var mix in mixes)
        {
            if (tank.HoldableSubsetOf(mix) != null)
                return true;
        }
        return false;
    }
}
