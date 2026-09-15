using System.Collections.Generic;
using Brutal.Numerics;

// Native-independent boundary fixtures. LaunchRequest and LaunchMath are production sources.
namespace KSA
{
    public readonly record struct UniverseTime(int Value);
    public interface IParentBody
    {
        string Id { get; }
        doubleQuat GetCcf2Cci(UniverseTime time);
        double3 GetAngularVelocityCci();
    }
    public sealed class Body : IParentBody
    {
        public string Id => "body";
        public doubleQuat Rotation = doubleQuat.Identity;
        public double3 Spin;
        public doubleQuat GetCcf2Cci(UniverseTime time) => Rotation;
        public double3 GetAngularVelocityCci() => Spin;
    }
    public sealed record Orbit(IParentBody Parent, UniverseTime Time, double3 Position, double3 Velocity)
    {
        public byte4 OrbitLineColor => default;
        public static Orbit CreateFromStateCci(IParentBody parent, UniverseTime time,
            double3 position, double3 velocity, byte4 color) => new(parent, time, position, velocity);
    }
    public sealed class Vehicle(IParentBody parent)
    {
        public string Id => "vessel";
        public bool IsDisposed;
        public bool IsEditedVehicle;
        public bool RejectTeleport;
        public bool Updated;
        public int Teleports;
        public double3 BodyRates;
        public doubleQuat Orientation = doubleQuat.Identity;
        public Orbit Orbit = new(parent, default, new double3(7000000, 0, 0), new double3(0, 7500, 0));
        public object FlightPlan = new();
        public IParentBody Parent => Orbit.Parent;
        public double3 GetPositionCci() => Orbit.Position;
        public double3 GetVelocityCci() => Orbit.Velocity;
        public doubleQuat GetBody2Cci() => Orientation;
        public void UpdatePerFrameData() => Updated = true;
        public void Teleport(Orbit orbit, doubleQuat? rotation, double3? rates)
        {
            if (RejectTeleport) return;
            Orbit = orbit;
            FlightPlan = new();
            if (rotation.HasValue) Orientation = rotation.Value;
            if (rates.HasValue) BodyRates = rates.Value;
            Teleports++;
        }
    }
    public static class Program { public static bool EditorFlag; }
}
namespace MeowSci.KsaAbstractions
{
    public interface ISubmod { }
    public static class PhysicsFrameHook
    {
        public static event System.Action<double, KSA.UniverseTime>? BeforePhysics;
        public static int Subscribers => BeforePhysics?.GetInvocationList().Length ?? 0;
        public static void Dispatch(KSA.UniverseTime time) => BeforePhysics?.Invoke(0, time);
    }
    public static class VehicleProvider
    {
        public static readonly List<KSA.Vehicle> Vehicles = new();
        public static List<KSA.Vehicle> GetAllVehicles(bool includeDebris) => Vehicles;
    }
}
namespace Brutal.ImGuiApi
{
    public sealed class ImInputString(int capacity) { public void Clear() { _ = capacity; } }
}
namespace MeowSci.KsaAbstractions.Persistence
{
    public interface ISaveParticipant { }
    public sealed class SaveRestoreContext { }
    public static class SaveJson
    {
        public static System.Text.Json.JsonElement ToElement<T>(T value) => System.Text.Json.JsonSerializer.SerializeToElement(value);
    }
}
namespace MeowSci.DentWizardLib
{
    public sealed partial class DentWizardSubmod
    {
        internal float FormSpeed => _speed;
        internal bool Armed => _armed;
        internal bool Automatic => _automatic;
    }
}
