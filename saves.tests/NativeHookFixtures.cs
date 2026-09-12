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
    }

    public static class Universe
    {
        public static object? CurrentSystem = new();

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
        public void Wait() => Trace.Events.Add("join " + name);
    }

    public static class JobSystems
    {
        public static readonly Jobs OrbitSolvers = new("orbit");
        public static readonly Jobs VehicleSolver = new("vehicle");
        public static readonly Jobs ClothSolvers = new("cloth");
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
