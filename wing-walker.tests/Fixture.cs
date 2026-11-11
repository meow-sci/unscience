using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

public enum ViewportType { CharacterPortrait }
public enum CameraMode { Exterior, IVA }
public sealed class IvaController { public IVASeat? Seat; }

public interface IViewport
{
    bool Visible { get; }
    Camera GetCamera();
    bool Is(ViewportType type);
    CameraMode Mode { get; }
}

public sealed class Camera { }

public interface IGameViewport : IViewport { IvaController IvaController { get; } }

public sealed class TestViewport(bool visible = true, bool portrait = false, bool iva = false) : IGameViewport
{
    public bool Visible { get; set; } = visible;
    public bool Portrait { get; set; } = portrait;
    public CameraMode Mode { get; set; } = iva ? CameraMode.IVA : CameraMode.Exterior;
    public IvaController IvaController { get; } = new();
    public Camera GetCamera() => new();
    public bool Is(ViewportType type) => type == ViewportType.CharacterPortrait && Portrait;
}

public sealed class SuperMeshRenderSystem
{
    public int ClearCalls;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ClearBuckets(IViewport viewport) => ClearCalls++;
}

public sealed class Template { public string Id = string.Empty; }
public sealed class FullPart { public string Id => Template.Id; public Template Template = new(); }
public sealed class Part { public FullPart FullPart = new(); }

public readonly struct KeyHash(string value) : IEquatable<KeyHash>
{
    private readonly string _value = value;
    public static KeyHash Zero => new(string.Empty);
    public static KeyHash Make(string value) => new(value);
    public bool Equals(KeyHash other) => string.Equals(_value ?? string.Empty, other._value ?? string.Empty, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is KeyHash other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);
    public static bool operator ==(KeyHash left, KeyHash right) => left.Equals(right);
    public static bool operator !=(KeyHash left, KeyHash right) => !left.Equals(right);
}

public enum CrewSeatResult { Seated, AlreadyAboard, NoSuchSeat, SeatTaken, NoFreeSeat }

public sealed class EVADoor(IVASeat? alignedSeat)
{
    public IVASeat? AlignedSeat { get; set; } = alignedSeat;
}

public sealed class IVASeat(string partId, string templateId = "CrewSeat")
{
    private KeyHash _renderedKittenHash;
    public static readonly AnimationAssetRef DefaultSeatedIdle = new("seated-idle");
    public static readonly AnimationAssetRef DefaultGroundIdle = new("ground-idle");
    public string TemplateId { get; } = templateId;
    public Part Parent { get; } = new() { FullPart = new() { Template = new() { Id = partId } } };
    public KeyHash AssignedKittenHash { get; set; }
    public KittenRenderable? Renderable { get; set; }
    public Func<KittenRenderable> RenderableFactory { get; set; } = () => new KittenRenderable(DefaultSeatedIdle, DefaultGroundIdle);
    public int RenderCalls { get; private set; }
    public bool ThrowOnRender { get; set; }
    public Action? DuringRender { get; set; }
    public double3 PositionAsmb = new(4, 5, 6);
    public double3 ForwardAxisAsmb = new(1, 0, 0);
    public double3 UpAxisAsmb = new(0, 0, -1);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateSeatedRenderable(IViewport viewport, int inFrameIndex, double dt, in double4x4 matrixAsmb2Ego)
    {
        RenderCalls++;
        DuringRender?.Invoke();
        if (AssignedKittenHash == KeyHash.Zero)
        {
            Renderable?.Dispose();
            Renderable = null;
            _renderedKittenHash = KeyHash.Zero;
            if (ThrowOnRender) throw new InvalidOperationException("fixture seat failure");
            return;
        }
        if (Renderable == null || _renderedKittenHash != AssignedKittenHash)
        {
            Renderable?.Dispose();
            Renderable = RenderableFactory();
            _renderedKittenHash = AssignedKittenHash;
        }
        Renderable.HideHead = viewport.Mode == CameraMode.IVA && viewport is IGameViewport gameViewport
            && ReferenceEquals(gameViewport.IvaController.Seat, this);
        LocomotionState locomotionState = default;
        KittenAnimInputs animInputs = default;
        Renderable.UpdateRenderData(viewport, inFrameIndex, dt, float4x4.Identity,
            double3.Zero, double3.Zero, in locomotionState, in animInputs);
        if (ThrowOnRender) throw new InvalidOperationException("fixture seat failure");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public double3 GetSeatedModelPointAsmb(in float3 seatedOffset, double heightAboveModelOrigin)
    {
        double3 forward = double3.Normalize(ForwardAxisAsmb);
        double3 right = double3.Cross(double3.Normalize(-UpAxisAsmb), forward).Normalized();
        double3 up = -double3.Cross(forward, right);
        return PositionAsmb + forward * seatedOffset.X + right * seatedOffset.Y
            + up * ((double)seatedOffset.Z + heightAboveModelOrigin);
    }
}

public sealed class ModuleCollection
{
    private readonly IVASeat[] _seats;
    public ModuleCollection(IVASeat[] seats) => _seats = seats;
    public Span<T> Get<T>() where T : class => _seats is T[] matching ? matching : Span<T>.Empty;
}

public sealed class PartTree(IVASeat[]? seats = null)
{
    public bool IsDisposed { get; set; }
    public ModuleCollection Modules { get; } = new(seats ?? Array.Empty<IVASeat>());
}

public class Vehicle(PartTree? parts = null)
{
    public string Id { get; set; } = "fixture-vehicle";
    public bool HasLaunched { get; set; }
    public bool IsDisposed { get; set; }
    public PartTree Parts { get; } = parts ?? new PartTree();
    public bool LargeEnough { get; set; } = true;
    public ReadOnlySpan<IVASeat> Crew => Parts.Modules.Get<IVASeat>();
    public int SeatRenders;

