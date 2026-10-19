# Current work

Updated 2026-10-18. The user has redirected implementation to complete Fallout 1
and make its 3D art faithful to the original game. FO1 is the immediate priority.
Root alone owns code, builds, input and Git. Existing FNV work is preserved;
package g remains rejected for physical VR use.

## Current owner

Current owner: root. Verified block: native module initialization and exported
variable ownership. Next owner: FO1 map-entry lifecycle on a fresh `codex/`
feature branch after checked publication of this block.
The user requires the entire FO1 game in our 3D presentation and three complete
problem-free playthroughs before stopping. Completed qualifying playthroughs:
**0 of 3**. Preserve separate run identities, starting character, source/build
identity, ordinary-input progression, encountered issues, saves/cold continuations
and ending evidence. A run with an unresolved problem does not qualify; fixes
require renewed validation. Component tests and fixture placements never count
as playthroughs. The full art/quest/enemy inventory remains required even when a
particular route does not encounter it.

The opening MAP joins to `scripts/v13cave.int`. The current change executes its
source module initializer, including exports between global-value pushes, and
retains owning-module metadata in transactions and v6 saves. Synthetic checks
and the selected owned opening check pass. The full scripts.lst initializer-only
audit passes 883 entries and leaves 63 visible failures (62 absent resources,
one unsupported module with zero-body declarations). Source `Start` spelling is admitted without
changing source procedure indexes. FO2 startup/grants/cold continuation pass for
all three premade characters; 18 FO1 timer fixtures and 72-map door admission
retain their previous results. The full runtime gate and `git diff --check`
pass. One earlier scenery-audit launch exited without its result; both isolated
checks and the complete gate rerun pass, but its initial exit is unexplained.

Next connect FO1 map/object lifecycle execution. The owned opening map-entry
body has no random calls, so that work can proceed independently of the remaining
source-specific random contract. Do not mistake initializer-only admission for opening gameplay.
Its first-entry path reaches `base_inventory`; the next concrete unsupported
operations are source single-item inventory transfer (0x80d8) and signed integer
less-than-or-equal (0x8035). Preserve the map's
first-run flag across saves/revisits, and use the actual source-created grants.
The legacy store-to-undeclared-external path still needs removal alongside the
complete module owner. General scenery interaction, floating messages and audio
also remain needed for the entrance terminal.

PR #233 merged FO1 script time, callbacks, nested source use and v5 persistence
after the full local gate and all CI checks. Main was clean and synchronized.
Eighteen eligible Vault 13 metal-door fixtures now schedule,
cold-restore, close through the source timed callback and drain their subsequent
event. Queue/map binding, transition cancellation and retained callback faults
also pass fixture checks. Synthetic late-clock, nested-frame, budget and rollback
checks pass. The full local gate and final-build owned checks pass, including
18/18 timer candidates, repeated use and frame cadence. FO1 source admission
remains 72 maps/392 doors/four unbound initializers; FO2 ordinary-door regression
passes. Ordinary-input scripted-door and campaign acceptance
are still unproven. See [classic-script-events.md](classic-script-events.md).
The Windows Computer Use helper currently fails before initialization with
`failed to write kernel assets: ... path specified (os error 3)`, including after
one reset/retry. No app input was sent. This prevents that helper's ordinary-input
check; it does not prevent further implementation or owned-data audits.

The preceding authored-scenery repair is merged as PR #231.
The first measured divergence is authored scenery fitting: projecting mesh
bounding boxes includes empty corners, shrinking and shifting irregular models
relative to their source FRMs. Static scenery now projects referenced mesh
vertices for fitting, art anchoring and paint alignment. Skin bounds retain
their existing behavior; donor stack fitting is a separate remaining owner.

The native synthetic check covers transformed indexed geometry, unused vertices
and hidden meshes. The owned check exercises 23 furniture bindings in all six
directions. At 32 pixels/metre the old fitting lost up to 31.64% of the intended
limiting extent; repaired extent error is below 0.000001 and center error below
0.001 source pixels. The audit now reads the production world scale from the
recipe. The production-scale audit passes all 138 fits, with maximum relative
extent error 0.000000358 and center error 0.000240 pixels. The full runtime gate
passes, including Release/Debug builds, formatting/analyzers, contract probes,
launcher checks and native Godot checks. `git diff --check` passes.
These are geometry-fit checks, not silhouette, material or final-pixel acceptance.

A separate source-only Vault 13 world audit stops at its obsolete
`SourceJoinedWallBlockout` lookup after constructing elevation 0. That audit does
not supply donor libraries and is not a full current-world visual check. Repair
its scene-owner assumptions before using it for world acceptance; do not treat
its missing-shell failure as a passing whole-map check.

## Next owners and acceptance

- Compare original FO1 art with the actual native 3D presentation, starting with
  Vault 13 furniture and cave props. Fix source-facing geometry, dimensions,
  projection, materials and animation; donor identity alone is not fidelity.
- Recover reproducible first-party furniture authoring. The GLBs exist, but the
  documented local Blender helper is absent from this checkout.
- Extend source-bound art coverage across all locations, keeping missing models
  visible. Original UI/portraits remain intentional 2D.
- Complete starting grants, combat, script events, dialogue, quests, world travel,
  timers, companions, endings and persistent world changes through ordinary play.
- Finish shared first-person/OpenXR views and physical acceptance. No campaign,
  1:1 art or headset completion is established.

See [classic-fallout-plan.md](classic-fallout-plan.md),
[classic-world-realization.md](classic-world-realization.md),
[classic-inventory.md](classic-inventory.md) and
[classic-interactions.md](classic-interactions.md). Older classic overview rows
are superseded by the more specific inventory/interaction evidence.

FNV's preserved failures and requirements remain in [status.md](status.md),
[implementation-plan.md](implementation-plan.md), [runtime-performance.md](runtime-performance.md)
and [vr-rig-diagnostics.md](vr-rig-diagnostics.md). Original owned files, saves
and recordings remain private and unchanged. Recording stays off except for
specific visual checks; remove new temporary frames after inspection. Do not
retry cleanup actions previously rejected by automatic approval review.
