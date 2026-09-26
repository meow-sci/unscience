using System;
using System.Collections.Generic;
using System.Reflection;
using Brutal;
using KSA;

namespace MeowSci.DohLib.Spawning;

/// <summary>Game catalog lookups: backpack part, propellant mix and character references.</summary>
public sealed partial class KittenSpawner
{
    private Part? CreateBackpackPart()
    {
        var partTemplate = FindPartTemplate("KittenBackPackPart");
        if (partTemplate == null) return null;

        var part = new Part(partTemplate.Id, partTemplate);
        // KSA 5482 no longer builds a tree in the Part constructor; mirror EVADoor.GetBackPackPart.
        var tree = part.CreateOwnTree();
        tree.ReinitializeDerivedValues();

        var mix = TryGetReactantMix("MMH_NTO");
        if (mix != null)
        {
            var tanks = part.SubtreeModules.Get<Tank>();
            for (int i = 0; i < tanks.Length; i++)
                tanks[i].ConfigureFor(mix);
        }

        tree.RefillConsumables();
        return part;
    }

    /// <summary>
    /// Resolves a propellant mix by reaction id.
    ///
    /// KSA 2026.7.9.5018 replaced the combustion-process model
    /// (SubstanceLibrary.TryGetCombustionProcess, ids like "MMH_NTO_1.6") with a
    /// reaction model: mixture ratio is no longer baked into the id, so a
    /// MixtureReaction must be evaluated at its default ratio to obtain a mix.
    /// </summary>
    private static ReactantMix? TryGetReactantMix(string reactionId)
    {
        var reaction = SubstanceLibrary.TryGetReaction(KeyHash.Make(reactionId.AsSpan()));
        return reaction switch
        {
            MixtureReaction mixture => mixture.AtMixtureRatio(mixture.DefaultMixtureRatio).ReactantMix,
            IReactantMix fixedMix => fixedMix.ReactantMix,
            _ => null,
        };
    }

    private string GetRandomCharacterId()
    {
        var characters = GetAllCharacterReferences();
        if (characters.Count == 0) return "";
        return characters[Random.Shared.Next(characters.Count)].Id;
    }

    /// <summary>Finds a PartTemplate by name via reflection on ModLibrary.AllParts (internal).</summary>
    private static PartTemplate? FindPartTemplate(string partName)
    {
        try
        {
            var field = typeof(ModLibrary).GetField("AllParts",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return null;

            var collection = field.GetValue(null);
            if (collection == null) return null;

            var findMethod = collection.GetType().GetMethod("Find");
            if (findMethod == null) return null;

            var keyHash = KeyHash.Make(partName.AsSpan());
            return findMethod.Invoke(collection, new object[] { keyHash }) as PartTemplate;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: FindPartTemplate error: {ex.Message}");
            return null;
        }
    }

    /// <summary>Gets all CharacterReference entries via reflection on ModLibrary.AllCharacters (internal).</summary>
    private static List<CharacterReference> GetAllCharacterReferences()
    {
        try
        {
            var field = typeof(ModLibrary).GetField("AllCharacters",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return new List<CharacterReference>();

            var collection = field.GetValue(null);
            if (collection == null) return new List<CharacterReference>();

            var getListMethod = collection.GetType().GetMethod("GetList");
            if (getListMethod == null) return new List<CharacterReference>();

            return getListMethod.Invoke(collection, null) as List<CharacterReference> ?? new List<CharacterReference>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: GetAllCharacterReferences error: {ex.Message}");
            return new List<CharacterReference>();
        }
    }
}
