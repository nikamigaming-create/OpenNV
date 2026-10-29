# Runtime capability map

This is the working map for finding missing capabilities before entering the
game. It describes the currently available evidence and the joins that still
need implementation; it is not a claim that every source object is supported.
The implementation plan owns batch order and acceptance. Owned-data audit
outputs and retail observations stay private.

## Evidence chain

Each winning source identity needs a traceable path through these independent
stages. A failure or missing observation at any stage remains visible.

1. **Discovery:** selected plugin stack, winning record or resource, provenance,
   raw extent and identity, including deleted records and missing references.
2. **Interpretation:** decoded fields, resource blocks, script source and
   compiled extents; unrecognized layouts, opcodes and references are errors.
3. **Ownership:** the actual C# state owner, entry point and prerequisites for
   each reachable event, command, resource and interaction. A source-code
   dispatch guard is a candidate, not proof of an executing owner.
4. **Execution:** ordered inputs and transitions, effects, time and randomness,
   errors and rollback, with explicit boundaries for native physics or GPU work.
5. **Continuity:** persistence, cold restore, streaming and interruption retain
   the state and pending work without replay or invented defaults.
6. **Presentation and comparison:** Godot/XR consumes that state; matched retail
   evidence separately checks timing, sound, UI, motion and final pixels.

Every map row must retain its source identity, source/build configuration,
applicable inputs and stage-specific disposition: supported with evidence,
failed with reason, or uninspected. Counts must reconcile to the discovered
denominator; a success in one stage never fills a missing later stage. A row
cannot claim retail parity from an offline result.

## Current evidence and missing joins

| Area | What is available now | What still must be mapped and connected |
| --- | --- | --- |
| Source graph | The corpus audit accounts for winning plugin payloads and resource bytes, source identities and failures. | Join each winning record and resource to every dependent reference and runtime consumer, including overrides, missing dependencies and each selected plugin stack. Byte accounting does not prove decoding. |
| Scripts and conditions | The quest-graph audit inventories source bodies, compiled extents, authored branches and expression inspection. `script-capability-map` reconciles operations and candidate dispatch sites. | Map compiled instruction execution, actual caller/argument context, every reached command and function effect, event delivery, locals, continuation and cold state. Parse failures and uninspected statements remain separate denominators. |
| Inventory and interactions | Item-event audit enumerates attached item scripts; source event blocks have a staged placed-item pickup owner for a limited set of effects. All 21 discovered placed cards in the selected owned stack pass headless pickup. Inventory has staged transactions and selected-variant removal. | Provide persistent per-instance identity and general transactional event/effect owners for pickup, transfer, equipment, containers and destruction. Unsupported scripts and ambiguous instances still fail closed; ordinary gameplay and retail timing remain unverified. |
| Quest, dialogue and menus | Source dialogue and quest execution run in selected paths; menu audit attempts all winning menu parses and expansions. A separate literal include graph discovers 550 edges across 121 selected resources. | Connect each event and result to authoritative state and modal continuation; map all menu grammar, include timing, traits, input and effects. Seven menu parse/expansion failures remain in the selected audit; the include graph does not validate expansion semantics. |
| Caravan | Source card/deck readers, board/rules/match/session/wager owners, player collection, NativeCaravanMenu and opening-driver integration exist. | Complete ordinary deck/bet/turn/settlement, conversation continuation, persistence and retail comparison remain unverified. The old no-connected-ShowCaravanMenu-owner claim is obsolete. |
| World, navigation and AI | Source cells, references and several packages run; navigation and physics owners have targeted offline probes. | Account for every source reference, package, procedure, collision shape and cross-cell continuation. The latest route succeeded only after a long search around a wall; bounded real traversal and all actor families are unresolved. |
| Combat, creatures and loot | Selected encounters, damage and corpse transfers have working paths. | Join each actor, equipment, attack, projectile, animation event, hit reaction, death and loot transition to its source and persistent runtime owner. General encounter and retail equivalence remain unverified. |
| VATS | FalloutVatsSession, source chance/cost/miss readers, native target/visibility queries, player input/execution and NativeVatsMenu are connected. Ordinary selection/cancel and two queued misses produced shots, ammunition and AP consequences. | Hit/damage, broader weapon/target behavior, interruption, presentation, persistence and physical XR remain unverified. The old no-session/no-execution-owner claim is obsolete; selected misses do not establish complete retail behavior. |
| Geometry, materials and animation | Selected NIF, texture, material and animation paths render. A single-bone embedded skin outside the specialized head layout can use the general hardware-partition owner; the selected owned outfit passes headless construction. | Enumerate winning resources and each referenced block through decode, dependency resolution, upload, live instance and destruction. SpeedTree, distant materials, some collision and actor presentation remain unresolved; outfit deformation and retail pixels remain unverified. A rendered frame is not full coverage. |
| Audio | Selected source sounds have runtime owners. | Trace every sound/media dependency, voice trigger, spatialization, lifetime, mix and interruption through save/streaming and matched recordings. Full audio behavior is unverified. |
| Performance | Frame and streaming instrumentation exposes selected costs. A recent exterior run observed a 491 ms creature upload step, including 475 ms constructing one part. | Attribute CPU, GPU, upload and synchronization under repeatable 4K full-content travel and combat workloads, including cold streaming and saves. Selected creature geometry and source-bone attachments now assemble in staged steps, but the latest headless maxima were 13.61 ms for blowfly material and 13.47 ms for Victor attachment geometry; earlier individual steps exceeded 16.667 ms. Full streaming and actual 4K frame timing remain unverified against the 16.667 ms budget. |
| XR | Flat and XR share gameplay ownership; selected rig and menu paths exist. | Map input, presentation, lifecycle and physical headset timing to the same gameplay state. Prior physical acceptance failed; simulator or desktop evidence is insufficient. |

