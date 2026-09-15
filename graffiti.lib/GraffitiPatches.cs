using System;
using System.Reflection;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using KSA.Rendering;

namespace MeowSci.GraffitiLib;

/// <summary>
/// Harmony surface for graffiti: a postfix on <see cref="RenderTarget.ResolveAttachments"/> —
/// the one moment in the frame where the resolved single-sample scene depth and the scene colour
/// are both current and neither is bound as an attachment (the window KSA's own GridPass draws
/// in). Applied by both the standalone host and unscience.
/// </summary>
/// <remarks>
/// <c>Program.RenderGame</c> calls <c>ResolveAttachments</c> unconditionally per viewport; the
/// method body is MSAA-gated but a postfix fires either way, which is what makes this a reliable
/// seam at every MSAA setting. The editor renders the SAME offscreen target through the main
/// viewport index, so both identity checks below pass in the VAB — <c>Program.EditorFlag</c> is
/// the only thing that separates the two, and decals are a flight-scene feature.
/// </remarks>
public static class GraffitiPatches
{
    private static bool _loggedFault;

    private static MethodBase? Target() =>
        AccessTools.Method(typeof(RenderTarget), nameof(RenderTarget.ResolveAttachments), new[]
        {
            typeof(CommandBuffer), typeof(bool)
        });

    private static MethodInfo Postfix() =>
        AccessTools.Method(typeof(GraffitiPatches), nameof(AfterResolveAttachments))!;

    public static void Apply(Harmony harmony)
    {
        var original = Target();
        if (original == null)
            throw new MissingMethodException(typeof(RenderTarget).FullName,
                nameof(RenderTarget.ResolveAttachments));
        harmony.Patch(original, postfix: new HarmonyMethod(Postfix()));
    }

    public static void Remove(Harmony harmony)
    {
        var original = Target();
        if (original != null) harmony.Unpatch(original, Postfix());
    }

    private static void AfterResolveAttachments(RenderTarget __instance, CommandBuffer inCmdBuffer,
        bool inResolveDepth)
    {
        // KSA 5438 resolves color before underwater composition with depth resolution disabled.
        // The decal pass needs the final depth and therefore belongs only after the normal resolve,
        // immediately before the game's GridPass.
        if (!inResolveDepth)
            return;
        RecordPass(__instance, inCmdBuffer);
    }

    private static void RecordPass(RenderTarget __instance, CommandBuffer inCmdBuffer)
    {
        if (!GraffitiSubmod.RenderActive)
            return;
        try
        {
            if (Program.EditorFlag)
                return;
            // Main viewport only: every other viewport (crew portraits, secondary views) resolves
            // its own target with its own camera, and the decal matrices were composed against
            // the main camera this frame.
            if (!ReferenceEquals(__instance, Program.OffscreenTarget)
                || !ReferenceEquals(Program.RenderedViewport, Program.MainViewport))
                return;
            GraffitiSubmod.Instance?.RecordPass(inCmdBuffer);
        }
        catch (Exception ex)
        {
            // A per-frame render exception would spam; log once, the submod self-disables.
            if (!_loggedFault)
            {
                _loggedFault = true;
                Console.WriteLine($"graffiti: render postfix error (logged once): {ex.Message}");
            }
        }
    }
}
