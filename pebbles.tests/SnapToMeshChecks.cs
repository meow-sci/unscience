using System;
using MeowSci.PebblesLib;

/// <summary>
/// KSA 5903+: the planet-mesh acceleration structure exists only where stock clutter snaps to the mesh, and
/// covers only the stock snapping reach around the mesh pivot. <see cref="SnapToMeshPolicy"/> must refuse
/// enabled collider-less types the game cannot place, and accept everything else, before allocation.
/// Reach mirrors <c>PlanetMeshLibrary.GetEcotypeRangeMeters</c> with the native cell-width formula.
/// </summary>
internal static class SnapToMeshChecks
{
    private const double EarthRadius = 6_371_000;

    public static void Run()
    {
        double CellWidth(double separation) => NativeCellWidth(2 * Math.PI * EarthRadius, separation);

        // Stock Earth: Grass is the only snapping type (80 m / 1.45 m); the patch is sized for it alone.
        double grassReach = SnapToMeshPolicy.ReachMeters(80, CellWidth(1.45));
        const double snapStep = 0.0017; // native patch = snap step + inclusion; recovering it here re-rounds below the reach
        var earth = new SnapToMeshSurface(true, ((double)(float)snapStep + grassReach / EarthRadius) - (float)snapStep, EarthRadius);
        var luna = new SnapToMeshSurface(false, 0, 1_737_400);

        var stock = Recipe(Ecotype("Grass", true, ClutterCollisionMode.None, 80, 1.45), Ecotype("Tree", true, ClutterCollisionMode.PrimitiveList, 5500, 18.3));
        Check(SnapToMeshPolicy.Refusal("Earth", stock, earth, CellWidth) == null, "stock Earth snapping reach fits its own patch despite re-rounding");

        var bareTree = Recipe(Ecotype("Grass", true, ClutterCollisionMode.None, 80, 1.45), Ecotype("Tree", true, ClutterCollisionMode.None, 5500, 18.3));
        string? beyond = SnapToMeshPolicy.Refusal("Earth", bareTree, earth, CellWidth);
        Check(beyond != null && beyond.Contains("Earth / Tree") && beyond.Contains("custom colliders") && beyond.Contains("KSA 5949") && beyond.Contains("beyond"),
            "a collider-less type reaching past the patch is refused with body, type and reason: " + beyond);

        var rocks = Recipe(Ecotype("Rocks", true, ClutterCollisionMode.None, 2250, 25));
        string? missing = SnapToMeshPolicy.Refusal("Luna", rocks, luna, CellWidth);
        Check(missing != null && missing.Contains("Luna / Rocks") && missing.Contains("custom colliders") && missing.Contains("stock clutter snaps to the mesh"),
            "a collider-less type on a body without the mesh structure is refused: " + missing);
        Check(Throws<InvalidOperationException>(() => SnapToMeshPolicy.Require("Luna", rocks, luna, CellWidth)), "Require throws the refusal");

        // Accepted: disabled types keep stock collision; custom colliders use the non-snap path.
        Check(SnapToMeshPolicy.Refusal("Luna", Recipe(Ecotype("Rocks", false, ClutterCollisionMode.None, 2250, 25)), luna, CellWidth) == null, "disabled collider-less types are accepted");
        Check(SnapToMeshPolicy.Refusal("Luna", Recipe(Ecotype("Rocks", true, ClutterCollisionMode.PrimitiveList, 2250, 25)), luna, CellWidth) == null, "custom-collider types are accepted without the structure");
        Check(SnapToMeshPolicy.Refusal("Luna", Recipe(Ecotype("Rocks", true, ClutterCollisionMode.ConvexHullList, 2250, 25)), luna, CellWidth) == null, "convex-hull types are accepted without the structure");
        Check(SnapToMeshPolicy.Refusal("Earth", Recipe(Ecotype("Tree", false, ClutterCollisionMode.None, 5500, 18.3)), earth, CellWidth) == null, "a disabled type is never limited by the patch");
        Check(SnapToMeshPolicy.Refusal("Luna", Recipe(), luna, CellWidth) == null, "an empty recipe needs no structure");

        // Negative control: the tolerance absorbs rounding only; one metre past the stock reach is refused.
        Check(SnapToMeshPolicy.Refusal("Earth", Recipe(Ecotype("Grass", true, ClutterCollisionMode.None, 81, 1.45)), earth, CellWidth) != null, "a metre beyond the patch is refused");
        Check(SnapToMeshPolicy.Refusal("Earth", Recipe(Ecotype("Grass", true, ClutterCollisionMode.None, 80, 2.0)), earth, CellWidth) != null, "a wider cell diagonal is refused like a longer range");
        Console.WriteLine("PASS: Pebbles refuses collider-less clutter the KSA 5903+ planet mesh cannot place and accepts disabled or collider types.");
    }

    /// <summary>The arithmetic of native <c>CubeCellGrid.GetCellWidth(circumference, separation)</c>.</summary>
    private static double NativeCellWidth(double circumference, double separation)
    {
        double quarter = circumference / 4.0;
        return quarter / Math.Ceiling(quarter / (16.0 * separation));
    }

    private static PebblesRecipe Recipe(params EcotypeRecipe[] ecotypes) => new() { Ecotypes = [.. ecotypes] };

    private static EcotypeRecipe Ecotype(string name, bool enabled, ClutterCollisionMode mode, double range, double separation) => new()
    {
        Name = name, Enabled = enabled, CollisionMode = mode, Placement = new PlacementRecipe { Range = range, Separation = separation }
    };

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Snap-to-mesh check failed: " + message);
    }
}
