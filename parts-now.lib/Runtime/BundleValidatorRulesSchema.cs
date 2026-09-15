// THREADING RULE (repeated in every parts-now file):
// Everything runs on the game thread except RuntimeModLoader's loader step, which runs on a
// Task.Run worker. The worker touches only ILoader.Load(). Completion is polled from Update(dt).
// Do not introduce background access to KSA state; parts-now must remain safe standalone.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using KSA;

namespace MeowSci.PartsNowLib;

/// <summary>
/// Document-shape rules: unsupported element kinds (V8), the material-channel crash guard (V9),
/// file paths (V11) and the missing-mesh-view warning (V12).
/// </summary>
public static partial class BundleValidator
{
    /// <summary>
    /// Element names whose registries parts-now cannot safely extend and roll back. This includes
    /// startup-only libraries and the explosion registries introduced in KSA 5438, which are not
    /// included in this loader's registration snapshots or unload cleanup.
    /// </summary>
    private static readonly string[] UnsupportedElements =
    {
        "Substance", "MixtureReaction", "FixedReaction", "ThermalReaction",
        "GrainGeometry", "Situation", "EditorTagDef",
        "Explosion", "ExplosionVolume",
    };

    /// <summary>
    /// V8 — reject element kinds that would need a new registry. Read from the XDocument, restricted
    /// to the bundle's direct children: these are asset-level elements, and matching them anywhere in
    /// the document would collide with same-named reference elements nested inside game data.
    /// </summary>
    private static void RuleV8UnsupportedElements(ValidationContext context)
    {
        HashSet<string> unsupported = new HashSet<string>(UnsupportedElements, StringComparer.Ordinal);

        foreach (ParsedBundle bundle in context.Bundles)
        {
            XElement? root = bundle.Document.Root;
            if (root is null)
            {
                continue;
            }

            foreach (XElement element in root.Elements())
            {
                string name = element.Name.LocalName;
                if (!unsupported.Contains(name))
                {
                    continue;
                }

                AddError(context, "V8", bundle.SourceName,
                    element.Attribute("Id")?.Value ?? string.Empty,
                    "<" + name + "> (line " + BundleParser.LineNumber(element)
                    + ") is out of scope for parts-now: its registry is not managed by this runtime loader. "
                    + "Reference an existing id instead, or "
                    + "ship this file as a normal mod and restart the game.");
            }
        }
    }

    /// <summary>
    /// V9 — the crash-prevention rule. Every model component needs a <c>&lt;Material&gt;</c>, and that
    /// material must declare all three of <c>&lt;Diffuse&gt;</c>, <c>&lt;Normal&gt;</c> and
    /// <c>&lt;AoRoughMetal&gt;</c>.
    /// <para>
    /// <c>ThumbnailRenderResources.AddDraw</c>, <c>PartModel.WriteInstancesToGpu</c>,
    /// <c>PartModelGlass.WriteInstancesToGpu</c> and <c>PartModelDynamic.WriteInstancesToGpu</c> all
    /// read <c>Material.DiffuseReference.BindlessHandle</c>, <c>.NormalReference.BindlessHandle</c>
    /// and <c>.PBRMap.BindlessHandle</c> with no null check, so a missing channel takes the whole game
    /// down at the first thumbnail or the first frame the part is visible.
    /// </para>
    /// <para>
    /// Read from the object graph, because a material may be declared inline inside the component or
    /// referenced by id — and an id-only <c>&lt;PbrMaterial&gt;</c> is a <i>reference</i>
    /// (<c>PbrMaterialReference.OnDataLoad</c> sets <c>_isReference</c> when all three channels are
    /// null), which has to be resolved against this set and then against the live registry before its
    /// channels can be judged.
    /// </para>
    /// </summary>
    private static void RuleV9MaterialChannels(ValidationContext context)
    {
        foreach (ParsedBundle bundle in context.Bundles)
        {
            foreach (PartTemplate template in BundleParser.AllPartTemplates(bundle))
            {
                foreach (BundleParser.ModelComponent model in BundleParser.ModelComponents(template))
                {
                    if (model.Material is null)
                    {
                        AddError(context, "V9", bundle.SourceName, template.Id,
                            "<" + model.ElementName + " Id=\"" + model.Id + "\"> in '" + template.Id
                            + "' has no <Material>. KSA dereferences the material's texture handles "
                            + "without a null check and would crash the game when the part is drawn.");
                        continue;
                    }

                    CheckMaterialChannels(context, bundle, template, model, model.Material);
                }
            }
        }
    }

