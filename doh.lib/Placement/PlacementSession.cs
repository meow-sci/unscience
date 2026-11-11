namespace MeowSci.DohLib.Placement;

/// <summary>A cancellable, one-shot input gesture; never part of scene saves.</summary>
internal sealed class PlacementSession
{
    public long Generation { get; private set; }
    public bool Armed { get; private set; }
    public bool Pending { get; private set; }
    private object? _world;

    public void Arm(object world)
    {
        Cancel();
        _world = world;
        Armed = true;
    }

    public bool IsCurrent(long generation, object? world) =>
        Armed && generation == Generation && ReferenceEquals(_world, world);

    public bool BeginCommit(long generation, object? world)
    {
        if (!IsCurrent(generation, world) || Pending) return false;
        Pending = true;
        return true;
    }

    public void Finish(bool success)
    {
        Pending = false;
        if (success) Cancel();
    }

    public void Cancel()
    {
        Generation++;
        Armed = Pending = false;
        _world = null;
    }
}
