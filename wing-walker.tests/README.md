# Wing Walker checks

Run `dotnet run --project wing-walker.tests -p:NuGetAudit=false`. The managed fixture links the production Harmony patches against no-native KSA stubs. It checks exact seat marking, standing idle selection and restoration, transformed origin adjustment, repeated-seat boarding, render filtering, nested scopes, exceptions, and apply/remove/reapply.

The native assembly signature and XML serialization checks live in `wing-walker.native-tests`. This fixture exercises only Harmony behavior and does not load native game systems.
