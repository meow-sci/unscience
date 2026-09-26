using KSA;

namespace MeowSci.PebblesLib;

public sealed class ClutterLiveRecord
{
    public string BodyId { get; internal set; } = "";
    public PebblesRecipe Recipe { get; internal set; } = new();
    public string Status { get; internal set; } = "Waiting";
    public long VertexCount => Resources?.Graph.Geometry.VertexCount ?? 0;
    public int MaterialCount => Resources?.Graph.Materials.Count ?? 0;
    public int EcotypeCount => Recipe.Ecotypes.Count;
    internal Celestial Body = null!;
    internal GroundClutterRenderer? Owner;
    internal CelestialTemplate? OriginalTemplate;
    internal CelestialTemplate? OwnedTemplate;
    internal GroundClutterPlacementData[] OriginalPlacement = [];
    internal ClutterEcotypeRenderData[] OriginalRender = [];
    internal ClutterEcotypePhysicalData[] OriginalPhysical = [];
    internal float OriginalRadius;
    /// <summary>Removed/displaced clutter per grid (ecotype + exact separation) seen during this record's lifetime.</summary>
    internal readonly ClutterGridMemory Memory = new();
    internal ClutterResources? Resources;
}
