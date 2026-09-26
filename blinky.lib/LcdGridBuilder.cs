using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.BlinkyLib;

/// <summary>
/// Builds an LCD pixel grid of engine parts on a vehicle at runtime.
/// Each pixel position gets two engine parts (a/b pair) following blinken's naming convention
/// <c>pixel_{gridName}_{row}_{col}_{a|b}</c>, so that <see cref="PixelGrid.ScanFromVehicle"/> can be reused directly.
///
/// All pixel parts are attached as children of the vehicle's root part via manual
/// <c>TreeParent</c>/<c>TreeChildren</c> assignment.  The <c>PartTree</c> is rebuilt once
/// at the end with <c>PartTree.CreateFromNewPartTree()</c>, avoiding the per-part
/// <c>RecomputeAllDerivedData()</c> cost that <c>PartTree.Merge()</c> would trigger.
/// </summary>
public static class LcdGridBuilder
{
    /// <summary>
    /// Builds a grid of engine parts on the vehicle and returns a fully initialised
    /// <see cref="BlinkyPixelGrid"/>. Returns null on failure (e.g. unknown part template,
    /// no root part, or all parts failed to merge).
    /// </summary>
    public static BlinkyPixelGrid? BuildGrid(Vehicle vehicle, string gridName, LcdGridConfig config)
    {
        if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
        if (config == null) throw new ArgumentNullException(nameof(config));

        var swTotal = Stopwatch.StartNew();
        var timings = new List<(string Label, long Ms)>();

        // ── Find attachment root ─────────────────────────────────────────────────
        var root = vehicle.Parts.Root;
        if (root == null)
        {
            Console.WriteLine("blinky: vehicle has no root part — cannot build grid");
            return null;
        }

        // ── Lookup PartTemplate ──────────────────────────────────────────────────
        // NOTE: TryGet<PartTemplate> is NOT supported by ModLibrary — only Get<PartTemplate> works.
        // Get throws NullReferenceException if the id is unknown, so we catch that.
        PartTemplate? template;
        var sw = Stopwatch.StartNew();
        try
        {
            template = ModLibrary.Get<PartTemplate>(config.EnginePartId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"blinky: PartTemplate '{config.EnginePartId}' not found in ModLibrary: {ex.Message}");
            return null;
        }
        timings.Add(("ModLibrary.Get<PartTemplate>", sw.ElapsedMilliseconds));

        // ── Find all fuel-carrying parts to use as connection anchors ──────────────
        // PERFORMANCE: With N pixel engines all connected to 1 fuel part, each
        // ResourceManager's PopulateGraph DFS traverses all N+1 nodes using
        // O(N) List.Contains visited checks → O(N²) per engine → O(N³) total.
        // By distributing engines across K fuel parts (round-robin), each DFS
        // only sees N/K nodes → O(N/K)² per engine → O(N³/K²) total.
        // With K=4 fuel parts on a 1444-engine grid: 3B → ~188M ops (~16× faster).
        sw.Restart();
        var fuelParts = FindAllFuelParts(vehicle);
        timings.Add(($"FindAllFuelParts ({fuelParts.Count} found)", sw.ElapsedMilliseconds));
        if (fuelParts.Count > 0)
            Console.WriteLine($"blinky: found {fuelParts.Count} fuel part(s) for partitioned connections: {string.Join(", ", fuelParts.Select(p => p.Id))}");
        else
            Console.WriteLine("blinky: WARNING — no fuel parts found; engines may not fire");

        Console.WriteLine($"blinky: building {config.Width}x{config.Height} {config.Layout} grid using '{config.EnginePartId}' — {config.TotalParts} total parts");

        var createdParts = new List<Part>(config.TotalParts);

        // ── Part instantiation — new Part() + position/rotation/scale ───────────
        sw.Restart();
        for (int row = 0; row < config.Height; row++)
        {
            for (int col = 0; col < config.Width; col++)
            {
                var partA = CreatePixelPartInstance(template, gridName, row, col, "a", config);
                var partB = CreatePixelPartInstance(template, gridName, row, col, "b", config);
                if (partA != null) createdParts.Add(partA);
                if (partB != null) createdParts.Add(partB);
            }
        }
        timings.Add(($"Part instantiation ({createdParts.Count} parts)", sw.ElapsedMilliseconds));

        if (createdParts.Count == 0)
        {
            Console.WriteLine("blinky: no parts were successfully created");
            return null;
        }

        // ── Tree wiring — TreeParent / TreeChildren ──────────────────────────────
        sw.Restart();
        foreach (var part in createdParts)
        {
            part.TreeParent = root;
            root.TreeChildren.Add(part);
        }
        timings.Add(($"Tree wiring ({createdParts.Count} parts)", sw.ElapsedMilliseconds));

        // ── Fuel connections — round-robin across all fuel parts ─────────────────
        // Each engine is assigned to exactly one fuel part (by index mod K).
        // This creates K isolated subgraphs of ~N/K engines each, reducing the
        // O(N³) PopulateGraph cost to O(N³/K²).
        //
        // Stage alignment: pixel parts default to stage 0. KSA's resource managers
        // use FlowRule.NearestToFurtherestSameStage, so they only find tanks whose
        // stage matches the engine's stage. We set each pixel part's stage to match
        // its fuel anchor before the PartTree rebuild so resource managers are
        // created with correct stage filtering.
        if (fuelParts.Count > 0)
        {
            sw.Restart();
            int fedParts = 0;
            for (int i = 0; i < createdParts.Count; i++)
            {
                var fuelPart = fuelParts[i % fuelParts.Count];
                createdParts[i].SetStage(fuelPart.Stage);
                if (ConnectToFuel(createdParts[i], fuelPart))
                    fedParts++;
            }
            Console.WriteLine($"blinky: stage-aligned {createdParts.Count} pixel parts to stage {fuelParts[0].Stage} (fuel anchor: '{fuelParts[0].Id}')");
            Console.WriteLine($"blinky: fuel-fed {fedParts}/{createdParts.Count} pixel parts via their declared feed connectors");
            if (fedParts < createdParts.Count)
                Console.WriteLine($"blinky: WARNING — {createdParts.Count - fedParts} pixel part(s) got no propellant feed and will never light");
            timings.Add(($"Fuel connections + stage align ({createdParts.Count} parts, {fuelParts.Count} anchors)", sw.ElapsedMilliseconds));
        }

        // ── SetMinimumThrottle — iterate EngineControllers ──────────────────────
        // Must run BEFORE the tree rebuild: RecomputeRocketControls folds every MinimumThrottle into
        // PartTree.EngineThrottleMin when the new tree's derived data is computed (lazily since KSA 5482,
        // by the next frame's flush at the latest), and that is what clamps the vehicle's manual throttle.
        sw.Restart();
        SetMinimumThrottle(createdParts, 0.0001f);
        timings.Add(("SetMinimumThrottle", sw.ElapsedMilliseconds));

        // ── PartTree rebuild — CreateFromNewPartTree ─────────────────────────────
        // Walks full TreeChildren hierarchy from root, registers all modules/states,
        // and calls RecomputeAllDerivedData exactly once.
        sw.Restart();
        vehicle.Parts = PartTree.CreateFromNewPartTree(root);
        timings.Add(("PartTree.CreateFromNewPartTree", sw.ElapsedMilliseconds));

        // ── UpdateVehicleConfiguration — reinitialise solver state + resync FlightComputer ──
        // UpdateAfterPartTreeModification alone only calls FlightComputer.ReadUpdatedVehicleConfiguration,
        // which rebuilds VehicleConfig.Gimbals from the new PartTree. But
        // _threadWorkerUpdateState (which holds FlightComputerOutput, including the
        // Gimbals StateList accessed by UpdateTvcParams on the worker thread) is not
        // resized for the new gimbal count. UpdateVehicleConfiguration calls
        // _threadWorkerUpdateState.InitializeUpdateData() first, which reallocates all
        // output state lists to match the new PartTree, then calls
        // UpdateAfterPartTreeModification. Without this the solver crashes with
        // ArgumentOutOfRangeException in UpdateTvcParams on the first frame after build.
        sw.Restart();
        vehicle.UpdateVehicleConfiguration();
        timings.Add(("UpdateVehicleConfiguration", sw.ElapsedMilliseconds));

        // ── Propellant-feed verification ─────────────────────────────────────────
        // KSA 5482 recomputes derived tree data lazily (normally at the next frame's flush), so
        // resource managers do not exist yet. Build the resource groups now, as 5438 did eagerly,
        // or every pixel engine reports no reachable tank.
        sw.Restart();
        vehicle.Parts.EnsureDerived(DerivedData.ResourceGroups);
        VerifyPropellantFeeds(createdParts);
        timings.Add(("VerifyPropellantFeeds", sw.ElapsedMilliseconds));

        // ── PixelGrid.ScanFromVehicle ────────────────────────────────────────────
        sw.Restart();
        var pixelGrid = PixelGrid.ScanFromVehicle(vehicle, gridName);
        timings.Add(($"PixelGrid.ScanFromVehicle ({pixelGrid.Count} pairs)", sw.ElapsedMilliseconds));
        if (pixelGrid.Count == 0)
            Console.WriteLine("blinky: WARNING — PixelGrid scan found 0 pixel pairs after creation");

        // ── RefreshEngineControllers ─────────────────────────────────────────────
        sw.Restart();
        pixelGrid.RefreshEngineControllers();
        timings.Add(("RefreshEngineControllers", sw.ElapsedMilliseconds));

        // ── Print timing summary ─────────────────────────────────────────────────
        swTotal.Stop();
        Console.WriteLine($"blinky: BuildGrid timing ({config.Width}x{config.Height}, {createdParts.Count} parts, total={swTotal.ElapsedMilliseconds}ms):");
        foreach (var (label, ms) in timings)
            Console.WriteLine($"blinky:   {label,-45} {ms,6}ms");

        return new BlinkyPixelGrid(pixelGrid, createdParts);
    }

