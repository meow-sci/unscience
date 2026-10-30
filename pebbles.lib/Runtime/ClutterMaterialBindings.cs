namespace MeowSci.PebblesLib;

/// <summary>
/// <c>ClutterEcotypeRenderData</c> methods that each bind the stock clutter material buffer (or look up a
/// stock material index) exactly once. <see cref="ClutterHooks"/> redirects every one of them to
/// Pebbles-owned materials; a stock pipeline missing from this list samples the wrong materials.
/// KSA 5707 (rev 5677) added the depth-normal pre-pass. ksa-upgrade.tests compares this list with every
/// binding method in the real KSA.dll, so keep it free of game types.
/// </summary>
internal static class ClutterMaterialBindings
{
    public static readonly string[] Methods =
    {
        "SortMaterialIds", "CreateColorRenderer", "CreateDepthPrePassRenderer", "CreateDepthNormalPrePassRenderer",
        "CreateShadowDepthRenderer",
    };
}
