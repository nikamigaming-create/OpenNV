# Current work

Updated 2026-10-18. The user has redirected implementation to complete Fallout 1
and make its 3D art faithful to the original game. FO1 is the immediate priority.
Root alone owns code, builds, input and Git. Existing FNV work is preserved;
package g remains rejected for physical VR use.

## Current owner

Current owner: root. Active block: exported-variable assignment on
`codex/fo1-export-assignment`, a prerequisite for map/object lifecycle.
The equipped-player repair merged as PR #237 after its full local gate, owned
component audit, bounded ordinary-input/cold check and every CI check. Local
main was clean and equal to origin/main at `fef7c11b` with no outstanding task
PRs before this block. Private original-engine inspection confirms that map
departure unloads script programs and exported variables; the retained door
programs do not yet reproduce that lifecycle. It also confirms that assignment
to an absent export is an error, not an implicit declaration. The active repair
rejects absent names while preserving cross-module writes, ASCII name resolution
and declaration ownership. Synthetic checks cover missing declarations, shared
writes and rollback after a later source fault. Synthetic checks, all three
owned FO1 starting profiles and FO2 startup/cold regression pass. The complete
initializer-only corpus is identical to the retained final-build baseline:
880 pass and 66 remain unbound. The full runtime gate and diff check pass.
Starting inventory merged as PR #236 after
the full local gate, owned audits, bounded ordinary-input/cold check and every
CI check. Local main was clean and equal to origin/main at `e4b6d628` with no
outstanding task PRs before this block. Comparison/elevation and module ownership
prerequisites are also merged (PRs #234/#235).
The merged inventory change connects FO1 Begin to source startup and its source-routed
map-entry body. FO1 level-one skills, clock-sensitive Night Person inputs,
tags/traits and instance-aware item transfers supply the opening's grants.
Synthetic checks and actual Begin/restore fixtures pass for all three owned
premades, including source variation and no duplicate grants after dropping.
FO2 startup regression and the full runtime gate pass. Fresh ordinary-input
New Game opens the source inventory at its source arrival. F5 and a separate
process continuation retain the inventory and equipped pistol without duplicate
grants. Equipping the pistol exposes an existing presentation blocker: unbound
held-animation channels retire the 3D player, including after cold continuation.
The merged repair supplies the selected model's source target names to the
existing animation binder and removes recipe-specific inactive-name lists.
Owned inspection shows the failing aim channels belong to other weapon models;
declared targets without runtime owners still fail closed in the synthetic check.
All 15 owned component combinations (five recipes across three premades) bind
their source socket and advance locomotion, turn and return to idle; no dropped
donor physics remains in the held subtree. The same formerly failing save now
renders Max with his pistol in the world and inventory. Ordinary input walked
ten source steps through different directions, unequipped/re-equipped, saved
and cold-continued with identical position, facing, steps and inventory. Timed
frames show motion and retained attachment; all temporary captures were deleted.
The full runtime gate and diff check pass. This closes only the measured missing-player failure;
likeness, complete animation and campaign acceptance remain open. Resume map/object lifecycle after
this repair. These fixtures do not
establish the original engine's complete character-load or campaign behavior.
The user requires the entire FO1 game in our 3D presentation and three complete
problem-free playthroughs before stopping. Completed qualifying playthroughs:
**0 of 3**. Preserve separate run identities, starting character, source/build
identity, ordinary-input progression, encountered issues, saves/cold continuations
and ending evidence. A run with an unresolved problem does not qualify; fixes
require renewed validation. Component tests and fixture placements never count
as playthroughs. The full art/quest/enemy inventory remains required even when a
particular route does not encounter it.

A non-headless opening check used the existing first-party input harness with an
isolated save. Ordinary pointer input selected Max Stone, entered the source
Vault 13 entrance and opened inventory. The save was written, but inventory was
empty: that build bypassed FO1 campaign initialization. The viewport also
reported 39 missing 3D models; runtime logs retain unbound scripts/combat and
three unresolved world entries. This is a failed opening acceptance check, not
a qualifying run. Temporary screenshots were inspected and deleted, continuous
recording stayed off, and only the process launched for this check was stopped.
The repaired opening now shows the six source stacks, including 72 loose rounds;
the flare has no 3D model. The cave still reports 39 missing models, rising to 40
when the equipped player's presentation fails. Both bounded captures were
inspected and deleted. This remains failed opening acceptance, not a full run.
The existing harness works for classic pointer input, but its FNV-oriented
gameplay state summary does not yet expose classic authoritative progression.

The opening MAP joins to `scripts/v13cave.int`. The merged runtime executes its
source module initializer, including exports between global-value pushes, and
retains owning-module metadata in transactions and v6 saves. Synthetic checks
and the selected owned opening check pass. The full scripts.lst initializer-only
audit passes 880 entries and leaves 66 visible failures (62 absent resources,
one unsupported module with zero-body declarations and three modules requiring
typed reference globals). Source `Start` spelling is admitted without
changing source procedure indexes. FO2 startup/grants/cold continuation pass for
all three premade characters; 18 FO1 timer fixtures and 72-map door admission
retain their previous results. The full runtime gate and `git diff --check`
pass. One earlier scenery-audit launch exited without its result; both isolated
checks and the complete gate rerun pass, but its initial exit is unexplained.
The final reference-literal guard passes synthetic and selected owned checks;
the full corpus correctly leaves those three typed-reference modules unbound.
Its complete gate rerun passes.

Next complete FO1 map/object lifecycle execution. The opening has no random
calls and now reaches its actual first-entry grants. New Game passes action 15
to source startup, which routes map entry itself; it is not dispatched twice.
Item transfer preserves source stack matching, incoming representative identity,
partial ammunition and removal from world placement. Created item flags use the
source's selective prototype mapping. Unowned retirement/active-device effects
remain explicit failures. FO1 level-one skill rules use two-stat averaging,
cap 200 and source trait adjustments; the existing FO2 contract is preserved.
Night Person inputs change at 06:00/18:00. Live skill/stat progression is not
established by this startup binding. Map first-run flags, unload, exported-variable
lifetime and revisit/cold-load semantics still need verification before completing
the general lifecycle owner. Old FO1 preview saves without initialization identity
remain unchanged; new source-initialized saves reconstruct and verify their
baseline before applying saved inventory changes.
The undeclared-external repair does not establish complete module lifetimes.
General scenery interaction, floating messages and audio
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
  documented local Blender helper is absent from this checkout. Blender 5.2.2
  is verified in the Steam installation; tool availability is not the gap.
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
