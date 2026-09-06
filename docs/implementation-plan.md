# Generic runtime and Goodsprings implementation plan

## User objective

Deliver a working, data-driven game. Complete the New Vegas opening through
Doc, original character creation and questionnaire, the Pip-Boy, leaving the
house, Easy Pete, Sunny and her entire tutorial. Extend verification across
every Goodsprings interior and exterior cell, their connections, actors,
objects, interactions and quest progression. Verify cold Continue and the
shared flat/OpenXR gameplay path. All installed DLC and winning plugins remain
in scope. Preserve the broader NV/FO3/TTW product objective in AGENTS.md.

The user's September 6 instruction is to stop building individual scenes by
hand. Build general runtime capabilities and an aggressive, separate
development lab that exposes failures and removes repetitive manual work.
The user requires concrete results today; do not turn that urgency into
unverified completion claims or an indefinite research loop.

## Freedom to replace the implementation

This is greenfield first-party code. No current class, architecture detail,
adapter, test fixture, diagnostic, documentation conclusion or previous effort
has legacy compatibility value by itself. Inspect it independently. Replace,
consolidate or remove it when that makes the requested product more correct.
There is no requirement to preserve the current opening driver, script
interpreter, harness or scene construction strategy. Avoid parallel old/new
product paths and compatibility scaffolding without a demonstrated need.

Do not assume the previous task's diagnosis is complete or that its suggested
design is right. Current code, owned source data and reproducible observations
are evidence to examine. Existing tests may encode a wrong assumption: repair
them against independent source/behavior evidence rather than preserving that
assumption or weakening a gate to obtain green results.

Keep the user-owned installation read-only, the public tree asset-free, and
the clean-room/C#/Godot/OpenXR boundaries in AGENTS.md and architecture.md.
Preserve unrelated user work. Main is the only working/publication branch.
One implementation task at a time; no subagents or duplicate game instances.

## What the handoff establishes, and what it does not

The last published runtime commit before this plan is 80c4db0. Its ordinary
run reached stage 80 after original creation and Vigor; player couch activation
and the original questionnaire did not work. We have not reached the exterior
or completed Sunny in the current ordinary path. Source-linked component
corrections exist for textures, faces, placement, lighting, script calculations
and NPC animation/furniture. They do not prove another cell or game works.

Current inspection found model-less references skipped by the cell builder,
reference script variables explicitly rejected, activation dispatch tied to
selected interactions, and opening-specific progression/replacement panels.
These are investigation entry points, not mandatory diagnoses or a prescribed
replacement design. Inspect persistence, event ordering, dialogue, world
loading, actors, physics and presentation together before selecting the fix.

All existing acceptance requirements remain open. Nothing is accepted merely
because it looks plausible or a previous task described it as fixed.
At the last process check on September 6 both games and the harness had exited;
old live-state files are not live sessions. Revalidate before any input.

## Required development lab

### Retail systems, contracts, headless execution, then presentation

The user's explicit method is to dissect retail by system, identify each
system's responsibilities and boundaries, implement that behavior in shared
code, and prove it before attaching graphics. Text-only/headless execution is
a valid development result for gameplay layers. A screenshot is not required
to prove a script calculation, event ordering or state restoration; it is
required when claiming the corresponding final presentation is correct.

For each selected system:

1. Inspect the winning source formats and read-only retail behavior. Identify
   inputs, outputs, state, events, timing, interactions with other systems and
   unsupported cases. Keep raw retail investigation private; derive an
   implementation-neutral contract. Do not spend an unbounded pass dissecting
   the entire executable before delivering the first working system.
2. Implement a complete useful behavior layer behind ordinary runtime APIs.
   Existing code is optional. Avoid another sequence of per-command or
   per-scene handlers with no complete system-level behavior.
3. Run synthetic, owned-data and cross-instance/cell headless tests. Print
   exact state/event differences, failures and reproduction commands. Exercise
   interruption, repeat execution, source overrides, lifecycle and cold state
   where applicable. Test the actual layer, not a second implementation.
4. Connect that tested layer to gameplay and the original presentation, then
   verify ordinary inputs and matched retail audio/visual/timing evidence.
   Keep the next system and all unverified integration boundaries explicit.

