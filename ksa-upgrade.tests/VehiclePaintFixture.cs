using System;
using System.Runtime.CompilerServices;

// Compile-only stand-ins for the shader compilation surface VehiclePaintPatches also binds
// (FromFile seam). No GPU or shader compiler is involved; TryGetPatchedSource returns null so the
// FromFile prefix always defers to this no-op original.
namespace Brutal.ShaderCApi
{
public readonly struct CompileOptions
{
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
}
}

namespace RenderCore
{
using Brutal.ShaderCApi;
using Brutal.VulkanApi;

public static class ShaderModuleUtils
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static VkShaderModule FromFile(Device device, string filePath, out VkShaderStageFlags shaderStage,
        CompileOptions? options = null)
    {
        shaderStage = ShaderStageFromFileExtension(filePath);
        return default;
    }

    public static VkShaderModule FromString(Device device, ReadOnlySpan<byte> shaderCode, VkShaderStageFlags shaderStage,
        CompileOptions? options = null, ReadOnlySpan<byte> debugName = default) => default;

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
    public static byte[]? TryGetPatchedSource(string filePath) => null;
    public static void NoteCompiled() { }
    public static void NoteCompileFailed(string filePath, Exception ex) { }
}
}
