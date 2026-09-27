# Current work

## Active implementation

Finish ordinary flat gameplay with autonomous NPC/creature response, source
quest progression, looting and crafting. The latest direction prioritizes every
weapon family across player/NPC use, firing, reloading, hits and persistence.
Add separate combat weapon and consumable wheels with shared flat/VR inventory
actions. Real throwing weapons, including grenades, must own release, travel,
contact, fuse and explosion behavior; a throwing animation is insufficient.
Detailed VR presentation is
outside this immediate work order; flat/OpenXR share authoritative state.
Follow [flat gameplay work](flat-gameplay-plan.md) and
[implementation plan](implementation-plan.md). Work without subagents.

The completed repair is nonfatal combat response. Player hits already changed
health and enemies fired back in the latest user play log, but no owner selected
or played the source hit-reaction IDLE tree. The repair evaluates winning IDLE
conditions against hit location, actual limb damage and actor activity; qualifying
hits interrupt the attack and publish the source KF before combat resumes.
Healthy limbs do not receive an invented stagger on every bullet. NPCs and
creatures use the same owner. Source sibling ordering retains a node's original
plugin identity when a later plugin overrides it.

Reaction identity, KF hash, elapsed phase, pose and selection randomness are
retained in v20 saves. Supported earlier schemas remain readable. Death clears
the living reaction owner. Telemetry exposes selection, rejected conditions,
active phase, completion, sound events and failures. Repeated hits retain an
active reaction instead of restarting it; that interruption policy, blend timing
and special forced power-attack triggers are not matched retail behavior.

Synthetic checks pass winning overrides, sibling ordering, absent hit context,
unknown predicate rejection and saved-state validation. Native source NPC and
gecko checks pass qualifying limb reactions, changed bone poses, fresh-skin cold
resumption, repeated-hit handling and subsequent attacks without a player.
An ordinary flat run from the genuine Primm checkpoint used mouse firing to
trigger the authored arm response, saved its active phase with F5 and observed
the hostile resume shooting. The final Release export cold-continued that save
at exactly 1.3111111111111085 seconds, completed the reaction and resumed firing.
Release/Debug builds, formatting/analyzers, contracts, launcher checks and native
Godot checks pass. The final NPC/gecko checks also reject a radial explosion's
anatomical reaction without an unresolved condition.
Selected diagnostics are `tmp/combat-hit-contracts.log`,
`tmp/combat-hit-npc.log`, `tmp/combat-hit-creature.log` and
`tmp/combat-hit-flat`. Keep frame recording off; temporary inspection frames
must be removed immediately after viewing.

## Verified gameplay baseline

Resident acquisition and faction assistance consider NPCs and creatures without
a player dependency. Natural/unarmed attacks, source-body contact, squat-creature
movement and delayed killer-filtered OnDeath dispatch have shared owners. Source
package schedules use the saved clock; patrol and follow can move without a
player. The genuine Primm checkpoint continues with an unprovoked hostile firing,
ED-E responding and two patrols advancing. These are selected encounters, not
campaign or retail parity.

Loot transfers support source quantity selection. Crafting rejects missing
ingredients and evaluates player note/perk/equipment conditions. Reference scripts
read live spatial distance and shared game time. A parser admission repair retains
all 446 quest owners in the genuine older save, including existing clocks, errors
and quest progress. It does not silently replay failed scripts. The selected
owned graph has 628,395 winning records and 57 visible source-body parse failures;
counts are not execution coverage.

## Next executable work

Complete publication of the hit-response export, then audit all loaded weapon
records and ammunition pairs through their actual runtime owners. Fix reached
weapon-family restrictions and exercise both player and NPC input paths, including
reload/empty/broken states, projectile contact, melee, thrown weapons and mines.
Current code restricts player hand grips to three variants and lacks mine
placement/detonation; projectile-effect and actor weapon-exhaustion restrictions
also remain. Source coverage must precede an all-weapons claim.
Other combat owners include hit events, essential recovery, death XP, source
tactics and forced reactions. Resident-world owners still
include creature patrol/wander, condition 300, package event scripts/topics and
sandbox/eat/sleep behavior. The current Primm run exposes those source-linked
failures. Continue the shared flat work order; do not add location-specific
success paths or a second testing framework.

All 36 recovery requirements remain open at full scope. OpenNV is experimental;
full campaign, all mods and physical-headset acceptance are not complete.

## Mod state

All ten requested targets in [mod compatibility](mod-compatibility.md) have local
packages. Twenty-six archives are under `D:\OpenNV-Mods`; TTW is under
`D:\TTW\Installed`. Launcher selection, source precedence and load ordering work.
All ten selected stacks open; this does not establish mod gameplay. JAM/MCM,
arrays, effect/perk/render events and reached script commands remain open. Resume
[the JAM/MCM plan](jam-luna-max-plan.md) after immediate flat gameplay blockers.

## Private saves and deliverables

Preserve `local/playtest-20260927-world/save.json` and the original
`local/playtest-20260920-companion` checkpoint. The previous playable export is
`local/releases/OpenNV-0.1.0-experimental.20260927.1-windows-x64`.
Keep the genuine ED-E checkpoints under `tmp/development-lab` and the requested
comparison reel under `local/recordings/playtest-20260920` unchanged.
Assets, saves and captures remain private and outside Git/releases. Retain only
requested deliverables and diagnostics for active defects.
