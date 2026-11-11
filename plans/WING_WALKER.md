# Wing Walker validation

Implementation adds native External Seat and Standing Mount parts to Unscience. The
standalone host is for development; only the combined package is released. [Surface inventory](../scope/wing-walker.md).

Chair placement and native schema come from CURRENT 5801 XML/source. Mesh names and bounds were
also checked against the installed macOS 5707.1 Core chair atlas; the reference snapshot contains
no mesh payload. Live 5801 visual acceptance therefore remains necessary.

## Verification status

The full solution built against the 5801 references with zero warnings and errors. The production
Harmony fixture in `wing-walker.tests` passed, including repeated-seat boarding and failure paths.
The shared `saves.tests`, `world-saves.tests`, `ksa-upgrade.tests`, and eight release-policy cases
also passed. The fresh `/private/tmp/unscience-wing-walker-dist` passed `scripts/check-dist.py`:
one Unscience mod with 34 feature dependency assemblies.

`wing-walker.native-tests` exercised the 5801 KSA managed parser: eight flattened rows, two Part
rows and zero content errors. It verified six Harmony targets and two fields, inherited part data,
the native surface connector normal, four meals and one scrubber via `Habitat.ComputeFullLoad`, and
rejection of an unknown attribute. Native `IVASeatData` / `PartInstance` XML round-tripped distinct
crew names and both part IDs. The parser does not resolve asset references, initialize a live Core
library, call `IVASeat.GetSaveData`, or initialize the renderer.

For the ARM64 local run, the test explicitly retargeted only the PE machine field in a temporary
copy of the original 5801 IL-only, non-ReadyToRun assembly, leaving the source DLL unchanged and
using cached matching third-party dependencies. See [native test instructions](../wing-walker.native-tests/README.md).
Graphics, physics, boarding and scene save/load still require the in-game acceptance below.

## Native acceptance (pending)

- Place both parts, assign crew in the editor, and check seated/standing alignment, IVA camera eye,
  portraits and animation. Test absent standing idle if a suitable character exists.
- Verify controlled, noncontrolled and no-controlled-vehicle views, multiple vehicles and secondary
  viewports; check for missing or duplicate crew. Test scaled parts.
- Board near the entry point and use portrait EVA on the ground and in space; check occupancy,
  roster and reboarding. Put two identical part IDs on the same craft, board each exact door, and
  check duplicate, occupied and stale targets as well as native suit return and control handoff.
- Save scene A, load B, return to A and repeat: posture, seat assignment, crew and consumables must
  restore through native data without duplication or a Wing Walker sidecar.
- Exercise editor undo, copy, symmetry, focused and unattached part trees and seat reassignment.
- Check finite oxygen, water, food and LiOH, refill/available fluid connectivity and habitat passage
  behavior, isolated local food/scrubber supplies, passenger-only behavior without a Control module,
  plus radiation dose without shielding. Verify cleanup and patch removal on unload.
