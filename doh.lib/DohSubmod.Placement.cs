using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Placement;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions;

namespace MeowSci.DohLib;

public sealed partial class DohSubmod
{
    private SpawnRequest? _placementRequest;
    private SurfaceSpawnTarget? _placementPreview;
    private bool _placementValid;
    private string _placementHint = "Point at ground or a vessel part.";
    private long _previewQueued = -1;
    private long _nextPreviewAt;

    private void InitializePlacement()
    {
        _placementCancelled = () =>
        {
            PlacementInputPatches.Release(this);
            _placementRequest = null; _placementPreview = null;
            _previewQueued = -1; _placementValid = false;
        };
        PhysicsFrameHook.BeforePhysics += PlacementFrame;
    }

    private void PlacementFrame(double dt, UniverseTime time)
    {
        if (_placement.Armed && (!_placement.IsCurrent(_placement.Generation, Universe.CurrentSystem)
            || !Program.DrawUI || Program.EditorFlag || Program.MainViewport.Mode == CameraMode.Map)) CancelPlacement();
    }

    private void ArmPlacement()
    {
        if (!PhysicsFrameHook.IsApplied || !PlacementInputPatches.IsApplied || Universe.CurrentSystem == null) return;
        CancelPlacement();
        _placementRequest = new SpawnRequest
        {
            Count = 1, VehicleName = _spawnName.ToString(),
            CharacterId = _selectedCharacterIndex >= 0 && _selectedCharacterIndex < _availableCharacters.Length
                ? _availableCharacters[_selectedCharacterIndex] : null,
            PaintPresetName = _paintPresetName, PaintSettings = _spawnPaint
        };
        _placement.Arm(Universe.CurrentSystem);
        _placementHint = "Point at ground or a vessel part. Left-click to spawn; Esc/right-click cancels.";
        _nextPreviewAt = 0;
        PlacementInputPatches.Claim(this, QueuePlacementClick, CancelPlacement);
    }

    public void RenderFloatingWindows()
    {
        if (!_placement.Armed) return;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { CancelPlacement(); return; }
        DrawPlacementPreview();
        var draw = ImGui.GetForegroundDrawList();
        var position = ImGui.GetMousePos() + new float2(18, 18);
        ImString hint = $"DOH: {_placementHint}";
        var size = ImGui.CalcTextSize(hint);
        var viewport = Program.MainViewport;
        position.X = Math.Max(viewport.Position.X + 8, Math.Min(position.X, viewport.Position.X + viewport.Size.X - size.X - 8));
        position.Y = Math.Max(viewport.Position.Y + 8, Math.Min(position.Y, viewport.Position.Y + viewport.Size.Y - size.Y - 8));
        draw.AddText(position + new float2(1, 1), ImColor8.Black, hint);
        draw.AddText(position, ImColor8.White, hint);
        if (!PlacementInputPatches.WorldViewport || ImGui.GetIO().WantCaptureMouse || _placement.Pending) return;
        long generation = _placement.Generation;
        if (_previewQueued == generation || Environment.TickCount64 < _nextPreviewAt) return;
        var point = Cursor.GetPosition(Program.MainViewport);
        _previewQueued = generation;
        _nextPreviewAt = Environment.TickCount64 + 200;
        PhysicsFrameHook.Enqueue(() =>
        {
            if (!_placement.IsCurrent(generation, Universe.CurrentSystem)) return;
            if (!Program.DrawUI || Program.EditorFlag || Program.MainViewport.Mode == CameraMode.Map)
            { CancelPlacement(); return; }
            _previewQueued = -1;
            if (_placement.Pending) return;
            EvaluatePlacement(point);
        });
    }

    private SurfaceSpawnTarget? EvaluatePlacement(float2 point)
    {
        _placementPreview = null; _placementValid = false;
        try
        {
            var camera = Program.MainViewport.GetCamera();
            var scene = new SurfaceScene(camera);
            var ray = camera.ScreenToEgoRay(point);
            var hit = scene.Pick(ray);
            if (hit == null)
            {
                _placementHint = "No solid surface within 2 km. Click again or Esc to cancel.";
                return null;
            }
            var target = new SurfaceSpawnTarget(scene, hit, SurfaceCapsule.Preview(), ray.Direction);
            _placementPreview = target;
            _placementValid = target.IsClear();
            _placementHint = _placementValid
                ? $"Spawn on {target.Description} — left-click (Esc/right-click cancels)."
                : "Blocked: the kitten capsule would collide. Choose a clearer spot.";
            return target;
        }
        catch (Exception ex)
        {
            _placementHint = ex.Message;
            return null;
        }
    }

    private void QueuePlacementClick(float2 point)
    {
        long generation = _placement.Generation;
        if (!_placement.BeginCommit(generation, Universe.CurrentSystem)) return;
        PhysicsFrameHook.Enqueue(() =>
        {
            if (!_placement.IsCurrent(generation, Universe.CurrentSystem)) return;
            if (!Program.DrawUI || Program.EditorFlag || Program.MainViewport.Mode == CameraMode.Map)
            { CancelPlacement(); return; }
            try
            {
                var target = EvaluatePlacement(point);
                if (target == null || !_placementValid || _spawner == null || _placementRequest == null)
                {
                    _placement.Finish(false);
                    return;
                }
                var result = _spawner.SpawnAtSurface(_placementRequest, target);
                ReportSpawnResult(result, n => $"Spawned {n} kitten at {target.Description}.");
                if (result.Success) CancelPlacement();
                else
                {
                    _placementHint = result.Error ?? "Spawn failed. Click again or Esc to cancel.";
                    _placement.Finish(false);
                }
            }
            catch (Exception ex)
            {
                _placement.Finish(false);
                _placementHint = ex.Message;
                LogStatus($"Surface spawn failed: {ex.Message}", true);
            }
        });
    }
}
