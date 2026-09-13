using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.SkittlesLib;

public sealed partial class SkittlesSubmod : ISaveParticipantSource
{
    private bool _sceneThemeRestored;

    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get
        {
            // The user's configured startup theme remains the baseline for native saves without state.
            ThemeDefinition baseline = ThemeDefinition.CaptureFromImGui();
            yield return new SaveParticipant<ThemeDefinition>("skittles", ThemeDefinition.CaptureFromImGui,
                () => { baseline.ApplyToImGui(); _sceneThemeRestored = false; },
                (theme, _) => { theme.ApplyToImGui(); _sceneThemeRestored = true; }, order: 5, validate: ValidateSavedTheme);
        }
    }

    private static void ValidateSavedTheme(ThemeDefinition theme)
    {
        if (theme.Colors == null || theme.Colors.Length != 60 || theme.Colors.Any(c => c == null || c.Length != 4 || c.Any(v => !float.IsFinite(v))))
            throw new InvalidOperationException("Invalid saved theme colors.");
        // ThemeDefinition is an explicit detached DTO. Validate its scalar/array style surface before native ImGui writes.
        foreach (var property in typeof(ThemeDefinition).GetProperties())
        {
            object? value = property.GetValue(theme);
            if (property.PropertyType == typeof(float) && (value is not float scalar || !float.IsFinite(scalar)))
                throw new InvalidOperationException($"Invalid theme value {property.Name}.");
            if (property.PropertyType == typeof(float[]) && (value is not float[] vector || vector.Length != 2 || vector.Any(v => !float.IsFinite(v))))
                throw new InvalidOperationException($"Invalid theme vector {property.Name}.");
        }
        if (theme.Alpha < .05f || theme.Alpha > 1 || theme.CurveTessellationTol <= 0 || theme.CircleTessellationMaxError <= 0)
            throw new InvalidOperationException("Saved theme would hide the UI or invalidate tessellation.");
    }
}
