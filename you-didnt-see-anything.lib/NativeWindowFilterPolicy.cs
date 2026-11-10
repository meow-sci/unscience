namespace MeowSci.YouDidntSeeAnythingLib;

/// <summary>Pure visibility decision after resolving a window's Begin-owner ancestry.</summary>
public static class NativeWindowFilterPolicy
{
    public static bool Allows(bool detachedOnly, bool hideGauges, bool mainViewport, bool stockGauge) =>
        (!detachedOnly || !mainViewport) && (!hideGauges || !stockGauge);

    public static bool HidePlatformOwner(bool ownerExcluded, bool allowedWindowPresent) =>
        ownerExcluded && !allowedWindowPresent;
}
