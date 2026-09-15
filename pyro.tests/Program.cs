using System;
using System.Collections.Generic;
using MeowSci.PyroLib;
static void Check(bool condition, string text) { if(!condition) throw new Exception(text); }
static void Near(float actual, float expected, string text)
{
    if (MathF.Abs(actual - expected) > 0.0001f) throw new Exception($"{text}: {actual} != {expected}");
}
var cycle = new PlumeCycle { OnSeconds=2, OffSeconds=3 };
cycle.Restart(100);
Check(cycle.Running && cycle.IsOn && cycle.RemainingSeconds==2,"Start On");
cycle.Update(102);
Check(!cycle.IsOn && cycle.RemainingSeconds==3,"Exact On/Off boundary");
cycle.Update(102);
Check(!cycle.IsOn && cycle.RemainingSeconds==3,"Pause/repeated viewport sample does not advance");
cycle.Update(105);
Check(cycle.IsOn && cycle.RemainingSeconds==2,"Repeat at period boundary");
cycle.Update(100000103);
Check(!cycle.IsOn && cycle.RemainingSeconds==2,"Large warp jumps retain phase without stepping loops");
cycle.Update(10);
Check(cycle.IsOn && cycle.RemainingSeconds==2,"Backward clock resets safely");
cycle.Stop();
cycle.Update(999);
Check(!cycle.Running && cycle.IsOn,"Stopping removes the cycle gate");
cycle.OnSeconds=0; cycle.OffSeconds=float.NaN;
cycle.Restart(1);
Check(cycle.OnSeconds==.05f && cycle.OffSeconds==1,"Invalid/zero typed values are sanitized");
cycle.Update(double.NaN);
Check(cycle.IsOn && double.IsFinite(cycle.RemainingSeconds),"Invalid timestamps cannot poison state");
cycle.OnSeconds=2; cycle.OffSeconds=3;
cycle.RestorePhase(500, false, 1.25);
Check(cycle.Running && !cycle.IsOn && Math.Abs(cycle.RemainingSeconds-1.25)<1e-8,"Save restores Off phase at new simulation epoch");
cycle.Update(501.25);
Check(cycle.IsOn && cycle.RemainingSeconds==2,"Restored cycle crosses next boundary once");
cycle.RestorePhase(10, true, .5);
Check(cycle.IsOn && cycle.RemainingSeconds==.5,"Save restores On phase without restarting it");

Near(PlumeMigrationMath.SyntheticChamberPressurePa(49f, 1f), 4_900_000f, "Full throttle pressure");
Near(PlumeMigrationMath.SyntheticChamberPressurePa(49f, .5f), 2_450_000f, "Half throttle pressure");
Near(PlumeMigrationMath.SyntheticChamberPressurePa(49f, 0f), 4_900f, "Zero throttle shutdown pressure");
Near(PlumeMigrationMath.RefractionFallback(506.625f, 2000f, 1f), .5f, "Half-atmosphere refraction ramp");
Near(PlumeMigrationMath.RefractionFallback(1013.25f, 2000f, 1f), 1f, "Full refraction ramp");
Check(float.IsNaN(PlumeMigrationMath.RefractionScale(0f, 1f)), "Zero template refraction uses fallback");
Near(PlumeMigrationMath.RefractionScale(2f, 3f), 1.5f, "Relative refraction scale");

var currentIds = new HashSet<string>(StringComparer.Ordinal) { PlumeTemplateIds.Auxiliary };
Check(PlumeTemplateIds.Normalize("EngineAVernier", currentIds.Contains) == PlumeTemplateIds.Auxiliary,
    "Vernier alias migrates");
Check(PlumeTemplateIds.Normalize("EngineATurbine", currentIds.Contains) == PlumeTemplateIds.Auxiliary,
    "Turbine alias migrates");
currentIds.Add("EngineAVernier");
Check(PlumeTemplateIds.Normalize("EngineAVernier", currentIds.Contains) == "EngineAVernier",
    "Installed legacy ID wins");
Check(PlumeTemplateIds.Normalize("UserDefined", currentIds.Contains) == "UserDefined",
    "Unknown ID remains explicit");
Console.WriteLine("PASS: cycle state, throttle pressure, refraction scaling and template ID migration");
