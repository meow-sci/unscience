using System;

PatchApplicationChecks.Run();
ShutdownChecks.Run();
PropellantFeedChecks.Run();
FreeFallinChecks.Run();
FreeFallinShaderChecks.Run();
CapsuleGlassChecks.Run();
RenderFilterChecks.Run();
VehiclePaintChecks.Run();
KittenVisorChecks.Run();
PaintCompilationChecks.Run();
Ksa5707Checks.Run();
Ksa5801Checks.Run();
DataReferenceChecks.Run();
Console.WriteLine("PASS: KSA 5438/5482/5638/5707/5801 managed upgrade regressions");
