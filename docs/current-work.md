# Current work

Updated 2026-10-18. Package g failed the user's physical FNV VR playtest and is
not a playable/headset candidate. Root alone owns code, builds, input and Git.
Priority remains vanilla FNV, standalone FO3, mods, then TTW. The full repair and
acceptance inventory remains in [implementation-plan.md](implementation-plan.md).
The active goal still requires runtime, persistence, final-eye temporal and
physical-headset acceptance; selected component passes cannot close it.

## Current owner

Next owner: the measured player-presentation/material loading stall and combat
frame pacing. Start the next block on a fresh codex branch from verified
main/origin/main after checked publication of the dependency repair below. The
navigation, rig diagnostic and XR viewport-transition repairs are integrated;
the viewport contract is described in [xr-session-lifetime.md](xr-session-lifetime.md).

The pinned Godot profile leak is repaired in a separately identified experimental
source template. The reviewed patch records created RIDs once in the interface's
member cleanup collection. The exact source pin is
c971f93e7e76b0ef919bf6009e7b868bea04db7f and the build name is
opennv_xr_profile_fix1. Public builder/fixture instructions and boundaries are in
[godot-xr-profile-fix.md](godot-xr-profile-fix.md). Synthetic normal/shared-path
fixtures report zero leaks and no other errors; the official template reports
one leak in each negative. Distinct resources with the same path exercise RID
reuse, rather than a repeated resource removed by action-map deduplication.
Actual optimized owned direct and launcher Continue checks also exit zero with
readers drained, no startup/exit draw errors, zero profile leaks and an unchanged
original checkpoint. Flat Vulkan Continue/main-menu/Continue/quit also passes
with the original unchanged. Overall XR acceptance remains false. The final
complete official-editor runtime gate passes, as does diff checking. One earlier
gate attempt failed the recorded-input fixture while other fixture work was
running. Its isolated recheck and the full uncontended rerun pass; the cause of
that first failure has not been isolated.

The product's official SDK/editor/template pin and installed tools remain intact.
The source template has distinct source/patch/dependency/binary provenance and
uses the matching official editor's glue and existing managed assemblies. Its
isolated AccessKit headers match the source; older installed headers failed the
first build. No private engine access or diagnostic suppression is used. This
experimental template is not a distributed package or a physical candidate;
distributionReady is explicitly false. The full gameplay, performance and
physical acceptance inventory below remains open. The official-generated core
glue hash is 889748612; the release template's native ClassDB hash has not been
independently measured. Native hash verification, complete distribution licensing
and package/toolchain integration remain required before promoting this template.

Two optimized baseline launcher sessions render the world, hands and weapon in
both submitted eyes. Ordinary XR session-menu Create New Save completes after
awaiting three finite source voices. Capture takes 117.054 ms and writing
120.128 ms: saving still stalls. A separate XR process Continues from that new
save with the exact saved player position and rendered world in both eyes.
These are bounded simulator results, not complete state parity or physical
acceptance. The original checkpoint remains unchanged.

The baselines each report a startup view-count error and, at exit, another
view-count error, a 2D/2DArray uniform mismatch and an interaction-profile RID
leak. Both ordinary menu Quit actions drain readers and dispose source owners;
those private launchers did not retain exit codes. The new retirement script
retains real process handles and compares renderer errors before/after close.
The old optimized executable is rejected in both direct and launcher entry:
two new draw errors appear during exit despite zero exit code and drained readers.
Launcher entry additionally retains the startup error.

The first-party candidate waits for FramePostDraw before the launcher's mono to
stereo transition. Process exit retains the final stereo viewport/camera contract,
releases raw hand queries while physics is alive, disables tracked input and
leaves final driver teardown to Godot. The final Debug launcher component reaches
the owned world, renders both submitted eyes and ordinarily quits with exit zero,
readers drained, source owners disposed and no view-count or texture-array errors.
One interaction-profile RID still leaks. The pinned engine's action-map loader
fails to record created profile RIDs in its cleanup collection; that dependency
failure remains visible and open. Do not suppress it or free engine memory through
private access. The final full runtime gate passes. Optimized direct and launcher
owned checks both exit zero with readers drained, the checkpoint unchanged,
startup draw errors zero and exit draw errors zero. Both still report the profile
leak and explicitly leave overall XR acceptance false. The old optimized build is
a rejecting negative in both paths. The flat Vulkan Continue/main-menu/Continue/
quit regression also passes with exit zero, drained readers and an unchanged
checkpoint. Its readiness check now waits for loading feedback to clear and
further drawn frames: the old player-exists check could send Escape during
loading, where input is correctly refused. No physical or continuous final-eye
acceptance is claimed.

Private active evidence: local/vr-rig-repair-20261018 contains the owned component
reports, selected both-eye views, simulator-results.json, exit negative results
and candidate exit metadata. Trial profiles/logs are under the active
local/save-combat-repair-20261017 investigation. Recording is off. The selected
still views are insufficient for continuous stereo or source-pose/frame joins.

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
