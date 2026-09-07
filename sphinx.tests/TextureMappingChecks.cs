using System;
using System.Numerics;
using System.Runtime.InteropServices;
using MeowSci.SphinxLib;

internal static class TextureMappingChecks
{
    public static void Run()
    {
        StaticVertex[] source =
        [
            new() { Position = new(3, 4, 5), Normal = Vector3.UnitY, Uv = new(.25f, .75f) },
            new() { Position = new(3, 4, 5), Normal = -Vector3.UnitY, Uv = new(.25f, .75f) },
            new() { Position = new(-2, 7, 9), Normal = Vector3.UnitZ, Uv = new(-1, 2) }
        ];
        var before = (StaticVertex[])source.Clone();
        var mapping = new TextureMapping(new(2, .5f), new(-.5f, .25f));
        var mapped = mapping.Apply(source);
        if (mapped[0].Uv != new Vector2(0, .625f) || mapped[2].Uv != new Vector2(-2.5f, 1.25f))
            throw new Exception("UV scaling must precede offsets, retaining out-of-range repeating coordinates.");
        if (mapped[0].Uv != mapped[1].Uv) throw new Exception("Backfaces must retain matching texture coordinates.");

        // Repeated live edits and reset use immutable import data, including after a failed edit.
        for (int i = 0; i < 200; i++)
        {
            var current = new TextureMapping(new(1 + i, .01f + i), new(i / 8f, -i / 4f));
            _ = current.Apply(source);
            AssertEqual(mapped, mapping.Apply(source), "Edits accumulated");
            AssertEqual(before, TextureMapping.Identity.Apply(source), "Reset did not restore imported UVs");
        }
        for (int i = 0; i < source.Length; i++)
            if (mapped[i].Position != source[i].Position || mapped[i].Normal != source[i].Normal)
                throw new Exception("Texture edits changed mesh shape or normals.");

        foreach (var invalid in new[]
        {
            new TextureMapping(new(0, 1), Vector2.Zero), new(new(1, -.1f), Vector2.Zero),
            new(new(1001, 1), Vector2.Zero), new(new(1, float.NaN), Vector2.Zero),
            new(Vector2.One, new(float.PositiveInfinity, 0)), new(Vector2.One, new(0, float.NaN))
        }) Reject(() => invalid.Apply(source));
        StaticVertex[] overflow = [new() { Uv = new(float.MaxValue, 1) }];
        Reject(() => new TextureMapping(new(1000), Vector2.Zero).Apply(overflow));
        AssertEqual(before, source, "Source mesh was mutated");
        if (Marshal.SizeOf<StaticVertex>() != 32 || Marshal.OffsetOf<StaticVertex>(nameof(StaticVertex.Uv)).ToInt32() != 24)
            throw new Exception("Native vertex layout changed.");
        Console.WriteLine("PASS: Sphinx UV scale/offset, backfaces, repeated edits/reset, source isolation, vertex ABI and invalid/overflow rejection.");
    }

    private static void AssertEqual(StaticVertex[] expected, StaticVertex[] actual, string message)
    {
        for (int i = 0; i < expected.Length; i++)
            if (expected[i].Position != actual[i].Position || expected[i].Normal != actual[i].Normal || expected[i].Uv != actual[i].Uv)
                throw new Exception(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception("Invalid texture mapping was accepted.");
    }
}
