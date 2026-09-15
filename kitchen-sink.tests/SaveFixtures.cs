using System.Collections.Generic;

namespace KSA
{
    internal static class Program
    {
        public static Vehicle? ControlledVehicle { get; set; }
    }

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
