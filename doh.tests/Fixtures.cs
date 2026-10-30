using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Materials;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions.Persistence;

// Native-free stand-ins. The real DOH save adapter, registry, material set, material release,
// spawner teardown and VehicleProvider are linked unchanged (see doh.tests.csproj).

namespace Brutal.Numerics
{
    public readonly record struct float3(float X, float Y, float Z);
    public readonly record struct double3(double X, double Y, double Z);

    public readonly record struct float4(float X, float Y, float Z, float W)
    {
        public static float4 One => new(1, 1, 1, 1);
    }
}

namespace Brutal.ImGuiApi
{
    /// <summary>Holds text for the production adapter without loading the native ImGui library.</summary>
    public sealed class ImInputString(int capacity, string initialContent)
    {
        private string _value = initialContent;
        public void SetValue(ReadOnlySpan<char> content)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(content) >= capacity)
                throw new InvalidOperationException("Input buffer capacity exceeded.");
            _value = content.ToString();
        }
        public override string ToString() => _value;
    }
}

namespace KSA
{
    internal static class Trace
    {
        public static readonly List<string> Events = new();
    }

    public class Astronomical(string id)
    {
        public string Id { get; } = id;
    }

    public class Vehicle(string id) : Astronomical(id)
    {
        public bool IsDisposed { get; set; }
        public bool IsDebris { get; set; }
    }

    public sealed class KittenEva(string id) : Vehicle(id);

    public enum CrewDisposition { EndMission, Kill, Boarded }

    internal static class Program
    {
        public static Vehicle? ControlledVehicle { get; set; }
    }

    /// <summary>KSA 5707: roster entries are found by a lowercased name hash.</summary>
    internal sealed class KittenRosterData
    {
        public List<string> Names { get; } = new();
        public string? Find(string name) => Names.Find(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase));
    }

    internal static class Universe
    {
        public static CelestialSystem? CurrentSystem { get; set; }
        public static KittenRosterData KittenRoster { get; set; } = new();
        public static bool FailDestroy { get; set; }

        /// <summary>KSA 5638: crew handling, camera/control/target hand-off, then Dispose, which deregisters.</summary>
        public static void DestroyVehicle(Vehicle vehicle, CrewDisposition crew = CrewDisposition.EndMission)
        {
            Trace.Events.Add($"destroy {vehicle.Id} {crew}");
            if (FailDestroy) throw new System.InvalidOperationException("teardown failed");
            if (Program.ControlledVehicle == vehicle) Program.ControlledVehicle = null;
            vehicle.IsDisposed = true;
            CurrentSystem?.Deregister(vehicle);
        }
    }

    internal sealed class CelestialSystem
    {
        public VehicleCollection All { get; } = new();

        public void Deregister(Astronomical astronomical)
        {
            Trace.Events.Add("deregister " + astronomical.Id);
            All.UnsafeAsList().RemoveAll(vehicle => ReferenceEquals(vehicle, astronomical));
        }
    }

    internal sealed class VehicleCollection
    {
        private readonly List<Vehicle> _vehicles = new();
        public List<Vehicle> UnsafeAsList() => _vehicles;

        public bool TryGet(string id, out Astronomical? value)
        {
            value = _vehicles.Find(vehicle => string.Equals(vehicle.Id, id, StringComparison.OrdinalIgnoreCase));
            return value != null;
        }
    }

    public sealed class Jobs(string name)
    {
        public void Wait() => Trace.Events.Add("wait " + name);
    }

    internal static class JobSystems
    {
        public static Jobs? VehicleSolver = new("vehicle solver");
        public static Jobs? NearestOrbitAndPerformanceWorker = new("nearest orbit");
    }
}

namespace MeowSci.DohLib.Materials
{
    /// <summary>Records GPU writes and slot releases instead of touching Vulkan.</summary>
    public static class MaterialSystemAccessor
    {
        public static readonly List<string> Destroyed = new();

