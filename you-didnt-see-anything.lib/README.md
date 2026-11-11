# You Didn't See Anything library

This library provides the optional Unscience toolbox control for keeping ImGui windows visible while KSA's F2 UI hide is active. The feature starts off. The options restrict retained windows to detached OS viewports and independently suppress the stock flight gauges. F2 retains its normal behavior while the feature is off. The game menu bar and world selection overlays stay hidden during F2.

The toolbox's native-save sidecar records the three scene options. Loading a vanilla save or starting a new scene resets them to off; no ImGui pointers or temporary rendering state are saved. A malformed or newer record is retained by the sidecar coordinator. Window visibility and layout are still owned by ImGui or the respective mod.

When filtering excludes an open ImGui popup or modal, that popup is dismissed so it cannot invisibly block the retained windows. Per-viewport background and foreground draw lists are hidden during F2; windows own their normal draw lists.

purr2d2's detached **Saber**, **Furce** and **Kittens** panels are supported. Its custom renderer checks F2 separately from the game's UI callbacks, so an optional compatibility patch opens those submission gates and keeps only its detached panels active. Attached HUD controls and world markers stay hidden; detached controls keep their custom mouse, wheel and keyboard routing. Changing F2/retention mode releases existing gestures. KSA's multiple-window mode and purr2d2's own panel visibility settings still apply. No purr2d2 files or settings are changed; incompatible versions log a compatibility warning and leave the main feature available.

The implementation patches specific current-build UI gates, preserving KSA's `Program.DrawUI` flag, and installs native ImGui filtering hooks for detached-only mode. Patch failures are reported in the toolbox/log. KSA and ImGui native runtime behavior, including multi-viewport window input and gauge state, must be verified in the game.
