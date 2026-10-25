# Current work

Updated 2026-10-25. Priority: playable flat New Vegas, then Fallout3. Classic
reconstruction and launcher polish remain parked. Character preservation and
old-save migration are not pre-1.0 acceptance goals. Recording is off.

## Baseline and active batch

Accepted main: 0d35117d6abe18c4faa376e350eb7af5b9cb8844, PR310. All five hosted
checks succeeded before normal merge. Local main/origin/live equality was verified.
Active branch: codex/fnv-food-effect-followthrough. One coordinator owns source,
Git, builds and input. No game or writer is running; smoke37 runner exited0.
The food follow-through is ready for checked publication. Required final gate and selected owned audit finished2026-10-25T01:11:49.5514599Z with Gate0/Owned0/Diff0 and OPENNV_CSHARP_GODOT_GATE_PASS.
Held PR242/269 remain outside this task.

## Implemented capability

MGEF resistance and Painless declarations now reach shared ingestion validation.
Gecko Meat applies healing and resistance-adjusted radiation through the existing
player vitals and separate actor-value pools. Creature Medicine/Survival use their
source noncombat base values, independently of combat skill. Actor resistance
uses current Endurance, source settings and admitted ability/armor/perk modifiers.

Eating queries source procedure branches with actual ancestor, used-item and
subject predicates. A valid no-file result preserves the actual base pose; a
required missing file still refuses. Declared ANIO geometry binds to its source
KF with retained source hashes and source visibility controlling the final pose. Last-idle-history conditions remain with their real owner; the current meal is not substituted for that history.
No source actor, item, seat or animation is fabricated.

## Verification and actual outcome

Debug build: zero warnings/errors. Focused tests pass independent player/actor
radiation, creature scaling, transaction rollback, timers and no-file receipt
invariants. Selected owned audit passes actual cactus, Stimpak and Gecko Meat.

Ordinary smoke37 Continue opened Pip-Boy and consumed Gecko Meat through Use:
item removed, actionError null, healing/modifier timers active, radiation0->2.7.
Publication failures remained0. A normal combat death used ordinary Load recovery.
The subsequent actor-food approach spent time in native route planning; bounded
manual movement then contacted canyon rock107109. Two obstructed segments were
stopped. Only discovered Goodsprings travel was available in the map; no travel
was injected. Requests48/49 normally quit, runner exit0.

Native actor consumption and campfire completion remain UNVERIFIED. The next
playable outcome is a supported native actor meal and the remaining well /
ingredient/campfire journey, then Primm/ED-E. Do not repeat the blocked rock segment
or infer an engine collision defect from ordinary contact. Multiple carried food
kinds, conditional ownership/resistance, world stacks, lethal/dispel, radiation
illness and Hardcore needs remain unsupported. No campaign or retail-parity claim.

## Parked operation

Owner approved the specific Sandbox current-location anchor/save-format change,
but approval review rejected it and it did not execute. Do not retry, route around
it or ask for identical approval. This meal/effect batch does not reinterpret that
anchor. Classic follow-up remains preserved at59aecc9d with no classic work active.
