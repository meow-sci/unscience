using System;

internal static class ProgramEntry
{
    private static void Main()
    {
        HarmonyFixtureChecks.Run();
        Console.WriteLine("PASS: Wing Walker managed Harmony checks");
    }
}
