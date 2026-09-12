using System;
using KSA;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.IronManLib;

internal sealed partial class IronManEvaSettings
{
    public sealed class Saved
    {
        public IronManFlightSettings.Saved Flight { get; set; } = new();
        public SavedPartReference? ControlPart { get; set; }
        public int ConnectorIndex { get; set; } = -1;
        public KittenControlMode ControlMode { get; set; }
    }

    public Saved Capture(KittenEva kitten)
    {
        var data = new Saved { Flight = _flight.Capture(), ControlMode = _controlMode };
        if (_controlPart != null && _controlPart.Tree == kitten.Parts)
        {
            data.ControlPart = SavedPartReference.Capture(kitten, _controlPart);
            if (_controlConnector != null)
                for (int i = 0; i < _controlPart.Connectors.Count; i++)
                    if (ReferenceEquals(_controlPart.Connectors[i], _controlConnector)) data.ConnectorIndex = i;
        }
        return data;
    }

    public IronManEvaSettings(Saved data, KittenEva kitten)
    {
        _flight = new(data.Flight);
        _controlPart = data.ControlPart?.Resolve();
        if (data.ControlPart != null && (_controlPart == null || _controlPart.Tree != kitten.Parts))
            throw new InvalidOperationException("The saved EVA control part is unavailable; its original settings were retained for recovery.");
        if (data.ConnectorIndex < -1 || (data.ConnectorIndex >= 0
            && (_controlPart == null || data.ConnectorIndex >= _controlPart.Connectors.Count)))
            throw new InvalidOperationException("The saved EVA control connector is unavailable; its original settings were retained for recovery.");
        if (_controlPart != null && data.ConnectorIndex >= 0 && data.ConnectorIndex < _controlPart.Connectors.Count)
            _controlConnector = _controlPart.Connectors[data.ConnectorIndex];
        _controlMode = data.ControlMode;
    }
}
