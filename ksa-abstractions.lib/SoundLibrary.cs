namespace MeowSci.KsaAbstractions;

/// <summary>Common copied sound files; decoding is validated by the playback backend.</summary>
public static class SoundLibrary
{
    public static SharedFileLibrary Files { get; } = new("sounds", "Sound", new[] { ".ogg", ".wav", ".mp3" });
}
