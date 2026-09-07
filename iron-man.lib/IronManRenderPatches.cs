using System;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Submit EVA equipment before the part GPU batch is uploaded; keep the avatar's late draw.</summary>
internal static class IronManRenderPatches
{
    private static bool _applied;
    private static Action<Vehicle, IViewport, int>? _submitParts;
    [ThreadStatic] private static Vehicle? _earlyVehicle;

    internal static void Apply(Harmony harmony)
    {
        if (_applied) return;
        var updateVehicle = AccessTools.DeclaredMethod(typeof(Vehicle), nameof(Vehicle.UpdateRenderData))
            ?? throw new MissingMethodException(typeof(Vehicle).FullName, nameof(Vehicle.UpdateRenderData));
        var updateBatch = AccessTools.Method(typeof(PartModelRenderer), nameof(PartModelRenderer.UpdateRenderData))
            ?? throw new MissingMethodException(typeof(PartModelRenderer).FullName, nameof(PartModelRenderer.UpdateRenderData));
        // A nonvirtual call enters the live patched Vehicle method, preserving other mods'
        // prefixes (notably I Feel Seen). A reverse patch would freeze/bypass those patches;
        // an ordinary C# call would dispatch into KittenEva and advance its animation twice.
        var method = new DynamicMethod("IronManSubmitVehicleParts", typeof(void),
            new[] { typeof(Vehicle), typeof(IViewport), typeof(int) }, typeof(IronManRenderPatches), true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, updateVehicle);
        il.Emit(OpCodes.Ret);
        _submitParts = method.CreateDelegate<Action<Vehicle, IViewport, int>>();
        harmony.Patch(updateVehicle, prefix: new HarmonyMethod(typeof(IronManRenderPatches), nameof(VehiclePrefix))
        {
            priority = Priority.First,
        });
        harmony.Patch(updateBatch, prefix: new HarmonyMethod(typeof(IronManRenderPatches), nameof(BeforePartUpload)));
        _applied = true;
    }

    internal static void Remove(Harmony harmony)
    {
        _applied = false;
        _submitParts = null;
        Unpatch(harmony, typeof(Vehicle), nameof(Vehicle.UpdateRenderData), nameof(VehiclePrefix));
        Unpatch(harmony, typeof(PartModelRenderer), nameof(PartModelRenderer.UpdateRenderData), nameof(BeforePartUpload));
    }

    private static bool VehiclePrefix(Vehicle __instance) =>
        !_applied || ReferenceEquals(_earlyVehicle, __instance) || Program.Editor != null || __instance is not KittenEva kitten ||
        !HasEquipmentData(kitten);

    private static void BeforePartUpload(IViewport viewport, int frameIndex)
    {
        if (!_applied || Program.Editor != null) return;
        foreach (var vehicle in Program.VehiclesInFrame)
        {
            if (vehicle is not KittenEva kitten || !HasEquipmentData(kitten)) continue;
            var previous = _earlyVehicle;
            try
            {
                // Program omits every KittenEva from this early submission phase. Its later
                // virtual UpdateRenderData also draws the character into SuperMesh buckets;
                // invoking only the Vehicle method here avoids advancing animation twice.
                _earlyVehicle = kitten;
                _submitParts!(kitten, viewport, frameIndex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"iron-man: equipment render submission failed: {ex.Message}");
            }
            finally { _earlyVehicle = previous; }
        }
    }

    private static bool HasEquipmentData(KittenEva kitten)
    {
        // Authored equipment survives disabling flight mode and save/reload. Rendering that
        // existing data must remain correct, without enabling any input or physics changes.
        foreach (var connector in kitten.Parts.Root.Connectors)
            if (IronManConnectors.IsOwned(connector)) return true;
        return false;
    }

    private static void Unpatch(Harmony harmony, Type type, string name, string patchName)
    {
        var target = AccessTools.Method(type, name);
        var patch = AccessTools.Method(typeof(IronManRenderPatches), patchName);
        if (target != null && patch != null) harmony.Unpatch(target, patch);
    }
}
