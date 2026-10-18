# Current work

Updated 2026-10-18. The user rejects package g after a physical FNV VR playtest:
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
completion and return to combat. Ordinary F5 and separate-process Continue now
also pass for the selected qualifying coyote reaction, as detailed below.

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

Merged performance PR #213 reuses incremental NPC preparation during loading,
prepares terrain on bounded workers and yields native cell publication, prepares
equipped weapon resources before input, and reduces particle matrix work and
native-array churn.
The native Vulkan particle-buffer check and two owned weapon checks pass; the
latter retain ammunition, RNG and audio history with no shots, visible muzzle
or particle births. Closing during active NPC preparation drains readers and
exits normally without changing the original checkpoint. The optimized first
trigger's preparation falls from 90.18 to 2.23 ms and publication from 36.10 to
15.49 ms; a later 179.35 ms interval remains unexplained. Repeated Continue takes
36.30 wall seconds, settled rolling p95 is 24.43 ms, and F5 completes but still
stalls (109.03 ms capture, 121.05 ms writing). Loading retains a roughly one-second
main-thread step. The complete runtime gate and selected owned/native checks
pass, and all hosted checks passed before merge. These changes are not accepted
as a playable VR build.

Merged PR #214 moves the full script scan off the scene thread. A fresh optimized
loading trace attributes 8.31 seconds of scene-thread sampled time to the full
embedded quest-script declaration scan. A recording-free Continue interval run
captures an 11.86-second loading draw interval; readiness takes 39.22 wall
seconds. The source count is seven embedded declarations, 496 initializations
and default delay five. These values are not gameplay/parity coverage.
The change warms the same complete source index on the indexing worker
and cancels it during session retirement. Cancellation must never publish a
partial count. The cancellation contract and owned index-window-close test pass;
the latter drains readers, exits zero and preserves the checkpoint. Optimized
Continue takes 36.35 wall seconds with a largest observed interval of 3.17
seconds, versus 39.22 seconds and 11.86 seconds before. Initial indexing now
includes the 7.83-second source scan; total-loading speedup is not established.
Script initialization values are unchanged. A follow-up trace attributes roughly
0.94 seconds to activation-parent indexing and 0.93 seconds to script restore,
including roughly 0.89 seconds indexing source radio stations.
The complete runtime gate and hosted checks pass. Per-part NPC stalls, remaining quest host
publication/restore work and explicit saves remain open.

Merged save/attack proof: an ordinary
foreleg shot on 169b7a, reached through streamed terrain from the unchanged user
checkpoint, selects the qualifying dog reaction. F5 completes and saves its
0.088888889-second phase, part-7 injury and 15.599999 health. Separate-process
Continue resumes that exact phase; the first observed cold phase is 0.177777778
seconds. Injury/health persist, the reaction completes and combat resumes with
no reaction error. These are flat ordinary-input results on the merged save
code, not physical-headset acceptance. A prior attempt used an older investigation save
that already retained four unsettled off-cell dust-devil loops before firing;
that continuation problem remains visible and open. A later head shot completed
F5 but did not qualify as reaction evidence.

Deathclaw attack-variant failure is independently reproduced in the native
component test. Merged PR #215 admits uniquely compatible base
groups in suffixed files, retains exact variant-group priority and supplies the
validated group to creature/player sequence binding. Wrong families, ambiguity
and partial failed catalogs remain refused. Synthetic contracts, Debug build,
owned deathclaw/coyote source hits, moved-out-of-range source misses, fresh-skin
cold attack clocks/RNG/event prefixes and both player weapon-preparation checks
pass. The complete runtime gate and hosted checks pass. Ordinary terrain
deathclaw combat and physical VR acceptance remain open.

Next owner: codex/navigation-loading-stalls, from verified main/origin/main
6891b8d0ccdca82bca0aabb9b17aa20463c6448a with no open task PR. The final trace
locates roughly 1,170 ms on the scene thread inside FalloutReferenceWorld.Restore,
including 1,068 ms validating retained travel and 1,012 ms loading its owned
navigation graph. These nested samples are not additive. Inspect immutable
source-graph preparation/reuse and allocation-heavy shared-edge/adjacency
validation while preserving saved routes, source hashes and portal/floor refusals.
A private C# continuation fixture restores the original 5,965 reference snapshots
without native contact or gameplay execution. Its real world's cached navigation
region count is zero after temporary-world validation. Querying the two retained
route regions rebuilds both graphs, with 6,131 and 14,203 NAVM record observations
(not physical disk reads). Both source hashes match the retained routes. The
temporary validation cache is not transferred by Restore. This is evidence of
repeated source work, not native movement acceptance. The private helper and
results are under source-index-audit in the active investigation directory.
The navigation candidate now caches immutable graphs by source kind and
CELL/WRLD scope, preserving per-cell legacy hashes and per-world gameplay state.
Save reading prepares needed routes on the indexing worker before validation;
source traversal, adjacency and hashing are cancellable with no partial cache.
Boolean adjacency checks avoid temporary collections while retaining the old
intersection/reciprocity/flag policy (27,625 synthetic cases agree). Scope,
concurrent cold/cancelled/failed retry, lifetime, cold travel and portal checks
pass. The final owned probe restores all 5,965 references and queries both
retained regions with zero NAVM record observations after worker preparation;
both hashes and all three saved travel continuations remain exact. Native
window close during the first actual graph preparation exits zero, publishes
no ready result, drains readers and preserves the original checkpoint.
The same whole-world source graph was also rebuilt by the native combat route
adapter. It now reuses an already-complete matching scope; an uncached scope
retains the old loader and unavailable-mesh reporting. Native NPC legacy-cell
and furniture queries also reuse their matching per-cell graphs. Dynamic doors,
clearance and route execution remain separate live-world operations.
Optimized Continue takes 32.08 versus 25.15 wall seconds, with the largest
observed loading interval falling from 1,723.47 to 1,225.19 ms. This single pair
is not a controlled total-speedup or headset claim. The final trace observes
roughly 136 ms of scene-thread reference restoration, graph construction on the
worker and matching-source reuse by combat. The earlier 684 ms direct combat
graph rebuild is absent from that captured main-thread path. The final runtime
gate passes. Five starting-view shots retain no handling error, with
0.34-1.03 ms preparation; later rolling firing windows still reach 123.92 ms.
This is not encounter acceptance. Player presentation, material preparation, explicit save/combat spikes,
ordinary deathclaw terrain combat and physical VR remain open.
A separate ordinary-input regression on this export hits the resident coyote's
part-7 limb, completes F5 at reaction phase 0.088888889 and cold Continues in a
new process. The exact phase resumes; 15.599999 health and 14.400001 limb damage
persist, the reaction completes and combat returns to pursuit without reaction
error. Both processes quit normally. This is flat persistence evidence, not
physical-headset or full encounter acceptance.

