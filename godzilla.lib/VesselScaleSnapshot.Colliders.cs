using Brutal.Numerics;
using KSA;

namespace MeowSci.GodzillaLib;

internal sealed partial class VesselScaleSnapshot
{
    private double3 OriginalLocalScale(Part part)
    {
        var original = _byPart[part];
        // Basic owns child scales; Smart leaves them animated. Neither mode owns child position
        // or orientation. Reconstruct the unscaled hierarchy without mutating any game matrices.
        return original.FullPart || _basic ? original.Scale : part.Scale;
    }

    internal double3 OriginalScaleTotal(Part part)
    {
        var local = OriginalLocalScale(part);
        if (_byPart[part].FullPart) return local;
        var parent = OriginalScaleTotal(part.PartParent!);
        return new(local.X * parent.X, local.Y * parent.Y, local.Z * parent.Z);
    }

    internal double4x4 OriginalPartMatrix(Part part)
    {
        var original = _byPart[part];
        var position = original.FullPart ? original.Position : part.PositionParentAsmb;
        var matrix = double4x4.CreateScale(OriginalLocalScale(part)) *
            double4x4.CreateFromQuaternion(part.Asmb2ParentAsmb) * double4x4.CreateTranslation(position);
        return original.FullPart ? matrix : matrix * OriginalPartMatrix(part.PartParent!);
    }
}
