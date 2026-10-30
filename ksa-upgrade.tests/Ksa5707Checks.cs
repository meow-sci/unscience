using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.PebblesLib;

/// <summary>
/// KSA 5707 seams only the shipped KSA.dll can prove (metadata only; nothing is loaded or run):
/// the Part constructor chain Iron Man's anchor restore depends on (rev 5643), the clutter pipelines
/// binding the stock material buffer that Pebbles redirects (rev 5677), and the static-object color
/// pass null-pipeline skip that Sphinx mirrors (rev 5640).
/// </summary>
internal static class Ksa5707Checks
{
    // Must match IronManConnectorPatches.ConstructorParameters (the private constructor it patches).
    private static readonly string[] PrivatePartConstructor =
    {
        "System.String", "KSA.PartTemplate", "KSA.PartInstance", "KSA.Part", "KSA.TransformReference",
        "System.Int32", "System.Int32", "System.Int32", "KSA.Part", "System.Boolean",
    };

    internal static void Run()
    {
        using var probe = KsaAssemblyProbe.TryOpen("KSA 5707 Part/clutter/static-object metadata checks");
        if (probe == null) return;
        PartConstructors(probe);
        ClutterBindings(probe);
        StaticObjectColorPass(probe);
        Console.WriteLine("PASS: KSA 5707 Part constructor chain, clutter material bindings and static color pass (real KSA.dll)");
    }

    private static void PartConstructors(KsaAssemblyProbe probe)
    {
        var constructors = probe.Constructors("KSA.Part");
        var seam = constructors.SingleOrDefault(c => c.Parameters.SequenceEqual(PrivatePartConstructor));
        Require(seam != null && !seam.IsPublic, "Part has the private 10-argument constructor Iron Man patches");
        foreach (var signature in new[]
        {
            new[] { "KSA.PartInstance", "KSA.Part" },                                   // save load
            new[] { "System.String", "KSA.PartTemplate", "KSA.PartInstance", "KSA.Part" }, // runtime creation
            new[] { "KSA.Part", "KSA.Part" },                                           // PartCloner copies
        })
        {
            var constructor = constructors.SingleOrDefault(c => c.IsPublic && c.Parameters.SequenceEqual(signature));
            Require(constructor != null, $"Part({string.Join(", ", signature)}) exists");
        }
        // One patch sees every construction only while every other constructor reaches the seam.
        foreach (var constructor in constructors.Where(c => !ReferenceEquals(c, seam)))
            Require(ReachesSeam(constructor, constructors, seam!, new HashSet<string>()),
                $"Part({string.Join(", ", constructor.Parameters)}) chains to the private constructor");
    }

    private static bool ReachesSeam(KsaAssemblyProbe.Constructor constructor, List<KsaAssemblyProbe.Constructor> all,
        KsaAssemblyProbe.Constructor seam, HashSet<string> visited)
    {
        if (!visited.Add(string.Join(",", constructor.Parameters))) return false;
        foreach (var chain in constructor.ChainsTo)
        {
            if (chain.SequenceEqual(seam.Parameters)) return true;
            var next = all.SingleOrDefault(c => c.Parameters.SequenceEqual(chain));
            if (next != null && ReachesSeam(next, all, seam, visited)) return true;
        }
        return false;
    }

    private static void ClutterBindings(KsaAssemblyProbe probe)
    {
        const string type = "KSA.ClutterEcotypeRenderData";
        string[] anchors = { "KSA.GroundClutterRenderer::get_MaterialBuffer", "KSA.GroundClutterRenderer::GetMaterialIndex" };
        var binding = anchors.SelectMany(anchor => probe.MethodsCalling(type, anchor)).Distinct().OrderBy(n => n).ToArray();
        Require(binding.SequenceEqual(ClutterMaterialBindings.Methods.OrderBy(n => n)),
            $"Pebbles redirects exactly the stock material-binding methods [{string.Join(", ", binding)}]");
        foreach (string method in ClutterMaterialBindings.Methods)
        {
            var overloads = probe.CallsOf(type, method);
            Require(overloads.Count == 1 && overloads[0].Count(anchors.Contains) == 1,
                $"{method} has one overload binding exactly one stock material buffer or index");
        }
    }

    private static void StaticObjectColorPass(KsaAssemblyProbe probe)
    {
        var overloads = probe.CallsOf("KSA.StaticObjectRenderer", "WriteCommandsColor");
        Require(overloads.Count == 1 && overloads[0].Count(call => call == "Brutal.VulkanApi.VkPipeline::IsNull") == 1,
            "the static color pass still skips a null pipeline, which Sphinx's postfix mirrors");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
