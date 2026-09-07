using Brutal.Numerics;
using KSA;

namespace MeowSci.GarrysTorchLib;

/// <summary>Represents a single active weld between two vehicles.</summary>
public class WeldEntry
{
    public Vehicle Source = null!;
    public Vehicle Target = null!;
    /// <summary>
    /// Specific part on the target vehicle to use as the weld anchor.
    /// When set, position and rotation are relative to this part's local frame.
    /// When null, falls back to the target vehicle's body frame (CoM origin).
    /// </summary>
    public Part? TargetPart;
    /// <summary>Offset relative to the anchor — the target part's frame when set, otherwise the target vehicle's body frame (metres).</summary>
    public float3 Position;
    /// <summary>Euler pitch/yaw/roll relative to the anchor orientation (degrees).</summary>
    public float3 Rotation;
    /// <summary>Independent X/Y/Z scale factors applied to all source parts.</summary>
    public float3 Scale = WeldScale.Identity;
    /// <summary>When false, only position is locked; source can rotate freely.</summary>
    public bool LockRotation = true;
    /// <summary>Allow source collisions while enabled. Defaults to false; module simulation continues.</summary>
    public bool Collisions;
    /// <summary>When false, the weld is suspended (no physics applied) but kept in the list.</summary>
    public bool WeldEnabled = true;
}
