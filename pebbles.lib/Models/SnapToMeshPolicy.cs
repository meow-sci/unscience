using System;

namespace MeowSci.PebblesLib;

/// <summary>
/// What one body's planet mesh offers snap-to-mesh clutter (KSA 5903+). The game builds the clutter
/// acceleration structure only for meshes whose stock clutter has a snap-to-mesh (non-collideable) ecotype,
/// and only over a patch around the mesh pivot sized for those stock ecotypes' reach.
/// </summary>
/// <param name="HasAccelerationStructure">The TLAS (ray queries) or BVH (fallback) the game would bind exists.</param>
/// <param name="PatchInclusionRadians">Covered reach beyond the billboard snap step, in radians of the body.</param>
/// <param name="BodyRadiusMeters">The body's mean radius, as the game divides the reach by.</param>
public readonly record struct SnapToMeshSurface(bool HasAccelerationStructure, double PatchInclusionRadians, double BodyRadiusMeters);

/// <summary>
/// Refuses recipes the game cannot generate since KSA 5903. A clutter type without colliders snaps to the
/// planet mesh. On a body with no mesh acceleration structure the game's GPU resource build throws. Beyond
/// the covered patch, every placement ray misses and nothing is placed there. Pure and game-independent.
/// The runtime adapter supplies native facts.
/// </summary>
public static class SnapToMeshPolicy
{
    /// <summary>
    /// Relative slack for recovering the inclusion angle as patch minus snap step, which can differ from the
    /// game's own sum by rounding. It is far below a millimetre at any stock reach.
    /// </summary>
    private const double Tolerance = 1e-9;

    /// <summary>Disabled types keep their stock collision type, so only enabled collider-less types snap.</summary>
    public static bool SnapsToMesh(EcotypeRecipe ecotype) => ecotype.Enabled && ecotype.CollisionMode == ClutterCollisionMode.None;

    /// <summary>The native per-ecotype reach in metres: generation range plus a cell diagonal.</summary>
    public static double ReachMeters(double rangeMeters, double cellWidthMeters) => rangeMeters + cellWidthMeters * Math.Sqrt(2.0);

    /// <summary>The first refusal for <paramref name="recipe"/>, or null when every snapping type is supported.</summary>
    /// <param name="cellWidthMeters">Cell width for an object separation, from the game's own grid helper.</param>
    public static string? Refusal(string bodyId, PebblesRecipe recipe, SnapToMeshSurface surface, Func<double, double> cellWidthMeters)
    {
        foreach (var ecotype in recipe.Ecotypes)
        {
            if (!SnapsToMesh(ecotype)) continue;
            if (!surface.HasAccelerationStructure)
                return $"{bodyId} / {ecotype.Name}: this clutter type needs custom colliders on {bodyId}. Without colliders it snaps to the planet mesh, "
                    + $"and since KSA 5949 the game builds that mesh structure only on bodies whose stock clutter snaps to the mesh, which {bodyId}'s does not. "
                    + "Add colliders in the Workshop, or keep the original collision.";
            double reach = ReachMeters(ecotype.Placement.Range, cellWidthMeters(ecotype.Placement.Separation));
            double covered = surface.PatchInclusionRadians * surface.BodyRadiusMeters;
            if (reach / surface.BodyRadiusMeters > surface.PatchInclusionRadians * (1 + Tolerance))
                return $"{bodyId} / {ecotype.Name}: this clutter type needs custom colliders on {bodyId}. Without colliders it snaps to the planet mesh, "
                    + $"and since KSA 5949 the game builds that mesh structure only as far as {bodyId}'s stock snapping clutter reaches (about {covered:N0} m), "
                    + $"so nothing would be placed beyond that. This type reaches about {reach:N0} m. Add colliders in the Workshop, or keep the original collision.";
        }
        return null;
    }

    /// <summary>Throws the first refusal as an <see cref="InvalidOperationException"/>.</summary>
    public static void Require(string bodyId, PebblesRecipe recipe, SnapToMeshSurface surface, Func<double, double> cellWidthMeters)
    {
        if (Refusal(bodyId, recipe, surface, cellWidthMeters) is { } message) throw new InvalidOperationException(message);
    }
}