    /// <summary>
    /// Removes pixel parts from the vehicle.
    /// Works for both owned (blinky-built) and scanned (save-loaded) grids.
    /// Uses manual tree unlinking + single <c>CreateFromNewPartTree</c> rebuild,
    /// mirroring the BuildGrid approach to avoid N per-part <c>RecomputeAllDerivedData</c> calls.
    /// </summary>
    public static void DestroyGrid(Vehicle vehicle, BlinkyPixelGrid grid)
    {
        if (vehicle == null || grid == null) return;

        var partsToRemove = CollectGridParts(grid);
        if (partsToRemove.Count == 0) return;

        int partCount = partsToRemove.Count;
        Console.WriteLine($"blinky: destroying grid — removing {partCount} parts (owned={grid.IsOwned})");
        var swTotal = Stopwatch.StartNew();
        var timings = new List<(string Label, long Ms)>();
        var sw = Stopwatch.StartNew();

        // ── Disconnect fuel connections ──────────────────────────────────────────
        foreach (var part in partsToRemove)
        {
            foreach (var conn in part.Connections.ToArray())
            {
                try { conn.Disconnect(); } catch { }
            }
        }
        timings.Add(($"Disconnect fuel connections ({partCount} parts)", sw.ElapsedMilliseconds));

        // ── Manually unlink all pixel parts from the tree ────────────────────────
        sw.Restart();
        foreach (var part in partsToRemove)
        {
            var parent = part.TreeParent;
            if (parent != null)
            {
                parent.TreeChildren.Remove(part);
                part.TreeParent = null;
            }
        }
        timings.Add(($"Tree unlink ({partCount} parts)", sw.ElapsedMilliseconds));

        // ── Rebuild vehicle PartTree once from the now-clean root ────────────────
        // CreateFromNewPartTree walks only the remaining (non-pixel) tree children,
        // so the pixel parts are simply gone, and recompute runs exactly once.
        var root = vehicle.Parts.Root;
        sw.Restart();
        vehicle.Parts = PartTree.CreateFromNewPartTree(root);
        timings.Add(("PartTree.CreateFromNewPartTree", sw.ElapsedMilliseconds));

        // ── UpdateVehicleConfiguration — reinitialise solver state + resync FlightComputer ──
        // Same reason as BuildGrid — must use UpdateVehicleConfiguration (not just
        // UpdateAfterPartTreeModification) so _threadWorkerUpdateState output state lists
        // are resized for the reduced gimbal count after pixel parts are removed.
        sw.Restart();
        vehicle.UpdateVehicleConfiguration();
        timings.Add(("UpdateVehicleConfiguration", sw.ElapsedMilliseconds));

        swTotal.Stop();
        Console.WriteLine($"blinky: DestroyGrid timing ({partCount} parts, total={swTotal.ElapsedMilliseconds}ms):");
        foreach (var (label, ms) in timings)
            Console.WriteLine($"blinky:   {label,-45} {ms,6}ms");
    }

