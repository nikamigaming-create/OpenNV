# Current work

## Active playtest blockers

The September 20 combat correction fixes player sight-ray ownership, stationary
turning and pursuit of an occluded target within weapon range. Friendly spread
queries now select actor/player layers; dense scenery previously exhausted the
128-contact query and held every shot. Dead/disabled allies no longer hold fire.
The native source Fiend test passes with 160 world contacts and a blocked route.
In an ordinary exported flat encounter the Fiend fires, reloads and damages the
player; ED-E kills him. Fresh Elliott Tate simulator gameplay also confirms the
Fiend firing and ED-E's kill followed by Follow. Ordinary Pip-Boy Stimpak use
consumes one in each mode: flat HP 26 to 65 and simulator saved HP 94 to 133.

Cloud UV rates now include normalized weather wind and the winning
fWeatherCloudSpeedMax setting. All 98 loaded WTHR records and synthetic calm,
override and malformed-input tests pass. Fresh flat motion was inspected.
Weather transitions and matched retail timing remain open.

The source wind-responsive rigid-body flag now drives a shared exterior wind
owner. Selected owned native checks pass wake, independent gusts, frozen/interior
exclusion, disabled stops and warm reentry. Ordinary flat/XR telemetry confirms
active forces and changing poses. One distant body falls below terrain, and cold
prop pose persistence remains open; see [environmental wind](environmental-wind.md).
Moving LOD/loading and frame performance remain blocking:
whole-object uploads exceed their nominal frame budget. Property-free distant
meshes are flat water surfaces in the inspected blocks; their world-water
material owner remains absent. They must not be described as missing LAND.

The current performance work caches winning float/integer settings, publishes
shared exterior shader constants, reuses HDR texture names and bounds background
content preparation by process CPU/memory availability. This 28-thread/32-GiB
Windows machine selects four workers. LOD uses a bounded upload queue; obsolete
grid preparations cancel. Source scene/gameplay publication stays on its owner.
Both D3D12 and Vulkan environment pixel checks pass. See
[runtime performance](runtime-performance.md) for policy and measurements.

The same stationary flat checkpoint improves from 15 FPS (p95 80.61 ms) to
60 FPS (p95 17.60 ms). Current safe-mode XR measures 45 FPS (p95 about 28 ms).
Separate rendering measured about 83 FPS in XR, but this Godot build emits a
render-thread finalize error on exit; safe mode remains the release default.
Cell-crossing flat gameplay exposed a 175.25 ms upload and 69.57 ms commit.
These are selected, non-isolated Windows measurements, not performance acceptance.
The full runtime gate and selected wind/weather audits pass; current private
logs are `tmp/development-lab/wind-core-{runtime-gate,native,owned-weather}.log`.

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

The private two-minute-fifteen comparison is
`local/recordings/playtest-20260920/OpenNV-flat-vr-companion-showcase-updated.mp4`.
It pairs dialogue, barter, Pip-Boy inventory, recruitment, companion combat,
corpse looting, post-combat healing and ED-E outside. It uses independent ordinary-input takes,
normal playback speed, flat audio and only the simulator's left projection eye.
Both final eyes were inspected separately. The edit manifest identifies every
source and cut. The illustrated local document is `PLAYTEST-STATUS.md` beside it.
Its outdoor combat chapter uses fresh corrected flat/XR takes through the kill.
The healing chapter uses the verified post-damage takes; earlier working
dialogue/inventory/recruitment/loot chapters remain, identified in the manifest.
The full edit decodes successfully and its selected action frames were inspected.
The reel does not demonstrate player melee, VR crafting or
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

Streaming retains the upload/commit spikes measured above. Profile moving
gameplay and distinguish CPU, GPU and streaming costs. Moving LOD, missing draws,
vegetation/alpha, wind-body terrain residency and all eligible actor spawns require further
work. Clouds and water-tower cutouts were viewed, but that does not prove exterior
completion. Use the
new package's `release-manifest.json` to identify its exact source commit.

No retail assets, saves, captures or private executable analysis belong in Git
or public releases. Recording stays off outside selected visual checks. Remove
temporary frames after inspection/export; keep the requested deliverables and
only diagnostics still needed for active defects.
