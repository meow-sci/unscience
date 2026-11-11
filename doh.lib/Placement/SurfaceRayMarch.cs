using System;

namespace MeowSci.DohLib.Placement;

internal static class SurfaceRayMarch
{
    public static bool TryFind(Func<double, double> clearance, double range, out double distance)
    {
        distance = 0;
        double previous = clearance(0);
        if (!double.IsFinite(previous) || previous <= 0 || !double.IsFinite(range) || range <= 0) return false;
        double above = 0;
        for (int i = 0; i < 4096 && above < range; i++)
        {
            double below = Math.Min(range, above + Math.Clamp(previous * .25, .05, 4));
            double next = clearance(below);
            if (!double.IsFinite(next)) return false;
            if (next <= 0)
            {
                for (int j = 0; j < 28; j++)
                {
                    double mid = (above + below) * .5;
                    double depth = clearance(mid);
                    if (!double.IsFinite(depth)) return false;
                    if (depth <= 0) below = mid; else above = mid;
                }
                distance = below;
                return true;
            }
            above = below; previous = next;
        }
        return false;
    }
}
