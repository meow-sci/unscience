using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>
/// Wraps KSA's built-in game-save entry point so mods can create saves through the same code path as
/// the GAME SAVES window and the terminal <c>save</c> command (<c>GameSaves.MakeUncompressedSave</c>).
/// Every native save hook (including the Unscience sidecar) fires exactly as for a manual save.
/// </summary>
public static class GameSaveProvider
{
    /// <summary>True when a solar system is loaded, i.e. there is something to save.</summary>
    public static bool IsWorldLoaded => Universe.CurrentSystem != null;

    /// <summary>True while the vehicle editor is open; KSA refuses to save or load in that state.</summary>
    public static bool IsEditorOpen => Program.IsEditorOpen;

    /// <summary>True when KSA would accept a save request right now.</summary>
    public static bool CanSaveNow => IsWorldLoaded && !IsEditorOpen;

    /// <summary>Longest save name KSA accepts before truncating (<c>SaveName.MAX_LENGTH</c>).</summary>
    public static int MaxSaveNameLength => SaveName.MAX_LENGTH;

    /// <summary>
    /// Creates (or overwrites) a save named <paramref name="name"/> the way the terminal <c>save</c>
    /// command does: editor refusal, <c>SaveName.TryAccept</c> sanitising, then <c>UncompressedSave.Make</c>.
    /// Returns false when KSA refused the request or could not write the files; KSA logs and alerts itself.
    /// </summary>
    public static bool TryMakeSave(string name, out string acceptedName)
    {
        acceptedName = string.Empty;
        if (!CanSaveNow) return false;
        if (!SaveName.TryAccept(name, out string sanitized)) return false;
        acceptedName = sanitized;
        return UncompressedSave.Make(sanitized) != null;
    }
}
