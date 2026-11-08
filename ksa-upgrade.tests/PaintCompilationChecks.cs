using System;
using System.Linq;
using System.Text;
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using HarmonyLib;
using MeowSci.HumbleArteestLib;
using RenderCore;

internal static class PaintCompilationChecks
{
    internal static void Run()
    {
        var harmony = new Harmony("ksa-upgrade.tests.paint-compilation");
        VehiclePaintPatches.Apply(harmony);
        try
        {
            VehiclePaint.Enable();
            byte[] source = Encoding.UTF8.GetBytes("#version 460\nvoid main() {}\n");
            VehiclePaintShaders.FixtureSource = source;
            const string path = "C:/Game/Content/Core/Shaders/Mesh/MeshIndirect.frag";
            var options = new CompileOptions { Marker = 123 };
            ShaderModuleUtils.FileCalls = ShaderModuleUtils.StringCalls = 0;
            int before = VehiclePaintShaders.Compiled;
            var module = ShaderModuleUtils.FromFile(new Device(), path, out var stage, options);
            Require(module.Token == 42 && stage == VkShaderStageFlags.FragmentBit &&
                ShaderModuleUtils.FileCalls == 0 && ShaderModuleUtils.StringCalls == 1, "actual prefix replaces the file compile exactly once");
            Require(ShaderModuleUtils.ReceivedSource!.SequenceEqual(source) &&
                ShaderModuleUtils.ReceivedOptions!.Value.Marker == 123 &&
                ShaderModuleUtils.ReceivedName!.SequenceEqual(Encoding.UTF8.GetBytes(path + "\0")),
                "shader bytes, native variant options and terminated include/debug path are forwarded unchanged");
            Require(VehiclePaintShaders.Compiled == before + 1, "successful intercepted compile is counted");
            ShaderModuleUtils.FailString = true;
            ShaderModuleUtils.FromFile(new Device(), path, out _, options);
            Require(ShaderModuleUtils.FileCalls == 1 && VehiclePaintShaders.Failed > 0,
                "compile failure reports and executes the original file path");
            ShaderModuleUtils.FailString = false;
            VehiclePaint.Disable();
            ShaderModuleUtils.FromFile(new Device(), path, out _, options);
            Require(ShaderModuleUtils.FileCalls == 2, "disabled paint leaves native compilation intact");
        }
        finally
        {
            ShaderModuleUtils.FailString = false; VehiclePaintShaders.FixtureSource = null;
            VehiclePaint.Disable(); VehiclePaintPatches.Remove(harmony);
        }
        Console.WriteLine("PASS: production paint compile prefix forwarding, result/stage, disable and stock fallback");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
