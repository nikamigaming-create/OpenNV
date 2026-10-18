# Current work

Updated 2026-10-18. Package g failed the user's physical FNV VR playtest and is
not a playable/headset candidate. Root alone owns code, builds, input and Git.
Priority remains vanilla FNV, standalone FO3, mods, then TTW. The full repair and
acceptance inventory remains in [implementation-plan.md](implementation-plan.md).
The active goal still requires runtime, persistence, final-eye temporal and
physical-headset acceptance; selected component passes cannot close it.

## Current owner

Current owner: codex/terrain-visibility-streaming, based on main/origin/main
3a3ef697ce332cf0dffb49926dcd884bd51af731. PR #225 is merged and all
hosted checks pass. Manual save capture is sliced; validation and transactional
write run on a managed worker. The official export passed repeated ordinary
combat/save and cold continuation with the original checkpoint unchanged.

The current block replaces whole-cell LAND publication with worker-prepared
geometry/collision faces and resumable native steps. Terrain and LOD share a
CPU admission allowance with alternating first admission. LOD demand no longer
freezes while source reads/uploads are pending; obsolete preparation cancels and
forward apparent-size priority retains the surrounding cover for head turns.
The owned 25-cell terrain audit passes collision heights and 44 face-material
queries. Prepared faces retain source order and agree with native extraction
within a millimetre (the native mesh path introduces floating-point rounding).
The building atlas GPU audit preserves all 12 authored mip levels and alpha.
Its object shader now uses the documented FNV LOD Fixes runtime cutout contract,
not the former almost-zero alpha threshold; vanilla pixel parity is unverified.
See [distant-world.md](distant-world.md). The exported Vulkan candidate completes
two shots and manual saves. Ordinary boundary walks cover about 83 metres and
exercise stream cancellation at quit. Retirement now drains cancelled reads,
explicitly retires detached actor callbacks, and frees staged models before the
world. Two repaired walk/quit sessions exit zero with the original checkpoint
unchanged. The final-source full Godot gate passes, including the additional
equipment-change cancellation drain. Publication is in progress.
The next measured owner is reference activation and synchronous creature
construction: a boundary commit measured 52-60 ms, and a creature upload exceeded
38 ms. These are not terrain uploads and do not establish smooth/144 Hz play.
Weapon roll/support and physical XR acceptance remain in the implementation plan.

Use the official Godot 4.8-dev7 template and safe render-thread mode for this
block. The separate-render-thread experiment produced a texture upload error
and is not accepted. The separately identified XR profile-fix source engine
and its evidence are documented in godot-xr-profile-fix.md. Recording remains
off. Private runtime trials use copied saves under
local/save-combat-repair-20261017; the original user checkpoint stays unchanged.
Physical playability and 144 Hz require measured final-eye and headset evidence.

## Verified repairs and limits

Combat saving: source stream-27 KF layout and empty BSAnimNotes now decode with
exact extents; nonempty notes and unknown layouts fail closed. Persistent finite
player/effect audio has an explicit completion owner. Future-cell preparation is
detached until residency commits, and source loops retire with residency,
including warm construction-cache entries. An ordinary part-7 shot on coyote
169b7a completes F5 during its qualifying source reaction at phase 0.088888889.
A new process resumes that exact phase; the first observed phase is 0.177777778.
Health 15.599999 and injury 14.400001 persist, the reaction completes and combat
resumes pursuit without a reaction error. The latest navigation export repeats
this sequence successfully. This is flat ordinary-input persistence proof;
physical VR and older unsettled off-cell dust-devil histories remain open.

Creature attacks: uniquely compatible base groups in suffixed KFs retain exact
variant priority and validated binding. Owned deathclaw/coyote hit, moved-out-of-
range miss and cold attack-clock/RNG/event-prefix fixtures pass. Ordinary terrain
deathclaw combat remains unaccepted. An approach from the healthy checkpoint used
two Stimpaks through Pip-Boy and ordinary shots killed a resident coyote. It then
stopped roughly 390 metres short of deathclaw 0e745f: native capsule refinement
exhausts its node bound near a 0.481-metre residual with slope/step/contact
refusals. Diagnose planner/executor support before changing collision or claiming
arrival; do not teleport or raise bounds to manufacture proof.

