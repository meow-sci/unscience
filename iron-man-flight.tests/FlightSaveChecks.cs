using System;
using Brutal.Numerics;
using KSA;
using MeowSci.IronManLib;
using MeowSci.KsaAbstractions.Persistence;

internal static class FlightSaveChecks
{
    public static void Run()
    {
        var computer = new FlightComputer
        {
            AttitudeMode = FlightComputerAttitudeMode.Auto,
            BurnMode = FlightComputerBurnMode.Auto,
            CustomAttitudeTarget = new double3(1.25, -2.5, 3.75),
            RateLimit = 2.5f,
            AngleDeadband = .4f
        };
        var original = new IronManFlightSettings(computer);
        var dto = SaveJson.FromElement<IronManFlightSettings.Saved>(SaveJson.ToElement(original.Capture()));
        IronManFlightSettings.Validate(dto);
        var restored = new FlightComputer();
        new IronManFlightSettings(dto).Restore(restored);
        if (restored.AttitudeMode != computer.AttitudeMode || restored.BurnMode != computer.BurnMode
            || restored.CustomAttitudeTarget != computer.CustomAttitudeTarget || restored.RateLimit != computer.RateLimit
            || restored.AngleDeadband != computer.AngleDeadband)
            throw new Exception("Flight settings save lost values.");
        dto.RateLimit = float.NaN;
        bool rejected = false;
        try { IronManFlightSettings.Validate(dto); } catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("Invalid flight settings accepted.");
        Console.WriteLine("Flight save: preferences/nonzero vector round-trip and invalid data rejection passed.");
    }
}
