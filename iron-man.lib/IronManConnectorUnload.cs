using System;
using System.Collections.Generic;
using System.Linq;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Converts authored connections to stock part endpoints before persistence hooks leave.</summary>
public static class IronManConnectorUnload
{
    private sealed record Link(Part.Connection Connection, Part.Connection.IConnector OriginalOne,
        Part.Connection.IConnector OriginalTwo, Part.Connection.IConnector One, Part.Connection.IConnector Two);

    public static bool PrepareForUnload()
    {
        var roots = IronManConnectors.GetOwnedRoots();
        if (roots.Length == 0) return true;
        var links = new List<Link>();
        var seen = new HashSet<Part.Connection>();
        var replacements = new List<Part.Connection>();
        var disconnected = new List<Link>();
        try
        {
            JobSystems.VehicleSolver.Wait();
            foreach (var root in roots)
            foreach (var anchor in IronManConnectors.GetOwned(root))
            {
                var connection = anchor.Connection;
                if (connection == null || !seen.Add(connection)) continue;
                var one = connection.Connectors[0];
                var two = connection.Connectors[1];
                var replacementOne = StockEndpoint(one);
                var replacementTwo = StockEndpoint(two);
                // Both wildcard part endpoints cannot retain bulk-fluid capability in stock KSA.
                if (replacementOne is Part && replacementTwo is Part &&
                    connection.HasCapabilities(ConnectorCapability.BulkFluid))
                    throw new InvalidOperationException("A bulk-fuel link between two custom anchors cannot be converted to stock surface endpoints.");
                links.Add(new Link(connection, one, two, replacementOne, replacementTwo));
            }
            foreach (var link in links)
            {
                link.Connection.Disconnect();
                disconnected.Add(link);
                if (!Part.Connection.Connect(link.One, link.Two))
                    throw new InvalidOperationException("A stock surface replacement connection was refused.");
                replacements.Add(link.One.ConnectionPart.Connections.Single(c =>
                    (ReferenceEquals(c.Connectors[0], link.One) && ReferenceEquals(c.Connectors[1], link.Two)) ||
                    (ReferenceEquals(c.Connectors[1], link.One) && ReferenceEquals(c.Connectors[0], link.Two))));
            }
            // A root whose deserialization aborted has no tree (KSA 5482+) and therefore no links.
            foreach (var tree in roots.Select(root => root.Tree).OfType<PartTree>().Distinct()) tree.RecomputeAllDerivedData();
            if (roots.Any(root => IronManConnectors.GetOwned(root).Any(anchor => anchor.Connection != null)))
                throw new InvalidOperationException("An Iron Man anchor unexpectedly remained connected during unload.");
            foreach (var root in roots) IronManConnectors.RemoveAll(root);
            Console.WriteLine($"iron-man: converted {links.Count} attachment links to stock endpoints before unload.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"iron-man: cannot safely remove persistence hooks: {ex.Message}");
            foreach (var replacement in replacements) replacement.Disconnect();
            foreach (var link in disconnected)
                if (!Part.Connection.Connect(link.OriginalOne, link.OriginalTwo))
                    Console.WriteLine("iron-man: ERROR restoring an original anchor connection after unload conversion failed.");
            foreach (var tree in disconnected.Select(link => link.OriginalOne.ConnectionPart.Tree).OfType<PartTree>().Distinct())
            {
                try { tree.RecomputeAllDerivedData(); }
                catch (Exception restoreError) { Console.WriteLine($"iron-man: ERROR refreshing restored anchor links: {restoreError.Message}"); }
            }
            return false;
        }
    }

    private static Part.Connection.IConnector StockEndpoint(Part.Connection.IConnector endpoint) =>
        endpoint is Part.Connector anchor && IronManConnectors.IsOwned(anchor) ? anchor.Parent : endpoint;
}
