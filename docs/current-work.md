# Current work

## Active implementation

The current user direction is to finish ordinary flat gameplay, with NPC and
creature response, source quest progression, looting and crafting first. Detailed
VR presentation is deferred from this immediate work order; flat/OpenXR retain
one authoritative state. Follow [flat gameplay work](flat-gameplay-plan.md) and
[implementation plan](implementation-plan.md). Work without subagents.

The current candidate adds resident package schedules and Continue recovery to
the actor and interaction repairs merged through PRs #52 and #53. Those repair
compiled local admission, unarmed NPC attacks, squat creature motion,
source-body detection and player-only acquisition/assistance. New deaths reach delayed, killer-filtered OnDeath
script blocks with pending time in v19 saves. Existing v18 and earlier supported
saves remain readable; historical quest outcomes are not fabricated on restore.

Native component runs pass hostile NPC acquisition/firing, turning and wall
refusal, unarmed NPC retaliation and gecko retaliation through actual source Hit
keys/anatomical contacts. A three-actor native encounter, with no player object
or injected hit, passes NPC initiation, creature retaliation and faction ally
assistance. Source-unaggressive actors and opaque walls prevent initiation;
telemetry records candidate eligibility and visibility. Native event checks update a source quest
variable once after the dying delay and speech completion. Synthetic cold-state
checks retain the remaining delay and prevent duplicate death dispatch. These
are integration fixes, not a completed encounter/campaign or retail-parity claim.
The full repository checks and CI passed for that actor block.

The merged interaction changes reject missing crafting ingredients atomically, evaluate
player-targeted recipe note/perk/equipment conditions, and provide source-based
quantity selection for container transfers. The native interaction check passes
all 127 selected source note/perk conditions, modal cancellation, partial counts,
stale choices and cold inventory restoration. The quantity dialog was rendered
and inspected; its temporary image was deleted. Reference scripts now read live
three-dimensional game-unit distance and the shared clock. Synthetic checks
pass movement, fractional time, unrelated-space rejection and crafting rollback.
The full repository checks pass for both merged blocks, including Release/Debug,
formatting, contract probes, launcher tests and native Godot loading.

The active block evaluates source package schedules against the saved calendar,
before conditions, and reevaluates resident actors when the hour changes.
NPCs also poll changing conditions every ten seconds; that cadence is an explicit
OpenNV policy, not measured retail timing. A failed procedure cannot freeze a
different later package, while event-prefix failure latches remain intact.
Resident movement and actor-to-actor cell queries no longer require a player.
Creature following uses the actual target's running state.

Synthetic checks pass hour boundaries, overnight/weekday/date transitions,
multi-day windows and authored selection order. All 4,885 selected PACK schedules
admit and evaluate across seven sampled days; this is not procedure coverage.
Native NPC and creature checks move through source root animation and collision
without any player object; disabled actors and unloaded collision remain blocked.
Ordinary flat Continue from a copy of the genuine playtest save reaches the
Primm exterior. A hostile acquires/fires at the player without provocation,
ED-E attacks that hostile, and two patrol actors advance their source routes.
The startup collision-readiness race is repaired; temporarily absent follow or
dialogue targets wait for residency instead of permanently faulting a creature.

The same Continue exposed a previous parser regression: four saved quest owners
were rejected because their source contains trailing text on Else. Admission
now retains those owners; unsupported reached syntax still fails visibly and
does not acquire invented semantics. All 446 original quest owners restore
with unchanged clocks, execution counts, errors and quest progress, then survive
a current-version cold round trip. Parser v5 also migrates the exact admission
omissions from v3/v4 saves. The full repository checks pass, including
Release/Debug, formatting/analyzers, all contract probes, launcher checks and
native Godot loading. `tmp/package-schedules-checks-final.log` is the final run.

