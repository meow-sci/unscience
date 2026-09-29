using MeowSci.KsaAbstractions;

namespace MeowSci.TheTickLib;

/// <summary>
/// Submod for the-tick: make chosen vessels indestructible by forces. Parts never exceed their crash
/// tolerance and whole-vehicle G-load / dynamic-pressure destruction is suppressed for exact vehicle instances.
/// </summary>
public sealed partial class TheTickSubmod : ISubmod
{
    public string Name => "The Tick";
    public string Tooltip => "Nigh-invulnerable vessels: parts never break and G-load / aero-pressure destruction is ignored for chosen vehicles.";

    public static TheTickSubmod? Instance { get; private set; }

    public void Initialize() { Instance = this; }

    public void Update(double dt) => TickProtection.Prune(VehicleProvider.GetAllVehicles(includeDebris: true));

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##tick_content");
        RenderProtectionPanel();
        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        ResetProtection();
        if (Instance == this) Instance = null;
    }
}
