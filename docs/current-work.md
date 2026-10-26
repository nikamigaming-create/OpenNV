# Current work

## Active owner: exterior performance and Doc Mitchell tutorial acceptance

The user requires one maintained `main`, reliable exterior play and all authored
Doc Mitchell tutorials. Whole-game completion remains unverified.

Main retains immutable CELL definitions per plugin stack, indexes resident
references and bounds repeated failed materialization. Unsupported SpeedTree
geometry remains an explicit divergence. Terrain shadows are retained. Dynamic
bodies retain their final live physics before teardown. Owned presentation and
physics audits, the full runtime gate and hosted checks pass for those owners.

The current repair branch adds:

- Resumable native capsule samples during lattice expansion and smoothing,
  retaining collision, floor, actor-size and route checks. Native scheduling and
  full locomotion checks pass.
- Authoritative tutorial death/health/limb/sneaking/equipped-condition/statistic
  queries. The 43 statistic names come from the owned executable. Legacy player
  record errors retry only a proven initial read with no possible effect replay.
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
also completes F5. The final release passes cold Continue, pause/resume and another F5 with one
retained recovery receipt, a healthy tutorial clock, no HUD error and no cactus
resolution failure. An ordinary checkpoint load visibly displays the movement prompt with mapped
W/A/S/D labels in the rendered room. The final full gate passes.

Recording-free final release diagnostics use a private Doc-exit save, 49 resident
cells and Forward+ at 1280x720. The warmed stationary sample measures 49 FPS,
median draw interval 19.88 ms and p95 27.84 ms. This is a selected diagnostic,
not a controlled comparison, frame-budget acceptance or retail parity. Source
route work, particle publication/finalizers, streaming and combat remain open.

Next: publish the checked repair PR, merge to main and verify both main refs
agree. Continue ordinary Goodsprings lessons and campaign progression from
the next reached source/runtime failure. Missing SpeedTree geometry, unsupported
Sandbox flags and other logged owners remain visible and incomplete.
