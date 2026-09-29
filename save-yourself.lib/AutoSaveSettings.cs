using System;
using System.Text;

namespace MeowSci.SaveYourselfLib;

/// <summary>
/// User-configured auto-save settings. Plain DTO: this is the exact payload of the version-1
/// <c>save-yourself</c> scene record, so keep it backward compatible.
/// </summary>
public sealed record AutoSaveSettings
{
    public const string DefaultPrefix = "autosave_";
    public const int DefaultIntervalSeconds = 30;
    public const int MinIntervalSeconds = 5;
    public const int MaxIntervalSeconds = 300;

    /// <summary>KSA truncates save names at 64 characters; the timestamp suffix uses 16 of them.</summary>
    public const int MaxPrefixLength = 64 - AutoSaveNaming.SuffixLength;

    /// <summary>Save name prefix; alphanumeric, underscore and hyphen only. Empty means <see cref="DefaultPrefix"/>.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Whether timed saves run.</summary>
    public bool Enabled { get; init; }

    /// <summary>Seconds between timed saves, clamped to [<see cref="MinIntervalSeconds"/>, <see cref="MaxIntervalSeconds"/>].</summary>
    public int IntervalSeconds { get; init; } = DefaultIntervalSeconds;

    public static AutoSaveSettings Default => new();

    /// <summary>The prefix actually used for save names.</summary>
    public string EffectivePrefix => string.IsNullOrEmpty(Prefix) ? DefaultPrefix : Prefix;

    /// <summary>True when every field is already within the accepted domain.</summary>
    public bool IsValid => Prefix != null && IsValidPrefix(Prefix)
        && IntervalSeconds >= MinIntervalSeconds && IntervalSeconds <= MaxIntervalSeconds;

    /// <summary>Returns a copy with the prefix sanitised and the interval clamped.</summary>
    public AutoSaveSettings Normalized() => this with
    {
        Prefix = SanitizePrefix(Prefix),
        IntervalSeconds = ClampInterval(IntervalSeconds)
    };

    public static bool IsAllowedPrefixChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-';

    /// <summary>True for an empty prefix or one made only of allowed characters within the length limit.</summary>
    public static bool IsValidPrefix(string? prefix)
    {
        if (prefix == null || prefix.Length > MaxPrefixLength) return false;
        foreach (char c in prefix)
            if (!IsAllowedPrefixChar(c)) return false;
        return true;
    }

    /// <summary>Drops disallowed characters and truncates to <see cref="MaxPrefixLength"/>.</summary>
    public static string SanitizePrefix(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var builder = new StringBuilder(Math.Min(raw.Length, MaxPrefixLength));
        foreach (char c in raw)
        {
            if (builder.Length == MaxPrefixLength) break;
            if (IsAllowedPrefixChar(c)) builder.Append(c);
        }
        return builder.ToString();
    }

    public static int ClampInterval(int seconds) => Math.Clamp(seconds, MinIntervalSeconds, MaxIntervalSeconds);
}