    /// <summary>
    /// Re-wires an existing grid's pixel parts into the vehicle's propellant network and
    /// forces the resource managers to be rebuilt.
    ///
    /// Grids that were discovered by scanning — after a save/load, or built by an older
    /// blinky that used a bare part-to-part connection the game now rejects — have no
    /// declared feed connection, so their engines can never reach propellant and stay dark
    /// however often they are activated. This gives them the same wiring
    /// <see cref="BuildGrid"/> produces, without rebuilding the part tree.
    /// </summary>
    /// <returns>The number of pixel parts that now hold a declared feed connection.</returns>
    public static int RepairFuelFeeds(Vehicle vehicle, BlinkyPixelGrid grid)
    {
        if (vehicle == null || grid == null) return 0;

        var parts = CollectGridParts(grid);
        if (parts.Count == 0)
        {
            Console.WriteLine("blinky: repair — grid holds no pixel parts");
            return 0;
        }

        var fuelParts = FindAllFuelParts(vehicle);
        if (fuelParts.Count == 0)
        {
            Console.WriteLine("blinky: repair — vehicle has no fuel parts; the grid cannot be fed");
            return 0;
        }

        var sw = Stopwatch.StartNew();
        int fed = 0;
        int rewired = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (HasDeclaredFeedConnection(part))
            {
                fed++;
                continue;
            }

            var fuelPart = fuelParts[i % fuelParts.Count];
            part.SetStage(fuelPart.Stage);
            if (ConnectToFuel(part, fuelPart))
            {
                fed++;
                rewired++;
            }
        }

