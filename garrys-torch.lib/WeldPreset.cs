using Brutal.Numerics;

namespace MeowSci.GarrysTorchLib;

/// <summary>Preset weld configuration (position/rotation/XYZ scale/lockRotation).</summary>
public struct WeldPreset
{
    public float3 Position;
    public float3 Rotation;
    public float3 Scale;
    public bool LockRotation;
    public bool Collisions;
}
