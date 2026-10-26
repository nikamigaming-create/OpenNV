# Current work

## Active owner: sustained 144 Hz frame pacing

The user requires one maintained `main`, all authored gameplay and sustained
144 Hz. The frame budget is 1000/144 = 6.944 ms. The previous 49 FPS at 720p
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

Release diagnostics at actual 3840x2160, scale 1, Forward+, uncapped and VSync
disabled use a private Doc-exit save and 49 resident cells. The warmed stationary
sample reports 72 FPS, median draw interval 12.79 ms, p95 18.32 ms, p99 21.13 ms
and maximum 25.77 ms. All 512 intervals exceed 6.944 ms. Root GPU time is 4.72 ms
plus 0.31 ms for first-person rendering; the latest physics step is 7.70 ms.
The display reports 60 Hz. This is a failed selected diagnostic, not a controlled
comparison, movement/streaming/combat acceptance or retail parity.

Explicit performance publication omits full gameplay/identity/control snapshots,
labels their null fields and keeps ordinary simulation/input running. Its sampled
summary cost is 0.19 ms instead of the full snapshot's 7-10 ms. Recording and trace
capture remain off. Separate source allocation sampling identifies reference-event
closures, defensive record copies/subrecord parsing, script-expression parsing and
navigation projection arrays. Wall-stack samples still identify creature motion,
native route queries, particle publication and finalizers. An additional companion
fixture stops at unowned actor-launch history; complete combat remains open.

Next: remove measured source-query allocations and trace native query/finalizer
cost before another actual-resolution release measurement. The current Windows
session reports 3840x2160 at 60 Hz; the user's target resolution is pending.
Separate uncapped engine throughput from actual display presentation. Retain shadows, particles,
actors, collision and authoritative simulation. Complete ordinary movement,
exterior streaming and combat windows with median/p95/p99 frame times and late
frame counts before accepting 144 Hz.
Keep unsupported SpeedTree/Sandbox and other behavior visible. Then resume the
remaining ordinary Doc/Goodsprings lessons and campaign coverage.
