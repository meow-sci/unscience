using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.HotPursuitLib;

/// <summary>
/// Hot Pursuit's reusable camera manager. Every entry owns one secondary viewport lease; the
/// registry's finite pool is intentionally exposed to the user instead of being hidden behind a
/// custom renderer.
/// </summary>
public sealed partial class HotPursuitSubmod : ISubmod
{
    public string Name => "Hot Pursuit - Extra Cameras";
    public string Tooltip => "Place live, part-mounted cameras in KSA's stock secondary viewports.";

    public static HotPursuitSubmod? Instance { get; private set; }

    private readonly List<HotPursuitCamera> _cameras = new();
    public IReadOnlyList<HotPursuitCamera> Cameras => _cameras;

    private float _placementRange = 2000f;

    public void Initialize()
    {
        Instance = this;
        Console.WriteLine("hot-pursuit: initialized");
    }

    public void Update(double dt)
    {
        foreach (var entry in _cameras)
        {
            ResolveTarget(entry);
            SyncLease(entry);
            if (entry.Viewport == null)
                continue;

            var camera = entry.Viewport.BaseCamera;
            if (!entry.IsResolved)
            {
                if (camera.Following != null)
                    camera.Unfollow(changeControl: false);
                entry.Viewport.SetVisible(false);
                continue;
            }

            if (camera.Following != entry.Vehicle)
            {
                camera.SetFollow(entry.Vehicle!, tidalLocking: true,
                    changeControl: false, alert: false);
            }
            camera.SetFieldOfView(entry.FieldOfView);
            entry.Viewport.SetVisible(entry.Visible);
        }
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##hot_pursuit_content");
        ImGui.Text("Place cameras on visible vehicle parts. Each camera uses one of KSA's finite");
        ImGui.Text("preallocated secondary viewport slots; closing its stock viewport releases the slot.");
        ImGui.TextDisabled($"Free shared secondary slots: {ViewportRegistry.AvailableSecondaryCount} / 4");

        ImGui.SetNextItemWidth(-1f);
        ImGui.DragFloat("Placement range (m)##hp_range", ref _placementRange, 10f, 10f, 100000f, "%.0f");
        if (_armed)
        {
            ImGui.TextColored(new float4(1f, 0.85f, 0.2f, 1f),
                "Waiting for a world click... (Esc/right-click cancels)");
            if (ImGui.Button("Cancel placement##hp_cancel"))
                Disarm("Placement cancelled.");
        }
        else if (ImGui.Button("Arm camera placement##hp_arm"))
        {
            Arm();
        }

        if (!string.IsNullOrEmpty(_placeStatus))
            ImGui.TextColored(_placeStatusIsError
                    ? new float4(1f, 0.3f, 0.3f, 1f)
                    : new float4(0.4f, 1f, 0.4f, 1f), _placeStatus);

        ImGui.Spacing();
        ImGui.SeparatorText($"Mounted cameras ( {_cameras.Count} )");
        for (var i = 0; i < _cameras.Count; i++)
        {
            var entry = _cameras[i];
            var remove = RenderCamera(entry, i);
            if (remove)
            {
                RemoveCamera(entry);
                i--;
            }
        }

        if (_cameras.Count == 0)
            ImGui.TextDisabled("No cameras placed yet.");
        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        foreach (var entry in _cameras)
            ReleaseLease(entry);
        _cameras.Clear();
        if (ReferenceEquals(Instance, this))
            Instance = null;
        Console.WriteLine("hot-pursuit: disposed");
    }

    /// <summary>
    /// Applies a mounted pose and returns true when the viewport belongs to Hot Pursuit, telling
    /// the Harmony prefix to skip stock FixedController math for this one viewport.
    /// </summary>
    internal bool ApplyFixedPose(IViewport viewport)
    {
        foreach (var entry in _cameras)
        {
            if (!ReferenceEquals(entry.Viewport, viewport))
                continue;

            // The stock viewport window can release this lease after our previous Update. Do not
            // suppress FixedController for a slot that another owner has already reclaimed.
            if (!ViewportRegistry.TryGetOwned(entry.Owner, out var ownedViewport) ||
                !ReferenceEquals(ownedViewport, viewport))
            {
                DropViewport(entry);
                entry.LeaseLost = true;
                return false;
            }

            // FixedController runs before the StarMap update hook. Re-resolve here as well so a
            // vehicle destruction/part-failure handoff cannot leave the pose patch touching last
            // frame's disposed Part reference.
            ResolveTarget(entry);
            if (entry.IsResolved && entry.Visible)
                HotPursuitPose.TryApply(entry, viewport);
            return true;
        }
        return false;
    }

