# Pyro cycle and KSA migration checks

Run `dotnet run --project pyro.tests`. Links production `PlumeCycle` without native KSA to check
On/Off boundaries, repeat, pause/repeated render samples, large warp, backward clocks, cancellation
and invalid durations/timestamps. Native transient appearance is outside this managed check.

The executable also links production `PlumeMigrationMath` and `PlumeTemplateIds` for the 5438
upgrade. It checks fractional throttle and the shutdown pressure floor, refraction scaling and
the native 0.01-atmosphere ramp, and migration of removed Vernier/Turbine templates to Auxiliary.
Installed legacy templates retain their IDs and unknown IDs remain explicit failures. These
checks do not execute native plume physics, GPU submission or the game's refraction pass.
