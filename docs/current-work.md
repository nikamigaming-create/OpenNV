# Current work

## Owner and delivery

Root owns scripted retail/Godot input, bounded capture, integration, packaging
and publication. User-authorized Sol-medium workers prepare and cross-review
shared-system repairs. No computer use or new test suites. Preserve unrelated
classic asset-import edits.

Active branch: `codex/gameplay-actions-quest-and-scene-owners`, based on checked
PR340 (`9e034a0a`). Local main was verified equal to origin/main after merge.
PR341 remains unmerged. Its initial 115-file revision `e71b60da` passes all five
CI checks, but visible-target VATS smoke exposed further blockers. Subsequent
integrated UI, texture-sentinel and navigation-query repairs pass the full
`gameplay-visible-final-gate`. Detailed-only per-part VATS instrumentation then
supported a successful two-shot UI execution and cold restore. The fresh
publication full gate passed (`gameplay-publication-final-gate.log`); no
further gameplay source edits are planned before publication. Performance gains
and native visibility/outcome correspondence remain unverified.
The owned water audit covers 17 qualifying child worldspaces; vanilla has no
legacy XCLL rows. Root owns the remaining live diagnosis, packaging/performance
checks and checked PR merge.

## Current route and target

The user target is native 3840x2160 at sustained 60 FPS during movement,
combat, interiors and transitions. Both render surfaces were configured at
native 4K. Sustained performance and faster-than-retail behavior remain open.

Both games actually walked out of Goodsprings and reached Jean Sky Diving.
OpenNV discovered Jean's fast-travel marker. After the Sandbox Eat repair,
ordinary F5 and cold reload pass at checkpoint
`87b8854192fd49309e89476989fc6fe4`. The onward ordinary route and Freeside/Strip
door chain remain open. Inventory contains every source map marker, interior
and XTEL edge; enumeration does not mean visited, playable or verified.

Separate diagnostic player-only authored arrivals, with matched yaw and time,
have captured Primm, Nipton, Novac, Boulder City, McCarran and Freeside North.
They use independent saves, AI and weather; they are not fast travel, continuous
campaign travel or parity certification. Freeside North completed after the
helper reported more than five seconds of stale state during loading and then
recovered fresh loaded state. This does not pass load responsiveness. Freeside
East arrival is underway.

Ordinary E on McCarran gate `1287f5` now completes the exterior-to-courtyard
transfer `0dac34` -> `148dc4` in 13.813 seconds. The guard preserves the world's
own DNAM water when UseWater is clear; actual water inheritance remains
unsupported. The paired courtyard frame shows a severe exposure difference
and 15 reference divergences. Successful transfer does not establish a matched
or complete courtyard.

Retail's character-confirmation menu interrupted departure. It was completed
through ordinary input. The private walker now stops on stalled rendering or
unresponsive look input instead of repeatedly steering through a blocked menu.
Native modal-menu telemetry remains incomplete. Capture is off outside explicit
checks and temporary raw frames are removed after inspection.

## Verified ordinary gameplay

- Pip-Boy rows equip the 9mm pistol and Varmint Rifle with actual ammo changes.
- Two queued VATS actions in the preceding build spend ammo/AP and damage the
  target. One physical head impact differs from the queued torso selection.
  F5 and cold reload retain ammunition and damage; outcome parity remains open.
- Actual ammo breakdown consumes one 20-gauge round and produces 18 powder,
  one hull, one primer and 26 lead. F5 and cold reload retain the quantities.
  Native recipe/quantity input was also exercised; UI parity remains open.
- Ordinary repair-kit use consumes one kit, changes the same rifle instance
  from 27.3% to 37.3% condition and records one completed script receipt. F5 and
  cold reload pass: three kits remain, the same rifle retains 37.3% condition,
  and one completed receipt is restored without replay.
- The GPU VATS path prepared 24 parts in about 2508 ms and reached Selecting
  without the cloned-label failure. Visible labels render. Selecting torso with
  Up then Enter queued actor `106b16` part 0 at 54.704063%; two shots completed,
  ammo changed 5 -> 3, 70 AP was spent and health changed
  60 -> 45.68938 -> 31.402279, with no error or pending publications. The actual
  pose-freeze witness stayed identical over one second. Both physical impacts
  hit head part 1 despite torso selection; retail outcome parity remains open.
- F5 checkpoint `58154d0fcad24219a19dbe183ca91783` cold-loaded in PID21820 with
  ammo 3, weapon condition 0.2699999, target health 31.402279 and no opening
  error. Prior Jean, crafting and repair-kit cold persistence proofs remain valid.
