# Current work

## Verified candidate

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

The user must playtest the experimental candidate in flat and a physical headset
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