Merged PR #217 addresses the previous source-index freezes. A fresh optimized
loading trace attributes roughly 779 ms to activation-parent source indexing and
711 ms to source radio station indexing during script restore, inside roughly
1,039 ms of quest-host creation. These sampled nested durations are not additive
frame timings. The repair prepares immutable stack-owned indexes on the
existing loading worker before save-validation consumers; fresh worlds reuse
declarations while keeping activation cycles, radio discovery/reception and
saved clocks independently owned. Worker cancellation/retry, concurrent cold
publication, malformed retries, winning/deleted records, the five placed families,
disposed sources and existing cold gameplay contracts pass. The selected owned
audit matches the complete previous source algorithms: 90 parents, 108 links,
29 stations, zero issues/errors; querying source issues creates no gameplay
instances. Ordinary window close during each real first scan exits zero with
readers drained, no completed index and the original checkpoint unchanged.
Optimized Continue takes 26.12 versus 23.40 wall seconds, with the largest
observed loading interval falling from 2,449.09 to 1,368.75 ms. This ordered pair
is not a controlled total-speedup or headset claim. A separate trace locates
both scans on the worker and roughly 254 ms of scene-thread quest-host creation.
The complete runtime gate and all hosted checks pass. Material dependency preparation, native part
publication, explicit saves and unexplained firing spikes remain open. Do not
claim that the remaining second-scale loading freeze is fixed.

Merged PR #216 adds a bounded post-audit hold so NPC construction profiling can collect rundown
before host exit. Resolved stacks identify source BSA inflation during material
publication, dominated by bit-at-a-time DEFLATE validation. The repair adds a
bounded nine-bit Huffman lookup and byte-window reads, retaining the canonical
long-code/tail path and strict extent policy. Selected independent vectors and
23,200 private baseline differential cases agree. A controlled Release helper on
32 owned character DDS members (27,536,230 compressed bytes) retains exact
extents and byte-for-byte archive output; median validation time falls from
523.77 to 194.05 ms across five alternating passes after warmup. This is a helper
benchmark, not game/headset frame timing. Debug profiled NPC construction retains
source vectors and cancellation checks; the worst selected part step falls from
751.66 to 223.83 ms, still a stall. Optimized Continue takes 30.06 versus 24.46
wall seconds, but the largest loading draw interval remains 2,457.12 versus
2,408.73 ms. Five starting-view shots retain no handling error; later firing
windows still exceed 100 ms. This ordered pair is not encounter/headset timing
acceptance. The complete runtime gate and all hosted checks pass. Short-host traces without resolved
stacks are unusable evidence. See runtime-performance.md for methods and limits.

An optimized ordinary terrain run from the nearby healthy checkpoint has used
two Stimpaks through Pip-Boy and killed a resident attacking coyote by mouse
firing without a combat-owner error. Travel toward 0e745f then stops roughly
390 metres away: capsule refinement exhausts its node bound on a source corridor
near a 0.481-metre residual, with slope/step/contact rejection evidence retained.
The session quit normally after retaining that failure. Diagnose native movement
support and planner/executor agreement before changing collision or claiming arrival. The route overlapped
diagnostic/build work and is not a controlled frame-performance benchmark.

The selected approach recorded a 203.19 ms streaming upload and an
82.84 ms commit, including 33.86 ms reference publication. Settled optimized flat
rolling p95 remains roughly 26-29 ms. Managed samples identify particle work and
substantial wrapper finalization; all-thread percentages include sleeping workers.
The timing method and limits are in runtime-performance.md. Keep older off-cell
audio continuation, attack variants, assistance, population behavior, the tracked
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
asset-free package/hash validation does not establish playability. Repair PRs
#210, #212, #213, #214, #215, #216 and #217 are integrated into main. Local main was verified equal to
origin/main with no open task PR before starting the current feature branch.
Do not change package g's manifest to imply acceptance.

Recording is off for development and checks. Keep only requested deliverables
and diagnostics still needed for the active investigation. Earlier cleanup of
superseded packages/diagnostics was rejected by automatic approval review with
"blocked by policy"; those artifacts remain. Do not bypass or retry that denial.

Automatic approval review also rejected deletion of this video review's new
temporary frames/clips as blocked by policy. They remain in private temporary
storage; do not retry that deletion. The requested video is retained separately.
