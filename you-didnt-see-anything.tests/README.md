# You Didn't See Anything checks

Run `dotnet run --project you-didnt-see-anything.tests`. Managed checks cover the actual scene-save participant (defaults, capture, reset, repeated load, vanilla saves, and malformed records), UI gate and dispatch decisions, and the detached-window and stock-gauge filter truth table. They also read metadata and IL from the configured KSA.dll to check the two `OnFrame` UI gates, the thread-safe UI guard, and callback order; the DLL is not loaded or executed.

These checks do not initialize KSA or ImGui or exercise native rendering and input. In-game acceptance is still required.