    /// <summary>
    /// V11 — every <c>Path=</c> attribute must resolve to an existing file inside the mod folder.
    /// Read from the XDocument so that a path on any element is covered, not just the file-reference
    /// types parts-now happens to know about.
    /// <para>
    /// The C# member is <c>FileReference.LocalPath</c> even though the XML attribute is
    /// <c>Path</c>. <c>FileReference.Load()</c> swallows and merely logs its own failures, so a bad
    /// path otherwise produces a silently half-loaded mod.
    /// </para>
    /// </summary>
    private static void RuleV11Paths(ValidationContext context)
    {
        bool reportedMissingFolder = false;
        string root = context.ModDirectoryFullPath;
        string rootWithSeparator = root.Length == 0
            ? string.Empty
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

        foreach (ParsedBundle bundle in context.Bundles)
        {
            foreach (XElement element in bundle.Document.Descendants())
            {
                XAttribute? attribute = element.Attribute("Path");
                if (attribute is null)
                {
                    continue;
                }

                string value = attribute.Value;
                int line = BundleParser.LineNumber(element);
                string where = "<" + element.Name.LocalName + "> on line " + line;

                if (string.IsNullOrWhiteSpace(value))
                {
                    AddError(context, "V11", bundle.SourceName, element.Attribute("Id")?.Value ?? string.Empty,
                        where + " has an empty Path attribute. Omit the attribute entirely to make it "
                        + "a reference to an already-loaded file.");
                    continue;
                }

                if (Path.IsPathRooted(value) || value.Contains(':'))
                {
                    AddError(context, "V11", bundle.SourceName, value,
                        where + " uses an absolute path '" + value
                        + "'. Paths must be relative to the mod folder.");
                    continue;
                }

                if (HasParentSegment(value))
                {
                    AddError(context, "V11", bundle.SourceName, value,
                        where + " uses '..' in its path '" + value
                        + "'. Paths must stay inside the mod folder.");
                    continue;
                }

                if (!context.ModDirectoryAvailable)
                {
                    if (!reportedMissingFolder)
                    {
                        reportedMissingFolder = true;
                        AddWarning(context, "V11", bundle.SourceName, string.Empty,
                            "the mod folder '" + context.ModDirectory + "' does not exist yet, so "
                            + "Path attributes were checked for escapes but not for existence.");
                    }

                    continue;
                }

                string absolute;
                try
                {
                    absolute = Path.GetFullPath(Path.Combine(root, value));
                }
                catch (Exception ex)
                {
                    AddError(context, "V11", bundle.SourceName, value,
                        where + " has an unusable path '" + value + "' — " + ex.Message);
                    continue;
                }

                if (!absolute.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    AddError(context, "V11", bundle.SourceName, value,
                        where + " resolves to '" + absolute + "', which is outside the mod folder '"
                        + root + "'.");
                    continue;
                }

                if (!File.Exists(absolute))
                {
                    AddError(context, "V11", bundle.SourceName, value,
                        where + " points at '" + value + "', which does not exist under '" + root
                        + "'. KSA only logs a missing asset file, so the mod would load half-broken.");
                }
            }
        }
    }

    /// <summary>
    /// V12 — a <c>&lt;SubPart&gt;</c> with no <c>&lt;MeshView&gt;</c> degrades editor picking.
    /// Read from the object graph: <c>MeshViewModule.Template</c> lands in
    /// <c>PartTemplate.Components</c>, which is exactly what the editor reads. Warning only.
    /// </summary>
    private static void RuleV12SubPartMeshView(ValidationContext context)
    {
        foreach (ParsedBundle bundle in context.Bundles)
        {
            foreach (PartTemplate subPart in BundleParser.SubParts(bundle))
            {
                if (BundleParser.HasMeshView(subPart))
                {
                    continue;
                }

                AddWarning(context, "V12", bundle.SourceName, subPart.Id,
                    "SubPart '" + subPart.Id + "' has no <MeshView>, so the editor has no collision "
                    + "geometry to pick it with.");
            }
        }
    }

    private static void CheckMaterialChannels(
        ValidationContext context,
        ParsedBundle bundle,
        PartTemplate template,
        BundleParser.ModelComponent model,
        PbrMaterialReference material)
    {
        string origin = "<" + model.ElementName + " Id=\"" + model.Id + "\"> in '" + template.Id + "'";
        PbrMaterialReference definition = material;

        if (IsPureReference(material))
        {
            if (string.IsNullOrWhiteSpace(material.Id))
            {
                AddError(context, "V9", bundle.SourceName, template.Id,
                    origin + " has a <Material> with neither an Id nor any texture channel, so it "
                    + "names no material at all.");
                return;
            }

            if (context.DeclaredMaterials.TryGetValue(material.Id, out PbrMaterialReference? declared))
            {
                definition = declared;
            }
            else
            {
                PbrMaterialReference? registered = GameRegistry.FindMaterial(material.Id);
                if (registered is null)
                {
                    AddError(context, "V9", bundle.SourceName, material.Id,
                        origin + " references material '" + material.Id
                        + "', which is neither declared in this set nor already registered.");
                    return;
                }

                definition = registered;
            }
        }

        List<string> missing = new List<string>(3);
        if (definition.DiffuseReference is null)
        {
            missing.Add("<Diffuse>");
        }

        if (definition.NormalReference is null)
        {
            missing.Add("<Normal>");
        }

        if (definition.PBRMap is null)
        {
            missing.Add("<AoRoughMetal>");
        }

        if (missing.Count == 0)
        {
            return;
        }

        string materialId = string.IsNullOrEmpty(definition.Id) ? "(inline)" : definition.Id;
        AddError(context, "V9", bundle.SourceName, materialId,
            "material '" + materialId + "' used by " + origin + " is missing "
            + string.Join(" and ", missing)
            + ". All three of <Diffuse>, <Normal> and <AoRoughMetal> are mandatory — KSA reads their "
            + "bindless handles without a null check and would crash the game.");
    }

    /// <summary>
    /// Mirrors <c>PbrMaterialReference.OnDataLoad</c>'s <c>_isReference</c> test: a material that
    /// declares none of the three primary channels is a pointer at another material, not a definition.
    /// </summary>
    private static bool IsPureReference(PbrMaterialReference material) =>
        material.DiffuseReference is null
        && material.NormalReference is null
        && material.PBRMap is null;

    private static bool HasParentSegment(string path)
    {
        string[] segments = path.Split('/', '\\');
        for (int i = 0; i < segments.Length; i++)
        {
            if (string.Equals(segments[i], "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
