# Current work

## Active implementation

Finish ordinary flat/OpenXR gameplay with source NPC and creature combat,
quest progression, looting, crafting and every weapon family. The immediate
work is shared weapon timing, separate weapon/consumable wheels, real throws,
explosions and destructible cars. The user requests a flat/VR side-by-side
showcase as soon as a stable candidate exists. Work without subagents.
Follow [the flat gameplay plan](flat-gameplay-plan.md) and
[implementation plan](implementation-plan.md).

Shared C# owners now consume source Fire/Loop/Hold/Release timing, additional
hand grips, inventory throws, timed fuses, explicit projectile rotation,
explosion effects and destruction stages. Flat and XR wheels call the same
equipment and Aid transactions. The candidate also retains destruction,
knockdown and explosion exposure in v21 saves; earlier schemas remain readable.

Selected native checks pass wheel equip/cancel/single consumption, held grenade
release and fuse, Fat Man reload/ammo/impact, car explosion/wreckage and cold
destruction state. Source grenade bursts last only ten microseconds; effect
clocks now split at emitter keys and all four source systems emit. Nuclear
models use managed transform, visibility, alpha, emittance and texture channels.
Automatic held/released fire, source grenade friction/restitution and cold state
also pass. These are diagnostic fixtures, not campaign or retail acceptance.

The reported purple Flamer was a shaderless export helper. The same model's
negative visibility preroll hid its gun, and source Spine2 metadata had not
separated the fuel pack from the hand attachment. The runtime now suppresses
that helper's draw, samples initial visibility at clock zero and binds body
subtrees to their source bones. Held XR fingers retain the source grip when
controller touches are released. Owned grip/contact and Flamer assembly checks
pass; both final eyes were inspected after the repairs.

The reported Fat Man delay was a synchronous impact stall. Destruction-owner
lookup scanned ancestor child lists for each overlapping collision shape.
Reference roots now publish their owner, and blast classification deduplicates
colliders before resolving targets. A repeated ordinary flat shot fell from
1,730 to 128 ms inside detonation processing, with 3,028 overlapping shapes and
65 distinct colliders. Its roughly two-second flight has no added fuse. Native
car checks cover owner lookup before and after wreck replacement. Effect
construction and recording stalls remain; this is not retail timing parity.
An ordinary Elliott Tate shot completes one impact/detonation without a fuse
or bounce, spending 198 ms in detonation processing. The complete runtime
checks and selected native weapon checks pass. Two flat diagnostic runs still
report a native heap-corruption crash on ordinary Quit Game.

The replacement 44-second reel has been delivered:
`local/recordings/weapon-showcase-20260927/OpenNV-weapons-flat-VR-expanded.mp4`.
It pairs new ordinary flat and Elliott Tate simulator input on copied Primm
checkpoints with inventory-only diagnostic additions: 9mm, laser rifle,
dynamite, knife, hatchet, spear, Fat Man, Flamer and cleaver. Two-hand support
is engaged for laser, Fat Man and Flamer. Car destruction remains a separate
native fixture and is not shown. Raw takes contain about 23.4 and 15.6 distinct
captured frames/sec; repeats remain visible. The lighter, flame and impact
faults are captioned. This is not all-weapon, smooth-performance, matched-retail
or physical-headset acceptance. The earlier rejected reel is retained privately.

The latest source/native-animation sweep covers 496 winning WEAP records,
including 302 playable inventory weapons. It reports 167 records with failures
(145 playable inventory weapons); rows without those failures are not verified
gameplay. The next fixes connect external KF particle channels on dynamite and
source BeamEnd models for instant laser rays. Fresh native input checks pass
laser model endpoints and normal/long-fuse dynamite hold, release, one-item
consumption, physical flight and timed detonation. A fresh simulator take shows
laser damage, a source hit reaction and death in the nearby hostile encounter;
two-hand support is engaged for the laser, Fat Man and Flamer. The take also
exposes an unbound VR lighter attachment and thrown-contact decal/material
errors. Both laser eyes were inspected; this does not close physical acceptance.
These checks also exposed sound voices surviving their emitter in bookkeeping;
tree-exit cleanup now removes them.

The next impact repair enables Jolt's actual ray face index for mixed-material
NIF collision. The existing owned windmill check failed with face -1 before
the setting and now resolves source materials 5 and 9. Decal DODT readers retain
reserved high bits present in owned thrown/melee impacts instead of rejecting
the whole impact. Native effect/decal assembly passes all ten populated IPDS
entries for each throwing knife, hatchet and spear. Reserved bits remain in
telemetry; pixel/alpha/parallax equivalence is not established. Fresh ordinary
flat knife, hatchet and spear throws each discharge once, finish their source
flight after contacts and report no impact/decal error. The full runtime checks
pass. Simulator rechecks and publication of this new block are in progress.

## Verified shipped baseline

The checked experimental candidate is
`local/releases/OpenNV-0.1.0-experimental.20260927.4-windows-x64` (PRs 52-56).
PR 56 is merged and its merge commit was synchronized with local main before
the next branch. The asset-free package was built from clean commit
`f81edc9096d53980c1b64a39aa8ccb9204493dfd`; a copied genuine save cold-Continues
into the native Primm world. `local/playtest-20260927-world/Play Flat.cmd`
now points to this candidate. Both original saves retain their prior hash.
Its ordinary flat Primm checkpoint restores hostile fire, ED-E assistance,
source patrol motion and nonfatal IDLE hit reactions. Source NPC/gecko checks
also cover player-independent combat, reactions and cold resumption. Looting
supports quantity selection; crafting uses recipe requirements and ownership
conditions. All 446 quest owners in the genuine older save retain their clocks,
failures and progress after the parser recovery. These bounded improvements do
not establish all quests, world routines or mod execution.

## Next work

Finish ordinary impact rechecks and publish the mixed-material/decal repair.
Finish weapon effects
and families against the winning inventory: flame visuals and tracers,
remaining projectile/ammunition effects, mines and unarmed input. Keep each
weapon's source/assembly failures distinct from untested gameplay/presentation.
The expanded selected-weapon reel cannot close this
work or the full gameplay/mod requirements.

Remaining combat owners include mines/remote triggers, some weapon/ammunition
combinations, bare player fists, player knockdown, complete blast attenuation,
explosion placed objects/enchantments, IPDS projection, hit events, essential
recovery and death XP. Exact refraction, particle motion, blast force and
radiation behavior are not matched retail results. Source addon sound remains
visible as unbound. Resident routines still expose creature wander, condition
300, package event scripts/topics and sandbox/eat/sleep gaps.

All 36 recovery requirements remain open at full scope. Campaigns, all mods and
physical-headset acceptance are incomplete.

## Preserved private state

Do not change `local/playtest-20260927-world/save.json` or the original
`local/playtest-20260920-companion` checkpoint. Keep the requested prior reel in
`local/recordings/playtest-20260920`. Diagnostics are in
`tmp/combat-controls-audit.log` and `tmp/weapon-coverage-current.jsonl`.
Frame recording remains off except during the requested capture; remove
transient frames in cleanup paths.

## Mods

All ten requested targets in [mod compatibility](mod-compatibility.md) have local
packages. Selection, precedence and ordering work; opened stacks are not mod
gameplay support. JAM/MCM, arrays, event extensions and reached commands remain
open. Continue [the JAM/MCM plan](jam-luna-max-plan.md) after immediate gameplay
blockers without dropping the campaign/mod objectives.
