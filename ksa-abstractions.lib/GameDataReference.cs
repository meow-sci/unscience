using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>Builds a resolved game-data link for mod-owned runtime graphs, outside XML loading.</summary>
public static class GameDataReference
{
    public static DataReference<T>? To<T>(T? value) where T : SerializedId => value == null
        ? null : new DataReference<T> { Ref = value.Id, Value = value };

    /// <summary>Unwraps links used by reflection-based material consumers; a missing target stays null.</summary>
    public static object? Resolve(object? value) => value is DataReference
        ? value.GetType().GetField(nameof(DataReference<SerializedId>.Value))?.GetValue(value) : value;
}
