# Wing Walker checks

Run `dotnet run --project wing-walker.tests/wing-walker.tests.csproj -p:NuGetAudit=false`. The managed fixture links the production Harmony patches against no-native KSA stubs. It enters through `IVASeat.UpdateSeatedRenderable`, including lazy renderable creation, and exercises the public `KittenRenderable.UpdateRenderData` hook at zero delta. The animation stub models the constructor's seated clip and timed blend so the first standing draw catches a crossfade that would remain seated while the editor is paused. Animation call checks compare against each renderable's post-construction baseline, since native construction itself starts the seated clip.

The fixture checks exact seat marking, standing versus seated posture, restored private animation fields, helmet and ear posing before animation and draw, own-seat first-person hiding, preservation of meshes already hidden by another owner, direct unscoped native rendering, transformed origin adjustment, repeated-seat boarding, render filtering, nested scopes, exception restoration, avatar reassignment, missing standing clips, and apply/remove/reapply.

The native assembly signature and XML serialization checks live in `wing-walker.native-tests`. This fixture exercises only Harmony behavior and does not load native game systems.
