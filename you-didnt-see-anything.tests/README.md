# You Didn't See Anything checks

## KSA 5901 compatibility

Checks that the Crew Clock window is hidden and shown with the GameTime gauge it is anchored to.

See [upgrade evidence and native acceptance](../plans/KSA_5901_UPGRADE.md).

Run `dotnet run --project you-didnt-see-anything.tests`. Managed checks cover the actual scene-save participant (defaults, capture, reset, repeated load, vanilla saves, and malformed records), UI gate and dispatch decisions, and the detached-window and stock-gauge filter truth table. They also read metadata and IL from the configured KSA.dll to check the two `OnFrame` UI gates, the thread-safe UI guard, and callback order; the DLL is not loaded or executed.

These checks do not initialize KSA or ImGui or exercise native rendering and input. In-game acceptance is still required.
