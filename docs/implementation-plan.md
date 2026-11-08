# OpenNV complete-game implementation and acceptance plan

Updated 2026-11-08. This is the sole active implementation plan. Resumption is
authorized to execute the milestone sequence below (M0 -> M12) with strict
offline-first verification before integrated campaign gates.

## Objective and completion definition

Deliver standalone Fallout: New Vegas, standalone Fallout 3, all their official
DLC, Tale of Two Wastelands and declared supported plugin configurations through
ordinary gameplay, persistent saves and equivalent flat/OpenXR interaction.
Goodsprings and Vault 101/Megaton are intermediate gates, not complete games.
Complete campaigns include every authored ending, optional content, failure and
alternate branch, plus continuing play where the source permits it.

Every loaded object, state transition, event, audio voice, UI element, draw,
frame and final pixel belongs to the parity denominator. Record admission,
function counts, a running executable or plausible screenshots cannot close it.
Unknown layouts, missing entities, unsupported behavior and lost telemetry block
acceptance of the affected scope. Do not average unpassed lanes into a percentage.

Pin game versions, languages, DLC selections, TTW versions, plugin combinations,
platforms, hardware, graphics settings and XR devices in an acceptance matrix.
All official content is required; the matrix must not silently drop difficult
content or lower product targets. Third-party plugins form an open-ended domain:
state exact supported versions/combinations and preserve failures elsewhere.
Finite testing cannot prove universal compatibility with future mods or every
possible event interleaving. Do not call such testing mathematical proof.

The 100% label requires every milestone below to be accepted for the advertised
matrix, with reproducible evidence and no unresolved in-scope obligations.
Newly discovered in-scope behavior reopens acceptance. Classic Fallout and
unrelated ports are outside this plan; preserve their work without diverting it.

## Verified starting point

- Base main is fd90edde (PR352). Branch codex/compiled-script-runtime contains
  uncommitted, unpublished implementation changes. Preserve them.
- The complete private momusdigital/fnv_decomp checkout is pinned at
  91e1b6e8d8bb0f9a33e8cf88e77268757449e0a6, with 6,491 tracked files. Upstream
  byte-matching claims have not been independently reproduced.
- The private KG maps reference entries, OpenNV declarations, dispatch sites,
  guards, audits and neutral contracts. Name/family joins are candidates, never
  equivalence or passing receipts. See [graph contracts](runtime-knowledge-graph.md).
- SCDA framing reads 26,257 fields and 235,750 instructions across the selected
  official FNV/FO3 stacks without framing errors. Operands remain opaque; there
  is no compiled VM execution. See [script contracts](compiled-script-contracts.md).
- Effect-local integer assignment truncation has synthetic and 618 owned
  transient-storage checks. Those checks do not execute quest events.
- Selections contain 628,463 FNV and 930,306 FO3 winning records. The absent FNV
  diagnostic plugin accounts for eight fewer records than the older selection.
  Broad audits retain 15 FNV and 11 FO3 structural/source failures and exit
  nonzero. Classify their causes; do not suppress them to claim readiness.
- The required C#/Godot gate passed on November 7 after the graph writer-lifetime
  repair; log: tmp/knowledge-runtime-gate.log. This does not establish campaigns,
  native-extension execution or physical XR acceptance. No new packaged build,
  campaign route or matched-retail acceptance was produced.

Private inputs/outputs are local/fnv-decomp-reference,
local/knowledge-audit-20261107 and local/runtime-knowledge-20261107. The graph
predates this expanded plan; regenerate before using its plan-node coverage.
Keep current diagnostics, not duplicate graphs or proof archives.

## Product and evidence boundaries

One path: owned files -> C# readers and authoritative runtime -> Godot.
C# owns formats, records, state, gameplay, events, telemetry and persistence.
Godot owns presentation, input adaptation and OpenXR. Flat/XR share state and
saves. Use in-process indexes and decoded-resource reuse; transformed retail
resources must never become persistent launch inputs.

Read the complete winning ESM/ESP and loose/BSA graph with declaring-plugin
identity, overrides, deletions, precedence and provenance. Owned inputs are
read-only. Never publish retail assets, saves, binaries, captures or derivatives.
No OpenMW source/runtime dependency. Reduce private reverse engineering to
implementation-neutral contracts; never paste decompiler output into OpenNV.

No hand-placed named residents, proxy actors, metadata-only substitutes,
location-specific success paths, forced stages, skipped commands, fabricated
query defaults or weakened collision. Remove artificial restrictions when
source evidence establishes general behavior. Unsupported behavior must retain
source, call-site and missing-owner context.

## Dependency order and acceptance gates

