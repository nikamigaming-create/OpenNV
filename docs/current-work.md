# Current work

## Active playtest blockers

The September 20 combat correction fixes player sight-ray ownership, stationary
turning and pursuit of an occluded target within weapon range. Friendly spread
queries now select actor/player layers; dense scenery previously exhausted the
128-contact query and held every shot. Dead/disabled allies no longer hold fire.
The native source Fiend test passes with 160 world contacts and a blocked route.
In an ordinary exported flat encounter the Fiend fires, reloads and damages the
player; ED-E kills him. Pip-Boy Stimpak use consumes one and restores 26 to 65 HP.
Fresh XR verification of these corrections is still required.

Cloud UV rates now include normalized weather wind and the winning
fWeatherCloudSpeedMax setting. All 98 loaded WTHR records and synthetic calm,
override and malformed-input tests pass. Fresh flat motion was inspected.
Weather transitions and matched retail timing remain open.

The source tumbleweed has a wind-responsive rigid-body flag, but environmental
wind forces have no runtime owner yet. Implement and test that owner, including
waking and residency changes in both modes. Moving LOD/loading remains blocking:
whole-object uploads exceed their nominal frame budget. Property-free distant
meshes are flat water surfaces in the inspected blocks; their world-water
material owner remains absent. They must not be described as missing LAND.

The full runtime gate, owned companion audit and native companion friendly-fire/
empty-magazine checks pass after the combat correction. Private logs are
`tmp/development-lab/sky-combat-{runtime-gate,owned-companion,companion-audit}.log`,
`fiend-dense-world-audit.log` and `weather-motion-audit.log`.

## Existing candidate and showcase

The companion gameplay candidate repairs and recruits ED-E through ordinary
input in flat and Elliott Tate's OpenXR Simulator. Both runs leave Nash Residence
through its real door, let ED-E kill the source hostile outside, resume Follow
and loot the corpse. These are selected gameplay results, not campaign or retail
parity acceptance. Work in this task without subagents.

The private two-minute comparison is
`local/recordings/playtest-20260920/OpenNV-flat-vr-companion-showcase.mp4`.
It pairs dialogue, barter, Pip-Boy inventory, recruitment, companion combat,
corpse looting and ED-E outside. It uses independent ordinary-input takes,
normal playback speed, flat audio and only the simulator's left projection eye.
Both final eyes were inspected separately. The edit manifest identifies every
source and cut. The illustrated local document is `PLAYTEST-STATUS.md` beside it.
That reel and the published experimental package predate the Fiend sight/spread
and cloud-rate corrections above; the outdoor hostile can run in place in them.
The reel does not demonstrate player melee, post-damage healing, VR crafting or
an uninterrupted Goodsprings-to-Primm journey. Keep those requirements open.

## Shared implementation and checks

Recruitment results retain faction scope, perks, flags and combat style in save
v16; v15/v14 saves remain readable. Moved references are discovered and event-bound
before materialization. Source activation/greeting scripts and explicit INFO
sounds now reach their owners. Creature weapons use source contacts and damage;
teammates refuse friendly muzzle/spread obstructions and resume packages after
OnCombatEnd. Follower door arrivals require source NAVM and native capsule
clearance with destination physics active while staged gameplay is stopped.

Source-portal A* retains directed external edges and refines short segments with
the actual player capsule. The ordinary flat route reached Primm and Nash, then
collected the required three Scrap Metal, two Sensor Modules and one Scrap
Electronics. No repair skills or parts were granted.

The September 20 publication gate passes Release/Debug builds, formatting,
analyzers, contracts, launcher tests and native Godot loading. Selected owned
recruitment/cold-state and two-phase native companion combat checks pass. The
native fixture separately verifies friendly obstruction and empty-magazine
recovery. Its floor is synthetic; ordinary footage supplies the gameplay lane.
Logs: `local/status-audit-20260920/companion-publication-{runtime-gate,owned,native}.log`.
No matched-retail or physical-headset acceptance is claimed.

## Private continuation

- `tmp/development-lab/ede-ready-to-repair-20260920.json`: genuine Nash checkpoint,
  Repair 31 and all collected parts. Use copies for repeat checks.
- `tmp/development-lab/ede-outside-before-combat-20260920.json`: actual follower
  door save, 200 player HP, before the outdoor encounter.
- `tmp/development-lab/ede-flat-companion-kill-looted-20260920.json` and
  `tmp/development-lab/ede-xr-companion-kill-looted-20260920.json`: real kill/loot
  outcomes. Cold flat continuation was also exercised.

## Next work

The user must playtest the corrected experimental candidate in flat and a physical headset
before any golden release. Confirm controller fit, comfort, readability and
ordinary travel. Fix failed functionality first; preserve the wider
[implementation plan](implementation-plan.md), [status](status.md) and all 36
[recovery requirements](recovery-checklist.md).

Repaired flying actors initially sit too high above a raised MoveTo target in
Nash; following settles their root, and the exterior arrival is clear. Interior
height and antenna appearance still need review. Enhanced Sensors acquisition is
saved, but its detection effect and NPC radio are unbound. Embedded ammo-free
guns without reload clips refill at an attack boundary; exact retail cadence is
unmeasured. Muzzle-light flicker and some impact particles/materials remain
explicit visual divergences. Broader tactics, unarmed NPC fallback, hit/death
scripts, XP and full damage ordering are open.

Streaming retains measured upload/commit spikes. Moving LOD, missing draws,
vegetation/alpha, environmental wind and all eligible actor spawns require further
work. Clouds and water-tower cutouts were viewed, but that does not prove exterior
completion. The previous public package predates the companion changes; use the
new package's `release-manifest.json` to identify its exact source commit.

No retail assets, saves, captures or private executable analysis belong in Git
or public releases. Recording stays off outside selected visual checks. Remove
temporary frames after inspection/export; keep the requested deliverables and
only diagnostics still needed for active defects.
