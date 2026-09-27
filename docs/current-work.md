# Current work

## Active objective

Finish ordinary flat/OpenXR New Vegas gameplay, including every weapon,
autonomous NPC/creature combat, source routines and quests, looting, crafting
and the requested mods. Work without subagents. Follow the
[flat gameplay work order](flat-gameplay-plan.md) and
[implementation plan](implementation-plan.md). All 36 broad recovery
requirements remain open; no campaign, all-weapon, all-mod or retail-parity
completion is established.

## Current candidate and footage

The experimental candidate is
`local/releases/OpenNV-0.1.0-experimental.20260927.5-windows-x64`, built asset-free
from clean runtime commit `06ffd2cadd1f0e0a180882a666489503bea4aa30`.
The complete runtime checks, selected owned-data checks and PR checks pass.
A copied genuine Primm checkpoint with inventory-only diagnostic additions
cold-Continues in the exported build, retains all 446 saved quest owners and
completes a knife throw with two contacts and no impact/decal error.
`local/playtest-20260927-world/Play Flat.cmd` points to this candidate.

The latest 47-second side-by-side is
`local/recordings/weapon-showcase-20260927/OpenNV-weapons-flat-VR-updated.mp4`.
It contains ordinary flat and Elliott Tate simulator input from copied Primm
checkpoints: 9mm, laser rifle, dynamite, throwing knife/hatchet/spear, Fat Man,
Flamer and cleaver. Reachable source support grips are engaged for the laser,
Fat Man and Flamer takes. The laser encounter includes hostile return fire,
a hit reaction and death. New throw footage includes the impact repair.
Remaining faults are captioned. This selected footage is not all-weapon,
matched-retail or physical-headset acceptance.

Recording performance is poor. The main raw takes contain about 23.4 distinct
frames/sec flat and 15.6 in the simulator; the later Debug impact takes contain
12.1 and 10.4. The edit retains repeated frames and original playback speed.
Car destruction remains a separate native fixture, not a filmed world encounter.
Nearby Primm cars in this source inventory are static wrecks. Repeated exported
flat sessions report native heap corruption after ordinary Quit Game; successful
combat and exports do not establish shutdown stability.

## Repairs and current boundary

Shared C# owners consume source Fire/Loop/Hold/Release clocks, automatic cadence,
inventory throws, grenade friction/restitution, explicit rotation and timed
fuses. Weapon and Aid wheels use ordinary equipment/consumption transactions.
Normal and long-fuse dynamite external emitter channels now bind; native input
checks cover hold, release, single consumption, physical flight and detonation.
Source laser BeamEnd geometry follows the authoritative ray endpoint and source
visibility duration. The Flamer's shaderless helper, initial gun visibility,
body-mounted tank and retained XR finger grip were repaired. Its flame stream
is still absent.

Fat Man target lookup now resolves registered destruction owners and deduplicates
colliders. A repeated ordinary flat shot's detonation processing fell from 1,730
to 128 ms, with unchanged source flight and no added fuse. A simulator sample
spent 198 ms. Effect construction and recording stalls remain. Native checks
cover source car explosion/wreck replacement and cold destruction state.

Mixed-material NIF impacts now receive Jolt ray triangle indices; the existing
owned windmill check resolves both source materials after reproducing face -1.
DODT preserves reserved bits found in owned throwing/melee impacts. Native checks
load all ten populated material entries for each knife, hatchet and spear.
Ordinary flat and simulator throws reach contacts without the prior material
or decal errors. Decal pixels, alpha/parallax behavior and recoverable thrown
items remain incomplete.

The earlier source/native-animation sweep covers 496 winning weapons, including
302 playable inventory entries, with failures on 167 records (145 playable).
It predates the latest dynamite/impact repairs. Rows without failures are not
verified gameplay; it also does not certify every referenced projectile effect.

## Next owners

The current source work repairs saved read failures and world queries. A
conservative recovery inspection resumes only missing reads before mutations;
it preserves saved locals and rejects earlier effects. GetRandomPercent now
uses one saved script stream, and IsInInterior/condition 300 reads the reference's
current CELL. Synthetic and owned-data checks pass: 37 clock/random failures
recover across the genuine checkpoint, with 23 resident recoveries observed in
an ordinary copied flat Continue. Subsequent unsupported operations remain
visible. NPC interior conditions now reach the next selected procedure failure.

Continue source object animation ownership: PlayGroup and IsAnimPlaying remain
unbound; scripted window NIFs also fail because the controller player treats
multiple selectable loops as simultaneous automatic loops. Preserve authored
selection, clocks and cold state. Then repair the reached NPC/creature package
procedures and events through their existing movement and script owners.

The weapon work still needs the general source strip-particle decoder used by
`FlamerFlame01.NIF`: BSStripParticleSystem, BSStripPSysData and
BSPSysStripUpdateModifier are currently undecoded. Connect its effect lifetime
to the authoritative weapon/projectile owner; do not substitute a generic flame.
Then repair offhand prop attachment for the VR dynamite lighter, tracked
hand-velocity throws, thrown recovery, tracer/remaining projectile effects,
mines/remote triggers and bare-player fists. Preserve per-weapon/ammunition
failures and exercise actual damage, reaction, death and cold state in both modes.

Broader combat gaps include blast attenuation, player knockdown, explosion
placed objects/enchantments, hit events, essential recovery and death XP.
Resident routines still expose creature wander/patrol, package event
scripts/topics and sandbox/eat/sleep gaps. All ten requested mod packages are
available; selection/ordering is not runtime mod support. Continue the
[JAM/MCM plan](jam-luna-max-plan.md) without dropping the campaign/mod objectives.

## Private state

Do not change `local/playtest-20260927-world/save.json` or the original
`local/playtest-20260920-companion/save.json`; both still hash to
`800A37CC6C97C543393A544229C4E71ACB213B45C12FC4584EF3E70E94E2FBB7`.
Retain the requested prior reels. Frame recording is off outside specific
captures. Temporary review-image cleanup was rejected by automatic approval
review; those images remain in `tmp`, with no recording session left enabled.