The system inventory should cover world/reference lifetime and cell streaming,
script/event/quest execution, actors/AI/animation, physics/collision/ragdoll,
interaction/inventory/equipment/combat, dialogue/menus, persistence, audio and
rendering/effects. Derive dependencies and execution order from the inspected
code and source evidence. Do not treat this list as a complete behavior census
or an instruction to build every system simultaneously.

Tools must be recognizably development tools, separate from the actual game
interface. Use C# command-line/headless tools and purpose-built inspection
surfaces where useful. Do not put diagnostic/default controls into gameplay.
Reuse an existing tool only if it meets the requirement; these are required
capabilities, not a mandate to add six new frameworks.

| Capability | Required operation and evidence |
| --- | --- |
| Scope and capability inventory | Discover Goodsprings cells from winning records, exterior coordinates, location evidence and door links. Include unnamed connected interiors and outdoor areas; a name search alone is insufficient. Record inclusion and boundary evidence. Enumerate source entities, scripts, events, commands, resource types and missing runtime support. Do not silently exclude unsupported content. |
| Cell assembly and teardown | Load arbitrary source cells directly, inspect every reference and resource relationship, unload, reassemble and cross doors in both directions. Compare transforms, identities, live entities, retained state and resource lifetime. A model is not a prerequisite for gameplay existence. |
| Actor and physics stress | Instantiate source actors/creatures/outfits in disposable lab state, exercise all selected authored animation/attachment states, interrupt and repeat them, vary equipment and apply controlled impacts/forces. Exercise ragdoll/collision/interaction where the source behavior requires it. Detect invalid transforms, detached parts, missing channels and physics failures. Unsupported physics is a failure, not a staged pose. |
| Script, interaction and quest execution | Exercise reference activation, trigger enter/stay/leave, dialogue choices, timers, inventory changes and quest effects through the same runtime used by the game. Verify per-instance isolation, source conditions, event order and winning overrides. Run batches across unrelated source instances/cells without adding names or IDs to runtime behavior. |
| Persistence and replay | Save, destroy runtime state, cold-load and continue at meaningful checkpoints. Restore actual actor/reference/quest/inventory/interaction state. Reproduce a failing case from a small manifest/seed and retain the original failure. Avoid replaying the cinematic or manually retracing a room on every change. |
| Transparent diagnostics | Opt-in provenance from source bytes/records/resources through runtime ownership, events, animation, physics, audio, UI, draws and final pixels. Missing coverage, packets and unmatched frames must be explicit. Provide source/runtime drill-down and machine-readable failures; tracing off must stop its collection overhead. |

The lab may aggressively manipulate disposable OpenNV test state, assemble
and dismantle runtime objects, and use synthetic fixtures. Those operations
must remain identifiable as tests. They cannot become ordinary-game success
paths, manufacture a matched retail state, overwrite user saves, or count as
an ordinary-input playthrough. Never mutate retail state through internal
calls, memory writes or forced stages/poses. Retail remains read-only observed;
ordinary reference input uses the diagnostic keyboard/mouse bridge.

Tools must call the real runtime under test. A mock behavior or parallel lab
implementation that bypasses the failing owner provides no product proof.

## Execution sequence

1. Perform one bounded architecture/capability audit and create a source-derived
   scope manifest. Identify the smallest complete gameplay chain whose shared
   correction unlocks progress in multiple places. Do not spend successive
   work blocks restating findings, rereading large traces or polishing a still.
2. Build only the lab capabilities needed to reproduce, diagnose and repeatedly
   verify that chain. Use them immediately. A tool is useful when it eliminates
   a manual loop or exposes a previously hidden failure, not when it adds a UI.
3. Complete the chain through shared authoritative state and original
   presentation. The currently blocked couch/trigger/questionnaire sequence is
   an acceptance case; it does not dictate the replacement architecture.
   Include persistence so subsequent iterations can resume there reliably.
4. Demonstrate the capability on unrelated owned objects/scripts and a second
   cell, including a negative case and source override when relevant. General
   means source-selected behavior works without editing code for that object.
5. Proceed through original remaining menus, Pip-Boy, house exit, ordinary
   exterior/interior travel, Easy Pete, Sunny/Cheyenne and the full tutorial.
   Sweep the complete Goodsprings manifest through the lab and ordinary checks.
