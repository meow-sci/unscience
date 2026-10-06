using System.Collections.Generic;

namespace KSA
{
    internal static class Program
    {
        public static Vehicle? ControlledVehicle { get; set; }
        public static IGameViewport MainViewport { get; set; } = new GameViewport(new Camera());
        public static IGameViewport InputViewport { get; set; } = MainViewport;
        public static object? Editor { get; set; }
        public static bool IsWindowOpen { get; set; }
        public static ConsoleFixture ConsoleWindow { get; } = new();
    }

    internal sealed class ConsoleFixture { public bool IsOpen { get; set; } }

    internal static class Universe
    {
        public static CelestialSystem? CurrentSystem { get; set; }
    }

    internal sealed class CelestialSystem
    {
        public VehicleCollection All { get; } = new();
    }

    internal sealed class VehicleCollection
    {
        private readonly List<Vehicle> _vehicles = new();
        public List<Vehicle> UnsafeAsList() => _vehicles;
    }
}

namespace MeowSci.KsaAbstractions
{
    internal static class IvaForceRender
    {
        public static bool Enabled { get; set; }
        public static bool IsInstalled => true;
        public static bool Required { get; private set; }
        public static void SetRequired(string owner, bool required) => Required = required;
    }

    internal static class PartRenderFilter
    {
        public static bool IsOperational => true;
        public static void RegisterStaticModel(HarmonyLib.Harmony harmony, string owner, System.Func<KSA.PartModel, bool> predicate) { }
        public static void Unregister(HarmonyLib.Harmony harmony, string owner) { }
    }
}

namespace MeowSci.KitchenSinkLib
{
    public sealed partial class KitchenSinkSubmod
    {
        public int PickerResets { get; private set; }
        // Only native UI state is substituted; the real reset/adapter/registry are linked.
        private void ResetGLoadPicker() => PickerResets++;
    }
}
