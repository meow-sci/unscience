using System;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

public static class OrientationNumericsExtensions
{
    public static double3 Transform(this double3 vector, doubleQuat rotation) => double3.Transform(vector, rotation);
    public static doubleQuat Inverse(this doubleQuat rotation) => doubleQuat.Inverse(rotation);
    public static double3 NormalizeOrZero(this double3 vector) => double3.NormalizeOrZero(vector);
}

public interface IFollowable { }
public enum CameraReferenceFrame { Editor, Surface, Body }

public partial class Vehicle : IFollowable
{
    public bool IsDisposed;
    public Part? ControlPart;
    public Part.Connector? ControlConnector;
    public Vehicle() { Parts.OwningVehicle = this; Parts.Root.Tree = Parts; }
    // KSA 5638 Vehicle.cs:613, same expression (formerly Ctrl2Body). Read by navball, rates and
    // worker navigation (PhysicsBubble.cs:1372,1508,1888); see HelmFixture.cs for the FC chain.
    public doubleQuat Helm2Body
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
