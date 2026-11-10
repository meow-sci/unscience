# Hidden UI patch fixture

Run `dotnet run --project you-didnt-see-anything.patch-tests`. This managed fixture executes the production Harmony patch installation, transpilers, fallback hook, and removal against fake KSA methods with the expected gate shape. It verifies callback counts, mid-frame disabling, stock-gauge and menu guards, the unchanged F2 flag, failure on a changed IL shape, and preservation of another Harmony owner's patches. The ImGui/native filter is stubbed; KSA.dll metadata checks live in the adjacent test project and in-game rendering/input still requires native acceptance.
