using System;
using System.Reflection;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using KSA.Rendering.Rings.Rendering;
using MeowSci.KsaAbstractions;

namespace MeowSci.BloominOnionLib;

/// <summary>
/// Makes the game pick up ring references that were added to or removed from body templates
/// after load.
///
/// Two things decide what the game renders: <c>PlanetTransparenciesRenderer</c>'s list of
/// bodies with rings/atmospheres (built by its public <c>PopulatePlanets</c>, whose result is
/// cached in the private <c>_anyRings</c>), and <c>PlanetaryRingsRenderer</c>'s per-body
/// render data (built only in its constructor). So: wait for the device, dispose the existing
/// rings renderer, re-populate the body list, then run the game's own
/// <c>Program.RebuildRenderer()</c> — its <c>CreateRingsRenderer</c> branch rebuilds
/// everything from the current references with proper GPU sync.
/// </summary>
public static class RingRendererRebuilder
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static AccessTools.FieldRef<DistantSphereRenderer, DistantSphereMaterialData>? _distantMaterial;

    public static bool Rebuild(out string message)
    {
        try
        {
            var program = Program.Instance;
            if (program == null)
            {
                message = "game renderer not ready";
                return false;
            }
            var transparencies = ReflectionHelpers.GetFieldValue<PlanetTransparenciesRenderer>(program, "_planetTransparenciesRenderer");
            if (transparencies == null)
            {
                message = "Program._planetTransparenciesRenderer not found (game update?)";
                return false;
            }

            // In-flight frames may still reference ring pipelines/buffers/textures.
            Program.GetRenderer().Device.WaitIdle();
            DisposeRingsRenderer(transparencies);

            bool anyRings = transparencies.PopulatePlanets();
            ReflectionHelpers.SetFieldValue(transparencies, "_anyRings", anyRings);

            program.RebuildRenderer();
            message = anyRings ? "renderer rebuilt with rings" : "renderer rebuilt (no rings in system)";
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"bloomin-onion: renderer rebuild failed: {ex}");
            message = $"renderer rebuild failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>True while the game's planetary rings renderer exists.</summary>
    public static bool IsRingsRendererCreated()
    {
        var transparencies = ReflectionHelpers.GetFieldValue(Program.Instance, "_planetTransparenciesRenderer");
        return ReflectionHelpers.GetFieldValue(transparencies, "_ringRendererCreated") is true;
    }

    private static void DisposeRingsRenderer(PlanetTransparenciesRenderer transparencies)
    {
        if (ReflectionHelpers.GetFieldValue(transparencies, "_ringRendererCreated") is not true) return;
        if (ReflectionHelpers.GetFieldValue(transparencies, "_ringsRenderer") is not PlanetaryRingsRenderer ringsRenderer) return;
        ringsRenderer.Dispose();
        ReflectionHelpers.SetFieldValue(transparencies, "_ringRendererCreated", false);
    }

    /// <summary>
    /// Best-effort: a <c>StaticCelestial</c>'s distant-sphere renderer bakes "has ring shadow",
    /// the radii and the band texture handle into its private material struct at construction
    /// (<c>_material</c>, a <c>DistantSphereMaterialData</c> since KSA 5482; re-uploaded every
    /// frame, with the ring normal recomputed live). Refreshing those fields keeps the ring shadow
    /// correct on the far-away sphere and stops it sampling a pruned painted band.
    /// Cosmetic, so any mismatch is logged and skipped.
    /// </summary>
    public static void SyncDistantSphereShadow(Celestial celestial)
    {
        try
        {
            if (GetFieldFromHierarchy(celestial, "_distantRenderer") is not DistantSphereRenderer distant) return;
            _distantMaterial ??= AccessTools.FieldRefAccess<DistantSphereRenderer, DistantSphereMaterialData>("_material");
            ref DistantSphereMaterialData material = ref _distantMaterial(distant);

            var rings = celestial.BodyTemplate?.RingsReference;
            material.UseRingShadows = rings != null ? 1 : 0;
            if (rings == null)
            {
                material.RingTextureId = 0; // Matches a renderer constructed without rings.
                return;
            }
            material.RingInnerRadius = (float)rings.InnerRadius.InMeters();
            material.RingOuterRadius = (float)rings.OuterRadius.InMeters();
            material.RingTextureId = rings.Texture.Get().BindlessHandle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"bloomin-onion: distant sphere ring shadow sync skipped for {celestial.Id}: {ex.Message}");
        }
    }

    /// <summary>Private fields declared on a base class are invisible to <c>GetType().GetField</c>; walk up.</summary>
    private static object? GetFieldFromHierarchy(object instance, string fieldName)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(fieldName, AnyInstance | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(instance);
        }
        return null;
    }
}
