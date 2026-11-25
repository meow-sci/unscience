using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KSA
{
    internal static class Trace
    {
        public static readonly List<string> Events = new();
    }

    public class Program
    {
        public static bool IsEditorOpen;
        public static readonly Program Instance = new();
        public bool SwapWontFit;
        public bool FailSwap;

        // KSA 5916 Program.SwapSystem: joins, tears the old world down, then builds the new system.
        // WontFit is decided after teardown; the native caller then restarts the game.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public SystemSwapResult SwapSystem(SystemTemplate system)
        {
            JobSystems.WaitAll();
            Trace.Events.Add("native teardown");
            if (FailSwap) throw new InvalidOperationException("native swap failed");
            if (SwapWontFit) return SystemSwapResult.WontFit;
            Universe.CurrentSystem = new CelestialSystem { Id = system.Id };
            Trace.Events.Add("native new system " + system.Id);
            return SystemSwapResult.Swapped;
        }
    }

    public class GameSave
    {
        public string Id = "fixture";
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
        public bool ThrowWrite;

        // KSA 5482: SaveDirectory/write failures return false; unexpected faults still throw.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool Write()
        {
            Trace.Events.Add("native write");
            if (ThrowWrite) throw new InvalidOperationException("native write failed");
            return !FailWrite;
        }

        // KSA 5482: an unreadable universe file is logged and Load returns without replacing the world.
        // KSA 5916: the system decision is SaveSystemSwap.RequestLoad's; ApplyLoaded may run later.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Load()
        {
            if (Program.IsEditorOpen) { Trace.Events.Add("refused"); return; }
            Trace.Events.Add("native read");
            if (FailRead) return;
            if (SaveSystemSwap.RequestLoad(Id, UniverseData.SystemId, ApplyLoaded) == SystemSwapRequest.NotInstalled)
                Trace.Events.Add("native not installed alert");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ApplyLoaded()
        {
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
        public string SystemId => CelestialSystems.Count > 0 ? CelestialSystems[0].Id.Id : string.Empty;
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

        /// <summary>Vehicle ids the last native load reconstructed.</summary>
        public static readonly List<string> LoadedVehicles = new();

        // KSA 5638: joins every scheduler, destroys the old vehicles, then skips (rather than throws on)
        // vehicles with a missing parent, missing part templates or a failed build such as a missing character.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DeserializeSave(UniverseData universeData)
        {
            if (CurrentSystem == null) throw new InvalidOperationException("no system");
            JobSystems.WaitAll();
            Trace.Events.Add("native destroy");
            LoadedVehicles.Clear();
            if (universeData.FailReconstruction) throw new InvalidOperationException("native reconstruction failed");
            foreach (var vehicle in universeData.CelestialSystems[0].Vehicles)
            {
                if (CurrentSystem.Get(vehicle.ParentBody.Id) is not IParentBody || !Installed<CharacterReference>(vehicle.Character)
                    || (vehicle.RootPartInstance != null && !Installed(vehicle.RootPartInstance)))
                    Trace.Events.Add("native skipped " + vehicle.Id);
                else LoadedVehicles.Add(vehicle.Id);
            }
            Trace.Events.Add("native reconstructed");
        }

        private static bool Installed(PartInstance part) => Installed<PartTemplate>(part.InstanceOf)
            && (part.Children ?? new()).TrueForAll(Installed) && (part.SubPartInstances ?? new()).TrueForAll(Installed);

        private static bool Installed<T>(string id) where T : new()
        {
            if (string.IsNullOrEmpty(id) && typeof(T) == typeof(CharacterReference)) return true;
            try { ModLibrary.Get<T>(id); return true; }
            catch (InvalidOperationException) { return false; }
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
        public static readonly Jobs NearestOrbitAndPerformanceWorker = new("nearest-orbit-and-performance");

        // KSA 5638 join order.
        public static void WaitAll()
        {
            NearestOrbitAndPerformanceWorker.Wait();
            OrbitSolvers.Wait();
            VehicleSolver.Wait();
            ClothSolvers.Wait();
        }
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
        public static bool HasPendingWorldChange => _worldChange != null;
        public static event Action<double, KSA.UniverseTime>? BeforePhysics;
        /// <summary>The handoff runs these after any world-change replay.</summary>
        public static void RunBeforePhysics() => BeforePhysics?.Invoke(0, default);
    }
}
