using System;
using System.Linq;
using System.Text.Json;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.PebblesLib;

/// <summary>Grid memory carry-over decisions and the `pebbles.clutter-state` sidecar record (KSA 5482 displacement/native saves).</summary>
internal static class ClutterStateChecks
{
    private const string Rocks = "Rocks";

    public static void Run()
    {
        Keys();
        Masks();
        Displaced();
        GraphSwapScenario();
        SaveLoadScenario();
        Validation();
        SidecarRoundTrip();
        Console.WriteLine("PASS: Pebbles grid memory (AND masks, displaced carry-over/authority, per-spacing isolation, seed/export) and clutter-state sidecar validation/round trip.");
    }

    private static void Keys()
    {
        Check(ClutterGridMemory.Key(Rocks, 10) == "Rocks|10", "grid keys use invariant round-trip separation");
        Check(ClutterGridMemory.Key(Rocks, 0.1) != ClutterGridMemory.Key(Rocks, 0.1 + 1e-12), "grids at different exact spacings stay distinct");
        Check(Grid(10).GridKey() == ClutterGridMemory.Key(Rocks, 10), "state keys match memory keys");
    }

    private static void Masks()
    {
        var memory = new ClutterGridMemory();
        memory.Remember(Grid(10, Cell(1, 2, 0, 3)), authoritative: false);
        memory.Remember(GridCells(10, Cell(1, 2, 0, 37), new ClutterCellMask { X = 1, Y = 2, Face = 0 }), authoritative: true);
        var cell = memory.Get(Grid(10).GridKey())!.Cells.Single();
        Check(!Included(cell, 3) && !Included(cell, 37) && Included(cell, 4), "masks merge with AND; an all-included cell never resurrects a removal");
        var copy = memory.Get(Grid(10).GridKey())!;
        copy.Cells[0].Words[0] = uint.MaxValue;
        Check(!Included(memory.Get(Grid(10).GridKey())!.Cells[0], 3), "snapshots are detached from memory");
        Check(memory.Get(ClutterGridMemory.Key(Rocks, 20)) == null, "an unseen grid has no state to replay");
    }

    private static void Displaced()
    {
        var memory = new ClutterGridMemory();
        var key = Grid(10).GridKey();
        memory.Remember(Grid(10, displaced: [Moving(0, 1, 2, 7), Moving(0, 1, 2, 8)]), authoritative: false);
        Check(!Included(memory.Get(key)!.Cells.Single(), 7), "a displaced record always keeps its resting slot excluded");
        memory.Remember(Grid(10), authoritative: false);
        Check(memory.Get(key)!.Displaced.Count == 2, "a fresh non-authoritative placement (renderer recreation) cannot erase remembered records");
        var moved = Moving(0, 1, 2, 7); moved.Position = [9, 9, 9]; moved.Settled = true;
        memory.Remember(Grid(10, displaced: [moved]), authoritative: true);
        var state = memory.Get(key)!;
        Check(state.Displaced.Count == 1 && state.Displaced[0].Position[0] == 9 && state.Displaced[0].Settled, "an authoritative live grid replaces displaced records with its current poses");
        Check(!Included(state.Cells.Single(), 8), "a record dropped by the live grid (destroyed) stays excluded");
    }

    /// <summary>Stock 10 m → override 25 m → stock → override 25 m, as ClutterController.Apply/RestoreState drive the memory.</summary>
    private static void GraphSwapScenario()
    {
        var memory = new ClutterGridMemory();
        var stockKey = Grid(10).GridKey(); var overrideKey = Grid(25).GridKey();
        // Apply: remember the stock placement (not yet replayed into, so non-authoritative), replay 25 m: nothing known.
        memory.Remember(Grid(10, Cell(4, 4, 1, 12), displaced: [Moving(4, 4, 1, 13)]), authoritative: false);
        Check(memory.Get(overrideKey) == null, "stock subcells are never reinterpreted on another spacing");
        // Restore: the live 25 m override is authoritative; stock gets its own mask and displaced record back.
        memory.Remember(Grid(25, Cell(8, 8, 2, 100)), authoritative: true);
        var stock = memory.Get(stockKey)!;
        Check(stock.Displaced.Single().SubCell == 13 && !Included(stock.Cells.Single(), 12), "returning to stock restores its removed and displaced clutter");
        // Re-apply 25 m: its earlier removal returns; stock is untouched.
        Check(!Included(memory.Get(overrideKey)!.Cells.Single(), 100), "returning to an override spacing restores that grid's removals");
        Check(memory.Keys.Count == 2, "each spacing keeps separate state");
    }