        // PartTree.RecreateResourceManagers is internal to the game; CalculateStages is the
        // public entry point that calls it, so the new connections are picked up.
        vehicle.Parts.ResourceGroupList.CalculateStages();

        Console.WriteLine($"blinky: repair — rewired {rewired} part(s), {fed}/{parts.Count} now hold a declared feed connection ({sw.ElapsedMilliseconds}ms)");
        VerifyPropellantFeeds(parts);
        return fed;
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns every Part making up a grid — the owned parts for a blinky-built grid,
    /// or the scanned a/b pairs for a grid discovered on an existing vehicle.
    /// </summary>
    private static List<Part> CollectGridParts(BlinkyPixelGrid grid)
    {
        var parts = new List<Part>();
        if (grid.IsOwned)
        {
            parts.AddRange(grid.OwnedParts);
        }
        else
        {
            foreach (var (a, b) in grid.Grid.Grid.Values)
            {
                parts.Add(a);
                parts.Add(b);
            }
        }
        return parts;
    }

    /// <summary>True when any declared propellant feed connector of the part is already connected.</summary>
    private static bool HasDeclaredFeedConnection(Part part)
    {
        foreach (var connector in GetFeedConnectors(part))
        {
            if (connector.Connection != null)
                return true;
        }
        return false;
    }


