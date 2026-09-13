using System;
using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.KiwisMarblesLib;

public sealed partial class KiwisMarblesSubmod : ISubmod
{
    public string Name => "Kiwi's Marbles - Destroyer of Worlds";
    public string Tooltip => "Weld celestials onto one another.  For science.";

    /// <summary>Set by <see cref="Initialize"/>; read by <see cref="KiwisMarblesPatches"/>'s solver prefix.</summary>
    public static KiwisMarblesSubmod? Instance { get; private set; }

    private readonly List<CelestialWeldEntry> _welds = new();

    /// <summary>Welds removed from the UI whose orbit restore must wait for the next solver-safe window.</summary>
    private readonly List<CelestialWeldEntry> _pendingRestores = new();
    private int _pendingSourceIndex;
    private int _pendingTargetIndex;
    private float3 _pendingOffset = new(0f, 0f, 0f);
    private int _pendingOffsetScaleIndex = 1; // 0=m, 1=km, 2=Mm, 3=Gm
    private string? _weldError;
    private readonly Dictionary<int, (float3 proxy, int scaleIndex)> _weldEditState = new();
    private readonly Dictionary<int, (float lon, float lat, float radialKm, bool surfaceMode)> _weldSurfaceState = new();
    private readonly ImInputString _sourceFilter = new(128);
    private readonly ImInputString _targetFilter = new(128);

    private static readonly string[] OffsetScaleLabels = { "m", "km", "Mm", "Gm" };
    private static readonly double[] OffsetScaleFactors = { 1.0, 1_000.0, 1_000_000.0, 1_000_000_000.0 };

    public void Initialize()
    {
        Instance = this;
    }

    /// <summary>
    /// Render-loop tick. Intentionally empty: celestial mutation from here races the orbit-solver
    /// workers and gets overwritten by their staged results. All weld work happens in
    /// <see cref="UpdateBeforeVehicleSolvers"/>, driven by <see cref="KiwisMarblesPatches"/>.
    /// </summary>
    public void Update(double dt) { }

    /// <summary>
    /// Applies pending orbit restores and every active weld. Must be called from the
    /// <c>Universe.ExecuteNextVehicleSolvers</c> prefix (main thread, solvers drained, results applied,
    /// next sim step not yet queued) — see <see cref="CelestialWeldEngine"/> for the timing contract.
    /// </summary>
    public void UpdateBeforeVehicleSolvers()
    {
        if (_pendingRestores.Count > 0)
        {
            foreach (var entry in _pendingRestores)
                TryRestoreOrbit(entry);
            _pendingRestores.Clear();
        }

        if (_welds.Count == 0) return;

        List<CelestialWeldEntry>? toRemove = null;
        foreach (var weld in _welds)
        {
            if (!CelestialWeldEngine.UpdateWeld(weld))
                (toRemove ??= new List<CelestialWeldEntry>()).Add(weld);
        }

        if (toRemove == null) return;
        foreach (var weld in toRemove)
            RemoveWeld(weld);
    }

    private static void TryRestoreOrbit(CelestialWeldEntry entry)
    {
        try
        {
            CelestialWeldEngine.RestoreOrbit(entry);
            Console.WriteLine($"unscience/kiwis-marbles: Restored original orbit for {entry.Source.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience/kiwis-marbles: Failed to restore orbit for {entry.Source.Id}: {ex.Message}");
        }
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##km_content");

        RenderCreateSection();

        if (_welds.Count > 0)
        {
            ImGui.Spacing();
            ImGui.SeparatorText($"Active Welds ( {_welds.Count} )");

            CelestialWeldEntry? toRemove = null;
            for (int i = 0; i < _welds.Count; i++)
                RenderWeldSection(_welds[i], i, ref toRemove);
            if (toRemove != null)
                RemoveWeld(toRemove);
        }

        SubmodUI.EndContentArea();
    }

