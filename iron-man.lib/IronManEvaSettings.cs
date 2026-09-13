using KSA;

namespace MeowSci.IronManLib;

/// <summary>Native EVA preferences to restore when a temporary rocket flight ends.</summary>
internal sealed partial class IronManEvaSettings
{
    private readonly IronManFlightSettings _flight;
    private readonly Part? _controlPart;
    private readonly Part.Connector? _controlConnector;
    private readonly KittenControlMode _controlMode;

    public IronManEvaSettings(KittenEva kitten)
    {
        _flight = new IronManFlightSettings(kitten.FlightComputer);
        _controlPart = kitten.ControlPart;
        _controlConnector = kitten.ControlConnector;
        _controlMode = kitten.ControlMode;
    }

    public void Restore(KittenEva kitten)
    {
        _flight.Restore(kitten.FlightComputer);
        // Editor changes can remove the old reference. The native setter also validates
        // control modules/docking connectors and safely falls back to the default frame.
        Part? part = _controlPart?.Tree == kitten.Parts ? _controlPart : null;
        kitten.SetControlPart(part, part == null ? null : _controlConnector);
        kitten.SetControlMode(_controlMode);
    }
}
