using System;
using Brutal.Numerics;

namespace MeowSci.IronManLib;

/// <summary>Rocket controls expressed in the unchanged EVA assembly coordinates.</summary>
public static class IronManOrientation
{
    // Control +X (nose) -> EVA -Z (head), +Y -> +Y (right), +Z -> +X (face).
    public static readonly doubleQuat RocketCtrl2Body =
        doubleQuat.CreateFromAxisAngle(double3.UnitY, Math.PI / 2.0);
}
