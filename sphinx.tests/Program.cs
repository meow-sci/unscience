using System;
using System.Numerics;
using MeowSci.SphinxLib;

TextureMappingChecks.Run();
CollisionGeometryChecks.Run();

var min = new Vector3(-7, 3, -2);
var max = new Vector3(11, 13, 5);
var offset = new Vector3(12, 4, -9);
var random = new Random(1957);
for (int test = 0; test < 200; test++)
{
    var scale = test == 0 ? Vector3.One : new Vector3(Next(.01f, 50), Next(.01f, 50), Next(.01f, 50));
    var rotation = test == 0 ? Vector3.Zero : new Vector3(Next(-360, 360), Next(-360, 360), Next(-360, 360));
    var matrix = PlacementMath.GroundedLocal(min, max, scale, rotation, offset);
    var low = new Vector3(float.PositiveInfinity);
    var high = new Vector3(float.NegativeInfinity);
    for (int i = 0; i < 8; i++)
    {
        var corner = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
        var transformed = Vector3.Transform(corner, matrix);
        low = Vector3.Min(low, transformed); high = Vector3.Max(high, transformed);
    }
    Near(low.Y, offset.Y, "base must rest at the requested height");
    Near((low.X + high.X) / 2, offset.X, "horizontal center X");
    Near((low.Z + high.Z) / 2, offset.Z, "horizontal center Z");
    Near(Vector3.TransformNormal(Vector3.UnitX, matrix).Length(), scale.X, "X scale survives rotation");
    Near(Vector3.TransformNormal(Vector3.UnitY, matrix).Length(), scale.Y, "Y scale survives rotation");
    Near(Vector3.TransformNormal(Vector3.UnitZ, matrix).Length(), scale.Z, "Z scale survives rotation");
    if (matrix != PlacementMath.GroundedLocal(min, max, scale, rotation, offset)) throw new Exception("Transforms accumulated.");
}
var identity = PlacementMath.GroundedLocal(new(-1, 0, -1), new(1, 2, 1), Vector3.One, Vector3.Zero, Vector3.Zero);
if (identity != Matrix4x4.Identity) throw new Exception("Already grounded centered Y-up bounds should retain identity.");
Reject(() => PlacementMath.GroundedLocal(min, max, new(0, 1, 1), Vector3.Zero, Vector3.Zero));
Reject(() => PlacementMath.GroundedLocal(min, max, new(-1, 1, 1), Vector3.Zero, Vector3.Zero));
Reject(() => PlacementMath.GroundedLocal(min, max, new(1001), Vector3.Zero, Vector3.Zero));
Reject(() => PlacementMath.GroundedLocal(min, max, new(float.NaN), Vector3.Zero, Vector3.Zero));
Reject(() => PlacementMath.GroundedLocal(min, max, Vector3.One, new(float.PositiveInfinity), Vector3.Zero));
Reject(() => PlacementMath.GroundedLocal(min, max, Vector3.One, Vector3.Zero, new(float.NaN)));
Reject(() => PlacementMath.GroundedLocal(max, min, Vector3.One, Vector3.Zero, Vector3.Zero));
Reject(() => PlacementMath.GroundedLocal(new(float.NegativeInfinity), max, Vector3.One, Vector3.Zero, Vector3.Zero));
Reject(() => PlacementMath.GroundedLocal(new(-float.MaxValue), new(float.MaxValue), new(1000), Vector3.Zero, Vector3.Zero));
Console.WriteLine("PASS: Sphinx grounding, offsets, XYZ scale/rotation, repeatability and invalid/overflow transforms (200 deterministic cases).");
float Next(float a, float b) => a + (b-a) * (float)random.NextDouble();
static void Near(float actual, float expected, string name)
{
    if (MathF.Abs(actual - expected) > .002f) throw new Exception($"{name}: {actual} != {expected}");
}
static void Reject(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new Exception("Invalid transform was accepted.");
}
