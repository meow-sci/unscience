using System;
using System.Collections.Generic;
using System.Linq;

namespace MeowSci.Unscience;

/// <summary>One startup attempt's diagnostics, independent of game or saved scene state.</summary>
internal sealed class PatchApplicationReport
{
    private readonly List<Failure> _failures = new();

    public int CompletedCount { get; private set; }
    public int FailedCount => _failures.Count;
    public int AttemptedCount => CompletedCount + FailedCount;
    public IReadOnlyList<Failure> Failures => _failures;

    // Completion means only that the group returned without throwing. A helper may
    // intentionally disable an unsupported optional path or have no controller yet.
    public void TryApply(string feature, Action apply)
    {
        try
        {
            apply();
            CompletedCount++;
        }
        catch (Exception ex)
        {
            _failures.Add(new Failure(feature, ex));
            Console.WriteLine($"unscience: Failed to apply {feature} patches: {ex}");
        }
    }

    public string Summary
    {
        get
        {
            string summary = $"unscience: Harmony patch startup: {CompletedCount}/{AttemptedCount} groups completed without throwing; {FailedCount} failed.";
            if (FailedCount > 0)
                summary += $" Failed groups: {string.Join(", ", _failures.Select(failure => failure.Feature))}.";
            return summary;
        }
    }

    internal sealed record Failure(string Feature, Exception Error);
}
