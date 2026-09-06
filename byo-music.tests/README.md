# BYO Music managed checks

Run `dotnet run --project byo-music.tests`. Links the production repeat scheduler and shared file
catalog against an isolated temporary filesystem. Checks completion-based gaps, no overlapping
plays, live edits and repeat cancellation, invalid time steps, copied-file durability, duplicate
names, format filtering, same-file import, PNG compatibility and path containment. No native FMOD
or KSA runtime is loaded; game playback requires a live check as described in the mod README.