- Earlier 0% on the unavailable default leg did not establish a general mask
  pipeline failure. Measured fragment visibility and its native correspondence
  remain an independent unresolved evidence lane.

These are selected gameplay observations with incompletely matched save,
actor, clock and camera state. They do not establish retail parity.

## Integrated source

The batch repairs shared map cursor/pan/color, seated look, player/NPC health
and limbs, VATS namespaces/execution/publication, world-axis NIF nodes,
composed sunlight, atomic crafting, equipment-lock instance preservation,
inventory shortcuts/row actions, quest failure persistence, weapon selection,
mutable weapon lists, repair eligibility and qualified null-quest predicates.
It adds source VATS layout/desktop input, exact GPU mask reduction, cheap
pending-autosave audio witnesses and atomic instant Start-script ingestion.
Timed/mixed effects and downward weapon-damage writes remain unsupported.

Later smoke repairs cover staged menu-sound pitch/variant randomness, numeric
prefixes in source texture expressions and requester-scoped cloned UI names.
Jean exposed a real Sandbox Eat owner bug: phase publication replaced the
activity record while idle admission retained its predecessor. The repair
propagates the published record and preserves identity on same-phase reads.
All physical-owner checks remain. The rebuilt ordinary Jean route now commits
F5 and cold-loads successfully.

The immutable source NAVM spatial index is integrated. Its full spatial-index
gate passes; the selected owned route retains exactly the same 228 waypoints
and projection errors. This establishes the checked navigation result, not a
live performance improvement. Indexed live movement has been measured below;
its gain over an equivalent earlier run remains unverified.

A reviewed single-controller pose path removes redundant blend passes without
changing samples, arithmetic, events or physics frequency. Posed-bound sampling
reuses its world-transform read only within the synchronous invocation.
The moving measurements below do not isolate a gain from this pose change.

Additional integrated repairs restore native TileImage initialization defaults:
omitted image RGB channels retain 255, scoped to image tiles. The owned UI
audit passes with `ownedImages=1`, including XML and runtime overrides. The
beard texture fix preserves the source unset index `-1` without inventing a
shape target. It addresses the appearance failure that left actor `0f570d`
missing. The rebuilt ordinary gate visit now materializes that NPC: 16 actors
instead of 15, and 14 reference divergences instead of 15. Navigation diagnostics defer
immutable sweep-snapshot allocation while retaining immediate native/source
reads, and step admission reuses the exact synchronous obstructed sweep.
Independent source review preserves support, clearance and mismatch fallbacks;
the full gate passes, while live performance gains remain unverified.

## Performance and next owners

The optimized six-minute moving native-4K run averaged 51.2097 FPS. Its first
minute averaged 57.91 FPS and its last minute 42.81 FPS. Sustained 4K60 remains
open; the decline is not attributed to one subsystem. A subsequent indexed
ordinary movement segment lasted 179.468 seconds and averaged 55.2188 FPS.
Retail averaged 59.6318 FPS but stopped responding to camera input after
waypoint 32. The candidate reached waypoint 122 before the deadline; this is
not a matched-route speed comparison or an ordinary Primm arrival. The latest
query-reuse package has no verified live gain yet.

The pending-autosave path still needs a fresh interaction-driven check; a
checkpoint without a pending request does not exercise it.

- Root: publish the checked follow-up, finish performance/live checks,
  then update/check PR341, merge and verify local/origin main. Native VATS
  visibility/outcome comparison remains open after the working UI/cold case.
- Reviewed private next batch: source recipe/quantity UI, resident Follow and
  Accompany ownership, and Sleep door events with an empty effective lock set.
  These are not integrated or live-verified. The regional imposter owner packet
  requires actual regional temporary-CELL/script attachment and remains private
  and not integration-ready. Nonempty lock events and exterior scopes remain
  unqualified. No guessed CELL-public-state write is proposed.
- Ordinary player T/wait has no current menu binding. VATS cinematics/control
  paths, local-map rendering, LOD seams, full animation/actor process behavior,
  radio persistence, room completeness and campaign/plugin execution remain
  explicit gaps.

The exact GPU mask reduction passes its hardware audit. The broader optional
GPU audit then fails an obsolete cell-environment fixture; its reviewed fixture
correction remains private. Do not describe the entire optional audit as passed.
No tester-ready, campaign-complete, physical-XR or retail-parity claim is made.
