using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Instance-owned attachment points; shared game templates are never changed.</summary>
public static class IronManConnectors
{
    public const int MaximumAnchors = 16;
    public const string BackpackTemplateId = "KittenBackPackPart";
    private static readonly ConditionalWeakTable<Part.Connector, Part.Connector.TemplateBase> Owned = new();
    private static readonly List<WeakReference<Part>> Owners = new();

    public static bool IsOwned(Part.Connector connector) => Owned.TryGetValue(connector, out _);

    public static IReadOnlyList<Part.Connector> GetOwned(Part root) => root.Connectors.Where(IsOwned).ToArray();

    public static void EnsureDefaults(Part root)
    {
        if (GetOwned(root).Count != 0) return;
        // The game authors the kitten with feet at the origin and up along -Z.
        Add(root, "Up", new double3(0, 0, -0.43), -double3.UnitZ, 0.12);
        Add(root, "Down", new double3(0, 0, -0.10), double3.UnitZ, 0.12);
    }

    public static Part.Connector Add(Part root, string name, double3 position, double3 direction, double radius)
    {
        if (root.Template.Id != BackpackTemplateId || root.IsSubPart)
            throw new InvalidOperationException("Iron Man anchors require a kitten backpack root.");
        if (GetOwned(root).Count >= MaximumAnchors)
            throw new InvalidOperationException($"A kitten can have at most {MaximumAnchors} Iron Man anchors.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            throw new ArgumentException("Give the connector a name of 1–80 characters.", nameof(name));
        Validate(position, direction, radius);
        var template = new Part.Connector.TemplateBase
        {
            Id = name,
            // Bulk fuel plus the stock defaults (service fluid and electricity).
            Capabilities = ConnectorCapabilityFlags.BulkFluid,
        };
        var connector = new Part.Connector(template, root);
        Owned.Add(connector, template);
        WriteTransform(connector, template, position, direction, radius);
        root.Connectors.Add(connector);
        if (GetOwned(root).Count == 1) Owners.Add(new WeakReference<Part>(root));
        return connector;
    }

    public static bool Update(Part.Connector connector, double3 position, double3 direction, double radius)
    {
        if (connector.Connection != null || !Owned.TryGetValue(connector, out var template)) return false;
        Validate(position, direction, radius);
        WriteTransform(connector, template, position, direction, radius);
        return true;
    }

    public static bool Remove(Part.Connector connector)
    {
        if (connector.Connection != null || !IsOwned(connector)) return false;
        if (!connector.Parent.Connectors.Remove(connector)) return false;
        Owned.Remove(connector);
        return true;
    }

    public static bool RemoveAll(Part root)
    {
        var connectors = GetOwned(root);
        if (connectors.Any(c => c.Connection != null)) return false;
        foreach (var connector in connectors) Remove(connector);
        return true;
    }

    internal static Part[] GetOwnedRoots()
    {
        var roots = new HashSet<Part>();
        for (int i = Owners.Count - 1; i >= 0; i--)
        {
            if (!Owners[i].TryGetTarget(out var root) || GetOwned(root).Count == 0) Owners.RemoveAt(i);
            else roots.Add(root);
        }
        return roots.ToArray();
    }

    internal static void Validate(double3 position, double3 direction, double radius)
    {
        if (!Finite(position) || Math.Abs(position.X) > 100 || Math.Abs(position.Y) > 100 || Math.Abs(position.Z) > 100)
            throw new ArgumentException("Connector position must be finite and within 100 metres of the kitten.");
        if (!Finite(direction) || direction.LengthSquared() < 1e-12 || direction.LengthSquared() > 1e12)
            throw new ArgumentException("Connector direction must be a finite, nonzero vector.");
        if (!double.IsFinite(radius) || radius < 0.005 || radius > 10)
            throw new ArgumentException("Connector radius must be between 0.005 and 10 metres.");
    }

    private static bool Finite(double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private static void WriteTransform(Part.Connector connector, Part.Connector.TemplateBase template,
        double3 position, double3 direction, double radius)
    {
        direction /= Math.Sqrt(direction.LengthSquared());
        double dot = Math.Clamp(direction.X, -1, 1);
        double3 axis = double3.Cross(double3.UnitX, direction);
        doubleQuat rotation = axis.LengthSquared() < 1e-12
            ? (dot >= 0 ? doubleQuat.Identity : doubleQuat.CreateFromAxisAngle(double3.UnitZ, Math.PI))
            : doubleQuat.CreateFromAxisAngle(axis / Math.Sqrt(axis.LengthSquared()), Math.Acos(dot));
        double parentScale = new ScaleFactors(connector.Parent.Scale).Scale;
        if (!double.IsFinite(parentScale) || parentScale <= 0)
            throw new InvalidOperationException("The kitten scale must be positive and finite.");
        template.Transform.PositionValue = position / parentScale;
        template.Transform.RotationValue = rotation;
        template.Transform.ScaleValue = new double3(radius / parentScale);
        connector.PositionParentAsmb = position;
        connector.Asmb2ParentAsmb = rotation;
        connector.Scale = new double3(radius);
    }
}
