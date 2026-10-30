using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using Brutal.Numerics;

namespace KSA;

public static class Double3Ex
{
    public static double3 Transform(this double3 value, doubleQuat rotation) => double3.Transform(value, rotation);
}

// Minimal managed equivalents of the researched 5402 constructor/serialization seams, with the
// KSA 5707 connector storage: a fixed Connector[] and a lazily allocated connection list whose
// empty value is shared between parts. Production connector code and Harmony patches are linked
// unchanged into this runner.
[Flags]
public enum ConnectorCapabilityFlags { None = 0, BulkFluid = 1 }
[Flags] public enum ConnectorCapability { None = 0, BulkFluid = 1 }

public static class JobSystems
{
    public static readonly Worker VehicleSolver = new();
    public sealed class Worker { public int Waits; public void Wait() => Waits++; }
}
public sealed class PartTree
{
    public int Recomputes;
    public void RecomputeAllDerivedData() => Recomputes++;
}

public sealed class TransformReference
{
    public double3 PositionValue;
    public doubleQuat RotationValue = doubleQuat.Identity;
    public double3 ScaleValue = double3.One;
}

public readonly struct ScaleFactors(double3 axes)
{
    public readonly double Scale = Math.Max(axes.X, Math.Max(axes.Y, axes.Z));
}

public sealed class PartTemplate
{
    public static readonly Dictionary<string, PartTemplate> Library = new();
    public string Id = "KittenBackPackPart";
    public List<Part.Connector.TemplateBase> Connectors = new();
}

public sealed class PartInstance
{
    [XmlAttribute] public string Id { get; set; } = "";
    [XmlAttribute] public string InstanceOf { get; set; } = "";
    public List<int> ConnectedIndices = new();
    public PartTemplate GetTemplate() => PartTemplate.Library[InstanceOf];
}

public sealed class Part : Part.Connection.IConnector
{
    public string Id;
    public PartTemplate Template;
    public bool IsSubPart;
    public double3 Scale = double3.One;
    private static readonly List<Connection> SharedEmptyConnections = new();
    private List<Connection>? _connections;
    public Connector[] Connectors = Array.Empty<Connector>();
    public List<Connection> Connections => _connections ?? SharedEmptyConnections;
    public PartTree Tree = new();
    public Part ConnectionPart => this;
    public bool CanConnect() => true;
    public void AddConnection(Connection connection) => EnsureConnections().Add(connection);
    public void RemoveConnection(Connection connection) => _connections?.Remove(connection);
    internal List<Connection> EnsureConnections() => _connections ??= new List<Connection>();

    // KSA 5707 constructor chaining: save load, the public 4-argument constructor and PartCloner
    // copies all reach the private constructor; only it may decode the Iron Man marker.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Part(PartInstance inInstance, Part? parent)
        : this(inInstance.Id, inInstance.GetTemplate(), inInstance, parent, null, 0, 0, 0) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Part(string inName, PartTemplate inTemplate, PartInstance? inInstance = null, Part? parent = null)
        : this(inName, inTemplate, inInstance, parent, null, 0, 0, 0) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Part(Part source, Part? parent)
        : this(null, source.Template, null, parent, new TransformReference { ScaleValue = source.Scale }, 0, 0, 0, source) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Part(string? inName, PartTemplate inTemplate, PartInstance? inInstance, Part? parent, TransformReference? transformReference,
        int stage, int sequenceGroup, int sequenceOrder, Part? boundsSource = null, bool isTemplateSubPart = false)
    {
        Id = string.IsNullOrEmpty(inName) ? inTemplate.Id : inName;
        Template = inTemplate;
        if (transformReference != null) Scale = transformReference.ScaleValue;
        if (inTemplate.Connectors.Count > 0)
        {
            Connectors = new Connector[inTemplate.Connectors.Count];
            for (int i = 0; i < Connectors.Length; i++) Connectors[i] = new Connector(inTemplate.Connectors[i], this);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public PartInstance GetReferenceWithChildren(ref uint localRunningId) => GetReferenceWithChildren(ref localRunningId, null, false);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal PartInstance GetReferenceWithChildren(ref uint localRunningId, PartInstance? parent, bool includeSymmetry = false)
    {
        var result = new PartInstance { InstanceOf = Template.Id };
        for (int i = 0; i < Connectors.Length; i++)
            if (Connectors[i].Connection != null) result.ConnectedIndices.Add(i);
        return result;
    }

    public sealed class Connection
    {
        public static bool FailNextSurfaceConnection;
        public interface IConnector
        {
            Part ConnectionPart { get; }
            bool CanConnect();
            void AddConnection(Connection connection);
            void RemoveConnection(Connection connection);
        }
        public IConnector[] Connectors;
        private Connection(IConnector one, IConnector two) { Connectors = new[] { one, two }; }
        public static bool Connect(IConnector one, IConnector two)
        {
            if (FailNextSurfaceConnection && one is Part) { FailNextSurfaceConnection = false; return false; }
            if (!one.CanConnect() || !two.CanConnect()) return false;
            var connection = new Connection(one, two);
            one.AddConnection(connection); two.AddConnection(connection);
            return true;
        }
        public void Disconnect() { Connectors[0].RemoveConnection(this); Connectors[1].RemoveConnection(this); }
        public bool HasCapabilities(ConnectorCapability capability) =>
            (Connectors[0] is Connector || Connectors[1] is Connector) &&
            (Connectors[0] is not Connector one || one.Capabilities == ConnectorCapabilityFlags.BulkFluid) &&
            (Connectors[1] is not Connector two || two.Capabilities == ConnectorCapabilityFlags.BulkFluid);
    }

    public sealed class Connector : Connection.IConnector
    {
        public sealed class TemplateBase
        {
            public string Id = "";
            public TransformReference Transform = new();
            public ConnectorCapabilityFlags Capabilities;
        }
        public string Id;
        public Part Parent;
        public Connection? Connection;
        public double3 PositionParentAsmb;
        public doubleQuat Asmb2ParentAsmb;
        public double3 Scale;
        public ConnectorCapabilityFlags Capabilities;
        public readonly TemplateBase Authored;
        public Part ConnectionPart => Parent;
        public bool CanConnect() => Connection == null;
        public void AddConnection(Connection connection) { Connection = connection; Parent.EnsureConnections().Add(connection); }
        public void RemoveConnection(Connection connection) { if (Connection == connection) Connection = null; Parent.RemoveConnection(connection); }

        public Connector(TemplateBase template, Part part)
        {
            Id = template.Id;
            Parent = part;
            Authored = template;
            Capabilities = template.Capabilities;
            PositionParentAsmb = template.Transform.PositionValue;
            Asmb2ParentAsmb = template.Transform.RotationValue;
            Scale = template.Transform.ScaleValue;
        }
    }
}
