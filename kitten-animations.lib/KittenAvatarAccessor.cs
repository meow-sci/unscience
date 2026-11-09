using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.KittenAnimationsLib;

/// <summary>Provides access to live EVA kittens, their renderables and their character avatars.</summary>
public static class KittenAvatarAccessor
{
    /// <summary>Returns the controlled vehicle as KittenEva, or null if not a KittenEva.</summary>
    public static KittenEva? GetControlledKitten()
    {
        var vehicle = VehicleProvider.GetControlledVehicle();
        if (vehicle is KittenEva { IsDisposed: false } kitten)
            return kitten;
        return null;
    }

    /// <summary>Returns all live EVA kittens in the current system, sorted by vehicle id.</summary>
    public static List<KittenEva> GetAllKittens()
    {
        return VehicleProvider.GetAllVehicles()
            .OfType<KittenEva>()
            .Where(kitten => !kitten.IsDisposed)
            .OrderBy(kitten => kitten.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Finds a live EVA kitten by its stable vehicle id.</summary>
    public static KittenEva? FindKitten(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return VehicleProvider.FindVehicle(id) is KittenEva { IsDisposed: false } kitten ? kitten : null;
    }

    /// <summary>Back-compat alias for resolving the controlled EVA kitten.</summary>
    public static KittenEva? GetKitten() => GetControlledKitten();

    /// <summary>Returns the controlled kitten's KittenRenderable, or null. Typed — no reflection needed.</summary>
    public static KittenRenderable? GetKittenRenderable()
    {
        return GetControlledKitten()?.Renderable;
    }

    /// <summary>Returns the CharacterAvatar for the controlled KittenEva via private field reflection, or null.</summary>
    public static CharacterAvatar? GetKittenAvatar()
    {
        return GetAvatar(GetKittenRenderable());
    }

    /// <summary>Returns the CharacterAvatar owned by a KittenRenderable via private field reflection, or null.</summary>
    public static CharacterAvatar? GetAvatar(KittenRenderable? renderable)
    {
        if (renderable == null) return null;

        return ReflectionHelpers.GetFieldValue(renderable, "_characterAvatar") as CharacterAvatar;
    }
}