Work on the next demonstrated production blocker. Each implementation block
must deliver working behavior through existing owners and a regression that
fails without it. KG/inventory expansion alone cannot close a gameplay gate.
Persistence, XR, presentation and performance accompany relevant repairs; their
final milestones do not permit postponing their architecture.

### M0 — Reproducible baseline and retained batch

Pin source/plugin hashes, precedence, runtime revision, configuration, input mode
and test identity. Reuse corpus, quest-graph and capability tools. Classify each
failure as malformed input, missing layout/semantics/owner or test defect with
evidence. Separate standalone FO3, FNV, TTW and supported plugin configurations.
Keep healthy saves privately with the exact writer build.

Exit: a concrete next failure is reproducible and assigned to a production owner.
Review and publish the retained batch before the next implementation branch.
The completed gate need not be rerun solely for this documentation edit; runtime
changes require applicable validation again.

### M1 — Compiled scripts and event execution

Extend FalloutCompiledScript and existing quest/reference/effect script owners
into a general execution path. Establish operand/expression encodings, command
signatures, SCRO/SCRV resolution, local/reference/global storage, numeric rules,
control flow, return values and invocation context from owned fields and neutral
evidence. Implement attachment/lifetime, event scheduling/order, nested calls,
yielding and cold continuation where source behavior requires them.

Compiled-only programs must execute. Text interpretation cannot stand in for
missing SCDA semantics. Unknown instructions retain script ID, offset and
invocation context. Dispatch commands into actual authoritative owners, without
a second simulated state or inference from opcode names.

Exit: synthetic malformed/control-flow/numeric/context regressions pass; owned
programs cover supported encodings and real state transitions; private native
observation checks representative semantics. Every required official program
gap remains tracked until implemented. Framing coverage does not pass M1.

### M2 — Conditions, commands, quests and dialogue

Use FalloutConditionRuntime/Scope/Query, FalloutQuestState/Stages,
FalloutQuestScripts, FalloutReferenceScripts and conversation owners. Complete
state-dependent queries, target binding and every required command's real effect.
Implement start/stop/reset/failure, objectives/rewards, branches, result scripts,
dialogue eligibility, greetings, speech challenges, interruption and ordering.

Exit: no required reached command uses a placeholder or missing owner. Positive
and negative eligibility, alternate choices, repeat/interrupted interactions and
cold continuation produce the expected state and event order. Unvisited branches
remain obligations, not implied successes.

### M3 — Player progression, inventory and services

Complete independent FO3/FNV creation, SPECIAL, skills, tags, traits, levels,
perks and actor-value effects. Complete item variants, ownership, loot, equipped
and loaded weapons, ammunition, durability, repair, crafting and recipes.
Finish barter, restock, healing/repair services, containers, housing acquisition,
upgrades and safe storage. Resolve the observed SetCellOwnership dispatcher gap
through real cell ownership.

Complete locks/lockpicking, terminals/hacking, factions, karma, reputation,
disguise, crime/detection/alarm, radiation, addiction, ingestibles, hardcore needs,
waiting/sleep and time passage. Complete source-authored gambling/minigames and
Caravan, including payouts, failures and persistent consequences.

Exit: ordinary transactions and progression preserve quantities, ownership,
costs, eligibility and consequences through cancellation, failure and cold loads.
Menus cannot report success before the authoritative operation succeeds.

### M4 — Living world, movement, AI and combat

Use RuntimeCoordinator.NativeOwned and existing reference, streaming, physics,
navigation, package, perception and combat owners. Instantiate every eligible
reference as its real entity. Complete interiors/exteriors, doors/portals,
terrain, collision/floors, hazards, encounters/leveled spawning, enable parents,
resets/respawn, ownership and loaded/unloaded time behavior. Navigation follows
native collision; preserve failure where no valid route exists.

Complete actor/creature procedures, schedules, furniture, dialogue approach,
combat decisions and companion recruitment/commands/dismissal/death/continuity.
Complete guns, melee, unarmed, explosives, thrown/special weapons, reload,
spread, projectiles, obstruction, damage, armor, critical/sneak rules, limbs,
effects, ragdolls, death and off-residency consequences. Preserve FO3/FNV rule
differences. Complete FalloutVatsSession targeting, AP/ammo admission, action
order, cancellation, time/camera behavior and presentation.

Exit: player/actor actions survive traversal, combat and time changes without
missing entities, blocked authored paths or divergent state. Include interrupted
actions, destruction, death, respawn and fresh-process restoration.

### M5 — Presentation, audio, UI and controls

Complete owned model/skeleton/animation/skinning/morph/face readers and runtime
presentation; materials, textures, terrain, vegetation, decals, particles,
lighting, shadows, water, weather, sky, LOD and effects. Complete animation
publication, attachments, lipsync, voice, radio/music, positional audio,
attenuation, mixing, pauses and voice lifetime.

