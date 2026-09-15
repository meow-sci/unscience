using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using KSA;

namespace KSA
{

// The supplied game DLL is a Windows x64 binary and cannot be loaded by the macOS ARM64 managed
// test runner. These tiny declarations preserve only the fields and members touched by the linked
// production rules. They are fixtures, not copied game sources.
public sealed class Tank
{
}

public readonly struct FlowOrder<T>
{
    private readonly T[][]? _levels;

    public FlowOrder(T[][]? levels)
    {
        _levels = levels;
    }

    public int LevelCount => _levels?.Length ?? 0;

    public ReadOnlySpan<T> this[int index] => _levels is null ? default : _levels[index];
}

public sealed class ResourceManager
{
    public ResourceManager()
    {
    }

    public ResourceManager(FlowOrder<Tank> consumptionOrder)
    {
        ConsumptionOrder = consumptionOrder;
    }

    public FlowOrder<Tank> ConsumptionOrder { get; }
}

public class PartTemplate
{
    public string Id { get; set; } = string.Empty;
}

public sealed class TextureReference
{
}

public sealed class PbrMaterialReference
{
    public string Id { get; set; } = string.Empty;
    public TextureReference? DiffuseReference { get; set; }
    public TextureReference? NormalReference { get; set; }
    public TextureReference? PBRMap { get; set; }
}

}

namespace MeowSci.PartsNowLib
{

// This is deliberately the smallest object surface used by BundleValidatorRulesSchema. The V8
// rule reads the submitted XDocument directly; no KSA AssetBundle deserialization or registration
// is needed to prove its top-level-versus-nested document contract.
public sealed record ParsedBundle(string SourceName, string Xml, XDocument Document);

public static partial class BundleParser
{
    public sealed class ModelComponent
    {
        public string ElementName { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        public PbrMaterialReference? Material { get; init; }
    }

    public static IEnumerable<PartTemplate> AllPartTemplates(ParsedBundle bundle) =>
        Enumerable.Empty<PartTemplate>();

    public static IEnumerable<PartTemplate> SubParts(ParsedBundle bundle) =>
        Enumerable.Empty<PartTemplate>();

    public static IEnumerable<ModelComponent> ModelComponents(PartTemplate template) =>
        Enumerable.Empty<ModelComponent>();

    public static bool HasMeshView(PartTemplate template) => true;

    public static int LineNumber(XObject node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
    }
}

public static class GameRegistry
{
    public static PbrMaterialReference? FindMaterial(string id) => null;
}
}
