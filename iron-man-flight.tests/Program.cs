using System;
using HarmonyLib;
using KSA;
using MeowSci.IronManLib;
using Brutal.GlfwApi;
class Checks
{
    private static int _compatibilityCalls;

    // Model I Feel Seen's prefix: it renders parts itself and skips the stock body.
    private static bool OtherRenderPrefix(Vehicle __instance)
    {
        _compatibilityCalls++;
        __instance.BaseRenders++;
        PartModelRenderer.Pending++;
        return false;
    }

    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        var harmony = new Harmony("iron-man.fixture");
        var a = new KittenEva(); var b = new KittenEva(); var normal = new Vehicle();
        var sa = new VehicleUpdateState(a); var sb = new VehicleUpdateState(b); var sn = new VehicleUpdateState(normal);
        var submod = new IronManSubmod(); IronManSubmod.Instance = submod;
        var viewport = new Viewport(); KSA.Program.VehiclesInFrame = [a,b];
        // Warm methods before patching, avoiding runtime tiering/inlining surprises.
        a.OnKey(default); a.ProcessInput(InputAction.Forward, GlfwKeyAction.Press, GlfwModifier.None);
        sa.PrepareFromVehicle(false, new()); a.UpdateRenderData(viewport,0); PartModelRenderer.UpdateRenderData(viewport,0);
        a.BaseRenders = a.AvatarRenders = PartModelRenderer.Uploaded = 0;
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var otherMod = new Harmony($"iron-man.fixture.other-render-{cycle}");
            void InstallOtherRender() => otherMod.Patch(
                AccessTools.DeclaredMethod(typeof(Vehicle), nameof(Vehicle.UpdateRenderData)),
                prefix: new HarmonyMethod(typeof(Checks), nameof(OtherRenderPrefix)));
            if (cycle == 0) InstallOtherRender();
            IronManFlightPatches.Apply(harmony);
            if (cycle == 1) InstallOtherRender();
            int ea = a.EvaKeys; a.OnKey(default); Require(a.EvaKeys == ea+1, "default off preserves EVA keys");
            sa.PrepareFromVehicle(false,new()); Require(sa.IsKitten,"default off preserves character solver");
            submod.Enabled.Add(a);
            if (a.Parts.Root.Connectors.Count == 0) a.Parts.Root.Connectors.Add(new() { Owned = true });
            sa.PrepareFromVehicle(false,new()); sb.PrepareFromVehicle(false,new()); sn.PrepareFromVehicle(false,new());
            Require(!sa.IsKitten && sb.IsKitten && !sn.IsKitten,"per-instance solver gate");
            int keys = a.BaseKeys; ea = a.EvaKeys; Require(a.OnKey(default),"base result");
            Require(a.BaseKeys==keys+1 && a.EvaKeys==ea,"base key dispatch without recursion");
            int inputs = a.BaseInputs; int ei = a.EvaInputs;
            a.ProcessInput(InputAction.Forward,GlfwKeyAction.Press,GlfwModifier.None);
            Require(a.BaseInputs==inputs+1 && a.EvaInputs==ei,"base process dispatch");
            int renders=a.BaseRenders; int avatar=a.AvatarRenders;
            int compatibilityCalls = _compatibilityCalls;
            PartModelRenderer.Pending=0; PartModelRenderer.Uploaded=0;
            PartModelRenderer.UpdateRenderData(viewport,0);
            Require(a.BaseRenders==renders+1 && PartModelRenderer.Uploaded==1,"parts uploaded early");
            a.UpdateRenderData(viewport,0);
            Require(a.BaseRenders==renders+1 && a.AvatarRenders==avatar+1 && PartModelRenderer.Pending==0,"late avatar without duplicate parts");
            Require(_compatibilityCalls == compatibilityCalls + 1,
                "other render prefix runs once early, regardless of installation order");
            submod.Enabled.Remove(a); sa.PrepareFromVehicle(false,new()); Require(sa.IsKitten,"disable restores next snapshot");
            renders=a.BaseRenders; PartModelRenderer.UpdateRenderData(viewport,0); a.UpdateRenderData(viewport,0);
            Require(a.BaseRenders==renders+1 && PartModelRenderer.Pending==0,"disabled authored equipment remains visible without duplicate");
            submod.Enabled.Add(a); sa.PrepareFromVehicle(false,new());
            int waits=JobSystems.VehicleSolver.Waits; IronManFlightPatches.Remove(harmony);
            Require(sa.IsKitten && JobSystems.VehicleSolver.Waits==waits+1,"unload joins and restores snapshot");
            ea=a.EvaKeys; a.OnKey(default); Require(a.EvaKeys==ea+1,"unload restores EVA dispatch");
            submod.Enabled.Clear();
            otherMod.UnpatchAll(otherMod.Id);
        }
        FlightComputerChecks.Run();
        OrientationChecks.Run();
        Console.WriteLine("PASS: production Harmony apply/reapply, default-off and per-EVA solver/input gates, reverse-base input, live nonvirtual render dispatch, cross-mod install order, early GPU submission with late avatar, disabled equipment, and safe unload");
    }
}
