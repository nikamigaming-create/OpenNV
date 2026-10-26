# Current work

## Active owner: sustained physical headset 144 Hz frame pacing

The user requires one maintained `main`, all authored gameplay and sustained
144 Hz in the VR headset. The frame budget is 1000/144 = 6.944 ms. Desktop
measurements diagnose shared costs and do not establish headset throughput.
The previous 49 FPS at 720p
diagnostic is rejected as a performance handoff. Whole-game completion and
144 Hz acceptance remain unverified.

Main retains immutable CELL definitions per plugin stack, indexes resident
references and bounds repeated failed materialization. Unsupported SpeedTree
geometry remains an explicit divergence. Terrain shadows are retained. Dynamic
bodies retain their final live physics before teardown. Owned presentation and
physics audits, the full runtime gate and hosted checks pass for those owners.

The runtime implements:

- Resumable native capsule samples during lattice expansion and smoothing,
  retaining collision, floor, actor-size and route checks. Native scheduling and
  full locomotion checks pass.
- Authoritative tutorial death/health/limb/sneaking/equipped-condition/statistic
  queries. The 43 statistic names come from the owned executable. Legacy player
  record errors retry only a proven initial read with no possible effect replay.
  A persisted receipt prevents any second attempt after a stopped suffix, even
  when the suffix reports the same error; repeated/cold-prefix tests pass.
- Source menu identity for initial/transfer loading and the session menu; known
  technical pauses retain script clocks, while unbound modals remain visible.
- Live control labels and an explicit flat bootstrap-to-gameplay HUD handoff.
  The native audit proves one advancing owner, hidden waiting, retained notice
  ordinal/elapsed time, late binding, actual input-map labels and rebinding.
  XR retains its existing bootstrap consumer; headset acceptance is open.
- Correct NIF model roots beneath `meshes`, including `trees\...NIF`; only
  SpeedTree `TREE`/`.spt` resources keep the separate `trees` root. Three winning
  cactus models resolve to exact BSA bytes and parsed NIFs with inputs unchanged.

Ordinary-input New Game completes the Vigor Tester, questionnaire, skills/traits,
final Doc dialogue and the exterior door transition at VCG01 stage 200. The
source tutorial ticks without errors and ordinary F5 completes outside. A
separate copied existing save recovers the initial tutorial failure once and
also completes F5. The final release passes cold Continue, pause/resume and
another F5 with one
retained recovery receipt, a healthy tutorial clock, no HUD error and no cactus
resolution failure. An ordinary checkpoint load visibly displays the movement prompt with mapped
W/A/S/D labels in the rendered room. The final full gate passes.

The first 144 Hz block retains fixed native names, disposes temporary wind,
combat and navigation collection wrappers and samples particle source transforms
once per step without lifecycle observers. Exact native particle state, lifecycle,
random consumption, packed buffers, callback mutations and material interleaving
checks pass. Owned fountain/controller audits and the full runtime gate pass.
Startup honors engine resolution; the prior capture-size override forced every
ordinary window back to 1280x720. Default size remains 1280x720 in project settings.

The runtime removes idle reference closures, cached reference lookup
closures and navigation projection arrays. Synthetic and native checks retain
source event order, live delegate replacement, exact nearest-triangle selection,
nonfinite refusal and concurrent pooled queries. Warmed lookup/projection paths
allocate zero bytes. Script expression views reuse immutable source inputs while
retaining fresh command argument arrays and invocation semantics. The full final
gate and selected owned-data audits pass.

The user's Cheyenne report is reproduced outside beside the player, in her
source-selected shooting-tutorial idle package. The actor's saved current location
is authoritative; its authored saloon reference does not establish its live cell.
Before repair, 776 samples contain six one-tick false walk/idle transitions despite
identical body positions. The shared pursuit owner now consumes already-reached
waypoints after a newly completed route before choosing locomotion. A repeat
four-second observation has 799 samples, no clip switches or backward clocks and
361 distinct bone poses. A native contract retains genuine movement, next-waypoint
selection and exactly one stall update. Ordinary F5, separate-process Continue
and another F5 complete on the private copy. After cold Continue, 680 samples
retain idle, continuous clocks and 361 distinct bone poses. Further ordinary
movement and matched retail animation remain unproven. The final release also
retains idle across 640 samples with no clock resets and completes ordinary F5.