Complete title/New Game/load/save, HUD, Pip-Boy, maps, journal, inventory,
dialogue, containers, barter, crafting, terminals, lockpicking, VATS, messages,
endings/credits, settings and mouse/keyboard/controller/XR controls. Preserve
authored localization, text, icons and timing. Focus/pause policy must agree with
state. Missing resources, voices or animations remain explicit defects.

Exit: all required presentation functions during ordinary gameplay; verify
output through M11. A visually plausible substitute does not establish parity.

### M6 — Complete persistence and lifecycle

Extend FalloutNativeCampaignSave and restoration owners for every subsystem:
script locals/pending execution, quests, relationships, inventory variants,
actor/package state, timers/effects, deaths/spawns, cells/resets, services,
ownership and ongoing interactions. Specify save-version migrations; reject
unsupported/corrupt data clearly. Atomic save/recovery must not duplicate or lose
items/events. Save admission must preserve a valid resumable state.

Exit: ordinary save -> process close -> cold load -> continued player action
preserves authoritative state/order. Exercise cross-cell saves, timed/paused
systems, death/reload and interrupted writes. Flat/XR continuation uses the same
save. Fixtures do not replace campaign saves; retail-save compatibility is a
separate claim requiring evidence.

### M7 — First integrated routes, not complete campaigns

FNV: ordinary New Game, Doc Mitchell creation/release, Sunny's shooting/gecko
tutorials, campfire crafting, an offered resolution of the first Goodsprings
conflict and ordinary departure. Optional choices remain choices; skipping a
tutorial does not prove it. Records and real runtime owners drive all progress.

FO3: ordinary New Game, birth/creation, Baby Steps, Growing Up Fast, Future
Imperfect, Escape, real Vault 101 exit, wasteland travel and Megaton entrance.
Eligible residents must be source actors with appearance, movement, dialogue,
services and interaction. Verify arrival and early main-quest continuation.

Exit: both routes complete independently through ordinary controls, with correct
state, functioning residents and cold continuation. FNV/TTW cannot substitute
for standalone FO3. These routes establish only their own bounded coverage.

### M8 — Full base campaigns and every official expansion

Build a source-derived obligation ledger covering every main/side quest,
objective, offered branch, mutually exclusive outcome, ending, encounter,
location and system-dependent interaction. Include unmarked/repeatable content,
companions, challenges and authored post-ending restrictions/continuation.
Use the complete winning graph plus runtime observations; one main-quest
walkthrough or one ending is insufficient.

FNV includes Dead Money, Honest Hearts, Old World Blues, Lonesome Road,
Gun Runners' Arsenal and Courier's Stash. FO3 includes Operation: Anchorage,
The Pitt, Broken Steel, Point Lookout and Mothership Zeta. Cover special mechanics,
entry/return travel, inventory restrictions, rewards and expansion interactions.
Enumerate supported official selections without dropping the full-DLC selection.

Use genuine separate playthroughs or ordinary saved branch points for incompatible
choices. Exercise faction/companion outcomes, skill/build-dependent options,
death/failure/retry, optional exploration and repeatable loops. Bots may operate
ordinary controls only; they may not force stages or inject gameplay authority.

Exit: every obligation has linked execution/state/presentation evidence. All
campaign endings and DLC routes are covered; unresolved defects block acceptance.
Newly discovered in-scope content expands the ledger and reopens the gate.

### M9 — TTW and supported plugin configurations

Treat TTW as its own pinned integration: both worlds, character/rule changes,
travel, companions, quests, overrides, DLC and save continuation. Test declared
ESM/ESP/loose/BSA combinations for masters, order, conflicts, winning resources,
scripts and additions/removals where supported. Parsed records are insufficient.

Native/script-extender extensions need explicit versioned contracts and real
execution proof through the extension boundary. Building an extension domain
is not execution evidence. Disabled required extensions block the dependent
configuration; never silently ignore them or imply arbitrary plugin support.

Exit: each advertised configuration passes its relevant campaign, system, save
and presentation gates. Unverified versions/combinations remain labeled so.

### M10 — Physical OpenXR and sustained performance

Exercise shared state/input in both modes throughout development. Final XR
acceptance requires physical stereo/projection, predicted poses, frame timing,
head/hand motion, body/arms, grips/weapons, two-handed interaction, Pip-Boy,
pointer/UI focus, locomotion/turning, seated/standing calibration, occlusion,
audio and interruption checks. Hands/weapons/UI must remain attached during
motion. Simulator success cannot establish physical-headset acceptance.