    internal bool TryOpenViewport(HotPursuitCamera entry)
    {
        try
        {
            if (!ViewportRegistry.TryClaimSecondaryViewport(entry.Owner, out var viewport))
                return false;
            entry.Viewport = viewport;
            entry.LeaseLost = false;
            ConfigureViewport(entry, viewport);
            return true;
        }
        catch (Exception ex)
        {
            ReleaseLease(entry);
            Console.WriteLine($"hot-pursuit: failed to claim viewport for camera #{entry.Id}: {ex.Message}");
            return false;
        }
    }

    internal void RemoveCamera(HotPursuitCamera entry)
    {
        if (!_cameras.Remove(entry))
            return;
        ReleaseLease(entry);
        Console.WriteLine($"hot-pursuit: removed camera #{entry.Id}");
    }

    private void SyncLease(HotPursuitCamera entry)
    {
        try
        {
            if (ViewportRegistry.TryGetOwned(entry.Owner, out var viewport))
            {
                if (!ReferenceEquals(entry.Viewport, viewport))
                {
                    entry.Viewport = viewport;
                    entry.LeaseLost = false;
                    ConfigureViewport(entry, viewport);
                }
                return;
            }

            if (entry.Viewport != null)
            {
                // GameViewport.DrawImGui releases the registry lease when the stock window closes.
                // Do not retain or use that stale reference.
                DropViewport(entry);
                entry.LeaseLost = true;
            }
        }
        catch (Exception ex)
        {
            DropViewport(entry);
            entry.LeaseLost = true;
            Console.WriteLine($"hot-pursuit: viewport lease check failed for camera #{entry.Id}: {ex.Message}");
        }
    }

    private void ConfigureViewport(HotPursuitCamera entry, IGameViewport viewport)
    {
        viewport.SetName($"Hot Pursuit Camera {entry.Id}");
        viewport.SetCameraMode(CameraMode.Fixed);
        viewport.SetResizeAllowed(true);
        viewport.RequestResize(new int2(entry.Width, entry.Height));
        viewport.SetResizeAllowed(false);
        viewport.BaseCamera.SetFieldOfView(entry.FieldOfView);
        if (entry.IsResolved)
        {
            viewport.BaseCamera.SetFollow(entry.Vehicle!, tidalLocking: true,
                changeControl: false, alert: false);
            viewport.SetVisible(entry.Visible);
        }
        else
        {
            viewport.SetVisible(false);
        }
    }

    private static void ReleaseLease(HotPursuitCamera entry)
    {
        try
        {
            ViewportRegistry.ReleaseSecondaryViewport(entry.Owner);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"hot-pursuit: failed to release camera #{entry.Id} viewport: {ex.Message}");
        }
        DropViewport(entry);
    }

    /// <summary>Forgets a viewport Hot Pursuit no longer drives and clears the celestial mirror it wrote.</summary>
    private static void DropViewport(HotPursuitCamera entry)
    {
        if (entry.Viewport is { } viewport)
            HotPursuitCelestialState.Clear(viewport.BaseCamera);
        entry.Viewport = null;
    }

    private static void ResolveTarget(HotPursuitCamera entry)
    {
        entry.Vehicle = VehicleProvider.FindVehicle(entry.VehicleId);
        entry.Part = entry.Vehicle == null ? null : FindPart(entry.Vehicle, entry.PartInstanceId);
    }

    private static Part? FindPart(Vehicle vehicle, uint instanceId)
    {
        foreach (var part in vehicle.Parts.Parts)
        {
            var found = FindPartRecursive(part, instanceId);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Part? FindPartRecursive(Part part, uint instanceId)
    {
        if (part.InstanceId == instanceId)
            return part;
        foreach (var child in part.SubParts)
        {
            var found = FindPartRecursive(child, instanceId);
            if (found != null)
                return found;
        }
        return null;
    }
}