    /// <summary>
    /// Creates and configures a single pixel engine part (position, rotation, scale).
    /// Does NOT wire it into any tree — caller handles <c>TreeParent</c>/<c>TreeChildren</c>.
    /// </summary>
    private static Part? CreatePixelPartInstance(
        PartTemplate template, string gridName, int row, int col, string slot, LcdGridConfig config)
    {
        try
        {
            string partId = $"pixel_{gridName}_{row}_{col}_{slot}";
            var part = new Part(partId, template);

            double px, py, pz;
            double rotAngle; // Y-axis rotation angle in radians

            if (config.Layout == GridLayout.Cylinder)
            {
                // Cylinder: columns wrap around circumference, rows stack along Y.
                // Circumference = Width * Spacing  →  radius = (Width * Spacing) / (2π)
                double radius = (config.Width * config.Spacing) / (2.0 * System.Math.PI);
                double theta = col * 2.0 * System.Math.PI / config.Width;

                px = config.OffsetX + radius * System.Math.Sin(theta);
                py = config.OffsetY - row * config.Spacing;
                pz = config.OffsetZ + radius * System.Math.Cos(theta);

                // Engine a/b fire radially (in/out from cylinder centre).
                // Flat case uses ±π/2; cylinder adds θ to rotate with the surface.
                rotAngle = slot == "b" ? theta + System.Math.PI / 2.0 : theta - System.Math.PI / 2.0;
            }
            else
            {
                // Flat: columns along +X, rows down along -Y.
                px = config.OffsetX + col * config.Spacing;
                py = config.OffsetY - row * config.Spacing;
                pz = config.OffsetZ;

                // a rotates Y=-90°, b rotates Y=+90° (nozzles fire ±X, cancelling thrust).
                rotAngle = slot == "b" ? System.Math.PI / 2.0 : -System.Math.PI / 2.0;
            }

            part.PositionParentAsmb = new double3(px, py, pz);

            double halfAngle = rotAngle / 2.0;
            part.Asmb2ParentAsmb = new doubleQuat(0, System.Math.Sin(halfAngle), 0, System.Math.Cos(halfAngle));

            // Scale down to match blinken's convention (blinken XML uses Scale=0.1).
            part.Scale = new double3(config.PartScale, config.PartScale, config.PartScale);

            return part;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"blinky: error creating pixel_{gridName}_{row}_{col}_{slot}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Collects ALL parts on the vehicle that carry Tank modules (fuel/oxidizer),
    /// preferring non-sub-parts. Used for round-robin partitioned fuel connections
    /// to reduce ResourceManager.PopulateGraph cost from O(N³) to O(N³/K²).
    /// </summary>
    private static List<Part> FindAllFuelParts(Vehicle vehicle)
    {
        var result = new List<Part>();
        foreach (var part in vehicle.Parts.Parts)
        {
            if (part.IsSubPart) continue;
            var tanks = part.SubtreeModules.Get<Tank>();
            if (tanks.Length > 0)
                result.Add(part);
        }
        // Fallback: accept sub-parts if nothing found at top level
        if (result.Count == 0)
        {
            foreach (var part in vehicle.Parts.Parts)
            {
                var tanks = part.SubtreeModules.Get<Tank>();
                if (tanks.Length > 0)
                    result.Add(part);
            }
        }
        return result;
    }

    /// <summary>
    /// Wires a pixel engine part into the vehicle's propellant network so
    /// <c>ResourceManager.PopulateGraph()</c> can reach the fuel tanks.
    ///
    /// The connection MUST sit on one of the engine's own <b>declared feed connectors</b>.
    /// KSA's <c>ResourceManager.CanFlowAcross</c> rejects the first hop out of the consumer
    /// part unless that connection is on a connector named by the part template's
    /// <c>ConsumerFeedWiring</c>/<c>FeedsFrom</c> wiring (<c>IsDeclaredFeedConnection</c>),
    /// and the base <c>CanFlowAcross</c> additionally requires the connection to carry the
    /// combustor's plumbing capability (<c>BulkFluid</c> for these liquid engines).
    ///
    /// A bare part-to-part connection satisfies neither. <c>Part.EndpointCapabilities</c> is
    /// null for anything that is not a fuel-port part, so such a connection resolves to
    /// <c>Electricity | ServiceFluid</c> and is not a declared feed connection either — which
    /// is why grids wired the old way added mass but never received propellant and so never lit.
    ///
    /// The fuel side stays a plain <c>Part</c> endpoint: <c>Part.CanConnect()</c> is always
    /// true, so a single tank can anchor every pixel in the grid, and its null endpoint
    /// capability intersects to the engine connector's own capabilities.
    /// </summary>
    /// <returns>True when at least one declared feed connector was connected.</returns>
    private static bool ConnectToFuel(Part pixelPart, Part fuelPart)
    {
        try
        {
            bool connected = false;
            foreach (var connector in GetFeedConnectors(pixelPart))
            {
                if (!connector.CanConnect()) continue;

                if (Part.Connection.Connect(connector, fuelPart))
                    connected = true;
                else
                    Console.WriteLine($"blinky: Connection.Connect returned false for '{pixelPart.Id}' connector '{connector.Id}'");
            }

            if (!connected)
                Console.WriteLine($"blinky: '{pixelPart.Id}' has no free declared feed connector — it will reach no propellant");

            return connected;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"blinky: error connecting '{pixelPart.Id}' to fuel: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Collects the distinct propellant feed connectors declared by every rocket core on the
    /// part. <c>RocketCore.FeedConnectors</c> is bound during part construction from the
    /// template's <c>FeedsFrom</c> wiring; cores on the same part commonly share one connector
    /// (e.g. EngineA3's thrust chamber and gas generator both feed from <c>_connector3</c>),
    /// so duplicates are filtered out.
    /// </summary>
    private static List<Part.Connector> GetFeedConnectors(Part part)
    {
        var result = new List<Part.Connector>();
        var cores = part.SubtreeModules.Get<RocketCore>();
        for (int i = 0; i < cores.Length; i++)
        {
            var feeds = cores[i].FeedConnectors;
            for (int j = 0; j < feeds.Length; j++)
            {
                if (!result.Contains(feeds[j]))
                    result.Add(feeds[j]);
            }
        }
        return result;
    }

    /// <summary>
    /// Scans an existing vehicle for engine parts that match a blinky grid (e.g., after save/load
    /// when Part.Id names are lost). Identifies pixel parts by template ID and small scale, then
    /// reconstructs the grid layout from spatial analysis of part positions.
    /// </summary>
    public static BlinkyPixelGrid? ScanExistingGrid(Vehicle vehicle, string gridName, string engineTemplateId)
    {
        if (vehicle == null) return null;

        Console.WriteLine($"blinky: scanning for existing grid (template={engineTemplateId})...");

        // 1. Collect candidate parts: match template and small scale (blinky uses ~0.01)
        var candidates = new List<Part>();
        foreach (var part in PartHelpers.GetAllParts(vehicle))
        {
            if (part.Template?.Id != engineTemplateId) continue;
            double maxScale = Math.Max(Math.Max(part.Scale.X, part.Scale.Y), part.Scale.Z);
            if (maxScale >= 0.5) continue;
            candidates.Add(part);
        }

        Console.WriteLine($"blinky: found {candidates.Count} candidate parts (template={engineTemplateId}, scale<0.5)");
        if (candidates.Count < 2) return null;

        // 2. Group by position proximity (a/b pairs share the same position)
        var used = new HashSet<int>();
        var pairs = new List<(Part first, Part second)>();

        for (int i = 0; i < candidates.Count; i++)
        {
            if (used.Contains(i)) continue;
            var posI = candidates[i].PositionParentAsmb;

            for (int j = i + 1; j < candidates.Count; j++)
            {
                if (used.Contains(j)) continue;
                var posJ = candidates[j].PositionParentAsmb;
                double dx = posI.X - posJ.X;
                double dy = posI.Y - posJ.Y;
                double dz = posI.Z - posJ.Z;
                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (dist < 0.1)
                {
                    pairs.Add((candidates[i], candidates[j]));
                    used.Add(i);
                    used.Add(j);
                    break;
                }
            }
        }

        Console.WriteLine($"blinky: grouped into {pairs.Count} position pairs");
        if (pairs.Count == 0) return null;

        // 3. Determine grid layout from positions
        //    Rows: unique Y values sorted descending (higher Y = row 0)
        //    Cols: unique X (flat) or angle (cylinder) sorted ascending
        var uniqueY = pairs
            .Select(p => Math.Round(p.first.PositionParentAsmb.Y, 1))
            .Distinct().OrderByDescending(y => y).ToList();

        var uniqueZ = pairs
            .Select(p => Math.Round(p.first.PositionParentAsmb.Z, 1))
            .Distinct().ToList();
        bool isCylinder = uniqueZ.Count > 1;

        Func<Part, double> colKeyFn;
        if (isCylinder)
        {
            double centerX = pairs.Average(p => p.first.PositionParentAsmb.X);
            double centerZ = pairs.Average(p => p.first.PositionParentAsmb.Z);
            colKeyFn = p => Math.Round(Math.Atan2(
                p.PositionParentAsmb.X - centerX,
                p.PositionParentAsmb.Z - centerZ), 3);
        }
        else
        {
            colKeyFn = p => Math.Round(p.PositionParentAsmb.X, 1);
        }

        var uniqueCol = pairs.Select(p => colKeyFn(p.first)).Distinct().OrderBy(c => c).ToList();

        // 4. Build grid dictionary
        var gridDict = new Dictionary<(int row, int col), (Part a, Part b)>();

        foreach (var (first, second) in pairs)
        {
            int row = uniqueY.IndexOf(Math.Round(first.PositionParentAsmb.Y, 1));
            int col = uniqueCol.IndexOf(colKeyFn(first));

            if (row >= 0 && col >= 0)
                gridDict[(row, col)] = (first, second);
        }

        if (gridDict.Count == 0) return null;

        var pixelGrid = PixelGrid.BuildFromPartGroups(gridDict);
        return new BlinkyPixelGrid(pixelGrid, new List<Part>());
    }

    /// <summary>
    /// Post-rebuild sanity check. A pixel can only light if its combustor's
    /// <c>ResourceManager</c> ended up with a non-empty <c>ConsumptionOrder</c> — otherwise
    /// <c>Combustor.ComputePropellantAvailable</c> returns false forever and activating the
    /// engine does nothing at all. Logging it here turns a silently dark grid into a
    /// diagnosable console message.
    /// </summary>
    private static void VerifyPropellantFeeds(List<Part> parts)
    {
        int fedParts = 0;
        var starvedCores = new Dictionary<string, int>();
        string desiredMix = "?";

        foreach (var part in parts)
        {
            bool anyFed = false;
            var cores = part.SubtreeModules.Get<RocketCore>();
            for (int i = 0; i < cores.Length; i++)
            {
                if (cores[i] is not Combustor combustor) continue;

                if (desiredMix == "?") desiredMix = combustor.DesiredMix?.Name ?? "?";

                if (PropellantFeedDiagnostics.CountTanks(combustor.ResourceManager) > 0)
                    anyFed = true;
                else
                    starvedCores[combustor.TemplateId] = starvedCores.GetValueOrDefault(combustor.TemplateId) + 1;
            }
            if (anyFed) fedParts++;
        }

        Console.WriteLine($"blinky: propellant feed check — {fedParts}/{parts.Count} pixel parts reached at least one tank");
        if (fedParts == 0)
            Console.WriteLine($"blinky: WARNING — no pixel can light. The engines burn '{desiredMix}'; the vehicle's tanks must carry it, and the pixel parts must share the tanks' stage.");
        else if (fedParts < parts.Count)
            Console.WriteLine("blinky: WARNING — starved pixel parts will never light; check stage alignment and tank contents");

        foreach (var (coreId, count) in starvedCores)
            Console.WriteLine($"blinky:   starved core '{coreId}' x{count} (expected for gas-generator cores burning a propellant the vehicle does not carry)");
    }

    /// <summary>Sets MinimumThrottle on all EngineControllers of the given parts.</summary>
    private static void SetMinimumThrottle(List<Part> parts, float minThrottle)
    {
        int count = 0;
        foreach (var part in parts)
        {
            var controllers = part.SubtreeModules.Get<EngineController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i].MinimumThrottle = minThrottle;
                count++;
            }
        }
        Console.WriteLine($"blinky: set MinimumThrottle={minThrottle} on {count} engine controllers");
    }
}
