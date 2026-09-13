using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>Only use with explicit DTOs. Fields include Brutal numeric vector components; readonly computed properties are excluded.</summary>
public static class SaveJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type.Namespace != "Brutal.Numerics" || info.Kind != JsonTypeInfoKind.Object) return;
            // Brutal vectors expose writable swizzles (XY/XYZ/etc.). Those recursively expose
            // more swizzles even with IgnoreReadOnlyProperties, so allow only scalar axes.
            for (int i = info.Properties.Count - 1; i >= 0; i--)
            {
                var property = info.Properties[i];
                if (property.Name is not ("X" or "Y" or "Z" or "W") || !property.PropertyType.IsPrimitive)
                    info.Properties.RemoveAt(i);
            }
            if (info.Properties.Count == 0)
                throw new NotSupportedException($"Numeric type {info.Type.Name} needs an explicit save representation.");
        });
        return new()
        {
            IncludeFields = true,
            IgnoreReadOnlyProperties = true,
            WriteIndented = true,
            MaxDepth = 64,
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            TypeInfoResolver = resolver,
            Converters = { new FiniteSingleConverter(), new FiniteDoubleConverter() }
        };
    }
    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    public static T FromElement<T>(JsonElement value) => value.Deserialize<T>(Options)
        ?? throw new InvalidOperationException($"Missing {typeof(T).Name} state.");
}
