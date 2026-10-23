using System;
using System.Collections.Generic;

namespace MeowSci.PebblesLib;

/// <summary>
/// Game-independent field-type queries for partial native retirement. KSA changes container shapes
/// between builds (5638 made ClutterCubeCellGrid's per-viewport maps <c>MappedMemory[][]</c>), so
/// ordering rules must follow the element type through any array or sequence depth.
/// </summary>
internal static class FieldShapes
{
    private const int MaxDepth = 8;

    /// <summary>
    /// True when <paramref name="type"/> is <paramref name="element"/> or any (nested) array, nullable,
    /// generic sequence or dictionary value of it.
    /// </summary>
    public static bool Holds(Type type, Type element) => Holds(type, element, 0);

    private static bool Holds(Type type, Type element, int depth)
    {
        if (type == element) return true;
        if (depth >= MaxDepth) return false;
        if (type.IsArray) return Holds(type.GetElementType()!, element, depth + 1);
        if (Nullable.GetUnderlyingType(type) is { } underlying) return Holds(underlying, element, depth + 1);
        if (IsGeneric(type, typeof(KeyValuePair<,>))) return Holds(type.GetGenericArguments()[1], element, depth + 1);
        if (IsGeneric(type, typeof(IEnumerable<>))) return Holds(type.GetGenericArguments()[0], element, depth + 1);
        foreach (Type face in type.GetInterfaces())
            if (IsGeneric(face, typeof(IEnumerable<>)) && Holds(face.GetGenericArguments()[0], element, depth + 1)) return true;
        return false;
    }

    private static bool IsGeneric(Type type, Type definition) => type.IsGenericType && type.GetGenericTypeDefinition() == definition;
}
