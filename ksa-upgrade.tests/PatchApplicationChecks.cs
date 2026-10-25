using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using MeowSci.Unscience;

internal static class PatchApplicationChecks
{
    internal static void Run()
    {
        var report = new PatchApplicationReport();
        var order = new List<string>();
        using var output = new StringWriter();
        TextWriter originalOutput = Console.Out;
        try
        {
            Console.SetOut(output);
            report.TryApply("first", () => order.Add("first"));
            report.TryApply("wrapped failure", () =>
            {
                order.Add("wrapped failure");
                typeof(PatchApplicationChecks).GetMethod(nameof(ThrowWrappedFailure),
                    BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            });
            report.TryApply("after failure", () => order.Add("after failure"));
            report.TryApply("second failure", () => throw new ArgumentException("second patch target changed"));
            Console.WriteLine(report.Summary);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        Require(string.Join(",", order) == "first,wrapped failure,after failure",
            "a wrapped patch failure preserves order and does not prevent the next group running");
        Require(report.CompletedCount == 2 && report.FailedCount == 2 && report.AttemptedCount == 4,
            "the report counts completed and failed actions exactly once");
        Require(report.Failures[0].Feature == "wrapped failure" && report.Failures[1].Feature == "second failure",
            "all failed group names are retained in attempt order");
        Exception wrapped = report.Failures[0].Error;
        Require(wrapped is TargetInvocationException && wrapped.InnerException is InvalidOperationException
            && wrapped.InnerException.InnerException is MissingMethodException,
            "the real reflection wrapper and complete inner exception chain are retained");
        Require(wrapped.InnerException!.InnerException!.StackTrace!.Contains(nameof(ThrowRootFailure), StringComparison.Ordinal),
            "the innermost exception retains its originating stack");
        string log = output.ToString();
        Require(log.Contains(wrapped.ToString(), StringComparison.Ordinal),
            "the log includes the complete exception, inner failures and stack traces");
        Require(log.Contains(report.Failures[1].Error.ToString(), StringComparison.Ordinal),
            "later independent failures also retain their complete diagnostic context");
        Require(report.Summary.Contains("2/4 groups completed without throwing; 2 failed.", StringComparison.Ordinal)
            && report.Summary.Contains("Failed groups: wrapped failure, second failure.", StringComparison.Ordinal),
            "the final summary reports measured totals and every failed group");

        // Patch() creates this object afresh on each invocation. An absent optional
        // controller is still a completed action, without claiming its feature is active.
        var nextRun = new PatchApplicationReport();
        Action? optionalController = null;
        nextRun.TryApply("optional controller", () => optionalController?.Invoke());
        Require(nextRun.CompletedCount == 1 && nextRun.AttemptedCount == 1
            && nextRun.FailedCount == 0 && nextRun.Failures.Count == 0,
            "a new startup has fresh counts and no failures from the earlier run");
        Require(nextRun.Summary.Contains("1/1 groups completed without throwing; 0 failed.", StringComparison.Ordinal)
            && !nextRun.Summary.Contains("Failed groups:", StringComparison.Ordinal),
            "a clean summary has the current run's totals and no stale failed names");
        Console.WriteLine("PASS: consolidated patch application reporting (10 checks)");
    }

    private static void ThrowWrappedFailure()
    {
        try { ThrowRootFailure(); }
        catch (MissingMethodException ex)
        {
            throw new InvalidOperationException("patch group could not bind", ex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowRootFailure() => throw new MissingMethodException("missing physics frame anchor");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