Loading/performance: immutable script, activation, radio and scoped navigation
source indexes prepare on cancellable workers and are reused by fresh gameplay
owners. Saved navigation hashes and all three selected travel continuations are
exact; subsequent restore/queries perform no additional NAVM record observations.
The combat route adapter also reuses a complete matching source graph while doors,
clearance and movement remain live. The latest optimized ordered pair observes
32.08 versus 25.15 seconds for Continue and 1,723.47 versus 1,225.19 ms largest
loading intervals. This is not controlled total-speedup or headset timing proof.
Scene-thread reference restoration samples roughly 136 ms instead of 1,170 ms;
the direct combat graph rebuild is absent in the captured repaired path. Particle
publication, incremental source preparation and weapon preloading reduce selected
stalls, but later firing windows still reach 123.92 ms. Player presentation has a
roughly 478 ms nested source/material sample, a second-scale loading interval
remains, explicit saves stall and streaming has exceeded 200 ms. See
[runtime-performance.md](runtime-performance.md) for methods, measurements and
limits. No headset frame-budget acceptance exists.

Rig: the user's 46:25 recording was surveyed across its timeline and selected
failures densely reviewed. Rifle hand/forearm presentation around 32:10-32:48
remains adversely reported and physically unaccepted. The owned production-rig
diagnostic checks nine generated poses, source segment lengths, muzzle aim,
native skin palettes and a changed-length negative. Selected Vulkan rifle and
headless pistol components pass their respective checks. A near-eye mono view
reproduces the large projected hand while an external view shows a coherent arm;
head movement away from a fixed controller also reduces projected size in the
simulator. These observations do not establish the cause of the recorded defect,
source-pose/submitted-eye temporal joins, stereo comfort or complete weapon
motion. See [vr-rig-diagnostics.md](vr-rig-diagnostics.md). No rig calibration or
arm-solver repair is claimed.

## Remaining product failures

The user rejects NPC behavior and combat. Logs retain missing NPC travel,
dialogue-location/patrol and creature procedure owners; armed/embedded emitters;
faction assistance; script/condition/control-flow and AddReputation failures;
actor-value/effect owners; radio scheduling; source model/material and SpeedTree
support; constraints/phantoms; and renderer/session errors. Repeated messages do
not count distinct actors. Cross-cell creature travel, Sunny's saloon return,
post-travel population/persistence, progression and the full ordinary New Game ->
Doc exit -> tutorial/wells -> Primm/ED-E route remain unaccepted. FO3, mods and TTW
remain later work. Rendering, weather/lighting and final pixels are part of the
parity denominator; no world or campaign completion is claimed.

A visible local launcher plus Vulkan Continue recovered headset-world output
after a white D3D12 session. That recovery does not prove the physical D3D12 path
fixed. Official ESM selection does not establish a pristine installation: loose
resource winners and a mod-like missing creature path still require provenance
review. Original owned files, original saves and the user's recording stay private
and read-only. Package g pins 92148ee84f3643f1b11b93f90fcd1942bdcb896f; do not alter
its manifest to imply acceptance. The requested private 90-second edit contains
selected development moments and is not an acceptance video.

Keep recording off except a specific visual check. Remove new temporary frames
after inspection unless selected for an active investigation. Avoid duplicate
caches/proof archives. Earlier cleanup of superseded packages/diagnostics was
rejected by automatic approval review as "blocked by policy"; those artifacts
remain and must not be retried. Automatic approval review separately rejected
cleanup of the original video review's temporary frames/clips; do not retry that
deletion either. Cleanup of the new tmp/godot-xr-profile-exit duplicate exports
was also rejected as "blocked by policy"; none were deleted and that cleanup must
not be retried. The requested edit and original recording are preserved.
