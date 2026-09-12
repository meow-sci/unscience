# Godzilla managed regression checks

Run `dotnet run --project godzilla.tests`. This executable links the production snapshot and scale
ownership implementations to small managed KSA/numerics fixtures. It checks Smart layout and authored
scales, repeated edits, preservation of animated subpart state, Basic/Smart transitions, exact restore,
invalid input, staging, non-default kitten scale and competing owners. It cannot validate native
physics, rendering or actual game animation; build the full solution against the current KSA DLLs too.

The production `WeldScale` validator is linked too. Checks exercise Smart and Basic sizes below
0.05 and above 20, preserving exact requested values and restoring originals afterward.