6. At matched checkpoints retain differences, fix the responsible general
   owner and replay the affected section. Keep every open material, lighting,
   effect, movement, timing, audio and UI requirement visible. Complete the
   cold-save and integrated flat/OpenXR acceptance requirements.

Engine behavior, format definitions, command semantics and game-version
adapters necessarily need code. Quest outcomes, actor/prop placement, dialogue,
stage choices, UI content and cell-specific behavior must come from the owned
graph. Replacing a named branch with a fitted table does not generalize it.

## Engineering quality and performance

The user requires small, cohesive files, solid ownership and DRY shared code.
Split by responsibility; avoid giant scene/controller files, copied behavior,
or layers of tiny wrappers that obscure execution. Abstract actual shared
behavior, not speculative future requirements. Prefer direct, understandable
code and explicit lifetime/error handling.

Audit complexity and resource reuse as part of the implementation:

- Identify n for each hot path: records, cells, actors, surfaces, active scripts
  or events. Explain and measure repeated scans, nested traversal and growth
  across larger inputs. Do not promise O(1) for operations that must visit n
  objects; avoid accidental O(n squared) or exponential work and repeated
  whole-installation scans during frames, activation or cell transitions.
- Build source/load-order indexes once where appropriate and use stable-key
  lookups. Recompute dependent state when its inputs change, with explicit
  invalidation, instead of rebuilding the full graph for every operation.
- Use in-process caches for immutable decoded resources and share them safely.
  Bind keys to source identity, winning overrides and relevant decode options.
  Bound retained memory, measure hit/miss behavior, and release CPU/GPU/audio
  resources on their real owner lifetimes. Prevent stale cross-cell state,
  duplicate decoding/uploads and unbounded growth through repeated travel.
- Mutable actor/reference/script state belongs to the world and save owners;
  eviction of rendered resources must not erase or duplicate gameplay state.
  Transformed retail assets remain prohibited as persistent launch inputs.
- Measure cold/warm cell loading, frame hotspots, repeated teardown/reassembly
  and memory growth using the lab. Optimize measured bottlenecks while keeping
  the code simple. Instrumentation disabled must cease diagnostic collection.

These are implementation requirements, not a demand for another framework,
benchmark bureaucracy, progress dashboard or proof document for every edit.

## Concrete result and closure rules

- Maintain one finite acceptance ledger: recovery-checklist.md. R01-R33 are
  preserved; R34-R36 record the user's expanded Goodsprings/generalization/lab
  requests. The source-derived cell/capability manifest expands its evidence,
  not a smaller substitute for the requested scope. New failures are explicit.
- Every completed work block must produce an executable improvement: changed
  shared behavior or a working lab operation that removes a demonstrated
  bottleneck. Report the command, code revision, observed before/after result,
  remaining failures and next acceptance item. Commit counts and research
  notes alone are not the user's requested result.
- Prioritize working code and integration. Use focused checks that detect the
  actual failure during development and the single existing publication gate
  before push. Do not create extra proof/approval gates, repeat unchanged broad
  tests, or let evidence paperwork consume the implementation work block.
- Use separate statuses for source admission, runtime behavior, ordinary
  playability, persistence and matched parity. Never turn a component result
  into a green cell, game or tutorial. Reopen regressions explicitly.
- Closure requires appropriate synthetic and owned-data checks, the actual
  ordinary behavior, cold restoration where state matters, and inspected
  audio/visual evidence. Exact/parity claims additionally need equivalent
  source state, camera, animation/event time and all independent evidence lanes.
- If a work block produces no executable progress, inspect the actual blocker
  and change the approach. Do not rerun unchanged tests, wait on dead processes,
  rebuild the same manual route or consume another long block on the same plan.
- Keep the user informed with concrete results and limitations. Do not promise
  a completion time or 100 percent based on unmeasured remaining work.

## Publication and task handoff

Before pushing a runtime or claim change, run the selected owned-data audit,
the required full gate and git diff --check. Inspect what the tests prove.
The full gate remains mandatory; do not repeat it between every small edit when
no publication is occurring. Keep ordinary comparison evidence private.

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

Keep current-work.md short: actual current state, exact verification, active
blocker, next executable outcome and any live handles. Store reusable technical
contracts near their code/docs; do not require the next task to follow the
previous conversation, preserve investigative history or retrace failed routes.
The repository and source evidence are authoritative. The new task is free to
make a substantially different implementation decision and must prove it.
