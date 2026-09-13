using System;
using Brutal.Numerics;
using KSA;

namespace MeowSci.HotPursuitLib;

/// <summary>Persistent state for one part-mounted secondary camera.</summary>
public sealed class HotPursuitCamera
{
    internal MeowSci.KsaAbstractions.Persistence.SavedPartReference? SaveTarget;
    private static int _nextId = 1;

    public int Id { get; } = _nextId++;

    // Stable identity. Live references are only a per-frame convenience.
    public string VehicleId { get; init; } = "";
    public uint PartInstanceId { get; init; }
    public Vehicle? Vehicle;
    public Part? Part;

    // All three vectors are in the clicked subpart's local assembly frame.
    public double3 MountPoint;
    public double3 SurfaceNormal;
    public double3 MountTangent;
    public double3 Translation;
    public double3 RotationDeg;

    public float FieldOfView = 60f;
    public int Width = 500;
    public int Height = 500;
    public bool Visible = true;

    // One owner per entry is required by ViewportRegistry's ownership map.
    public CameraViewportOwner Owner { get; } = new();
    public IGameViewport? Viewport;
    public bool LeaseLost;
    public bool ResizePending;

    public bool IsResolved => Vehicle != null && Part != null;

    public string TargetDescription => Vehicle == null
        ? $"Vehicle {VehicleId} (not currently live)"
        : Part == null
            ? $"{Vehicle.Id} / part {PartInstanceId} (not currently live)"
            : $"{Vehicle.Id} / {Part.DisplayName} ({Part.InstanceId})";

    public string Status
    {
        get
        {
            if (!IsResolved)
                return "Dormant — target vehicle or part is not loaded";
            if (Viewport == null)
                return LeaseLost ? "Viewport closed — reopen to reclaim a secondary slot" : "No viewport lease";
            return Visible ? "Live" : "Hidden (viewport lease retained)";
        }
    }
}

/// <summary>Reference-identity token used to claim exactly one registry slot.</summary>
public sealed class CameraViewportOwner : IViewportOwner
{
}
