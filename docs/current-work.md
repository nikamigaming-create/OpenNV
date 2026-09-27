# Current work

## Active implementation

The current user direction is to finish ordinary flat gameplay, with NPC and
creature response, source quest progression, looting and crafting first. Detailed
VR presentation is deferred from this immediate work order; flat/OpenXR retain
one authoritative state. Follow [flat gameplay work](flat-gameplay-plan.md) and
[implementation plan](implementation-plan.md). Work without subagents.

The current block is `codex/actor-combat-recovery`, based on main `ce50bff`.
It repairs compiled local admission, unarmed NPC attacks, squat creature motion,
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
The updated full repository checks pass, including Release/Debug, formatting,
contract probes, launcher tests and native Godot loading. Checked publication
is the remaining step for this block.

Private selected diagnostics are `tmp/actor-detection-after.log`,
`tmp/unarmed-retaliation.log`, `tmp/creature-retaliation.log`,
`tmp/actor-death-events.log`, `tmp/actor-recovery-contracts.log`,
`tmp/ambient-creature-combat.log` and `tmp/ambient-unarmed-combat.log`.
The current selected graph inventory is `tmp/flat-gameplay-corpus-20260927-final`:
628,395 winning records, with no remaining compiled-local admission failures.
The 73 source-body parse failures remain visible and are not execution coverage.
Runtime script failures remain explicit, including missing GetDistance,
playGroup, GetCurrentTime and other reached commands. Parsing and record counts
are not execution coverage.

## Next executable work

Finish this block's publication. Then fix the reached source script/package
failures and quantity-aware looting/recipe requirements through existing owners. Continue the flat work
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

The existing playtest directory is `local/playtest-20260920-companion`.
The requested comparison reel is under `local/recordings/playtest-20260920`.
Neither is replaced by component fixtures. Private assets, saves and captures
stay outside Git/releases. Keep only requested deliverables and diagnostics for
active defects; temporary frames require cleanup even on failed runs.