The private selected-stack light graph decoded all 728 winning LIGH records,
including deleted winners. It found 582 declarations with zero flags, 93 with
`0x8`, and smaller other flag groups; 28 and 22 of those first two groups also
name models. These source counts do not describe placed instances or prove
animation, model resolution, runtime support or lighting parity.

The private selected-stack Find target graph accounts for 4,885 winning PACK
records: 4,797 non-Find, 87 decoded Find declarations and one Find refusal
(`FalloutNV.esm:02c9c8`, no complete PTDT). Among the decoded rows, 16 use
object-type Furniture and 12 object-type Food; other target types and categories
remain present. The selected gecko Find has an Allow Killing source flag.
This inventories declarations, not eligibility, execution, pickup, combat,
completion or parity. Output is
`local/find-target-graph-c10f7033a2504571affa4248dad81c8e.json`.

The selected-stack Sandbox capability graph, rerun against the current Release
build, reconciles all 4,885 winning PACK records: 4,094 other procedures and
791 decoded Sandbox declarations, with no declaration failures. The Sandbox
reader accepts 512 declarations and refuses 279. The observed mantis package
`FalloutNV.esm:0410af` is now accepted with a current-location radius of 512.
These are reader admission results, not proof of reachability, execution,
save continuity or retail behavior. Earlier saved graph output predates this
reader change.
The current shared package reader additionally preserves the raw primary
location word and resolves FormIDs for reference, in-cell and object-ID
locations. On the selected owned stack all 71 in-cell Sandbox declarations
resolve their cell identity; this is decoding, not evidence of an in-cell
search owner or runtime execution. The graph command reports both raw and
resolved words; the saved graph above predates those extra fields.

The private actor-package graph joins placed actors to their base records and
the base's ordered package-priority owner. For the selected stack it accounts
for 6,455 actor bases (6,182 resolved and 273 requiring a persistent leveled
template selection) and 7,681 actor references (all decoded). The two observed
mantises resolve to base `11d584` and Sandbox `0410af`; both observed geckos
resolve to base `165183`, whose first declared package is Find `1651b1`.
Source declarations alone do not evaluate conditions or prove that a package
is selected. The output is `local/actor-package-graph-v2.json`.

The former ambient INFO `FalloutNV.esm:17af3d` blocker has a merged source-text
timer for absent audio and absent LIP, retaining ordinary response/result
continuation. Owned/synthetic checks pass; the specific live line and matched
retail timing remain unverified. Audio-only, LIP-only, explicit sound, malformed
and ambiguous media remain distinct cases, not invented silent success.

