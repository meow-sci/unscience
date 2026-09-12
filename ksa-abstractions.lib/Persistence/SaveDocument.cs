using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MeowSci.KsaAbstractions.Persistence;

public sealed class SaveDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string UniverseSha256 { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, SaveFeature> Features { get; set; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; set; } = new();
}

public sealed class SaveFeature
{
    public int Version { get; set; } = 1;
    public JsonElement State { get; set; }
}
