using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.PartsNowLib;

internal static class BundleValidatorChecks
{
    internal static void Run()
    {
        const string source = "KSA5438Assets.xml";
        const string xml = "<Assets>\n"
            + "  <Substance Id=\"substance\" />\n"
            + "  <MixtureReaction Id=\"mixture\" />\n"
            + "  <FixedReaction Id=\"fixed\" />\n"
            + "  <ThermalReaction Id=\"thermal\" />\n"
            + "  <GrainGeometry Id=\"grain\" />\n"
            + "  <Situation Id=\"situation\" />\n"
            + "  <EditorTagDef Id=\"tag\" />\n"
            + "  <Explosion Id=\"explosion\" />\n"
            + "  <ExplosionVolume Id=\"volume\" />\n"
            + "  <Part Id=\"part\">\n"
            + "    <Explosion Id=\"nested-explosion\" />\n"
            + "    <ExplosionVolume Id=\"nested-volume\" />\n"
            + "    <Substance Id=\"nested-substance\" />\n"
            + "  </Part>\n"
            + "</Assets>";

        List<ValidationIssue> issues = BundleValidator.InvokeV8(source, xml);
        Require(issues.Count == 9,
            "V8 rejects all seven existing unsupported categories and both KSA 5438 categories");
        Require(issues.All(issue => issue.Rule == "V8" && issue.Severity == IssueSeverity.Error),
            "unsupported definitions are V8 errors");

        string[] expectedIds =
        {
            "substance", "mixture", "fixed", "thermal", "grain", "situation", "tag",
            "explosion", "volume",
        };
        Require(expectedIds.All(id => issues.Any(issue => issue.ElementId == id)),
            "each direct unsupported definition identifies its element id");
        Require(!issues.Any(issue => issue.ElementId.StartsWith("nested-", StringComparison.Ordinal)),
            "nested references are permitted");
        Require(issues.Any(issue => issue.ElementId == "explosion" && issue.Message.Contains("line 9",
                StringComparison.Ordinal)), "Explosion reports its source line");
        Require(issues.Any(issue => issue.ElementId == "volume" && issue.Message.Contains("line 10",
                StringComparison.Ordinal)), "ExplosionVolume reports its source line");

        List<ValidationIssue> ordinary = BundleValidator.InvokeV8(
            "ordinary.xml", "<Assets><Part Id=\"safe\"><Explosion Id=\"reference\" /></Part></Assets>");
        Require(ordinary.Count == 0, "nested-only explosion references do not block a bundle");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
