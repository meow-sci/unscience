using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>Integrity-bound extension to a completed native save. Does not change KSA's overwrite semantics.</summary>
public static class SaveStorage
{
    public const string FileName = "unscience.json";
    public const int MaximumBytes = 32 * 1024 * 1024;

    public static SaveDocument? Read(string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Unscience save exceeds the 32 MiB limit.");
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = SaveJson.Options.MaxDepth });
        ValidateUniqueProperties(json.RootElement);
        var document = SaveJson.FromElement<SaveDocument>(json.RootElement);
        Validate(document);
        if (!string.Equals(document.UniverseSha256, HashUniverse(directory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unscience state belongs to a different universe.xml; it was not restored.");
        return document;
    }

    public static void Write(string directory, SaveDocument document)
    {
        document.UniverseSha256 = HashUniverse(directory);
        Validate(document);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, SaveJson.Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Unscience save exceeds the 32 MiB limit.");
        string path = Path.Combine(directory, FileName);
        string temporary = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Validate(SaveDocument document)
    {
        if (document.SchemaVersion != 1) throw new InvalidDataException($"Unsupported Unscience save version {document.SchemaVersion}.");
        if (document.Features == null || document.Features.Count > 128) throw new InvalidDataException("Invalid feature table.");
        if (document.Warnings == null || document.Warnings.Count > 1024) throw new InvalidDataException("Invalid warning table.");
        foreach (var (id, feature) in document.Features)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || feature == null || feature.Version < 1
                || feature.State.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                throw new InvalidDataException($"Invalid feature record: {id}.");
        }
    }

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property '{property.Name}'.");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateUniqueProperties(child);
    }

    private static string HashUniverse(string directory)
    {
        using var stream = File.OpenRead(Path.Combine(directory, "universe.xml"));
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
