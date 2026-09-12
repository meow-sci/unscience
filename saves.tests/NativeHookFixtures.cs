using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KSA
{
    internal static class Trace
    {
        public static readonly List<string> Events = new();
    }

    public static class Program
    {
        public static bool IsEditorOpen;
    }

    public class GameSave
    {
        public UniverseData UniverseData = new();
        public bool FailCapture;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual void Populate()
        {
            Trace.Events.Add("native capture");
            if (FailCapture) throw new InvalidOperationException("native capture failed");
            UniverseData = new UniverseData();
        }
    }

    public sealed class UncompressedSave : GameSave
    {
        public bool FailRead;
        public bool FailWrite;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Write()
        {
            Trace.Events.Add("native write");
            if (FailWrite) throw new InvalidOperationException("native write failed");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Load()
        {
            if (Program.IsEditorOpen) { Trace.Events.Add("refused"); return; }
            Trace.Events.Add("native read");
            if (FailRead) throw new InvalidOperationException("native read failed");
            Universe.DeserializeSave(UniverseData);
            Trace.Events.Add("native menus closed");
        }
    }

    public sealed class UniverseData
    {
        public bool FailReconstruction;
        public ValidReference? GameTime = new();
        public CameraData? Camera = new();
        public object? KittenRoster = new();
        public List<CelestialSystemData> CelestialSystems = new() { new() };
    }

    public interface IParentBody { }
    public sealed class ValidReference { public bool Valid = true; public bool IsValid() => Valid; }
    public enum CameraMode { Fly, Orbit }
    public sealed class CameraData
    {
        public SerializedReference? Following = new() { Id = "" };
        public object? TidalLocking = new();
        public object? MapInverted = new();
        public SerializedReference? MapPreviouslyControlled = new() { Id = "" };
        public ValidReference? _positionRaw, _rotationRaw, _scaleRaw;
        public CameraMode CameraMode;
    }
    public sealed class FixtureBody : IParentBody { }
    public sealed class CelestialSystem
    {
        public string Id = "system";
        public object? Get(string id) => id == "body" ? new FixtureBody() : null;
    }
    public sealed class SerializedReference { public string Id = "system"; }
    public sealed class CelestialSystemData
    {
        public SerializedReference Id = new();
        public List<VehicleData> Vehicles = new();
    }
    public sealed class VehicleData
    {
        public string Id = "vehicle";
        public string Character = "";
        public SerializedReference ParentBody = new() { Id = "body" };
        public PartInstance? RootPartInstance;
    }
    public sealed class PartInstance
    {
        public string InstanceOf = "part";
        public List<PartInstance>? Children;
        public List<PartInstance>? SubPartInstances;
    }
    public sealed class CharacterReference { }
    public static class ModLibrary
    {
        public static T Get<T>(string id) where T : new()
        {
            if (id is not ("part" or "character")) throw new InvalidOperationException("missing template");
            return new T();
        }
    }

    public static class Universe
    {
        public static CelestialSystem? CurrentSystem = new();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DeserializeSave(UniverseData universeData)
        {
            if (CurrentSystem == null) throw new InvalidOperationException("no system");
            JobSystems.OrbitSolvers.Wait();
            JobSystems.VehicleSolver.Wait();
            JobSystems.ClothSolvers.Wait();
            Trace.Events.Add("native destroy");
            if (universeData.FailReconstruction) throw new InvalidOperationException("native reconstruction failed");
            Trace.Events.Add("native reconstructed");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void LoadSystem(string id)
        {
            if (SystemLibrary.Find(id) == null) throw new InvalidOperationException("unknown system");
            CurrentSystem = new();
            Trace.Events.Add("native new system");
        }
    }

    public static class SystemLibrary
    {
        public static object? Find(string id) => id == "valid" ? new object() : null;
    }

    public sealed class Jobs(string name)
    {
        public bool Fail;
        public void Wait()
        {
            Trace.Events.Add("join " + name);
            if (Fail) throw new InvalidOperationException("join failed");
        }
    }

    public static class JobSystems
    {
        public static readonly Jobs OrbitSolvers = new("orbit");
        public static readonly Jobs VehicleSolver = new("vehicle");
        public static readonly Jobs ClothSolvers = new("cloth");
        public static readonly Jobs ConcurrentWorkers = new("concurrent");
    }
}

namespace MeowSci.KsaAbstractions
{
    public static class PhysicsFrameHook
    {
        public static readonly Queue<Action> Pending = new();
        private static Action? _worldChange;
        public static bool IsApplied { get; private set; }
        public static bool IsReplayingWorldChange { get; private set; }
        public static void Apply(HarmonyLib.Harmony harmony) => IsApplied = true;
        public static void EnqueueWorldChange(Action action) => _worldChange = action;
        public static void ClearPendingWorldChange() => _worldChange = null;
        public static void ReplayPending()
        {
            Action? action = _worldChange;
            _worldChange = null;
            if (action == null) return;
            IsReplayingWorldChange = true;
            try { action(); }
            finally { IsReplayingWorldChange = false; }
        }
        public static void ClearPending()
        {
            Pending.Clear();
            KSA.Trace.Events.Add("clear pending");
        }
    }
}
