using System;
using MeowSci.KsaAbstractions;

// Just the resolved 5801 link contract. No content loader or game-backed resource is constructed.
namespace KSA
{
    public class SerializedId { public string Id = ""; }
    public abstract class DataReference { public string Ref = ""; }
    public class DataReference<T> : DataReference where T : SerializedId { public T? Value; }
}

internal static class DataReferenceChecks
{
    internal static void Run()
    {
        var material = new KSA.SerializedId { Id = "kitten/body" };
        var link = GameDataReference.To(material)!;
        Require(link.Ref == material.Id && ReferenceEquals(link.Value, material), "runtime links carry both identity and resolved target");
        Require(ReferenceEquals(GameDataReference.Resolve(link), material), "reflection consumers reach the actual material");
        Require(ReferenceEquals(GameDataReference.Resolve(material), material), "direct assets stay direct");
        Require(GameDataReference.Resolve(new KSA.DataReference<KSA.SerializedId> { Ref = "missing" }) == null,
            "unresolved references never masquerade as usable textures/materials");
        Require(GameDataReference.To<KSA.SerializedId>(null) == null && GameDataReference.Resolve(null) == null, "optional empty slots stay empty");
        Console.WriteLine("PASS: resolved runtime graph links and DOH material unwrapping");
    }
    private static void Require(bool result, string reason) { if (!result) throw new InvalidOperationException(reason); }
}
