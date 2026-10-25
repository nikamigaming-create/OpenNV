# Current work

Updated 2026-10-25. Immediate priority is fully playable flat Fallout: New Vegas,
then Fallout 3. Classic Fallout 1/2 reconstruction, Arroyo presentation and launcher
polish are parked by owner direction. Preserve their work; do not delete it or
continue classic-only implementation/captures. Character checkpoint preservation
and old-save migration are not acceptance goals before1.0. Recording is off.

## Baseline and sole ownership

Accepted main is39d95f457b0b2294fe43270abea8e9f33607da5e (PR309). All five hosted
checks succeeded on exact ed201772ce49cdea51322fa3c7778dbd83a1f343 before normal
merge. Local main, origin/main and live remote were verified equal and clean.
The required local gate ended OPENNV_CSHARP_GODOT_GATE_PASS at
2026-10-24T22:33:51.4342609Z, Gate0/Diff0.

Active branch: codex/fnv-gameplay-followthrough, based on origin/main. One
coordinator owns source integration, Git, builds and ordinary game input. No
runtime/build/writer job remains active. No new workers or competing runtime.
Unrelated held PR242/269 remain untouched.

## Active New Vegas outcome

Complete ordinary later-well, ingredient and campfire play, then the next campaign
loop toward Primm/ED-E. Implement connected missing gameplay owners in a broad
batch before one integrated smoke; collect failures and fix them together. Do not
repeat accepted opening content solely for evidence, preserve a particular test
character, treat normal deaths/navigation limits as engine defects, or fabricate
quest/world state. Independently ready gameplay should not wait on exact visual
parity or unrelated unsupported behavior.

The integrated smoke36 normally quit through requests20/21, runner exit0.
It loaded ordinary Continue, exercised one normal death recovery, opened Pip-Boy
and consumed one Stimpak (9 to8) without an action error. Native player movement
reached Sunny and opened her source dialogue. Publication failures stayed0.
No campfire completion or native actor Eat consumption is claimed. The run is
closed; no runtime remains.

Current uncommitted batch implements actor-owned ingestible transactions, timed
health and reversible SPECIAL effects, source-stat reads, script EquipItem aid
consumption, and selected Sandbox Eat approach/pose/one-use continuation. It does
not change the parked Sandbox location anchor. Debug runtime/probe build passed
with zero warnings/errors. Focused existing ingestible contracts pass actor/player
inventory isolation, contention for one world item, timed effects, modifier expiry
and cold remainder. Existing owned-data audit passes actual cactus food health /
Endurance effects and Stimpak behavior. Formatter completed successfully. Required publication gate completed
2026-10-25T00:02:04.7537659Z: Gate0/Diff0 and final
OPENNV_CSHARP_GODOT_GATE_PASS. The earlier LF-format failure was corrected before
this passing run; no runtime behavior was waived.

The integrated smoke completed the input/item regression scope above. Actor Eat
and campfire acceptance remain open. Actual Gecko Meat use refused effect020516
(DamageRadiationLevel) before consuming the item or adding effects. This needs its
radiation effect/presentation owner; do not remove the refusal or invent a value. Remaining supported-scope boundaries
are visible: creature noncombat skill scaling, missing source eating IDLE/ANIO,
multiple carried food kinds, scripted/conditional-owned food, world stacks, lethal
item effects and unimplemented Hardcore needs are refused rather than guessed.
The reached prior Eat source was CREA168c27 / world1210c9 / ALCH0ec01d; that old
observation is not a fresh reproduction. Recipe/ingredient interactions retain
the existing source script and inventory owners.

## Parked persistence operation

Owner approved the specific Sandbox current-location anchor/save-format change
without migration. Approval review nevertheless rejected that operation; it did
not execute. Do not repeat approval wording, retry it or route around the denial.
No anchor/schema workaround is selected. Continue independent gameplay code.

## Preserved classic checkpoint

PR309 contains the accepted renderer/material, source critter/event and conditional
combat foundation. It does not claim ordinary complete combat or campaign parity.
Further work is preserved locally on codex/classic-actor-locomotion at59aecc9d,
unpublished WIP: actor walk state/FRM clock/routes/live hex bindings and fixed-view
diagnostics. Debug build passed0warnings/errors. Its one native encounter reached
actor animation registration refusal before a walk/attack commit (HP44/actions0/
deaths0/XP0). No runtime remains. Do not resume that lane until reprioritized.

Six native camera views confirmed an architectural temple image flattened onto
ground beneath the real facade; that ground decomposition and black-edge layer
remain unresolved. Requested views are in Library; private diagnosis
libfile_e6539de821e4819186baf8e5ee1cc713 is version3. Original captures and ignored
staging are retained. No further classic art review or capture is active.
