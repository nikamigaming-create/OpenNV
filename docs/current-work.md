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

The requested selected-weapon reel is
`local/recordings/weapon-showcase-20260927/OpenNV-weapons-flat-VR.mp4`.
It pairs ordinary flat and Elliott Tate simulator input on copied checkpoints
with diagnostic inventory additions: automatic fire, Aid, laser pistol, Fat Man,
frag grenade, Flamer, spear and cleaver. Actual detonation state was checked
independently of capture-driver annotations. Car destruction is a separate
native fixture and is not shown. The main raw takes contain about 19.7 and 12.9
distinct captured frames/sec respectively; repeats remain visible. This is not
all-weapon, smooth-performance, matched-retail or physical-headset acceptance.

## Verified shipped baseline

The previous playable baseline is
`local/releases/OpenNV-0.1.0-experimental.20260927.2-windows-x64` (PRs 52-55).
Its ordinary flat Primm checkpoint restores hostile fire, ED-E assistance,
source patrol motion and nonfatal IDLE hit reactions. Source NPC/gecko checks
also cover player-independent combat, reactions and cold resumption. Looting
supports quantity selection; crafting uses recipe requirements and ownership
conditions. All 446 quest owners in the genuine older save retain their clocks,
failures and progress after the parser recovery. These bounded improvements do
not establish all quests, world routines or mod execution.

## Next work

Publish the locally checked weapon/rig block, verify the exported candidate on
a copied checkpoint, and point the playtest launcher at that package. Then
finish weapon effects and families against the winning inventory: flame/beam
visuals and tracers, remaining projectile/ammunition effects, mines and unarmed
input. Repeat the selected corpus capability audit after those changes; its
earlier 496-weapon/179-failure snapshot predates the AttackLoop cadence repair
and is not a current all-weapon result.

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
