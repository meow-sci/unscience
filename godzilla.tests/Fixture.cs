using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace KSA
{
    using Brutal.Numerics;
    public class Part
    {
        public Part? PartParent;
        public ModuleList Modules = new();
        public doubleQuat Asmb2ParentAsmb = doubleQuat.Identity;
        public doubleQuat Asmb2VehicleAsmb => PartParent == null ? Asmb2ParentAsmb : Asmb2ParentAsmb.Concatenate(PartParent.Asmb2VehicleAsmb);
        public double3 ScaleTotal => PartParent == null ? Scale : Scale * PartParent.ScaleTotal;
        public double4x4 Matrix => double4x4.CreateScale(Scale) * double4x4.CreateFromQuaternion(Asmb2ParentAsmb) * double4x4.CreateTranslation(PositionParentAsmb) * (PartParent?.Matrix ?? double4x4.Identity);
        public double3 PositionVehicleAsmb => Matrix.Translation;
        public double3 Scale = new(1);
        public double3 PositionParentAsmb;
        public List<Part> SubParts = new();
        public int Invalidations, Refreshes, Bounds;
        public void ResetCachedPosMatrixValues() => Invalidations++;
        public void RefreshScale()
        {
            Refreshes++;
            var scale = new ScaleFactors(ScaleTotal);
            foreach (var collider in Modules.Get<ColliderModule>()) collider.SetScale(in scale);
            foreach(var p in SubParts) p.RefreshScale();
        }
        public void UpdateBounds() => Bounds++;
    }
    public class PartTree
    {
        public ModuleList Modules = new();
        public Vehicle? OwningVehicle;
        public List<Part> Parts = new();
        public int Refreshes;
        public double4x4 RenderMatrix;
        public int Draws;
        public bool ThrowOnDraw;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void UpdateRenderData(ref readonly double4x4 matrixAsmb2Ego, bool isEditedVehicle, IViewport viewport, int frameIndex)
        {
            RenderMatrix = matrixAsmb2Ego;
            Draws++;
            if (ThrowOnDraw) throw new InvalidOperationException("test draw failure");
        }
        public void RecomputeAllDerivedData() => Refreshes++;
    }
    public class Vehicle
    {
        public Vehicle() { Parts.OwningVehicle = this; }
        private readonly PhysicsStates _physics = new();
        public PhysicsStates GetPhysicsStatesMutable() => _physics;
        public int ColliderRebuilds;
        public bool ThrowOnColliderRebuild;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void UpdateCollisionGeometry()
        {
            ColliderRebuilds++;
            var size = 1d;
            foreach (var collider in Parts.Modules.Get<ColliderModule>())
            {
                size = Math.Max(size, collider.PositionVehicleAsmb.Length() + collider.AppliedScale);
                collider.NeedsColliderUpdate = false;
            }
            _physics.Props.BoundingSphereRadiusBody = (float)size;
            _physics.Props.BoundingBoxAsmb = new BepuPhysics.Collidables.Box((float)size, (float)size, (float)size);
            _physics.Props.GeometricCenterAsmb = new float3((float)size);
            if (ThrowOnColliderRebuild) throw new InvalidOperationException("test collider rebuild failure");
        }
        public bool IsDisposed;
        public virtual double MeanRadius { [MethodImpl(MethodImplOptions.NoInlining)] get => 1; }
        public double4x4 GetMatrixAsmb2Ego(Camera camera) =>
            double4x4.CreateTranslation(camera.Position - CenterOfMassAsmb);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual void UpdateRenderData(IViewport viewport, int inFrameIndex)
        {
            var camera = viewport.GetCamera();
            if (camera.GetObjectDiameterPixels(2 * MeanRadius, camera.Position.Length()) < 1) return;
            var matrix = GetMatrixAsmb2Ego(camera);
            Parts.UpdateRenderData(in matrix, false, viewport, inFrameIndex);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public float4x4? GetWorldMatrix(Camera camera)
        {
            if (camera.GetObjectDiameterPixels(2 * MeanRadius, camera.Position.Length()) < 1) return null;
            return float4x4.CreateTranslation(float3.Pack(camera.Position));
        }
        public double3 CenterOfMassAsmb;
        public PartTree Parts = new();
        public int Refreshes;
        public void UpdateAfterPartTreeModification()
        {
            Refreshes++;
            if (Parts.Modules.Get<ColliderModule>().Length != 0) UpdateCollisionGeometry();
        }
    }
    public interface IViewport { Camera GetCamera(); }
    public sealed class TestViewport : IViewport
    {
        public Camera Camera = new();
        public Camera GetCamera() => Camera;
    }
    public class Camera
    {
        public double3 Position = new(100, 200, 300);
        public double PixelFactor = 1;
        public double GetObjectDiameterPixels(double diameter, double distance) => diameter * PixelFactor;
    }
    public class KittenEva : Vehicle { public KittenRenderable Renderable = new(); }
    public class KittenRenderable { public CharacterAvatar Avatar = new(); public float3 Correction; }
    public class CharacterAvatar { public CharacterCore Core; }
    public struct CharacterCore { public float Scale; }
}
namespace MeowSci.KsaAbstractions
{
    public static class ReflectionHelpers
    {
        public static object GetFieldValue(KSA.KittenRenderable r, string field) => r.Avatar;
    }
}
namespace MeowSci.GarrysTorchLib
{
    public static class KittenScalePatches
    {
        public static void SetScale(KSA.KittenRenderable r, Brutal.Numerics.float3 f) => r.Correction=f;
    }
}
