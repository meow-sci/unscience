using System;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

public static class OrientationNumericsExtensions
{
    public static double3 Transform(this double3 vector, doubleQuat rotation) => double3.Transform(vector, rotation);
    public static doubleQuat Inverse(this doubleQuat rotation) => doubleQuat.Inverse(rotation);
}

public interface IFollowable { }
public enum CameraReferenceFrame { Editor, Surface, Body }

public partial class Vehicle : IFollowable
{
    public bool IsDisposed;
    public Part? ControlPart;
    public Part.Connector? ControlConnector;
    public Vehicle() { Parts.OwningVehicle = this; Parts.Root.Tree = Parts; }
    public doubleQuat Ctrl2Body
    {
        get => ControlConnector?.Asmb2VehicleAsmb ?? ControlPart?.Asmb2VehicleAsmb ?? doubleQuat.Identity;
    }
}

public partial class Part
{
    public class PartTemplate { public string Id = "ordinary-part"; }
    public PartTemplate Template = new();
    public PartTree Tree = null!;
    public doubleQuat Asmb2VehicleAsmb = doubleQuat.Identity;
    public partial class Connector { public doubleQuat Asmb2VehicleAsmb = doubleQuat.Identity; }
}

public partial class PartTree
{
    public Vehicle? OwningVehicle;
    public ModuleStateful<ThrusterController, ThrusterControllerState,
        ThrusterControllerGlobalState, EmptyStruct>.StateList States = new();
}

public class VehicleEditingSpace : IFollowable
{
    public doubleQuat Asmb2Ecl = doubleQuat.Identity;
}

public class VehicleEditor
{
    public Vehicle? ExistingVehicle;
    public VehicleEditingSpace EditingSpace = new();
    public double3 CameraOffset;
}

public class OrbitController
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private doubleQuat GetFrame2Ecl(IFollowable focused, CameraReferenceFrame referenceFrame)
    {
        if (focused is VehicleEditingSpace space && referenceFrame == CameraReferenceFrame.Editor)
            return doubleQuat.Concatenate(doubleQuat.CreateFromAxisAngle(double3.UnitY, Math.PI / 2), space.Asmb2Ecl);
        return doubleQuat.Identity;
    }

    public doubleQuat GetFrame(IFollowable focused, CameraReferenceFrame frame) => GetFrame2Ecl(focused, frame);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool EditorOnScroll(double amount)
    {
        if (Program.Editor == null) return false;
        if (amount > 0)
        {
            VehicleEditor editor = Program.Editor;
            if (editor.CameraOffset.X < 50)
                Program.Editor.CameraOffset += double3.UnitX.Transform(Program.Editor.EditingSpace.Asmb2Ecl);
        }
        else
        {
            VehicleEditor editor = Program.Editor;
            if (editor.CameraOffset.X > -50)
                Program.Editor.CameraOffset -= double3.UnitX.Transform(Program.Editor.EditingSpace.Asmb2Ecl);
        }
        return true;
    }

    public bool Scroll(double amount) => EditorOnScroll(amount);
}

[Flags]
public enum ThrusterMapFlags { None = 0, PitchUp = 1, RollRight = 2, TranslateForward = 4 }
public struct EmptyStruct { }
public struct ThrusterControllerState { }
public struct ThrusterControllerGlobalState
{
    public static ThrusterControllerGlobalState Zero => default;
    public int CacheMarker;
}

public class ThrusterController(Part root)
{
    public class PartParent(Part fullPart) { public Part FullPart = fullPart; }
    public PartParent Parent = new(root);
    public ThrusterMapFlags? ManualControlMap;
    public ThrusterMapFlags GeometricMap;
    public int GeometricMapCalls;
    public ThrusterMapFlags ResolvedMap;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void RecomputeDynamicData()
    {
        // Exactly the stock nullable-map selection seam; the actual geometry solver is not simulated.
        ResolvedMap = ManualControlMap ?? GenerateMap();
    }

    private ThrusterMapFlags GenerateMap() { GeometricMapCalls++; return GeometricMap; }
}

public static class ModuleStateful<TModule, TState, TGlobal, TFx> where TGlobal : struct
{
    public class StateList
    {
        public TGlobal Global;
        public ref TGlobal GetMutableGlobalStateForInitialization() => ref Global;
    }
    public static StateList InitializeHotPathList(StateList states) => states;
}
