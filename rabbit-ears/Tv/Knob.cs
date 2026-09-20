using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RabbitEars.Tv;

/// <summary>
/// One control of the web UI: a slider (Min/Max/Step), a drop-down (Choices) or a checkbox (IsToggle).
/// Values travel as invariant-culture text ("1"/"0" for toggles) so knob sets compare and serialise trivially.
/// </summary>
public sealed record Knob(string Name, string Label, string Group, string Help)
{
    /// <summary>The `palindrome render` flag this knob feeds, when it maps onto exactly one.</summary>
    public string? Flag { get; init; }

    public double Min { get; init; }
    public double Max { get; init; }
    public double Step { get; init; }
    public string Default { get; init; } = "0";
    public string[]? Choices { get; init; }
    public bool IsToggle { get; init; }

    /// <summary>Only passed to the decoder when moved off its default (the decoder's own default then applies).</summary>
    public bool OnlyWhenChanged { get; init; }

    public static Knob Slider(string name, string label, string group, string help, string? flag, double min, double max, double step, double @default) =>
        new(name, label, group, help) { Flag = flag, Min = min, Max = max, Step = step, Default = Format(@default) };

    public static Knob Choice(string name, string label, string group, string help, string? flag, string[] choices, string @default) =>
        new(name, label, group, help) { Flag = flag, Choices = choices, Default = @default };

    public static Knob Toggle(string name, string label, string group, string help, bool @default) =>
        new(name, label, group, help) { IsToggle = true, Default = @default ? "1" : "0" };

    public static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Validates and normalises a value from the UI; throws <see cref="ArgumentException"/> when unusable.</summary>
    public string Coerce(string text)
    {
        if (IsToggle) return text is "0" or "false" or "" ? "0" : "1";
        if (Choices is not null)
            return Choices.Contains(text) ? text : throw new ArgumentException($"{Name}: '{text}' is not one of {string.Join(" | ", Choices)}");
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value))
            throw new ArgumentException($"{Name}: '{text}' is not a number");
        return Format(Math.Clamp(value, Min, Max));
    }

    public double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    /// <summary>The knob as the page's JavaScript expects it.</summary>
    public Dictionary<string, object?> ToJson() => new()
    {
        ["name"] = Name, ["label"] = Label, ["group"] = Group, ["help"] = Help,
        ["min"] = Min, ["max"] = Max, ["step"] = Step, ["choices"] = Choices, ["boolean"] = IsToggle,
        ["default"] = JsonValue(Default),
    };

    /// <summary>A value as JSON: a number for sliders and toggles, text for choices.</summary>
    public object JsonValue(string value) => Choices is not null ? value : Number(value);
}
