using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Materials;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions.Persistence;

// Native-free stand-ins. The real DOH save adapter, registry, material set, material release
// and VehicleProvider are linked unchanged (see doh.tests.csproj).

namespace Brutal.Numerics
{
    public readonly record struct float3(float X, float Y, float Z);

    public readonly record struct float4(float X, float Y, float Z, float W)
    {
        public static float4 One => new(1, 1, 1, 1);
    }
}

namespace KSA
{
    public class Vehicle(string id)
    {
        public string Id { get; } = id;
        public bool IsDisposed { get; set; }
        public bool IsDebris { get; set; }
    }

    public sealed class KittenEva(string id) : Vehicle(id);

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
    public sealed class KittenSpawner(MaterialFactory factory)
    {
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
            return factory.CreateSet(tint);
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
        private bool _useCustomColor;
        private float4 _tintColor = float4.One;
        private bool _uniquePerKitten;
        private readonly string[] _availableCharacters = { "Calico", "Tabby" };
        private int _selectedCharacterIndex = -1;
        private Vehicle? _selectedVehicle;

        public DohSubmod() => _spawner = new KittenSpawner(_materialFactory!);

        public MaterialFactory Factory => _materialFactory!;
        public SpawnedKittenRegistry Registry => _registry!;
        public KittenSpawner Spawner => _spawner!;
        public Vehicle? SelectedVehicle { get => _selectedVehicle; set => _selectedVehicle = value; }

        public void Track(KittenEva kitten, string characterId, KittenMaterialSet? set) =>
            _registry!.Register(kitten.Id, characterId, set);

        /// <summary>Production Update() runs this every frame (after pruning disposed kittens).</summary>
        public void NextFrame() => ReleaseStaleSavedMaterials();
    }
}
