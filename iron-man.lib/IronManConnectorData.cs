using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Brutal.Numerics;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Uses the existing serialized instance Id, which stock template lookups do not use.</summary>
internal sealed class IronManConnectorData
{
    internal const string Marker = "iron-man:v1:";
    private const string MarkerFamily = "iron-man:v";
    public string OriginalId { get; set; } = "";
    public int StockConnectorCount { get; set; }
    public List<AnchorData> Anchors { get; set; } = new();

    public sealed class AnchorData
    {
        public string Name { get; set; } = "";
        public double[] Position { get; set; } = Array.Empty<double>();
        public double[] Direction { get; set; } = Array.Empty<double>();
        public double Radius { get; set; }
    }

    internal static string Capture(Part part)
    {
        var owned = IronManConnectors.GetOwned(part);
        int stockCount = part.Connectors.Count - owned.Count;
        // Connector references serialize by index. Preserve the stock prefix and owned suffix exactly.
        if (stockCount != part.Template.Connectors.Count ||
            part.Connectors.Take(stockCount).Any(IronManConnectors.IsOwned) ||
            part.Connectors.Skip(stockCount).Any(c => !IronManConnectors.IsOwned(c)))
            throw new InvalidOperationException("Iron Man connector ordering changed; saving this kitten would lose attachment references.");
        var data = new IronManConnectorData { OriginalId = part.Id, StockConnectorCount = stockCount };
        foreach (var connector in owned)
        {
            double3 direction = double3.UnitX.Transform(connector.Asmb2ParentAsmb);
            IronManConnectors.Validate(connector.PositionParentAsmb, direction, connector.Scale.Y);
            data.Anchors.Add(new AnchorData
            {
                Name = connector.Id,
                Position = ToArray(connector.PositionParentAsmb),
                Direction = ToArray(direction),
                Radius = connector.Scale.Y,
            });
        }
        string marker = Marker + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(data));
        _ = Decode(marker, part.Template); // Refuse to write anything the loader would reject.
        return marker;
    }

    internal static IronManConnectorData? Decode(string? id, PartTemplate template)
    {
        if (id == null || !id.StartsWith(MarkerFamily, StringComparison.Ordinal)) return null;
        try
        {
            if (!id.StartsWith(Marker, StringComparison.Ordinal))
                throw new InvalidDataException("Unsupported Iron Man anchor save version.");
            if (id.Length > 32000 || template.Id != IronManConnectors.BackpackTemplateId)
                throw new InvalidDataException("Iron Man anchor data is invalid for this part.");
            var data = JsonSerializer.Deserialize<IronManConnectorData>(Convert.FromBase64String(id[Marker.Length..]))
                ?? throw new InvalidDataException("Empty Iron Man anchor data.");
            if (data.OriginalId == null || data.OriginalId.Length > 1024 || data.OriginalId.StartsWith(MarkerFamily, StringComparison.Ordinal) ||
                data.StockConnectorCount != template.Connectors.Count || data.Anchors == null ||
                data.Anchors.Count == 0 || data.Anchors.Count > IronManConnectors.MaximumAnchors)
                throw new InvalidDataException("Iron Man anchor data or the stock connector layout has changed.");
            foreach (var anchor in data.Anchors)
            {
                if (anchor == null || string.IsNullOrWhiteSpace(anchor.Name) || anchor.Name.Length > 80)
                    throw new InvalidDataException("Invalid Iron Man anchor name.");
                IronManConnectors.Validate(ToVector(anchor.Position), ToVector(anchor.Direction), anchor.Radius);
            }
            return data;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or FormatException or InvalidDataException)
        {
            // Never continue to stock connector-index regeneration after a damaged marked save.
            throw new InvalidDataException("Cannot load this Iron Man kitten: its saved anchors are invalid. " + ex.Message, ex);
        }
    }

    internal void Restore(Part part)
    {
        foreach (var anchor in Anchors)
            IronManConnectors.Add(part, anchor.Name, ToVector(anchor.Position), ToVector(anchor.Direction), anchor.Radius);
    }

    private static double[] ToArray(double3 v) => new[] { v.X, v.Y, v.Z };

    private static double3 ToVector(double[]? values)
    {
        if (values == null || values.Length != 3) throw new InvalidDataException("An anchor vector must contain three coordinates.");
        return new double3(values[0], values[1], values[2]);
    }
}
