using System;
using System.Collections.Generic;
using System.Reflection;
using Brutal.GlfwApi;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.WingWalkerLib;

/// <summary>Scoped rendering support for native crew in the two external-seat templates.</summary>
public static partial class WingWalkerPatches
{
    private static readonly List<(MethodBase Target, MethodInfo Patch)> Patches = new();
    private static bool _renderErrorLogged;
    private static bool _missingIdleLogged;
    [ThreadStatic] private static IVASeat? _externalSeat;

    public static bool IsApplied { get; private set; }
    public static string? LastError { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        LastError = null;
        try
        {
            RequireField("_seatedIdleAnim", typeof(AnimationAssetRef));
            RequireField("_groundIdleAnim", typeof(AnimationAssetRef));
            RequireField("_characterAvatar", typeof(CharacterAvatar));
            RequireField("_catEarAnim", typeof(CatEarAnim));
            RequireField("_catConditionAnim", typeof(CatConditionAnim));
            Add(harmony, Required(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.ClearBuckets), typeof(IViewport)),
                postfix: nameof(AfterClearBuckets));
            Add(harmony, Required(typeof(IVASeat), nameof(IVASeat.GetSeatedModelPointAsmb),
                    typeof(float3).MakeByRefType(), typeof(double)), postfix: nameof(AdjustStandingOrigin));
            Add(harmony, Required(typeof(KittenRenderable), nameof(KittenRenderable.UpdateRenderData),
                    typeof(IViewport), typeof(int), typeof(double), typeof(float4x4), typeof(double3), typeof(double3),
                    typeof(LocomotionState).MakeByRefType(), typeof(KittenAnimInputs).MakeByRefType()),
                prefix: nameof(BeginExternalAppearance), finalizer: nameof(FinishExternalAppearance));
            Add(harmony, Required(typeof(IVASeat), nameof(IVASeat.UpdateSeatedRenderable),
                    typeof(IViewport), typeof(int), typeof(double), typeof(double4x4).MakeByRefType()),
                prefix: nameof(BeginSeat), finalizer: nameof(FinishSeat));
            Add(harmony, Required(typeof(KittenEva), nameof(KittenEva.BoardVehicle), typeof(Vehicle), typeof(EVADoor)),
                prefix: nameof(BeginBoarding), finalizer: nameof(FinishBoarding));
            Add(harmony, Required(typeof(Vehicle), nameof(Vehicle.AddCrew), typeof(KeyHash), typeof(string), typeof(string)),
                prefix: nameof(AddCrewToBoardingSeat));
            Add(harmony, Required(typeof(VehicleEditor), nameof(VehicleEditor.OnMouseButton),
                    typeof(GlfwWindow), typeof(GlfwMouseButton), typeof(GlfwButtonAction), typeof(GlfwModifier)),
                prefix: nameof(BeginEditorInteraction), postfix: nameof(FinishEditorInteraction));
            Add(harmony, Required(typeof(PartCloner), "Gather", typeof(Part), typeof(uint)),
                prefix: nameof(BeforeEditorClone));
            IsApplied = true;
            Console.WriteLine("wing-walker: helmeted external crew and immediate standing posture ready");
        }
        catch (Exception ex)
        {
            Remove(harmony);
            LastError = ex.Message;
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        IsApplied = false;
        for (int i = Patches.Count - 1; i >= 0; i--)
            harmony.Unpatch(Patches[i].Target, Patches[i].Patch);
        Patches.Clear();
        _externalSeat = null;
        StandingBodies.Clear();
        _boardingTarget = null;
        _renderErrorLogged = false;
        _missingIdleLogged = false;
        LastError = null;
    }

    private static MethodInfo Required(Type type, string name, params Type[] args) =>
        AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);

    private static void RequireField(string name, Type fieldType)
    {
        FieldInfo field = AccessTools.Field(typeof(KittenRenderable), name)
            ?? throw new MissingFieldException(typeof(KittenRenderable).FullName, name);
        if (field.FieldType != fieldType)
            throw new InvalidOperationException($"KittenRenderable.{name} has an unsupported type.");
    }

    private static void Add(Harmony harmony, MethodInfo target, string? prefix = null,
        string? postfix = null, string? finalizer = null)
    {
        MethodInfo? before = Find(prefix), after = Find(postfix), finish = Find(finalizer);
        // Record before installation so rollback also handles a partially installed patch set.
        foreach (MethodInfo? patch in new[] { before, after, finish })
            if (patch != null) Patches.Add((target, patch));
        harmony.Patch(target, prefix: Wrap(before), postfix: Wrap(after), finalizer: Wrap(finish));
    }

    private static MethodInfo? Find(string? name) => name == null ? null
        : AccessTools.Method(typeof(WingWalkerPatches), name)
            ?? throw new MissingMethodException(typeof(WingWalkerPatches).FullName, name);
    private static HarmonyMethod? Wrap(MethodInfo? method) => method == null ? null : new HarmonyMethod(method);

    private static void BeginSeat(IVASeat __instance, out IVASeat? __state)
    {
        __state = _externalSeat;
        _externalSeat = ExternalCrewParts.IsExternal(__instance) ? __instance : null;
    }

    private static void FinishSeat(IVASeat? __state) => _externalSeat = __state;

    private static void AdjustStandingOrigin(IVASeat __instance, in float3 __0, ref double3 __result)
    {
        if (!ExternalCrewParts.IsStanding(__instance)) return;
        // Keep PositionAsmb as the true eye for IVA cameras. Correct the model origin for
        // standing height, including native bone/portrait queries outside the draw callback.
        double3 forward = double3.Normalize(__instance.ForwardAxisAsmb);
        double3 right = double3.Cross(double3.Normalize(-__instance.UpAxisAsmb), forward).Normalized();
        double3 up = -double3.Cross(forward, right);
        __result -= up * (ExternalCrewParts.StandingEyeHeight + __0.Z);
    }
}