The latest selected release diagnostic uses actual 3840x2160 fullscreen, scale 1,
Forward+, uncapped, VSync disabled and native physics at 90 Hz. It uses the ordinary
Safe renderer on the main thread. A fresh unpaused 15-second exterior sample
reports 75 FPS at its final observation, median draw interval 13.09 ms, p95
18.97 ms, p99 24.23 ms and maximum 34.18 ms. All 512 intervals exceed 6.944 ms.
Root GPU time is 3.24 ms plus 0.24 ms for first-person rendering. The display
reports 60 Hz; headset metadata is explicitly null in this flat run. This fails
the engine budget and is not a controlled before/after comparison, headset
measurement or movement/streaming/combat acceptance. Separate rendering remains
an unpromoted experiment. The user's original save remains unchanged.

Explicit performance publication keeps simulation/input running and omits labeled
gameplay/identity/control snapshots. Recording and trace capture remain off.
Inclusive scopes identify native capsule sweeps (2.04 seconds, 59,861 queries in
the selected interval), actor movement and particle publication. Particle buffers
upload about 6.56 times active capacity; no particles or capacity have been removed.
Embedded skinning and alpha sorting are inactive in this interval. Native kernel
profiling is unavailable under the current Windows policy; wall-stack samples do
not establish finalizer or audio CPU consumption. Bounded main-thread callback and
draw-submission counters have pure contract coverage. The final 15-second sample
records 3.50 seconds of process callbacks, 4.32 seconds of physics callbacks and
4.37 seconds of draw submission, with no invalid or unpaired spans. These spans
are inclusive and must not be added to nested owner scopes. Fullscreen requests
now update the owning Godot Window property; actual 3840x2160 fullscreen survives
cold Continue. The desktop monitor advertises
4K at 60 Hz and no 144 Hz mode. The user explicitly targets the VR headset.
VDXR is the registered runtime. A physical preflight reaches the loader but
fails during chained instance creation, so no headset session is established.
The cause and headset model remain unqualified; no global runtime setting was
changed and the process exits cleanly. Cached read-only diagnostics now expose
the selected OpenXR interface's runtime identity, supported/active refresh,
actual/recommended target dimensions and view count, without initialization or
setting changes. Physical presentation and compositor delivered/missed frames
remain explicitly unverified. Qualify these before physical acceptance; a
simulator must not substitute for the headset.

Planning queries now reuse search-owned native parameter/result wrappers while
retaining every sweep/ray and live-world lookup. Paired native outcomes, exact
contacts, query counts, mask/margin changes, world changes and disposal pass;
256 warmed queries allocate 385,024 versus 716,800 bytes. Full native locomotion
passes. Particle distance/index value keys retain exact draw order with zero
warmed sort allocation; renderer-backed native buffer and owned fountain bounds
checks pass. Source capacity, active population, random consumption, callbacks and
full upload size remain unchanged. The final full runtime gate and renderer-backed
owned fountain audit pass. Independent review finds no material regressions.

Next: qualify the physical OpenXR runtime for the user's 144 Hz target and
reprofile the remaining source-allocation
and rendering owners on a fresh branch. Keep native sweeps: the pinned backend
can publish pending collision bodies during queries, so duplicate-looking calls
are not established as redundant. Preserve every
collision/support query, source callback, actor, shadow, particle and authoritative
simulation tick. Complete ordinary movement, exterior streaming and combat windows
with median/p95/p99 and late counts before accepting 144 Hz. Separate uncapped
engine throughput from actual display presentation. Require matched retail state,
timing and pixels before any 1:1 claim.
Keep unsupported SpeedTree/Sandbox and other behavior visible. Then resume the
remaining ordinary Doc/Goodsprings lessons and campaign coverage.
