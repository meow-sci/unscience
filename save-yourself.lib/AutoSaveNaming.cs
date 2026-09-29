using System;
using System.Globalization;

namespace MeowSci.SaveYourselfLib;

/// <summary>Builds auto-save names: <c>&lt;prefix&gt;_YYYYMMDDTHHMMSS</c>.</summary>
public static class AutoSaveNaming
{
    public const string TimestampFormat = "yyyyMMdd'T'HHmmss";

    /// <summary>Length of the "_YYYYMMDDTHHMMSS" suffix.</summary>
    public const int SuffixLength = 16;

    /// <summary>
    /// Joins the prefix and the timestamp with an underscore. A prefix that already ends in an
    /// underscore or hyphen is used as-is, so the default <c>autosave_</c> yields
    /// <c>autosave_20260928T203000</c> rather than a double underscore.
    /// </summary>
    public static string BuildName(string prefix, DateTime timestamp)
    {
        string stamp = timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        bool endsWithSeparator = prefix.Length > 0 && (prefix[^1] == '_' || prefix[^1] == '-');
        return endsWithSeparator ? prefix + stamp : prefix + "_" + stamp;
    }
}
