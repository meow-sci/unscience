using Brutal.Numerics;
using KSA;
using MeowSci.WarPaintLib;
using MeowSci.WarPaintLib.Materials;

namespace MeowSci.DohLib.Spawning;

public sealed partial class KittenSpawner
{
    /// <summary>Compatibility entrypoint; War Paint owns every kitten's private clone.</summary>
    internal KittenMaterialSet? ApplyClonedMaterials(KittenEva kittenEva, float4 tintColor,
        string characterId, KittenMaterialSet? reusable = null) =>
        WarPaintService.Shared.Apply(kittenEva, tintColor)?.MaterialSet;
}
