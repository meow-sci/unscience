using System;
using System.Numerics;
using BepuPhysics.Collidables;
using MeowSci.DohLib.Placement;

internal static partial class Checks
{
    private static void PlacementGeometryChecks()
    {
        var capsule = new Capsule(.35f, .45f);
        Require(Math.Abs(capsule.HalfLength + capsule.Radius - .575) < 1e-6, "capsule height includes both spherical ends");
        var authored = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -1.5708f);
        var centreAsmb = new Vector3(0, 0, -.59f);
        var random = new Random(831);
        for (int i = 0; i < 64; i++)
        {
            var normal = Vector3.Normalize(new Vector3((float)random.NextDouble() - .5f,
                (float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f));
            var body = CapsulePlacementMath.BodyOrientation(normal, i % 2 == 0 ? normal : Vector3.UnitX);
            Require(Vector3.Distance(Vector3.Transform(-Vector3.UnitZ, body), normal) < 1e-5,
                "kitten -Z, rather than Y, aligns with the clicked normal, including parallel headings");
            var shapeRotation = Quaternion.Concatenate(authored, body);
            var axis = Vector3.Transform(Vector3.UnitY, shapeRotation);
            var offset = CapsulePlacementMath.CentreOffset(capsule, shapeRotation, normal);
            var support = offset - axis * (Vector3.Dot(axis, normal) < 0 ? -capsule.HalfLength : capsule.HalfLength)
                - normal * capsule.Radius;
            Require(Vector3.Distance(support, normal * CapsulePlacementMath.Skin) < 1e-5,
                "support point has exactly the requested surface clearance under arbitrary rotations");
            var comAsmb = new Vector3(.07f, -.02f, -.49792f);
            var com = CapsulePlacementMath.ComOffset(offset, centreAsmb, comAsmb, body);
            Require(Vector3.Distance(com + Vector3.Transform(centreAsmb - comAsmb, body), offset) < 1e-5,
                "assembly origin, capsule centre and arbitrary COM are accounted for independently");
        }
        var sideways = CapsulePlacementMath.CentreOffset(capsule, Quaternion.Identity, Vector3.UnitX);
        Require(Math.Abs(Vector3.Dot(sideways, Vector3.UnitX) - capsule.Radius - CapsulePlacementMath.Skin) < 1e-6,
            "sideways capsules use their radius against a wall, rather than their full half-height");
        var moving = CapsulePlacementMath.PointVelocity(new Vector3(20, 0, 0), new Vector3(0, 0, 2), new Vector3(3, 0, 0));
        Require(moving == new Vector3(20, 6, 0), "rotating craft contribute the COM point velocity");
        bool invalid = false;
        try { CapsulePlacementMath.BodyOrientation(new Vector3(float.NaN, 0, 0), Vector3.UnitX); }
        catch (ArgumentException) { invalid = true; }
        Require(invalid, "invalid placement directions are rejected");
        invalid = false;
        try { CapsulePlacementMath.Validate(new Capsule(0, .45f)); }
        catch (ArgumentException) { invalid = true; }
        Require(invalid, "missing capsule radius is rejected");

        Require(SurfaceRayMarch.TryFind(t => 10 - t, 20, out double distance) && Math.Abs(distance - 10) < 1e-6,
            "terrain crossing is bracketed and bisected");
        Require(!SurfaceRayMarch.TryFind(t => 10 - t, 5, out _), "terrain behind a nearer part cannot replace its hit");
        Require(SurfaceRayMarch.TryFind(t => 5 - t, 10, out distance) && distance < 10,
            "foreground terrain is found before a farther part hit");
        Require(!SurfaceRayMarch.TryFind(_ => 3, 20, out _), "a horizon/sky ray does not invent a terrain hit");
        Require(!SurfaceRayMarch.TryFind(_ => -1, 20, out _), "an underground ray is rejected");
        Require(!SurfaceRayMarch.TryFind(t => t > 1 ? double.NaN : 10, 20, out _), "unavailable terrain data is rejected");
        Require(!SurfaceRayMarch.TryFind(_ => 1, double.PositiveInfinity, out _), "unbounded ray range is rejected");
    }
}