The selected installation's `settings ... VATS` inventory now retains every
matching executable-name and winning-GMST candidate instead of stopping on its
first refusal: 103 values resolved and 28 lookups failed. All 28 failed names
are section-qualified executable strings, so these are not established missing
GMSTs or proof that their corresponding INI owners are unsupported. The private
output is `local/vats-settings.private.jsonstream`; values and source names alone do
not establish how VATS consumes them.

The latest selected script audit found 14,854 parsed source bodies from 14,866,
12 source parse failures, three compiled-extent failures, 1,450 statement
inspection errors and 51,518 statements whose dispatch owner was not inspected.
The last two figures are **not** counts of unsupported commands. The audit did
not execute every compiled instruction or state-dependent branch. It is a map
of known gaps, not a census of completed gameplay.
The refreshed private audit at `local/system-map-active-20261027` was generated
by the same runtime build as the lab used to read it. The audit exits
unsuccessfully because it reports 15 structural failures. Future runtime
changes require a fresh audit before treating these results as current.

The fresh-game outdoor smoke found a 1,385 ms inclusive creature sandbox
destination operation. Its existing timer does not separate source navigation
loading from destination search; new nested timers require a subsequent build
and run. The fresh run also reported five unsupported plugin door-teleport
flag records while inspecting the portal graph. Those are source issues, not
proof that the current route encountered those doors.

## How to complete the map and use it

Build machine-readable, source-keyed joins in the development lab over the
existing audits rather than introducing a transformed launch cache. Add one
domain at a time with independent discovery and reconciliation: script and
event edges; record-to-resource dependencies; resource block decoding and
presentation; world/AI/combat state and persistence; audio and UI; performance
workloads. Capture owner entry points, explicit unsupported reasons and build
identity, then run deterministic transition and cold-restore cases against the
same runtime code. Treat unsupported inputs and missing execution owners as
failures, never as empty successful results.

Use those maps to choose a coherent implementation batch, connect its owners,
exercise source-backed cases offline, then run one ordinary gameplay smoke for
the batch and burn down its observed failures. Matched retail observation is a
separate evidence lane for behavior and visual/audio parity. The map cannot
prove that an arbitrary game state will work or replace real gameplay and GPU,
physics, and headset checks.

## Offline joins needed for the route

The route inventory must start with winning cells and their reachable
references, not a handpicked list of encounters. For each source identity,
record its dependency edges and independently account for discovery, decoding,
owner admission, transition execution, interruption and cold restoration,
presentation, and retail comparison. A missing edge or uninspected stage is
unknown; a refusal includes its identity and reason. For each stage, require
`discovered = supported + refused + uninspected`, with no overlapping rows.
Inputs and results stay private when derived from the owned installation.

Build and use the joins in this order, within coherent implementation batches:

1. Link cell references to actor bases, templates, package priorities, quest
   conditions, event scripts and resource winners. Keep unresolved leveled
   choices and condition-dependent branches as explicit alternatives.
2. Link script events and operations to actual dispatch, arguments, state owners
   and effects, with separate source-parser and compiled-execution dispositions.
   Exercise event order, errors and rollback against the real C# owners.
3. Link AI and combat declarations to candidates, navigation, inventory,
   equipment, effects, deaths and loot. Exercise source-backed selection,
   interrupts and cold restores without inventing targets or success.
4. Link each referenced geometry, material, animation, sound and UI resource
   to decoder, dependencies, live presentation and lifetime. Unsupported
   blocks and missing dependencies must identify the affected source objects.
5. Measure the assembled workload in ordinary 4K travel, combat and streaming.
   The 60 FPS budget is 16.667 ms per frame; report frame-time distribution,
   worst stalls and CPU/GPU attribution, including cold loads and saves.

Each offline scenario specifies initial state, input or event, expected ordered
effects, random/time inputs, interruption points and a cold-restore outcome.
It exercises the same owners used by the game. Offline coverage is a prerequisite
for a batch smoke, not a substitute for physics, GPU or retail observations.
