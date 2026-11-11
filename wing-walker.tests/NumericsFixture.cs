namespace Brutal.Numerics;

public readonly struct double3(double x, double y, double z)
{
    public readonly double X = x;
    public readonly double Y = y;
    public readonly double Z = z;
    public static double3 Zero => new(0, 0, 0);
    public static double3 Normalize(double3 value)
    {
        double length = System.Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return new double3(value.X / length, value.Y / length, value.Z / length);
    }
    public static double3 Cross(double3 left, double3 right) => new(
        left.Y * right.Z - left.Z * right.Y,
        left.Z * right.X - left.X * right.Z,
        left.X * right.Y - left.Y * right.X);
    public double3 Normalized() => Normalize(this);
    public static double3 operator +(double3 left, double3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    public static double3 operator -(double3 left, double3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    public static double3 operator -(double3 value) => new(-value.X, -value.Y, -value.Z);
    public static double3 operator *(double3 value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    public override string ToString() => $"({X}, {Y}, {Z})";
}

public readonly struct float3(float x, float y, float z)
{
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float Z = z;
    public static float3 Zero => new(0, 0, 0);
}

public readonly struct double4x4
{
    public static double4x4 Identity => new();
}

public readonly struct float4x4
{
    public static float4x4 Identity => new();
}
