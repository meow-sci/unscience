using System;
using MeowSci.DohLib.Placement;

namespace MeowSci.DohLib;

public sealed partial class DohSubmod
{
    private readonly PlacementSession _placement = new();
    private Action? _placementCancelled;

    private void CancelPlacement()
    {
        _placement.Cancel();
        _placementCancelled?.Invoke();
    }
}
