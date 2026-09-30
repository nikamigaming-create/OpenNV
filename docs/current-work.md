# Current work

## Active objective

Prioritize ordinary flat gameplay and polish across scripts, autonomous actors,
combat, interactions, travel and saves. Preserve shared VR behavior and fix
regressions there; detailed VR presentation is not the current focus. Work
without subagents. Follow the [flat work order](flat-gameplay-plan.md) and
[implementation plan](implementation-plan.md). All 36 broad recovery
requirements remain open; no whole-game or retail-parity completion is claimed.

## Verified runtime

Source object animation now binds PlayGroup and IsAnimPlaying to each resident
reference's authored NIF manager. Queued and immediate selection, authored loop
starts, source text-key order, callback changes and independent clocks have native
checks. Alternative looping sequences no longer reject the selected window.
Actor skeleton groups and ambiguous/absent object groups remain explicit failures.
Save v22 retains selected object clocks, consumed start events and pending groups;
v21 and earlier supported saves still load. Cold restoration, warm eviction,
replacement presentation and source mismatch checks pass.

Conservative recovery resumes only exact legacy missing-PlayGroup faults whose
unchanged block proves that the failed command preceded any other mutation.
All 51 selected saved plant failures recover in the owned-data audit. All six
source script/model families grant their authored rewards once through native
activation, retain destroyed state and reject duplicate rewards across cold
restoration. The selected window supplies the seventh tested source model.
The complete runtime gate and reference-presentation regression check pass.

An ordinary exported flat Continue on a copy of the genuine Primm checkpoint
recovers ten resident plant faults and 23 prior read faults. Ordinary traversal,
mouse aim and activation harvest reference FalloutNV.esm:157e35 once, adding one
Coyote Tobacco Chew. Manual saving retains its destroyed flag, source local and
completed Forward animation in v22. Cold exported Continue restores all three;
another ordinary activation attempt leaves the inventory count at one.
The bot now observes destroyed state as an interaction outcome; some source
contact aiming and final navigation segments still need work.

## Next owners

Continue the actual flat run's earliest script and actor failures. Six source
creature OnLoad blocks reach missing Kill; source script death must use shared
health, inventory, delayed death events and persistence without inventing a
killer. Essential recovery and additional command parameters need their own
source-backed behavior. NPC radio, creature package condition 136 and further
patrol/sandbox/eat/sleep procedures remain visible failures. Preserve all quest,
combat, mod and campaign objectives while fixing those owners.

Weapons still need the Flamer's source strip-particle decoder, thrown recovery,
remaining projectile effects, mines/remote triggers, bare fists and ammunition
variants. Blast rules, hit events, death XP, leveling/perks, radiation/addiction,
crime, crafting/barter completeness, JAM/MCM and TTW remain open. Do not replace
these requirements with selected component passes.

Exported Quit Game has previously reported native heap corruption; shutdown
stability remains unproven. Streaming spikes and broad rendering/audio fidelity
also remain open. Recording stays off during development.

## Candidate and private continuation

The public-facing local experimental candidate remains
`local/releases/OpenNV-0.1.0-experimental.20260927.5-windows-x64`, from runtime
commit `06ffd2cadd1f0e0a180882a666489503bea4aa30`. It predates these repairs.
Update the export after stable publication. Retain the requested September 27
weapon and companion reels; they are selected simulator/flat footage.

Current private flat checks are in
`tmp/development-lab/flat-polish-20260930-animation/` and its `-cold` continuation.
Selected native audit logs are `tmp/object-animation-owned.log`,
`tmp/object-animation-native-contract.log`,
`tmp/object-animation-presentation-regression.log` and
`tmp/object-animation-runtime-gate.log`. These are private diagnostics.

Do not change `local/playtest-20260927-world/save.json` or
`local/playtest-20260920-companion/save.json`. Both still hash to
`800A37CC6C97C543393A544229C4E71ACB213B45C12FC4584EF3E70E94E2FBB7`.
Do not retry the previously rejected temporary-image deletions.
