# Camera save recipe checks

`dotnet run --project camera-saves.tests` exercises the production `SavedAnimation` mapper and
shared serializer with constructor/property fixtures matching all ten native animation classes.
Checks parameter retention, nested groups and unsupported kinds. Fixtures avoid loading KSA's
x64 game executable on an ARM development host. They do not validate native camera playback;
that requires the in-game acceptance pass. A constructor/API change must update the fixtures.
