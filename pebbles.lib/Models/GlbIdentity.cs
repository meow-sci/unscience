using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MeowSci.PebblesLib;

/// <summary>Exact source identity carried by recipe asset strings. Parsing performs no file access.</summary>
public sealed record GlbIdentity(string Path, string Hash, string Part)
{
    public const string Prefix = "pebbles-glb:";
    public string SourceKey => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(Path)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ":" + Hash;
    public string MeshId(int mesh) => SourceKey + "/mesh/" + mesh.ToString(CultureInfo.InvariantCulture);
    public static GlbIdentity Parse(string id)
    {
        if (!id.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidDataException("Not a Pebbles GLB identity.");
        var colon = id.IndexOf(':', Prefix.Length); var slash = id.IndexOf('/', colon + 1);
        if (colon < 0 || slash < 0) throw new InvalidDataException("Malformed GLB identity.");
        string hash = id[(colon + 1)..slash];
        if (hash.Length != 64 || !System.Linq.Enumerable.All(hash, Uri.IsHexDigit)) throw new InvalidDataException("Invalid GLB content hash.");
        string encoded = id[Prefix.Length..colon].Replace('-', '+').Replace('_', '/');
        string path;
        try { path = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))); }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { throw new InvalidDataException("Invalid GLB source path.", ex); }
        bool windowsAbsolute = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        if ((!System.IO.Path.IsPathFullyQualified(path) && !windowsAbsolute && !path.StartsWith("\\\\", StringComparison.Ordinal))
            || path.IndexOf('\0') >= 0 || !System.IO.Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GLB sources require an absolute .glb file path.");
        return new(path, hash, id[slash..]);
    }
    /// <summary>Portable dependency name; runtime restore resolves only inside the copied shared library.</summary>
    public string LibraryFileName => System.IO.Path.GetFileName(Path.Replace('\\', '/'));
    public static string Label(string id)
    {
        if (!id.StartsWith(Prefix, StringComparison.Ordinal)) return id;
        try { var source = Parse(id); return System.IO.Path.GetFileName(source.Path) + " · " + (source.Part == "/mesh/-1" ? "Complete scene" : source.Part.Trim('/')); }
        catch (InvalidDataException) { return "Unresolved GLB asset"; }
    }
}
