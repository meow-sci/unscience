using System;
using System.Collections.Generic;
using System.IO;

namespace RabbitEars.Tv;

/// <summary>Finds the PALindrome command-line decoder, which is a separate, separately built native program.</summary>
public static class PalindromeLocator
{
    public const string EnvironmentVariable = "PALINDROME_BIN";
    private static readonly string RepoRelativePath = Path.Combine("plans", "palindrome-crt", "prototype", "native", ".work", "bin", ExecutableName);

    private static string ExecutableName => OperatingSystem.IsWindows() ? "palindrome.exe" : "palindrome";

    /// <summary>
    /// Search order: the explicit path (--palindrome), $PALINDROME_BIN, the repository's prototype build found by
    /// walking up from the executable and from the working directory, then PATH.
    /// </summary>
    public static string Find(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath))
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : throw Missing($"--palindrome {explicitPath} does not exist");

        string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(fromEnvironment))
            return File.Exists(fromEnvironment) ? Path.GetFullPath(fromEnvironment) : throw Missing($"${EnvironmentVariable}={fromEnvironment} does not exist");

        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            foreach (string directory in SelfAndParents(start))
            {
                string candidate = Path.Combine(directory, RepoRelativePath);
                if (File.Exists(candidate)) return candidate;
            }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (directory.Length == 0) continue;
            string candidate = Path.Combine(directory, ExecutableName);
            if (File.Exists(candidate)) return candidate;
        }
        throw Missing("the PALindrome decoder was not found");
    }

    private static IEnumerable<string> SelfAndParents(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            yield return directory.FullName;
    }

    private static InvalidOperationException Missing(string what) => new(
        $"{what}. The viewer needs the native `palindrome` CLI, which is not bundled: build it with " +
        "plans/palindrome-crt/prototype/native/build_cli.sh (or rabbit-ears/demo.sh), or point --palindrome / " +
        $"${EnvironmentVariable} at an existing binary.");
}
