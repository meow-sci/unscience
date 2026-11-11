using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;

namespace MeowSci.DohLib.Placement;

internal struct StaticRayHit : IRayHitHandler
{
    public bool Found;
    public float Distance;
    public Vector3 Normal;
    public bool AllowTest(CollidableReference reference) => reference.Mobility == CollidableMobility.Static;
    public bool AllowTest(CollidableReference reference, int childIndex) => true;
    public void OnRayHit(in RayData ray, ref float maximumT, float t, Vector3 normal,
        CollidableReference reference, int childIndex)
    {
        if (t <= 0 || t >= Distance) return;
        Found = true; Distance = maximumT = t; Normal = normal;
    }
}
