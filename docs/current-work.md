# Current work

## Active objective

Complete actual JAM/MCM gameplay first, then the combined TTW campaign from
Fallout 3's opening through its authored connection to New Vegas. Folder
registration and isolated scenes do not meet this objective. Prioritize flat
play while preserving shared VR behavior; detailed VR presentation follows.
Classic flat presentation must use winning Fallout/mod screens and controls;
the optional Nikami experience adds enhancements to the same gameplay owners.
Work without subagents. Follow the [mod implementation](mod-compatibility.md),
[implementation plan](implementation-plan.md) and [flat work order](flat-gameplay-plan.md).
All 36 broad recovery requirements remain open; no whole-game or retail-parity
completion is claimed.

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

Kill/KillActor with an optional killer now use the same health/death inventory
transition as combat. An omitted killer remains unknown, including delayed
OnDeath and cold saves. Repeated calls on a corpse add no loot or death event.
Native presentation detects a scripted death and activates its source ragdoll.
The owned native fixture recovers all 14 selected old Kill failures and checks
source corpse scripts, a living creature's ragdoll and its cold continuation.
Inherited actor script locals, including qualified reads, now resolve through
the retained world template owner. Synthetic checks cover both ownership paths,
filtered death events, conserved loot and rejected earlier mutations. Essential
recovery, player script death and limb/cause parameters remain visible boundaries.

An ordinary exported flat Continue on a copy of the genuine Primm checkpoint
recovers ten resident plant faults and 23 prior read faults. Ordinary traversal,
mouse aim and activation harvest reference FalloutNV.esm:157e35 once, adding one
Coyote Tobacco Chew. Manual saving retains its destroyed flag, source local and
completed Forward animation in v22. Cold exported Continue restores all three;
another ordinary activation attempt leaves the inventory count at one.
A later exported flat Continue also resumes 14 resident Kill faults; their
corpse locals and destroyed flags complete, and Tobacco remains at one.
The bot now observes destroyed state as an interaction outcome; some source
contact aiming and final navigation segments still need work.

Session retirement now drains title indexing and exterior source readers before
releasing the world, records and detached model prototypes. Ordinary exported
flat Quit on the copied checkpoint exits with code 0 after releasing 349
prototypes; the previous direct Quit reproduced native heap corruption.
Main-menu return, another Continue, fresh-title Quit and native window close
also exit cleanly, with the source checkpoint unchanged.
Cell destruction releases its compositor pipeline, shader and samplers on the
rendering thread even when managed references retain the effect. Four successive
synthetic rendered replacements and repeated/unused release checks pass without
GPU RID leaks. The full runtime gate and owned script-death regression pass.
Two ObjectDB exit warnings remain visible in the selected owned runs; their
owners still need diagnosis. One repeated exported reload also crashed during
native triangle-mesh construction before gameplay resumed; a subsequent complete
session audit passed. This intermittent construction failure remains open.

## Next owners

Reproduce JAM's reached execution failures on current main before extending the
ordinary native script, UI, event and gameplay owners. Typed strings, INI and
auxiliary state, UIO injection and UI component state already exist; they do not
establish a working MCM menu or any complete JAM module. Implement the next
reached missing owner, including ordinary input and persistent effects. Keep
the complete TTW opening, campaign progression, travel and dependency semantics
in scope; do not remove launch gates on the strength of source audits.

Preserve the actual flat run's remaining script and actor failures. Essential
recovery and additional death-command parameters need their own source-backed
behavior. The reached GetReference Player compiled binding, NPC radio,
creature package condition 136 and further
patrol/sandbox/eat/sleep procedures remain visible failures. Preserve all quest,
combat, mod and campaign objectives while fixing those owners.

Weapons still need the Flamer's source strip-particle decoder, thrown recovery,
remaining projectile effects, mines/remote triggers, bare fists and ammunition
variants. Blast rules, hit events, death XP, leveling/perks, radiation/addiction,
crime, crafting/barter completeness, JAM/MCM and TTW remain open. Do not replace
these requirements with selected component passes.

Selected exported flat shutdown paths now pass; broader session stability,
streaming spikes and rendering/audio fidelity remain open. Recording stays off
during development except for a requested visual check.

## Candidate and private continuation

The public-facing local experimental candidate remains
`local/releases/OpenNV-0.1.0-experimental.20260927.5-windows-x64`, from runtime
commit `06ffd2cadd1f0e0a180882a666489503bea4aa30`. It predates these repairs.
Update the dated candidate after stable publication. Retain the requested September 27
weapon and companion reels; they are selected simulator/flat footage.
The refreshed Windows development executable is
`tmp/development-runtime/windows/OpenNV.exe` and includes the September 30 repairs.

Current private flat checks are in
`tmp/development-lab/flat-polish-20260930-animation/` and its `-cold` continuation.
Selected native audit logs are `tmp/object-animation-owned.log`,
`tmp/object-animation-native-contract.log`,
`tmp/object-animation-presentation-regression.log` and
`tmp/object-animation-runtime-gate.log`. These are private diagnostics.
Script death checks are in `tmp/scripted-death-owned.log`,
`tmp/scripted-death-contract.log` and `tmp/scripted-death-runtime-gate.log`.
Session checks are in `tmp/native-shutdown-owned.log`,
`tmp/native-shutdown-runtime-gate.log` and `tmp/native-session-retirement/`.
The requested flat screenshot is extracted from the retained September 27
companion clip; it does not show the new September 30 code.

Do not change `local/playtest-20260927-world/save.json` or
`local/playtest-20260920-companion/save.json`. Both still hash to
`800A37CC6C97C543393A544229C4E71ACB213B45C12FC4584EF3E70E94E2FBB7`.
Do not retry the previously rejected temporary-image deletions.
