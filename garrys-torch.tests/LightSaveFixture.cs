using Brutal.Numerics;

namespace KSA
{
    public sealed class PartTemplate
    {
        public float3 Color = new(1);
        public float Intensity = 1;
    }
}

namespace MeowSci.ZippoLib
{
    public static class LightController
    {
        public static void ApplyColor(KSA.Part part, float3 color) => part.Template.Color = color;
        public static void ApplyIntensity(KSA.Part part, float intensity) => part.Template.Intensity = intensity;
        public static float3 ReadColor(KSA.PartTemplate template) => template.Color;
        public static float ReadIntensity(KSA.PartTemplate template) => template.Intensity;
    }
}
