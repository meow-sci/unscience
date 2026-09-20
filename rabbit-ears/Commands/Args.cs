using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RabbitEars.Commands;

/// <summary>Minimal "--name value" / "--flag" parser. Options may repeat; unknown options are an error.</summary>
public sealed class Args
{
    private readonly Dictionary<string, List<string>> _values = new();
    private readonly HashSet<string> _flags = new();

    public Args(string[] argv, string[] valueOptions, string[] flagOptions)
    {
        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            if (!a.StartsWith("--")) throw new ArgumentException($"unexpected argument '{a}'");
            string name = a.Substring(2);
            if (flagOptions.Contains(name))
            {
                _flags.Add(name);
            }
            else if (valueOptions.Contains(name))
            {
                if (i + 1 >= argv.Length) throw new ArgumentException($"--{name} needs a value");
                if (!_values.TryGetValue(name, out List<string>? list)) _values[name] = list = new List<string>();
                list.Add(argv[++i]);
            }
            else
            {
                throw new ArgumentException($"unknown option '--{name}'");
            }
        }
    }

    public bool Has(string name) => _flags.Contains(name) || _values.ContainsKey(name);

    public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out List<string>? list) ? list : Array.Empty<string>();

    public string? Get(string name) => _values.TryGetValue(name, out List<string>? list) ? list[^1] : null;

    public string Require(string name) => Get(name) ?? throw new ArgumentException($"--{name} is required");

    public double Double(string name, double fallback) =>
        Get(name) is { } text ? ParseDouble(name, text) : fallback;

    public double? OptionalDouble(string name) => Get(name) is { } text ? ParseDouble(name, text) : null;

    public int Int(string name, int fallback) =>
        Get(name) is { } text
            ? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : throw new ArgumentException($"--{name}: '{text}' is not an integer")
            : fallback;

    public double[] DoubleList(string name) =>
        Get(name) is { } text ? text.Split(',').Select(t => ParseDouble(name, t)).ToArray() : Array.Empty<double>();

    private static double ParseDouble(string name, string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : throw new ArgumentException($"--{name}: '{text}' is not a number");
}
