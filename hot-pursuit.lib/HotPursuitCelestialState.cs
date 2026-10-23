using Brutal.Numerics;
using KSA;

namespace MeowSci.HotPursuitLib;

/// <summary>
/// Mirrors KSA's main-camera nearby-celestial state for a mounted camera. KSA only updates the
/// main camera (<c>OnFrameCelestials</c>). Since 5638 terrain, atmosphere, lighting and distant-body
/// rendering resolve <c>Camera.GetEffectiveNearbyCelestial()</c> (<c>NearbyCelestial ??
/// FindNearbyCelestial</c>), which already picks the same body for an unset secondary camera. The
/// mirror keeps the raw <c>NearbyCelestial</c> readers (airless-body near/far flag, raw metrics)
/// consistent with that pick, applying the same 80,000 km rule as the main camera.
/// </summary>
internal static class HotPursuitCelestialState
{
    private const double MetersToKilometers = 0.001;
    private const double MaxSurfaceDistanceKm = 80000.0;

    internal static void Synchronize(Camera camera)
    {
        Celestial? nearby = Program.FindNearbyCelestial(camera);
        if (nearby == null)
        {
            camera.NearbyCelestial = null;
            return;
        }

        double distanceKm = camera.DistanceTo(nearby.GetPositionEcl()) * MetersToKilometers;
        double surfaceDistanceKm = distanceKm - nearby.MeanRadius * MetersToKilometers;
        if (surfaceDistanceKm > MaxSurfaceDistanceKm)
        {
            camera.NearbyCelestial = null;
            return;
        }

        camera.NearbyCelestial = nearby;
        camera.DistanceToNearbyCelestialKm = distanceKm;
        camera.DistanceToNearbyCelestialSurfaceMeanKm = surfaceDistanceKm;
        camera.NearbyCelestialTerrainHeight = nearby.GetTerrainHeight(camera) * MetersToKilometers;

        double3 positionCce = nearby.GetPositionCce(camera);
        double terrainRadius = nearby.MeanRadius +
            nearby.GetTerrainHeightFromDirCce(positionCce.Normalized());
        camera.CurrentAltitudeKm = (positionCce.Length() - terrainRadius) * MetersToKilometers;
    }

    /// <summary>
    /// Clears the mirror when Hot Pursuit stops driving a camera. Stock never writes a secondary
    /// camera's <c>NearbyCelestial</c> and lease reset does not clear it, so a stale body would
    /// otherwise override the next owner's effective lookup and render the wrong planet's terrain.
    /// </summary>
    internal static void Clear(Camera camera) => camera.NearbyCelestial = null;
}
