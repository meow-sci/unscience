using System;
using System.Linq;
using KSA;

namespace MeowSci.PebblesLib;

/// <summary>
/// Native adapter for <see cref="SnapToMeshPolicy"/> (KSA 5903+). It runs before any GPU allocation, so a
/// refusal reaches the Apply status or save-replay warning without a preparation fault to retire. Mirrors:
/// <list type="bullet">
/// <item><c>GroundClutterGpuResources.BuildFrameResources</c>: <c>_renderer.RayQueriesSupported</c>,
/// <c>MeshCollection.Value.LastMesh</c>, and the BVH check for snap-to-mesh grids without ray queries.</item>
/// <item><c>CreateRayQueriesGenerateWrites</c>: the TLAS check for snap-to-mesh grids with ray queries.</item>
/// <item><c>CubeMesh.Generate</c>: <c>ClutterPatchRadians = GetMaxBillboardSnapStep() + inclusion</c>.</item>
/// <item><c>PlanetMeshLibrary.GetEcotypeRangeMeters</c>:
/// <c>GenerationRange + GetCellWidth(2πR, separation)·√2</c>, divided by R.</item>
/// </list>
/// </summary>
internal static class ClutterSnapToMesh
{
    public static void Require(Celestial body, PebblesRecipe recipe)
    {
        if (!recipe.Ecotypes.Any(SnapToMeshPolicy.SnapsToMesh)) return;
        // The renderer exists whenever clutter can be built; without one there is nothing to refuse yet.
        if (Program.GetRenderer() is not { } renderer) return;
        double radius = body.MeanRadius;
        SnapToMeshPolicy.Require(body.Id, recipe, Surface(body, renderer.RayQueriesSupported),
            separation => CubeCellGrid.GetCellWidth(Math.PI * 2.0 * radius, separation));
    }

    private static SnapToMeshSurface Surface(Celestial body, bool rayQueries)
    {
        CubeMesh? mesh = body.BodyTemplate.MeshCollection?.Value?.LastMesh;
        bool built = mesh != null && (rayQueries ? mesh.TopLevelAccelerationStructure.HasValue : mesh.Bvh != null);
        double inclusion = built ? mesh!.ClutterPatchRadians - mesh.GetMaxBillboardSnapStep() : 0;
        return new SnapToMeshSurface(built, inclusion, body.MeanRadius);
    }
}
