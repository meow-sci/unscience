using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.GlfwApi;
using RenderCore.Input;
namespace Brutal.GlfwApi { public enum GlfwKeyAction { Press } public enum GlfwModifier { None } }
namespace RenderCore.Input { public struct GlfwKeyEvent { } }
namespace KSA
{
    public enum InputAction { Forward }
    public interface IViewport { }
    public class Viewport : IViewport { }
    public partial class Vehicle
    {
        public int BaseKeys, BaseInputs, BaseRenders;
        public PartTree Parts = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual bool OnKey(GlfwKeyEvent evt) { BaseKeys++; return true; }
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void ProcessInput(InputAction action, GlfwKeyAction keyAction, GlfwModifier modifiers) { BaseInputs++; }
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void UpdateRenderData(IViewport viewport, int inFrameIndex) { BaseRenders++; PartModelRenderer.Pending++; }
    }
    public partial class KittenEva : Vehicle
    {
        public int EvaKeys, EvaInputs, AvatarRenders;
        [MethodImpl(MethodImplOptions.NoInlining)] public override bool OnKey(GlfwKeyEvent keyEvent) { EvaKeys++; return false; }
        [MethodImpl(MethodImplOptions.NoInlining)] public override void ProcessInput(InputAction action, GlfwKeyAction keyAction, GlfwModifier modifiers) { EvaInputs++; }
        [MethodImpl(MethodImplOptions.NoInlining)] public override void UpdateRenderData(IViewport viewport, int inFrameIndex) { base.UpdateRenderData(viewport, inFrameIndex); AvatarRenders++; }
    }
    public partial class PartTree { public Part Root = new(); }
    public partial class Part { public List<Connector> Connectors = new(); public partial class Connector { public bool Owned; } }
    public class VehicleUpdateState(Vehicle vehicle)
    {
        public readonly Vehicle ReadOnlyVehicle = vehicle;
        public bool IsKitten;
        [MethodImpl(MethodImplOptions.NoInlining)] public void PrepareFromVehicle(bool useHighFidelityOceanPhysics, object manualControlInputs) { IsKitten = ReadOnlyVehicle is KittenEva; }
    }
    public class Scheduler { public int Waits; public void Wait() { Waits++; } }
    public static class JobSystems { public static Scheduler VehicleSolver = new(); }
    public static class Program { public static VehicleEditor? Editor; public static Vehicle[] VehiclesInFrame = []; public static Vehicle? ControlledVehicle; }
    public static class PartModelRenderer
    {
        public static int Pending, Uploaded;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void UpdateRenderData(IViewport viewport, int frameIndex) { Uploaded += Pending; Pending = 0; }
    }
}
namespace MeowSci.IronManLib
{
    public static class IronManConnectors { public const string BackpackTemplateId = "KittenEvaBackpack"; public static bool IsOwned(KSA.Part.Connector connector) => connector.Owned; }
    public class IronManSubmod
    {
        public static IronManSubmod? Instance;
        public HashSet<KSA.KittenEva> Enabled = new();
        public HashSet<KSA.KittenEva> Configured = new();
        public bool IsEnabled(KSA.KittenEva kitten) => Enabled.Contains(kitten);
        public bool IsConfigured(KSA.KittenEva kitten) => Configured.Contains(kitten) || Enabled.Contains(kitten);
    }
}
