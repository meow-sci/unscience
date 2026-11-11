namespace MeowSci.DohLib.Placement;

/// <summary>Owns complete button pairs, including releases after placement has already ended.</summary>
internal sealed class PlacementMouseCapture
{
    private int _owned;
    public void Clear() => _owned = 0;
    public bool Consume(int button, bool pressed, bool mayCapture)
    {
        int bit = 1 << button;
        if (!pressed)
        {
            bool owned = (_owned & bit) != 0;
            _owned &= ~bit;
            return owned;
        }
        if ((_owned & bit) != 0) return true;
        if (!mayCapture) return false;
        _owned |= bit;
        return true;
    }
}
