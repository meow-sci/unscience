using System;
using System.Runtime.CompilerServices;

// Compile-only stand-ins for the shader compilation surface VehiclePaintPatches also binds
// (FromFile seam). No GPU or shader compiler is involved; TryGetPatchedSource returns null so the
// FromFile prefix always defers to this no-op original.
namespace Brutal.ShaderCApi
{
public readonly struct CompileOptions
{
    public int Marker { get; init; }
}
}

namespace Brutal.VulkanApi
{
public class Device
{
}

public enum VkShaderStageFlags
{
    None = 0,
    FragmentBit = 16,
}

public readonly struct VkShaderModule
{
    public int Token { get; init; }
}
}

namespace RenderCore
{
using Brutal.ShaderCApi;
using Brutal.VulkanApi;

public static class ShaderModuleUtils
{
    public static int FileCalls, StringCalls;
    public static bool FailString;
    public static byte[]? ReceivedSource, ReceivedName;
    public static CompileOptions? ReceivedOptions;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static VkShaderModule FromFile(Device device, string filePath, out VkShaderStageFlags shaderStage,
        CompileOptions? options = null)
    {
        FileCalls++;
        shaderStage = ShaderStageFromFileExtension(filePath);
        return default;
    }

    public static VkShaderModule FromString(Device device, ReadOnlySpan<byte> shaderCode, VkShaderStageFlags shaderStage,
        CompileOptions? options = null, ReadOnlySpan<byte> debugName = default)
    {
        StringCalls++; ReceivedSource = shaderCode.ToArray(); ReceivedName = debugName.ToArray(); ReceivedOptions = options;
        if (FailString) throw new InvalidOperationException("shaderc fixture failure");
        return new VkShaderModule { Token = 42 };
    }

    public static VkShaderStageFlags ShaderStageFromFileExtension(string filePath) => VkShaderStageFlags.FragmentBit;
}
}

namespace MeowSci.HumbleArteestLib
{
public static class VehiclePaintShaders
{
    public static bool Installed { get; private set; }
    public static string? LastError => null;

    public static bool Install() => Installed = true;
    public static void Uninstall() => Installed = false;
    public static void OnBlendModeChanged() { }
    public static byte[]? FixtureSource;
    public static int Compiled, Failed;
    public static byte[]? TryGetPatchedSource(string filePath) => Installed ? FixtureSource : null;
    public static void NoteCompiled() { Compiled++; }
    public static void NoteCompileFailed(string filePath, Exception ex) { Failed++; }
}
}