    private void RenderCreateSection()
    {
        ImGui.SeparatorText("Create Weld");

        var celestials = CelestialProvider.GetAllCelestials();
        var orbiters = CelestialProvider.GetAllOrbiters();

        if (celestials.Count == 0) { ImGui.TextDisabled("No celestial bodies available."); return; }
        if (orbiters.Count == 0) { ImGui.TextDisabled("No orbiters available."); return; }

        // Source and Target combos in a 2-column SizingStretchProp table
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##km_selectors", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            // Build arrays
            var celestialIds = new string[celestials.Count];
            for (int i = 0; i < celestials.Count; i++) celestialIds[i] = celestials[i].Id;
            var orbiterIds = new string[orbiters.Count];
            for (int i = 0; i < orbiters.Count; i++) orbiterIds[i] = orbiters[i].Id;

            _pendingSourceIndex = Math.Clamp(_pendingSourceIndex, 0, celestials.Count - 1);
            _pendingTargetIndex = Math.Clamp(_pendingTargetIndex, 0, orbiters.Count - 1);

            // Source row
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Source");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##kmsrc", celestialIds[_pendingSourceIndex]))
            {
                if (ImGui.IsWindowAppearing()) { ImGui.SetKeyboardFocusHere(); _sourceFilter.Clear(); }
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##kmsrcfilter", "filter..."u8, _sourceFilter);
                string srcFilterText = _sourceFilter.ToString().Trim();
                for (int i = 0; i < celestials.Count; i++)
                {
                    if (srcFilterText.Length == 0 || celestialIds[i].Contains(srcFilterText, StringComparison.OrdinalIgnoreCase))
                    {
                        bool sel = _pendingSourceIndex == i;
                        if (ImGui.Selectable(celestialIds[i], sel)) _pendingSourceIndex = i;
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SetItemTooltip("Source: the celestial body (planet or moon) that will be moved and locked\nto the target's position each frame.");

            // Target row
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Target");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##kmtgt", orbiterIds[_pendingTargetIndex]))
            {
                if (ImGui.IsWindowAppearing()) { ImGui.SetKeyboardFocusHere(); _targetFilter.Clear(); }
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##kmtgtfilter", "filter..."u8, _targetFilter);
                string tgtFilterText = _targetFilter.ToString().Trim();
                for (int i = 0; i < orbiters.Count; i++)
                {
                    if (tgtFilterText.Length == 0 || orbiterIds[i].Contains(tgtFilterText, StringComparison.OrdinalIgnoreCase))
                    {
                        bool sel = _pendingTargetIndex == i;
                        if (ImGui.Selectable(orbiterIds[i], sel)) _pendingTargetIndex = i;
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SetItemTooltip("Target: any orbiter (vehicle or another celestial) that the source will follow.");

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        var selectedSource = celestials[_pendingSourceIndex];
        var selectedTarget = orbiters[_pendingTargetIndex];

        // Surface placement helper
        if (selectedTarget is Celestial targetCelestialPreview && (IOrbiter)selectedSource != selectedTarget)
        {
            double tR = targetCelestialPreview.MeanRadius;
            double sR = selectedSource.MeanRadius;
            double surfaceDist = tR + sR;
            ImGui.Spacing();
            ImGui.TextColored(new float4(0.6f, 0.8f, 0.6f, 1f),
                $"Target r: {FormatKm(tR)}   Source r: {FormatKm(sR)}");
            ImGui.TextColored(new float4(0.6f, 0.8f, 0.6f, 1f),
                $"Surface dist: {FormatKm(surfaceDist)}");

            if (ImGui.Button(" +X ##kmsurfX"))
            {
                double s = OffsetScaleFactors[_pendingOffsetScaleIndex];
                _pendingOffset = new float3((float)(surfaceDist / s), 0f, 0f);
            }
            ImGui.SetItemTooltip("Place source on surface of target along X+ axis");
            ImGui.SameLine(0, 6);
            if (ImGui.Button(" +Y ##kmsurfY"))
            {
                double s = OffsetScaleFactors[_pendingOffsetScaleIndex];
                _pendingOffset = new float3(0f, (float)(surfaceDist / s), 0f);
            }
            ImGui.SetItemTooltip("Place source on surface of target along Y+ axis");
            ImGui.SameLine(0, 6);
            if (ImGui.Button(" +Z ##kmsurfZ"))
            {
                double s = OffsetScaleFactors[_pendingOffsetScaleIndex];
                _pendingOffset = new float3(0f, 0f, (float)(surfaceDist / s));
            }
            ImGui.SetItemTooltip("Place source on surface of target along Z+ axis");
        }

        // CCI Offset in a 2-column table row, DragFloat3 + unit combo in widget column
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##km_offset_row", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##lbl2", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget2", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("CCI Offset");
            ImGui.SetItemTooltip("CCI (Center-of-Children-Independent) offset: the 3D displacement\nfrom the target's position where the source will be placed, in the chosen unit.");
            ImGui.TableNextColumn();
            float unitComboW = ImGui.CalcTextSize("Gm    ").X + ImGui.GetStyle().FramePadding.X * 2 + 20f;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - unitComboW - ImGui.GetStyle().ItemSpacing.X);
            ImGui.DragFloat3("##kmoffset", ref _pendingOffset, 1f, 0f, 0f);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(unitComboW);
            ImGui.Combo("##kmunit", ref _pendingOffsetScaleIndex, OffsetScaleLabels, OffsetScaleLabels.Length);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        double scale = OffsetScaleFactors[_pendingOffsetScaleIndex];
        double3 computedOffset = new(_pendingOffset.X * scale, _pendingOffset.Y * scale, _pendingOffset.Z * scale);
        ImGui.TextDisabled($"= ({computedOffset.X:G5}, {computedOffset.Y:G5}, {computedOffset.Z:G5}) m");

        ImGui.Spacing();

        if ((IOrbiter)selectedSource == selectedTarget)
        {
            ImGui.TextColored(new float4(1f, 0.4f, 0.4f, 1f), "Source and target must differ.");
        }
        else
        {
            if (_weldError != null)
                ImGui.TextColored(new float4(1f, 0.4f, 0.4f, 1f), _weldError);

            if (ImGui.Button(" Create Weld ##kmweld"))
                InitiateWeld(selectedSource, selectedTarget, computedOffset);
        }
    }

    private void RenderWeldSection(CelestialWeldEntry weld, int i, ref CelestialWeldEntry? toRemove)
    {
        ImGui.PushID(i);

        string header = $"Weld {i + 1}: {weld.Source.Id} \u2192 {weld.Target.Id}##km_weld_{i}";
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.PopID();
            return;
        }

        // Garry's Torch bordered child window pattern
        var wpadX = ImGui.GetStyle().WindowPadding.X;
        float childW = ImGui.GetContentRegionAvail().X + wpadX * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - wpadX);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(20f, 10f));
        ImGui.BeginChild($"km_child_{i}", new float2(childW, 0),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar(); // WindowPadding

        // Info row
        string parentName = weld.Source.Parent?.Id ?? "unknown";
        ImGui.TextDisabled($"Source parent: {parentName}");

        // Ensure edit state exists
        if (!_weldEditState.ContainsKey(i))
        {
            int si = 1;
            double sf = OffsetScaleFactors[si];
            _weldEditState[i] = (
                new float3((float)(weld.Offset.X / sf), (float)(weld.Offset.Y / sf), (float)(weld.Offset.Z / sf)),
                si
            );
        }

        var (proxy, scaleIdx) = _weldEditState[i];
        bool targetIsCelestial = weld.Target is Celestial;

        if (targetIsCelestial && !_weldSurfaceState.ContainsKey(i))
        {
            var (initLon, initLat) = OffsetToLonLat(weld.Offset);
            _weldSurfaceState[i] = (initLon, initLat, 0f, false);
        }

        bool surfMode = targetIsCelestial && _weldSurfaceState.ContainsKey(i) && _weldSurfaceState[i].surfaceMode;
        bool newSurfMode = surfMode;

        // Offset controls section using 2-column table
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##km_weld_tbl_{i}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            if (targetIsCelestial)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Surface Mode");
                ImGui.TableNextColumn();
                ImGui.Checkbox($"##km_surf_chk_{i}", ref newSurfMode);
                ImGui.SetItemTooltip("Lock source to the surface of the target celestial body using longitude/latitude.");
            }

            if (!newSurfMode)
            {
                // Unit selector row
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Unit");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo($"##km_wunit_{i}", ref scaleIdx, OffsetScaleLabels, OffsetScaleLabels.Length))
                {
                    double newSf = OffsetScaleFactors[scaleIdx];
                    proxy = new float3((float)(weld.Offset.X / newSf), (float)(weld.Offset.Y / newSf), (float)(weld.Offset.Z / newSf));
                }

                // Offset DragFloat3 row
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Offset (x/y/z)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat3($"##km_woffset_{i}", ref proxy, 1f, 0f, 0f))
                {
                    double sf = OffsetScaleFactors[scaleIdx];
                    weld.Offset = new double3(proxy.X * sf, proxy.Y * sf, proxy.Z * sf);
                }
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        if (targetIsCelestial && newSurfMode)
        {
            var targetCel = (Celestial)weld.Target;
            double dist = targetCel.MeanRadius + weld.Source.MeanRadius;

            float curLon = _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].lon : 0f;
            float curLat = _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].lat : 0f;
            float curRadialKm = _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].radialKm : 0f;

            if (!surfMode)
            {
                var (initLon2, initLat2) = OffsetToLonLat(weld.Offset);
                curLon = initLon2; curLat = initLat2; curRadialKm = 0f;
            }

            ImGui.TextDisabled($"Surface dist: {FormatKm(dist)}");
            ImGui.TextDisabled($"  (target r: {FormatKm(targetCel.MeanRadius)} + source r: {FormatKm(weld.Source.MeanRadius)})");

            bool lonChanged = false, latChanged = false, radChanged = false;
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
            if (ImGui.BeginTable($"##km_surf_tbl_{i}", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
            {
                ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Longitude (\u00b0)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                lonChanged = ImGui.DragFloat($"##km_lon_{i}", ref curLon, 0.3f, -360f, 360f, "%.1f");

                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Latitude (\u00b0)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                latChanged = ImGui.DragFloat($"##km_lat_{i}", ref curLat, 0.3f, -360f, 360f, "%.1f");

                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Altitude (km)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                radChanged = ImGui.DragFloat($"##km_alt_{i}", ref curRadialKm, 1f, -float.MaxValue, float.MaxValue, "%.1f");

                ImGui.EndTable();
            }
            ImGui.PopStyleVar(); // CellPadding

            if (lonChanged || latChanged || radChanged || !surfMode)
            {
                double actualDist = dist + curRadialKm * 1_000.0;
                double lonRad = curLon * Math.PI / 180.0;
                double latRad = curLat * Math.PI / 180.0;
                weld.Offset = new double3(
                    actualDist * Math.Cos(latRad) * Math.Cos(lonRad),
                    actualDist * Math.Cos(latRad) * Math.Sin(lonRad),
                    actualDist * Math.Sin(latRad)
                );
                double sf2 = OffsetScaleFactors[scaleIdx];
                proxy = new float3((float)(weld.Offset.X / sf2), (float)(weld.Offset.Y / sf2), (float)(weld.Offset.Z / sf2));
            }

            _weldSurfaceState[i] = (curLon, curLat, curRadialKm, true);
        }
        else if (targetIsCelestial)
        {
            _weldSurfaceState[i] = (
                _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].lon : 0f,
                _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].lat : 0f,
                _weldSurfaceState.ContainsKey(i) ? _weldSurfaceState[i].radialKm : 0f,
                false
            );
        }

        _weldEditState[i] = (proxy, scaleIdx);

        ImGui.TextDisabled($"= ({weld.Offset.X:G5}, {weld.Offset.Y:G5}, {weld.Offset.Z:G5}) m");

        ImGui.Spacing();

        // Unweld button styled as destructive
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32((float4)KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32((float4)KSAColor.Xkcd.PaleGrey));
        if (ImGui.Button($" Unweld ##km_{i}"))
            toRemove = weld;
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.EndChild();

        ImGui.PopID();
    }

    public void Dispose()
    {
        // Best-effort restore on unload: the solver prefix is being removed, so there is no later safe
        // window. Hosts call this from [StarMapUnload] on the main thread.
        foreach (var weld in _welds)
            if (weld.OriginalOrbit != null) TryRestoreOrbit(weld);
        foreach (var entry in _pendingRestores)
            TryRestoreOrbit(entry);
        _welds.Clear();
        _pendingRestores.Clear();

        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private void InitiateWeld(Celestial source, IOrbiter target, double3 offset)
    {
        foreach (var weld in _welds)
        {
            if (weld.Source == source)
            {
                _weldError = $"{source.Id} is already welded as a source.";
                return;
            }
        }

        _weldError = null;
        _welds.Add(new CelestialWeldEntry { Source = source, Target = target, Offset = offset, OriginalOrbit = source.Orbit });
        _pendingOffset = new float3(0f, 0f, 0f);
        SortWelds();
        Console.WriteLine($"unscience/kiwis-marbles: Welded {source.Id} to {target.Id}");
    }

    private void RemoveWeld(CelestialWeldEntry entry)
    {
        int idx = _welds.IndexOf(entry);
        _welds.Remove(entry);

        // Restore is deferred to the solver-safe window (UpdateBeforeVehicleSolvers); doing it here
        // (render loop) would race the in-flight CelestialUpdateTask for this body.
        if (entry.OriginalOrbit != null)
            _pendingRestores.Add(entry);

        _weldEditState.Remove(idx);
        var shifted = new Dictionary<int, (float3, int)>();
        foreach (var kv in _weldEditState)
        {
            int newKey = kv.Key > idx ? kv.Key - 1 : kv.Key;
            shifted[newKey] = kv.Value;
        }
        _weldEditState.Clear();
        foreach (var kv in shifted)
            _weldEditState[kv.Key] = kv.Value;

        _weldSurfaceState.Remove(idx);
        var shiftedSurf = new Dictionary<int, (float, float, float, bool)>();
        foreach (var kv in _weldSurfaceState)
        {
            int newKey = kv.Key > idx ? kv.Key - 1 : kv.Key;
            shiftedSurf[newKey] = kv.Value;
        }
        _weldSurfaceState.Clear();
        foreach (var kv in shiftedSurf)
            _weldSurfaceState[kv.Key] = kv.Value;

        Console.WriteLine($"unscience/kiwis-marbles: Unwelded {entry.Source.Id} from {entry.Target.Id}");
    }

    private void SortWelds()
    {
        var sorted = CelestialWeldEngine.TopologicalSort(_welds);
        _welds.Clear();
        foreach (var w in sorted)
            _welds.Add(w);
        _weldEditState.Clear();
        _weldSurfaceState.Clear();
    }

    private static (float lon, float lat) OffsetToLonLat(double3 offset)
    {
        double len = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z);
        if (len < 1e-10) return (0f, 0f);
        double lat = Math.Asin(Math.Clamp(offset.Z / len, -1.0, 1.0)) * (180.0 / Math.PI);
        double lon = Math.Atan2(offset.Y, offset.X) * (180.0 / Math.PI);
        return ((float)lon, (float)lat);
    }

    private static string FormatKm(double meters)
    {
        if (meters >= 1e9) return $"{meters / 1e9:G4} Gm";
        if (meters >= 1e6) return $"{meters / 1e6:G4} Mm";
        return $"{meters / 1e3:G4} km";
    }
}
