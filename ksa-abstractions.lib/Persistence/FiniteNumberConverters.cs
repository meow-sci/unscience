using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeowSci.KsaAbstractions.Persistence;

// Strict JSON syntax still permits exponents larger than IEEE-754 can represent.
internal sealed class FiniteSingleConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        float value = reader.GetSingle();
        if (!float.IsFinite(value)) throw new JsonException("Saved number exceeds the finite float range.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class FiniteDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        double value = reader.GetDouble();
        if (!double.IsFinite(value)) throw new JsonException("Saved number exceeds the finite double range.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
