# Current work

## Verified candidate

OpenNV remains an experimental flat/OpenXR playtest. The active implementation
plan and all 36 recovery requirements remain in scope. Work in this task without
subagents. The user must playtest flat and a physical headset before any golden
release; simulator evidence cannot provide that acceptance.

Fresh ordinary flat and Elliott Tate simulator checks repair and recruit ED-E
with previously collected parts. Mobile actor MoveTo now projects onto authored
NAVM; inherited Immobile creatures and ordinary objects retain exact offsets.
ED-E's controller root settles at the Nash floor in both modes, recruitment
completes and Follow is active. The source hover skeleton is unchanged.
A close camera can still crop him; antenna appearance needs reference review.

The preserved second-hostile checkpoint now crosses the source curb in both
modes, kills the additional hostile and resumes Follow. Source navigation is
refined with the actor's actual capsule. Unavailable routes idle and retry;
searches yield under a shared two-millisecond physics-thread budget. Source
mesh bounds accelerate exact triangle projection. A selected route request
measures 2.71 ms versus earlier 52-142 ms samples. This is a finite encounter
result, not all-route or combat acceptance. See [creature packages](creature-packages.md).

The earlier exported encounters verify the Fiend firing/reloading, player
damage, ED-E's first kill, corpse loot and post-damage Stimpak use in both modes.
Source acquisition of Enhanced Sensors is saved; its detection effect and NPC
radio are unbound. Ammo-free embedded weapon recharge, broader tactics,
unarmed NPC fallback, hit/death events, XP and full damage ordering remain open.

## Performance and exterior work

Content preparation adapts to process CPU/memory availability: one quarter of
logical processors, clamped to one through four workers, at most two below
8 GiB. This 28-thread/32-GiB Windows host selects four. Exterior/LOD preparation
shares that admission limit, bounded upload queues and cancellation. Scene,
gameplay and physics publication remains on its owning thread. The OS schedules
core types; no hard-coded affinity is applied.

Cached winning settings and shared exterior shader constants improved the
selected stationary flat checkpoint from 15 FPS / p95 80.61 ms to
60 FPS / p95 17.60 ms. A selected safe-renderer simulator continuation after the
navigation changes measured 82 FPS / p95 20.03 ms. Earlier safe XR measured
45 FPS / p95 28.23 ms; these are different samples, not an isolated A/B or
physical-headset performance acceptance. Separate rendering remains an explicit
development option because this Godot build errors during render-thread shutdown
on both D3D12 and Vulkan. See [runtime performance](runtime-performance.md).

Cloud rates use weather wind and the winning speed setting; all 98 declarations
and selected native wind tests pass. Ordinary flat/XR wind bodies move with
independent gusts. A distant body falls below terrain and cold prop persistence
remains open. Streamed NPC source geometry now prepares on bounded workers;
native assembly yields between body parts and publishes only complete actors.
The selected flat walk reduced the largest upload slice from 183.46 to 59.67 ms
and the worst draw interval from 197.09 to 81.33 ms. Rolling p95 and cell commit
did not improve; see the complete measurements in [performance](runtime-performance.md).
A fresh Elliott Tate thumbstick run also completes both cell transitions and
the streamed NPC; its maximum upload is 53.79 ms and last commit 46.63 ms.
Moving LOD, distant water
materials, vegetation/alpha and all eligible actor spawns need further work.
Property-free distant meshes in inspected blocks are water, not missing LAND.

## Deliverables and evidence

The private 2:15 comparison is
`local/recordings/playtest-20260920/OpenNV-flat-vr-companion-showcase-updated.mp4`.
It pairs dialogue, barter, Pip-Boy inventory/illustrations, recruitment, the
corrected first encounter, loot, healing and ED-E outside. Independent ordinary
input takes use normal speed, flat audio and Elliott Tate's left projection eye;
both eyes were separately inspected. The adjacent edit manifest identifies
sources/cuts/hashes. The reel predates the latest indoor placement/curb changes.
New inspected pictures and results are in the adjacent `PLAYTEST-STATUS.md`.
Player melee, VR crafting and an uninterrupted paired Goodsprings-to-Primm trip
remain unproved.

The current publication checks cover Release/Debug builds, formatting/analyzers,
contracts, launcher tests and native loading; all pass. Owned NPC preparation
checks verify exact source geometry/expression vectors, worker execution,
cancellation, complete-body publication and freeing abandoned native nodes.
Private evidence is `tmp/development-lab/stream-npc-publication-*` and
`stream-upload-{before-01,after-01,xr-01}/moving.private.json`.
Selected owned companion,
native follow/curb/combat and exhaustive-versus-bounded projection checks pass.
Private evidence: `tmp/development-lab/companion-publication-*`,
`companion-navigation-bounds.log`, `companion-owned-curb-fixed.log`,
`companion-curb-{flat-02,xr-01}/result.private.json` and
`companion-repair-{flat-02,xr-01}/result.private.json`.
The release manifest identifies the exact packaged commit; no retail parity is
claimed from these checks.

## Next executable work

The user's flat playtest exposed a full save after every completed weapon
animation: capture took 26.57 ms and writing took 131.16 ms. That per-shot trigger
is removed from the shared flat/XR weapon owner. Four ordinary shots and a reload
in each mode leave the save file unchanged; explicit F5/controller saving persists
the final 12-round magazine. Flat-to-XR and XR-to-flat cold Continue preserve
ammunition and shot random state. Private evidence is
`tmp/development-lab/weapon-save-{flat-01,xr-03,cold-flat-01}/result.private.json`.

Resident interaction indexing and single-buffer observation encoding are merged.
The matching exported flat walk reduces the two measured commits from 49.05/60.12
to 29.85/39.48 ms, with unchanged residency and an empty queue. The last interaction
phase is 4.91 ms versus 15.94 ms. Initial Continue loading contaminates the early
draw window, so it is not a gameplay frame-time comparison. Private evidence is
`tmp/development-lab/stream-commit-{before-01,after-01}/moving.private.json`.

The full runtime gate and selected owned companion audit pass for this fix;
private logs are `tmp/development-lab/weapon-save-{gate,owned}.log`. The corrected
experimental candidate preserves the user's existing shared save. The release
manifest identifies its exact packaged source. Do not run competing gameplay or
performance checks during the user's playtest.

Next performance work: remaining indivisible armor/material uploads, cell commit,
explicit save pauses and moving LOD still need work. Preserve the genuine
copied checkpoints below.
Complete the missing ordinary-input demonstrations and wider source-driven
actor/admission coverage; do not substitute a selected scene for a playthrough.

- `tmp/development-lab/ede-ready-to-repair-20260920.json`: Nash, Repair 31 and
  three Scrap Metal, two Sensor Modules and one Scrap Electronics collected.
- `tmp/development-lab/ede-outside-before-combat-20260920.json`: recruited follower
  after the real door exit, before combat.
- `tmp/development-lab/ede-{flat,xr}-companion-kill-looted-20260920.json`: genuine
  first-kill/loot outcomes; the XR checkpoint exposes the second encounter.

Private assets, saves, captures and executable observations stay out of Git and
public releases. Recording stays off outside selected visual checks. Keep only
requested deliverables and diagnostics needed for active defects; the private
cleanup-pending list records frame deletion previously blocked by automatic review.
