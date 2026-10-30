using System;

PatchApplicationChecks.Run();
PropellantFeedChecks.Run();
FreeFallinChecks.Run();
FreeFallinShaderChecks.Run();
CapsuleGlassChecks.Run();
RenderFilterChecks.Run();
VehiclePaintChecks.Run();
Ksa5707Checks.Run();
Console.WriteLine("PASS: KSA 5438/5482/5638/5707 managed upgrade regressions");
