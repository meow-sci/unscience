using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Small managed fixture of the 5402 result/snapshot lifecycle, not a replacement game simulation.
// The tests link the production Harmony patch and exercise it against this warmed-up caller.
namespace KSA
{
    public readonly record struct UniverseTime(double Seconds);
    public readonly record struct SimStep(UniverseTime PreviousTime, UniverseTime NextTime, double DeltaTime);

    public sealed class Program
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public SimStep RunFrame(double dt) => PrepareFrame(0, dt);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private SimStep PrepareFrame(double currentPlayerTime, double dtPlayer)
        {
            Universe.ApplyOrbitSolvers();
            Universe.ApplyVehicleSolvers();
            Universe.ApplyClothSolvers();
            SimStep step = Universe.GetJobSimStep(dtPlayer);
            Universe.ExecuteNextClothSolvers(dtPlayer, step);
            Universe.ExecuteNextVehicleSolvers(dtPlayer, step);
            Universe.ExecuteNextOrbitSolvers(dtPlayer, step);
            return step;
        }
    }

    public static class Universe
    {
        public static object? CurrentSystem = new();
        public static readonly List<string> Events = new();
        public static double Time;
        public static double PendingTime;
        public static double TimeCurrent;
        public static double PendingTimeCurrent;
        public static double Speed = 1;
        public static bool InBubble;

        public static void Reset()
        {
            CurrentSystem = new();
            Events.Clear();
            Time = PendingTime = 100;
            TimeCurrent = 0;
            PendingTimeCurrent = 0.25;
            Speed = 1;
            InBubble = true;
        }

        public static void ApplyOrbitSolvers() => Events.Add("apply orbit");
        public static void ApplyVehicleSolvers()
        {
            Events.Add("apply vehicle");
            if (InBubble) TimeCurrent = PendingTimeCurrent;
            Time = PendingTime;
        }
        public static void ApplyClothSolvers() => Events.Add("apply cloth");
        public static SimStep GetJobSimStep(double dtPlayer)
        {
            Events.Add("get step");
            return new(new(Time), new(Time + dtPlayer * Speed), dtPlayer * Speed);
        }
        public static void ExecuteNextClothSolvers(double dtPlayer, SimStep step) => Events.Add("queue cloth");
        public static void ExecuteNextVehicleSolvers(double dtPlayer, SimStep step)
        {
            Events.Add("queue vehicle");
            InBubble = true;
            PendingTimeCurrent = Math.Min(1, TimeCurrent + step.DeltaTime);
            PendingTime = step.NextTime.Seconds;
        }
        public static void ExecuteNextOrbitSolvers(double dtPlayer, SimStep step) => Events.Add("queue orbit");
    }
}

namespace MeowSci.GarrysTorchLib
{
    public sealed class GarrysTorchSubmod
    {
        public static GarrysTorchSubmod? Instance;
        public int Updates;
        public double PlayerDelta;
        public KSA.UniverseTime StateTime;
        public bool ThrowOnUpdate;
        public IReadOnlyList<WeldEntry> Welds { get; } = Array.Empty<WeldEntry>();

        internal void UpdateBeforeVehicleSolvers(double dt, KSA.UniverseTime stateTime)
        {
            KSA.Universe.Events.Add("weld");
            Updates++;
            PlayerDelta = dt;
            StateTime = stateTime;
            if (ThrowOnUpdate) throw new InvalidOperationException("fixture weld failure");
            KSA.Universe.InBubble = false;
        }
    }
}