Retain native 4K60 flat and physical 144 Hz XR targets. Pin hardware, resolution,
refresh, settings and runtime. Measure CPU/GPU frame times, p95/p99, missed/dropped
frames, stalls and memory in representative heavy gameplay and streaming. Use
16.67 ms and 6.94 ms frame budgets respectively; average/paused FPS is insufficient.
Set sustained duration and tail/stall criteria before measurement. Do not lower
fidelity or silently substitute reprojection/upscaling to claim a native target.

Exit: physical input completes the same gameplay and save actions as flat mode;
sustained sessions meet declared criteria without lost content/telemetry or
unbounded memory. Every failed device/configuration remains outside acceptance.

### M11 — Independent retail parity

Match source selections and genuinely equivalent gameplay states. Compare
canonical bytes/layouts, semantic fields/entities, state transitions, event order,
frame timing, animation, audio, UI, draws and final pixels independently. Record
environment and synchronization uncertainty. Establish metrics and justified
tolerances before evaluation; never widen tolerances merely to pass. Unexplained
differences remain divergence.

Private retail FNV observation uses Win32 Ghidrust observe mode for WOW64/x86.
A bridge may duplicate timestamped user input; retail measurements never become
OpenNV gameplay authority. Keep private addresses/binaries/captures out of public
inputs; return neutral contracts and synthetic regressions to first-party code.

Exit: all independent lanes agree over declared coverage. Screenshots, transport
identity or silent video cannot close other lanes. Record only for a specific
comparison; delete temporary frames in cleanup/finally, including failed runs.
Retain only needed selected diagnostics or requested deliverables.

### M12 — Release and final decision

Validate an asset-free package on a clean supported machine: installation
selection, missing/invalid data errors, direct reading, New Game, controls,
settings, saves, mode switching, updates, shutdown and failure recovery. Audit
licenses/dependencies and exclude private reference code, graphs containing
private data, retail files and generated asset caches. Test the shipped executable
and packaged revision, not only an editor run.

Exit: M0–M11 pass for the advertised matrix; required local/CI gates pass; no
in-scope missing behavior or unresolved failures remain; contents and limitations
are accurate. Publish checked work, verify local main equals origin/main and no
outstanding task PRs. Only then call the declared scope complete. Partial releases
must state their actual boundaries without presenting unfinished games as done.

## Per-divergence repair and evidence contract

1. Reproduce with pinned input/build/state and a compact failure trace.
2. Trace winning records, invoked behavior and the authoritative owner. KG links
   locate candidates; reviewed evidence establishes the contract.
3. Define inputs, outputs, ordering, lifetime, errors and save behavior in neutral
   terms. Keep unresolved semantics explicit.
4. Implement the general production behavior. Add a meaningful failing-before
   regression and selected owned-data checks.
5. During integrated acceptance, exercise ordinary controls, alternate/failure
   branches and cold continuation. Do not force success to obtain a recording.
6. Compare matched retail for parity claims; run applicable gates and review the
   whole diff. Report uncertainty and skipped checks accurately.
7. Publish a checked PR, merge, verify main equality, update current-work.md and
   start the next work block on a fresh codex/ branch.

Use existing reports/docs to retain obligation ID, source/build identity, owner,
dependency, reproducer, expected contract, result, evidence location and next
step. Distinguish discovered, implemented, component-tested, owned-data-tested,
gameplay-verified and retail-accepted. Test source is not a passing receipt.
Shared changes invalidate affected evidence; rerun affected checks without
repeating every historical audit unnecessarily. Do not create a parallel engine,
campaign simulator, inventory dashboard or generic proof framework.

## Build, publication and resumption

Work on codex/ branches based on current origin/main, preserving unrelated and
unique changes. Root owns integration, builds/exports and Git. Do not spawn
workers without separate authorization or run concurrent exporters into the
same output. Close bounded gameplay sessions before builds. Recording stays off
during normal development and headless checks.

Before pushing runtime or claim changes, run the selected owned-data audit and:

~~~powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
git diff --check
~~~

The gate covers Release/Debug builds, formatting/analyzers, contract probes,
launcher tests and native Godot loading. Review skips and diagnostic boundaries.
A passing gate is not campaign or physical XR acceptance. Parity claims require
matched retail evidence in addition to the gate.

On resumption, read current-work.md, architecture.md, status.md and this plan,
then inspect git status and retained diffs. Publish the M0 batch and begin M1 on
a fresh branch. The first M1 task is an evidence-backed SCDA operand/expression
contract and actual execution through existing script owners, including
compiled-only fixtures, real command effects and precise failure reporting.

Current work remains offline-first; no campaign is launched during M0/M1. Later
authorized integrated work can advance to M7 when dependencies permit. If work
stops, record verified state and next blocker. Never relabel a partial result
as 100% completion.
