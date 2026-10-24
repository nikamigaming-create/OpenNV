# Current work

Updated 2026-10-24. One active milestone: fresh flat New Vegas through Doc Mitchell,
Sunny's tutorial/wells, ingredients and campfire cooking, admitted access and
save/resume. Follow docs/implementation-plan.md. Recording is off. Owned inputs
and immutable saves are read-only. No campaign, XR or retail parity completion.

## Baseline and ownership

PR302 merged as74c717287f60ba26e6297326c2e23e935448da6f at08:50:54 UTC. Local main,
origin/main and live remote main were verified equal and clean. Its full local gate
passed08:44:11 UTC, Gate0/Diff0,2524 public files, no source/added-file/protected-save
drift. The selected owned ammo audit passed (source value1, unknown weight).
All five exact-head hosted checks are now successful; runtime completed08:57 UTC.
GitHub had immediately merged the requested auto-merge while runtime was still
running. This timing mistake is not a replacement for checking all five results;
future publication must verify success before requesting merge. Independent
exact-remote-patch review was clear. The completed branch was preserved in a
verified recovery bundle and its exact merged local/remote refs removed.

Active branch: codex/fnv-creature-turning, from synchronized main. One coordinator
owns source integration, ordinary input, runtime, builds and Git. Writer production
handoffs are complete/frozen; no writer input/build/Git job is active. Smoke18
normally quit through observed requests115/116, runner exit0. Sole corrected
smoke19 PID97568 / runner83303 used ordinary title Load/Slot/LoadSelected requests
1-3 for immutable8c8de602, then normally quit through requests11/12, runner exit0.
Corrected smoke20 PID166732 / runner26274 loaded the same immutable slot
through ordinary title controls and normally quit through requests75/76, exit0.
No runtime is active in this lane. After a genuine gecko defeat, ordinary Load
restored the earned checkpoint in the same process. Source Stimpak use, shotgun
equipment/reload and F5 produced preparation slot eb57653da03748e4b4f325ceef5d3e14
at09:43:06 UTC with100HP. Ordinary aim/fire then killed16aefe (20 to-17 health),
consuming one loaded shell. Ordinary movement passed a bounded planner failure
without changing collision; source ACTI activation then acquired Broc Flower and
completed the ingredient objective. Both ingredients and Meat1 were read back.
The subsequent F5 failed, so the kill/flower pickup remain unsaved and are not
claimed as durable progress. Publication failures stayed0. No state injection,
recording or build occurred while live.
The denied optional writer follow-up remains unexecuted and is not being retried.

## Current implementation and exact blocker

Main now accepts absent optional AMMO DAT2 as unknown weight while preserving
required DATA value. Present DAT2 remains strict; hardcore weight admission and
weapon behavior were not waived. Actual047419 AmmoBloatflyDart has DATA13/value1
and no DAT2. The source-compatible repair allowed all four reached creatures to
initialize inventory and publish genuine POBA once.

Main also retains qualified creature preselection Sandbox admission failures and
stopped motion prefixes, with source hashes, consumed event history, selection
attempts, pose, clocks and RNG. Detached construction stages authority until real
publication. Independent combat/script/audio/pose failures still refuse capture.
This does not implement Eat or creature locomotion merely by persisting a fault.

Actual smoke18 state114 showed selectionAttempts1/POBArev1 on all four168c25-28.
168c26/27/28 additionally failed the real aggro-warning turn owner against source
actor1073e8, not the player.168c27's separate Eat candidate failure therefore could
not be saved through the Sandbox owner. Later F5 failed; no admission was relaxed.
The schoolyard obstruction was a real closed gate1076bf: ordinary activation
opened it and subsequent movement passed it. No collision repair was needed.

The new batch implements the qualified creature turning getter: finite authored
TNAM belongs to Stats inheritance; exact zero (including signed zero) selects the
current fCreatureDefaultTurningSpeed setting. No numeric fallback is hardcoded.
The existing owned-default owner resolves the setting from winning GMST or owned
executable defaults. Turn integration refreshes the fallback at actual turns
rather than freezing its value during envelope setup. Negative/nonfinite values
remain refused. Existing historical fault receipts are not silently cleared.

Connected compile, synthetic movement and owned creature getter checks passed.
Actual smoke19 bound all four creatures, advanced warning timing to19.844 seconds
without a zero-turn refusal, and produced source package motion on168c25. The next
grouped blocker is ordinary weapon default255 on168c26/27/28; F5 correctly failed.
Eat remains an independent visible failure on168c27. No save gate was weakened.

The grouped correction now maps the qualified ordinary pistol default to AttackRight
and binds model-owner KFFZ attack overrides. Bare assigned names use SpecialAnims;
explicit source paths remain explicit. Matching overrides take precedence, unrelated
declarations allow ordinary lookup, and malformed/missing matching overrides refuse.
Base attack variants remain separate from up/down. Cache identity includes the
actual declaration set so shared skeleton directories cannot merge actor choices.
Native attack variants retain their existing RNG/event/cold owner; Hit is already
the ranged discharge event and was not aliased or replayed. General thrown/mine,
special-action and other unqualified default groups remain refused.

