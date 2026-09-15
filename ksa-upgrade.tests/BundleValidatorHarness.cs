using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using KSA;

namespace MeowSci.PartsNowLib;

public static partial class BundleValidator
{
    private sealed class ValidationContext
    {
        internal ValidationContext(IReadOnlyList<ParsedBundle> bundles, string modDirectory)
        {
            Bundles = bundles;
            ModDirectory = modDirectory;
            ModDirectoryFullPath = string.Empty;
        }

        internal IReadOnlyList<ParsedBundle> Bundles { get; }
        internal string ModDirectory { get; }
        internal string ModDirectoryFullPath { get; set; }
        internal bool ModDirectoryAvailable { get; set; }
        internal List<ValidationIssue> Issues { get; } = new List<ValidationIssue>();
        internal Dictionary<string, PbrMaterialReference> DeclaredMaterials { get; } =
            new Dictionary<string, PbrMaterialReference>(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddError(
        ValidationContext context,
        string rule,
        string sourceName,
        string elementId,
        string message) =>
        context.Issues.Add(new ValidationIssue(IssueSeverity.Error, rule, message, elementId, sourceName));

    private static void AddWarning(
        ValidationContext context,
        string rule,
        string sourceName,
        string elementId,
        string message) =>
        context.Issues.Add(new ValidationIssue(IssueSeverity.Warning, rule, message, elementId, sourceName));

    internal static List<ValidationIssue> InvokeV8(string sourceName, string xml)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        ParsedBundle bundle = new ParsedBundle(sourceName, xml, document);
        ValidationContext context = new ValidationContext(new[] { bundle }, string.Empty);
        RuleV8UnsupportedElements(context);
        return context.Issues;
    }
}
