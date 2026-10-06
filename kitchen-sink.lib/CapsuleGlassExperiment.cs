using System;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.KitchenSinkLib;

/// <summary>Reveals native IVA glass by hiding only the stock medium capsule's opaque window models.</summary>
public static class CapsuleGlassExperiment
{
    private const string Owner = "kitchen-sink.capsule-glass";
    private static bool _registered;

    public static bool Enabled { get; private set; }
    public static bool IsAvailable => _registered && PartRenderFilter.IsOperational && IvaForceRender.IsInstalled;

    public static void Apply(Harmony harmony)
    {
        if (_registered) return;
        if (!IvaForceRender.IsInstalled)
            throw new InvalidOperationException("Capsule glass requires the IVA rendering patches.");
        PartRenderFilter.RegisterStaticModel(harmony, Owner, ShouldHide);
        _registered = true;
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled && !IsAvailable)
            throw new InvalidOperationException("Capsule glass experiment render patches are unavailable.");
        IvaForceRender.SetRequired(Owner, enabled);
        Enabled = enabled;
    }

    public static void Remove(Harmony harmony)
    {
        SetEnabled(false);
        PartRenderFilter.Unregister(harmony, Owner);
        _registered = false;
    }

    private static bool ShouldHide(PartModel model) => Enabled && model.Template.Id is
        "CoreCommandA_Subpart_MediumCapsuleWindowA_Model" or
        "CoreCommandA_Subpart_MediumCapsuleWindowB_Model";
}