        public static bool WriteAlbedoColor(int handle, float4 color)
        {
            MaterialColorState.Record(handle, color);
            return true;
        }

        public static bool DestroyMaterial(string assetName)
        {
            Destroyed.Add(assetName);
            return true;
        }

        public static int GetExistingMaterialHandle(string materialName) => -1;
    }

    public sealed partial class MaterialFactory
    {
        private int _nextMaterialId;
        private int _nextHandle = 100;
        private readonly List<KittenMaterialSet> _createdSets = new();

        public IReadOnlyList<KittenMaterialSet> CreatedSets => _createdSets;

        /// <summary>Stands in for CloneAllMaterials + the fur clone: two owned slots per set.</summary>
        public KittenMaterialSet CreateSet(float4 tint)
        {
            var set = new KittenMaterialSet(NextPrefix(), tint);
            foreach (var (name, source, suffix) in new[] { ("Body", "CharacterModel", "_m0"), ("CatFur", "Fur", "_fur") })
            {
                int handle = _nextHandle++;
                set.AllMaterialHandles.Add(handle);
                set.AssetNames.Add(set.Id + suffix);
                set.Materials.Add(new MaterialEntry { Name = name, Source = source, Handle = handle, Color = tint });
            }
            _createdSets.Add(set);
            return set;
        }
    }
}

namespace MeowSci.DohLib.Spawning
{
    /// <summary>The real teardown partial (KittenSpawner.Teardown.cs) is linked; spawning is substituted.</summary>
    public sealed partial class KittenSpawner(MaterialFactory factory, SpawnedKittenRegistry registry)
    {
        private readonly MaterialFactory _materialFactory = factory;
        private readonly SpawnedKittenRegistry _registry = registry;

        public int Clones { get; private set; }

        /// <summary>Mirrors the production reuse contract: a released set is never reused.</summary>
        internal KittenMaterialSet? ApplyClonedMaterials(KittenEva kitten, float4 tint, string characterId,
            KittenMaterialSet? reusable = null)
        {
            if (reusable != null && !reusable.IsReleased)
            {
                reusable.UpdateTint(tint);
                return reusable;
            }
            Clones++;
            return _materialFactory.CreateSet(tint);
        }
    }
}

namespace MeowSci.DohLib
{
    /// <summary>Only the native UI half is substituted; the real save adapter partial is linked.</summary>
    public sealed partial class DohSubmod : ISaveParticipantSource
    {
        private readonly MaterialFactory? _materialFactory = new();
        private readonly SpawnedKittenRegistry? _registry = new();
        private readonly KittenSpawner? _spawner;
        private float3 _offset = new(0, 0, 10);
        private int _spawnCount = 1;
        private readonly ImInputString _spawnName = new(SpawnNameCapacity, SpawnRequest.DefaultNamePrefix);
        private bool _useCustomColor;
        private float4 _tintColor = float4.One;
        private bool _uniquePerKitten;
        private readonly string[] _availableCharacters = { "Calico", "Tabby" };
        private int _selectedCharacterIndex = -1;
        private Vehicle? _selectedVehicle;

        public DohSubmod() => _spawner = new KittenSpawner(_materialFactory!, _registry!);

        public MaterialFactory Factory => _materialFactory!;
        public SpawnedKittenRegistry Registry => _registry!;
        public KittenSpawner Spawner => _spawner!;
        public Vehicle? SelectedVehicle { get => _selectedVehicle; set => _selectedVehicle = value; }
        public string SpawnName { get => _spawnName.ToString(); set => _spawnName.SetValue(value.AsSpan()); }

        public void Track(KittenEva kitten, string characterId, KittenMaterialSet? set) =>
            _registry!.Register(kitten.Id, characterId, set);

        /// <summary>Production Update() runs this every frame (after pruning disposed kittens).</summary>
        public void NextFrame() => ReleaseStaleSavedMaterials();
    }
}