Scope is the turn helper, shared movement/attack binding, creature appearance and
animation resource/weapon declarations plus their existing probes. No separate
proof project. Grouped compile and selected owned binding passed, as did existing
animation/source contracts including unrelated-override fallback, malformed-match
refusal and cache isolation. Actual smoke20 state5 bound all four to source base,
_2/_3 attack paths with combat errors null. Attack attempts advanced, but all shots
were held by the existing friendly-spread guard at54.5478 median degrees
(unbounded-spread). No projectile emission/damage or firing parity is claimed.
The same F5 still refused:168c27 has both the Eat failure and active combat.
The source spread calculation/guard is the next actual shared firing dependency;
do not clamp accuracy, skip friend checks or capture combat as Sandbox.
The actual launch-radius/Euler sampler is qualified independently of the old
empirical median conversion. NPC aim-chase is separate retained angular state;
its source constructor starts at zero, which is not proof of a permanent zero
or permission to reset old/cold state. Its producer/retained integration remains
the next firing owner. Player aim/camera wobble and exact random rounding remain
separate unqualified boundaries.

Smoke20 state70 also reached QJMelissaRef0e5e51/PACK0e6343's editor dialogue wait.
The source persistent parent CELL0846ea and merged exterior scene share world
0da726; strict cell identity rejected their common coordinate space and blocked
F5 independently. The grouped correction now uses the existing TravelRegion
owner for dialogue wait/trigger/player-cell admission and editor travel. Exterior
cells share only their actual world; interiors remain separate. Native floor,
capsule movement and arrival predicates remain authoritative. Different-region
portal routes are still refused. Connected build passed with0 warnings/errors,
travel contracts passed and the selected owned Melissa coordinate audit passed;
the initial sandbox formatter could not open its build-host pipe, while the
approved host formatter/build/checks completed successfully. This new native
correction has not been replayed and does not remove168c27's
independent blocker. No broad replay is being launched expecting F5 to pass.
The broad checkpoint still needs durable Broc/campfire acceptance.

This independent turning/attack-binding/coordinate-admission batch passed the
full required runtime gate at10:53:33 UTC: Gate0/Diff0,2525 public files and no
source/added-file/protected-save drift. Independent runtime source review is clear.
Publication is proceeding; no main acceptance is claimed yet. The
next firing work must retain actual process aim-chase state and use the qualified
launch extent/sampler without resetting historical actors, changing friend
admission or inventing player wobble. Original saves remain unchanged.
Collect related blockers and fix together. Keep Eat unsupported unless its real
actor inventory/effect/animation/persistence owners exist. General KFFZ locomotion
override support, full turn integration/retail timing, four-actor save/cold and
campfire acceptance remain open.

## Durable ordinary progress

The genuine fresh character completed Doc stage200/rewards, saloon entry/dialogue,
Sunny's rifle/reload/bottle lesson, all seven tutorial gecko kills and resolved
death dispatch, and Gecko Meat looting. Sunny survived with actual retained limb
damage; settler10b3de died to10a212. Do not fabricate rescue or survival.

CompleteQuest is accepted on main through ordinary Sunny choice INFO15d97e:
VCG02 completed while stage50/running/active were preserved, source50XP advanced
20 to70 (still level1), objective60 completed and VCG03 started atstage10.
Immutable24108482708c43d98fdda49be9606f51 preserves this ordinary transition.

Immutable5e54e5f96c04498b82ec8f1415422055 preserves actual scripted ACTI15d982
Xander Root acquisition,79HP, GeckoMeat1 and VCG03stage10/objective10 incomplete.
Durable intermediate8c8de60245f14fd391b50e8b907c2ea4 was ordinary F5 at
08:04:46 UTC, v60/map0e1aa8/79HP, XanderRoot1+Meat1, no Broc. It preceded native
binding of the four affected creatures and is not their save acceptance.
Newest preparation slot eb57653da03748e4b4f325ceef5d3e14 retains ordinary Stimpak
use/equipment/reload at100HP, shotgun loaded2/reserve35. It is also an intermediate
checkpoint, not four-actor capture or Broc/campfire acceptance.
Smoke17 actually harvested Broc through ACTI15d981 and showed both ingredients
and the return objective, but a later save refused; that unsaved result was lost
through normal Quit. Gather Broc again through ordinary source activation.

Earlier typed pending-start and deferred-package boundaries passed genuine F5,
Load/resave and separate-process Continue/F5. Consumed POBA/assignment and actual
RNG/clock prefixes were preserved; timed blink draws explain observed progression.
These are bounded persistence capabilities, not general actor or campaign parity.

## Operating constraints and preserved holds

- Preserve the original slot4dc878ab017f4e75a41f80d213387284 at SHA256
  7B366B739C51102CCF62AD2C8CEE25D878EE8FAC7032A2C00B56AE36C9655F14 and every
  earned immutable slot. Evidence remains private under local/fnv-wells-fixed/.
- One local runtime/build/input owner. No build while gameplay is live; use normal
  Quit and reconcile runner exit. New runs retain the default zero-publication-loss
  guard; no counter/deadline reset or inherited recovery acknowledgement.
- Supported input leases are20-1000ms. Delivered receipts are not gameplay results.
  Observe paused=false/modalInput=false before actions. Preserve reload completion
  before opening menus. Real collision and player deaths require ordinary recovery.
- Publication requires the full repository gate, selected owned-data audit,
  diff check, checked PR, exact-head hosted success, then normal non-force merge
  and clean main/origin/live verification. Keep private source evidence and saves out.
- No unchanged refused route retries, named-location exceptions, actor discard,
  fabricated motion, quest progress or survival. No new workers or retail dumps.
- Source Eat, other Sandbox activities, full combat/animation/material/UI/audio,
  physical XR and retail parity remain incomplete. Historical faults remain visible.
- Physical NOTE/Karma/relationships, static ACTI defaults, remaining witness/crime
  effects, actor-max-health traps and Generator ordering stay at recorded gaps.
  Website copy remains outside this gameplay batch. Preserve PR242/269 recovery.