    /// <summary>Native saves own stock grids; the sidecar exports and seeds only override grids.</summary>
    private static void SaveLoadScenario()
    {
        var live = new ClutterGridMemory();
        live.Remember(Grid(10, Cell(1, 1, 0, 1)), authoritative: false);
        live.Remember(Grid(25, Cell(2, 2, 0, 2), displaced: [Moving(2, 2, 0, 3)]), authoritative: true);
        live.Remember(Grid(30), authoritative: true);
        var stockKeys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { Grid(10).GridKey() };
        var exported = live.Export(stockKeys);
        Check(exported.Count == 1 && exported[0].Separation == 25, "export skips native-owned stock grids and empty grids");
        // Load: native state lands in stock first; seeding must not overwrite it but adds the override grid.
        var loaded = new ClutterGridMemory();
        loaded.Remember(Grid(10, Cell(1, 1, 0, 1)), authoritative: false);
        var added = loaded.Seed(exported.Append(Grid(10, Cell(5, 5, 0, 5))));
        Check(added.SequenceEqual([Grid(25).GridKey()]), "seeding adds unknown grids only; known (native) grids win");
        Check(loaded.Get(Grid(10).GridKey())!.Cells.Count == 1, "saved stale stock state is not merged over native state");
        Check(loaded.Get(Grid(25).GridKey())!.Displaced.Single().SubCell == 3, "override displaced records survive the save round trip");
    }

    private static void Validation()
    {
        ClutterGridValidation.Validate([Body("Earth", Grid(25, Cell(1, 1, 0, 1), displaced: [Moving(1, 1, 0, 2)]))]);
        ClutterGridValidation.Validate([]);
        Reject(() => ClutterGridValidation.Validate([Body("Earth"), Body("Earth")]));
        Reject(() => ClutterGridValidation.Validate([Body(" ")]));
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(25), Grid(25))]));
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(0))]));
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(double.NaN))]));
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(25, new ClutterCellMask { Face = 6 }))]));
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(25, new ClutterCellMask { Words = new uint[7] }))]));
        Reject(() => ClutterGridValidation.Validate([Body("Earth", GridCells(25, Cell(1, 1, 0, 1), Cell(1, 1, 0, 2)))]));
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(25, displaced: [Moving(0, 0, 0, 256)]))]));
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(25, displaced: [Moving(0, 0, 0, 1), Moving(0, 0, 0, 1)]))]));
        var badScale = Moving(0, 0, 0, 1); badScale.ScaleId = ClutterGridValidation.Scales;
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(25, displaced: [badScale]))]));
        var badRotation = Moving(0, 0, 0, 1); badRotation.Rotation = [0, 0, 1];
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(25, displaced: [badRotation]))]));
        var badVelocity = Moving(0, 0, 0, 1); badVelocity.Velocity = [0, double.PositiveInfinity, 0];
        Reject(() => ClutterGridValidation.Validate([Body("Earth", Grid(25, displaced: [badVelocity]))]));
        Reject(() => ClutterGridValidation.Validate(Enumerable.Range(0, ClutterGridValidation.MaxBodies + 1).Select(i => Body("B" + i)).ToArray()));
    }

    /// <summary>Through the production SaveParticipant/SaveJson path used by PebblesSubmod.Saves.</summary>
    private static void SidecarRoundTrip()
    {
        var saved = new[] { Body("Earth", Grid(25, Cell(3, 4, 5, 255), displaced: [Moving(3, 4, 5, 9)])) };
        ClutterBodyState[]? restored = null;
        var participant = new SaveParticipant<ClutterBodyState[]>("pebbles.clutter-state", () => saved, () => { }, (value, _) => restored = value, 61, ClutterGridValidation.Validate);
        var element = participant.CaptureState();
        participant.PrepareRestore(element, new SaveRestoreContext(_ => { }))();
        Check(restored != null && JsonSerializer.Serialize(restored, SaveJson.Options) == JsonSerializer.Serialize(saved, SaveJson.Options), "clutter state round trips through the sidecar serializer");
        Check(!element.GetRawText().Contains("GridKey") && !element.GetRawText().Contains("HasContent"), "derived members are not persisted");
        var corrupt = JsonDocument.Parse(element.GetRawText().Replace("\"Face\": 5", "\"Face\": 9")).RootElement;
        Reject(() => participant.PrepareRestore(corrupt, new SaveRestoreContext(_ => { })));
    }

    private static ClutterGridState GridCells(double separation, params ClutterCellMask[] cells) => new() { Ecotype = Rocks, Separation = separation, Cells = cells.ToList() };
    private static ClutterGridState Grid(double separation, ClutterCellMask? cell = null, ClutterDisplacedState[]? displaced = null)
        => new() { Ecotype = Rocks, Separation = separation, Cells = cell == null ? [] : [cell], Displaced = displaced?.ToList() ?? [] };
    private static ClutterBodyState Body(string id, params ClutterGridState[] grids) => new() { BodyId = id, Grids = grids.ToList() };
    private static ClutterCellMask Cell(int x, int y, int face, int removedSubCell)
    {
        var cell = new ClutterCellMask { X = x, Y = y, Face = face };
        cell.Words[removedSubCell / 32] &= ~(1u << (removedSubCell % 32));
        return cell;
    }
    private static ClutterDisplacedState Moving(int x, int y, int face, uint subCell)
        => new() { X = x, Y = y, Face = face, SubCell = subCell, ObjectId = 1, ScaleId = 2, Position = [1, 2, 3], RestPosition = [1, 2, 2.5], Velocity = [0, -1, 0] };
    private static bool Included(ClutterCellMask cell, int subCell) => (cell.Words[subCell / 32] & (1u << (subCell % 32))) != 0;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid Pebbles clutter state accepted."); }
}
