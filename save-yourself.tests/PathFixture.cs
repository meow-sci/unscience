using System;
using System.IO;

namespace MeowSci.KsaAbstractions;

/// <summary>Native-free stand-in for the shared path helper; the store under test is given an explicit folder.</summary>
public static class KsaPaths
{
    public static string ModDataDir { get; } = Path.Combine(Path.GetTempPath(), "save-yourself.tests", ".unscience");
}
