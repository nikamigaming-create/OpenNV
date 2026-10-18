# Current work

Updated 2026-10-17. The user rejects package g after a physical FNV VR playtest:
NPCs do not perform their behavior, combat is poor, and the game is broadly
broken. Package g is not an accepted playable/headset candidate. Root alone owns
repairs, builds, live input and Git. Priority remains FNV, standalone FO3, mods,
then TTW. The active repair inventory and acceptance order are in
[implementation-plan.md](implementation-plan.md).

## Verified state and next owner

The user's private 46:25 recording has been surveyed across the timeline with
denser samples of selected defects. It contains recognizable world exploration,
changing wrist pages, interiors, some dialogue and Sunny movement. It also shows
severely malformed rifle hand/forearm presentation. This sampled recording review
is not continuous temporal/stereo acceptance. The requested private 90-second
edit contains selected development moments, not proof of a working game.

The session log independently reports missing NPC travel/dialogue/patrol and
creature procedure owners; deathclaw attack-variant binding failures; faction
assistance ambiguity; embedded-weapon emitter selection; script/condition and
AddReputation failures; missing models/materials and SpeedTree support; radio
scheduling; and renderer/session errors. Log timing is not joined to video frames.
Repeated messages do not count distinct actors or prove the visible cause.

The active goal includes immediate loading/combat performance work and continued
repairs toward a playable VR build. The first repair block admits the owned
stream-27 animation layout with exact block extents and empty BSAnimNotes;
nonempty notes and unknown layouts still fail closed. A fresh native dog test
passes qualifying limb damage, animated pose, serialized cold reaction phase,
completion and return to combat. Ordinary saving during that qualifying limb
reaction remains open; a torso hit does not close it.

Player weapon/effect audio now uses the persistent engine-player sound history
and finite completion owner. Future-cell preparation remains detached until
residency commits; source loops retire when a model leaves residency, including
warm construction-cache entries. An optimized package-g run reproduced an F5
failure from unowned active audio. The optimized repair candidate completed the
same firing/save sequence. A later genuine shot damaged the resident coyote by
14.40 health; F5 saved it during combat and a separate cold process retained its
15.599999 health and injury, then attacked the player again. Full runtime and
selected owned layout/reaction gates pass. These are bounded flat/component
results, not physical VR acceptance.

Next owner: codex/loading-combat-stalls, based on merged repair PR #212. The
active changes reuse incremental NPC preparation during loading, prepare terrain
on bounded workers and yield native cell publication, prepare equipped weapon
resources before input, and reduce particle matrix work and native-array churn.
The native Vulkan particle-buffer check and two owned weapon checks pass; the
latter retain ammunition, RNG and audio history with no shots, visible muzzle
or particle births. Closing during active NPC preparation drains readers and
exits normally without changing the original checkpoint. The optimized first
trigger's preparation falls from 90.18 to 2.23 ms and publication from 36.10 to
15.49 ms; a later 179.35 ms interval remains unexplained. Repeated Continue takes
36.30 wall seconds, settled rolling p95 is 24.43 ms, and F5 completes but still
stalls (109.03 ms capture, 121.05 ms writing). Loading retains a roughly one-second
main-thread step. The final complete runtime gate and selected owned/native
checks pass. Checked publication is next.
These changes have not been published or accepted as a playable VR build.

The selected approach recorded a 203.19 ms streaming upload and an
82.84 ms commit, including 33.86 ms reference publication. Settled optimized flat
rolling p95 remains roughly 26-29 ms. Managed samples identify particle work and
substantial wrapper finalization; all-thread percentages include sleeping workers.
The timing method and limits are in runtime-performance.md. Keep ordinary limb
reaction/save proof, attack variants, assistance, population behavior, the tracked
rig and session lifecycle open. Performance now accompanies each gameplay block.

The local Quest 3 launcher was made visible and changed to Vulkan after a white
D3D12 session. Continue then displayed the world, hands and pistol in the headset
mirror. This is a local recovery observation, not a D3D12 repair or full VR pass.
Official ESM selection was logged; a missing creature path with a mod-like name
requires tracing winning source provenance before calling the install pristine.
Owned files and original saves remain read-only investigation inputs.

Earlier quest-font, spatial-save, actor-residency, coyote/combat and shadow fixes
passed their selected component/runtime checks. Those results do not cover this
user session's rejected behavior. Cross-cell creature travel, Sunny's saloon
return, post-travel persistence/population, complete scripts, ED-E/campaign,
performance, FO3 VR, mods and TTW remain unaccepted.

Package g pins source 92148ee84f3643f1b11b93f90fcd1942bdcb896f; its previous
asset-free package/hash validation does not establish playability. Repair PR #210
is integrated into main. PR #212 passed all hosted checks and merged at
4592c4aedb0e8c0b606cf51f67731d636733c2d1. Local main was verified equal to
origin/main with no open task PR before starting the current feature branch.
Do not change package g's manifest to imply acceptance.

Recording is off for development and checks. Keep only requested deliverables
and diagnostics still needed for the active investigation. Earlier cleanup of
superseded packages/diagnostics was rejected by automatic approval review with
"blocked by policy"; those artifacts remain. Do not bypass or retry that denial.

Automatic approval review also rejected deletion of this video review's new
temporary frames/clips as blocked by policy. They remain in private temporary
storage; do not retry that deletion. The requested video is retained separately.