    public double4x4 GetMatrixAsmb2Ego(Camera camera) => double4x4.Identity;
    public bool IsLargeEnoughToRender(Camera camera) => LargeEnough;

    public void UpdateSeatedCrewRenderData(IViewport viewport, int inFrameIndex, double dt)
    {
        double4x4 matrix = GetMatrixAsmb2Ego(viewport.GetCamera());
        ReadOnlySpan<IVASeat> crew = Crew;
        for (int i = 0; i < crew.Length; i++)
        {
            SeatRenders++;
            crew[i].UpdateSeatedRenderable(viewport, inFrameIndex, dt, in matrix);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public CrewSeatResult AddCrew(KeyHash kittenHash, string partId, string seatId)
    {
        ReadOnlySpan<IVASeat> seats = Crew;
        for (int i = 0; i < seats.Length; i++)
        {
            IVASeat seat = seats[i];
            if (!string.Equals(seat.Parent.FullPart.Id, partId, StringComparison.Ordinal)
                || !string.Equals(seat.TemplateId, seatId, StringComparison.Ordinal)) continue;
            if (seat.AssignedKittenHash != KeyHash.Zero) return CrewSeatResult.SeatTaken;
            return SetSeatCrew(seat, kittenHash, Id, HasLaunched);
        }
        return CrewSeatResult.NoSuchSeat;
    }

    public static CrewSeatResult SetSeatCrew(IVASeat targetSeat, KeyHash kittenHash, string vehicleId, bool hasLaunched)
    {
        if (targetSeat.AssignedKittenHash != KeyHash.Zero) return CrewSeatResult.SeatTaken;
        targetSeat.AssignedKittenHash = kittenHash;
        return CrewSeatResult.Seated;
    }

    public CrewSeatResult AddCrewToFirstAvailableSeat(KeyHash kittenHash) => CrewSeatResult.NoFreeSeat;
}

public sealed class KittenEva(string id)
{
    public string Id { get; } = id;
    public bool IsDisposed { get; set; }
    public Action? AfterAddCrew { get; set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public CrewSeatResult BoardVehicle(Vehicle host, EVADoor door)
    {
        IVASeat? seat = door.AlignedSeat;
        CrewSeatResult result = seat == null
            ? host.AddCrewToFirstAvailableSeat(KeyHash.Make(Id))
            : host.AddCrew(KeyHash.Make(Id), seat.Parent.FullPart.Id, seat.TemplateId);
        Action? callback = AfterAddCrew;
        AfterAddCrew = null;
        callback?.Invoke();
        return result;
    }
}

public sealed class VehicleEditor
{
    public Vehicle EditingSpace = new();
    public List<PartTree> UnattachedPartTrees = new();
}

public sealed class GameProgram
{
    public SuperMeshRenderSystem SuperMeshRenderSystem = new();
    public int ResourceFrameIndex = 7;
}

public static class Program
{
    public static GameProgram Instance = new();
    public static IViewport? RenderedViewport;
    public static VehicleEditor? Editor;
    public static Vehicle[] VehiclesInFrame = Array.Empty<Vehicle>();
    public static Vehicle? ControlledVehicle;
    public static ulong FrameNumber;
    public static double GetPlayerDeltaTime() => 0.016;
}