Private selected diagnostics are `tmp/actor-detection-after.log`,
`tmp/unarmed-retaliation.log`, `tmp/creature-retaliation.log`,
`tmp/actor-death-events.log`, `tmp/actor-recovery-contracts.log`,
`tmp/ambient-creature-combat.log` and `tmp/ambient-unarmed-combat.log`.
The current selected graph inventory is `tmp/flat-world-corpus-20260927`:
628,395 winning records, with no remaining compiled-local admission failures.
The 57 source-body parse failures remain visible and are not execution coverage.
Runtime script failures remain explicit, including playGroup and other reached
commands. Sandbox, eat/sleep/guard procedures, complete event scripts/topics and
package flags/lifecycle still have missing owners. Schedules alone do not supply
those behaviors.
Parsing and record counts are not execution coverage. Active interaction
diagnostics are `tmp/world-interactions-contracts.log`,
`tmp/world-interactions-ui.log`, and `tmp/quantity-visual.log`.
Schedule/movement diagnostics are `tmp/package-schedules-owned.log`,
`tmp/package-schedules-selection.log`, `tmp/playerless-package-motion.log` and
`tmp/playerless-creature-package-motion.log`.
The copied-save run is `tmp/world-recovery-walk/retry-input`, with selected
state and source event logs beside it; its temporary images were inspected and
deleted. `tmp/owned-quest-save.log` records the unchanged cold quest/script state.

## Next executable work

The next resident-world owners are creature patrol and wander procedures,
package conditions, source package event scripts/topics and ordinary
sandbox/eat/sleep procedures, including their saved lifecycle. The live Primm
run reaches unsupported creature Patrol on seven references, Wander on three,
and missing condition 300 on two NPCs. These are source-linked failures, not
missing hand-placed actors. Continue the flat work
order through weapon exhaustion/reselection, essential recovery, hit events and
XP, source quest/package failures, all tutorial branches and connected travel.
Use the existing lab and bot for reproduction; do not add a second framework.
Keep recording off during development. Preserve user saves and owned files.

## Mod state

All ten requested targets in [mod compatibility](mod-compatibility.md) have local
packages. Twenty-six archives are under `D:\OpenNV-Mods`; TTW is under
`D:\TTW\Installed`. Private package/source inventories remain under `tmp`.
Launcher folder selection, additive checkboxes, source precedence and automatic
load ordering work. All ten selected stacks open; working mod gameplay is still
incomplete. JAM has numeric/typed expressions, scalar functions, loops, frame/key
callbacks, INI/auxiliary storage and source-owned UI component state. Full MCM
registration/options/presentation, arrays, effect/perk/render-event behavior and
ordinary mod play remain open. Resume [the JAM/MCM plan](jam-luna-max-plan.md)
after the immediate flat gameplay blockers, then TTW and the remaining targets.

## Playtest and continuation

OpenNV remains experimental. Prior selected flat/Elliott Tate runs cover ED-E
repair/recruitment, follow/door transfer, a Fiend encounter, corpse loot and Aid.
Selected Primm actors/interiors, linked patrols, pause/save/load/death menus and
cold saves have existing checks. These do not establish broader population,
campaign, combat, all mods or physical-headset acceptance. See [status](status.md)
and its owner documents. All 36 recovery requirements remain open at full scope.

Preserve genuine private checkpoints:
- `tmp/development-lab/ede-ready-to-repair-20260920.json`
- `tmp/development-lab/ede-outside-before-combat-20260920.json`
- `tmp/development-lab/ede-flat-companion-kill-looted-20260920.json`
- `tmp/development-lab/ede-xr-companion-kill-looted-20260920.json`

The September 27 flat candidate uses `local/playtest-20260927-world`, with a
separate copy of the genuine checkpoint. The previous playtest directory is
`local/playtest-20260920-companion` and remains unchanged.
The requested comparison reel is under `local/recordings/playtest-20260920`.
Neither is replaced by component fixtures. Private assets, saves and captures
stay outside Git/releases. Keep only requested deliverables and diagnostics for
active defects; temporary frames require cleanup even on failed runs.
