using System.Runtime.CompilerServices;
using HarmonyLib;

namespace KSA
{
// The stock KSA renderables are native-backed. These fixtures preserve only the private/protected
// members that FreeFallinPatches reflects and the Draw seam that Harmony prefixes.
public class AnimatedRenderable
{
    protected readonly int[] MaterialIndices;

    public AnimatedRenderable(int primaryMaterialHandle)
    {
        MaterialIndices = new[] { primaryMaterialHandle };
    }

    public int PrimaryMaterialHandle => MaterialIndices[0];

    public void SetPrimaryMaterialHandle(int handle)
    {
        MaterialIndices[0] = handle;
    }
}

public sealed class ChuteRenderable
{
    private readonly AnimatedRenderable _renderable;

    public ChuteRenderable(AnimatedRenderable renderable)
    {
        _renderable = renderable;
    }

    public AnimatedRenderable Renderable => _renderable;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Draw()
    {
    }
}
}

namespace MeowSci.FreeFallinLib
{
internal static class CanopyMaterialController
{
    internal static int CurrentMaterialHandle { get; set; } = -1;
    internal static bool Enabled => CurrentMaterialHandle >= 0;

    internal static void Disable()
    {
        CurrentMaterialHandle = -1;
    }
}

internal static class CanopyProjectionShaders
{
    internal static void Apply(Harmony harmony)
    {
    }

    internal static void Remove(Harmony harmony)
    {
    }
}
}
