using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.WarPaintLib.Materials;

public sealed partial class MaterialFactory
{
    // ---- Default handle accessors (from TextureSystem/GltfSystem) ----

    private int GetSamplerRepeatHandle()
    {
        try
        {
            var (_, textureSystem) = GetRenderSystems();
            if (textureSystem == null) return 0;
            return GetIntFieldOrProp(textureSystem, "SamplerRepeatHandle");
        }
        catch { return 0; }
    }

    private int GetDefaultBlackTextureHandle()
    {
        try
        {
            var (_, textureSystem) = GetRenderSystems();
            if (textureSystem == null) return 0;
            var tex = GetFieldOrPropValue(textureSystem, "DefaultBlackTexture");
            return tex != null ? GetIntFieldOrProp(tex, "BindlessHandle") : 0;
        }
        catch { return 0; }
    }

    private int GetDefaultWhiteTextureHandle()
    {
        try
        {
            var (_, textureSystem) = GetRenderSystems();
            if (textureSystem == null) return 0;
            var tex = GetFieldOrPropValue(textureSystem, "DefaultWhiteTexture");
            return tex != null ? GetIntFieldOrProp(tex, "BindlessHandle") : 0;
        }
        catch { return 0; }
    }

    private int GetBlankMaterialTextureHandle()
    {
        try
        {
            var (gltfSystem, _) = GetRenderSystems();
            if (gltfSystem == null) return 0;
            var tex = GetFieldOrPropValue(gltfSystem, "BlankMaterialTexture");
            return tex != null ? GetIntFieldOrProp(tex, "BindlessHandle") : 0;
        }
        catch { return 0; }
    }

    private (object? gltfSystem, object? textureSystem) GetRenderSystems()
    {
        var programType = typeof(Part).Assembly.GetType("KSA.Program");
        var instanceProp = programType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var programInstance = instanceProp?.GetValue(null);
        if (programInstance == null) return (null, null);

        var superMesh = GetFieldOrPropValue(programInstance, "SuperMeshRenderSystem");
        if (superMesh == null) return (null, null);

        var textureSystem = GetFieldOrPropValue(superMesh, "TextureSystem");
        var gltfSystem = GetFieldOrPropValue(superMesh, "GltfSystem");

        return (gltfSystem, textureSystem);
    }

    // ---- Reflection helpers ----

    private static object? GetFieldOrPropValue(object instance, string name)
    {
        var type = instance.GetType();
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null) return field.GetValue(instance);

        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return prop?.GetValue(instance);
    }

    private static int GetIntFieldOrProp(object instance, string name)
    {
        var val = GetFieldOrPropValue(instance, name);
        return val is int i ? i : -1;
    }

}
