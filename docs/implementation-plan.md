# Generic runtime and Goodsprings implementation plan

The latest user priority is a flat ragdoll/dismemberment showcase with geckos
and raiders. The private recording now shows ordinary pistol kills and labeled
controlled detachment of both legs, both arms and the head. Finish weapon-driven
limb selection, source gore/effects, collision/solver differences and combat
behavior through the shared owners; the controlled command does not close those
requirements. Follow [actor damage](actor-damage.md) and current-work.md.

Preserve the complete visible VR body with head-only eye
exclusion, continuous reactive gameplay and a single-eye recording with game
audio. Preserve physical VR hands, one/two-handed weapon handling,
real interaction and measured performance, while retaining the ordinary trip
to the Strip and universal cell repairs. Stay in this task without subagents.
Follow [the cell parity review](cell-parity-review.md) and current-work.md.
The separate classic work remains paused and preserved;
its scope is in [the classic Fallout delivery plan](classic-fallout-plan.md).

## User objective

The contact-resolved wrist must own visible hands, held models, muzzle and hit
queries together. Use original source collision and off-hand sockets, retain
tracking-loss and equipment lifetimes, and keep per-frame work bounded. Current
swept hand/weapon contact and physical prop pushing are connected; actor contact
response, deliberate punches/butts, finger/forearm collision, friendly-animal
touch and interactive controls still need their authoritative owners. Gentle
contact, resting contact and tracking jumps must not become attacks. The user's
pool-table and animal examples are product interaction goals, not permission to
fake outcomes or hand-place special objects. Profile allocations and long frames
before adding pools or native extensions; keep reusable data with its real owner.

The next New Vegas work compares each loaded retail/OpenNV cell, collects its
divergences and repairs the shared source/runtime owners before rechecking all
affected cells. Unreviewed cells remain unverified. Working VR combat and looting
remain required, including source gun,
impact, blood and drug effects. The user reported failed physical acceptance and
requires simulator death/dismemberment/looting before another headset run. Both
physical and simulator sessions are stopped with saves preserved. Retain one
tracked hands/weapon/Pip-Boy owner and small cohesive code. The whole device now
scales and twists around its forearm centerline while grip is held; ordinary SIM
page controls and release pass. Semi-automatic firing, cold ammunition state,
source casing ejection, muzzle lighting and material-selected impact presentation
have bounded simulator evidence. Constant-health creature damage, source ragdoll
death and persistent corpse loot now have ordinary flat evidence. Continue with
source limb destruction after the ordinary SIM kill, blood and corpse-loot pass;
complete NPC/encounter damage, combat AI and death scripts/XP remain required. Use the checkpoint and remaining
effects limitations in current-work.md. Measure CPU/GPU time and allocations
before choosing optimizations. Integrated controls, readability, motion and the
broader campaign/VR objective remain required; simulator success cannot replace
physical acceptance.

The full player world body now draws in XR with source head parts excluded only
from the eye cameras; shadows and source bone hit volumes remain. Anatomical
eye anchoring and source-length spine/leg constraints share posed shoulders with
the authored weapon/device owner. Finish room-scale stepping, slope-aware feet,
outfit fitting and integrated physical acceptance. Tracked head/wrist, visibility and
source-contact audits pass; ordinary flat and SIM shadow/self-ray checks pass.
Pip-Boy SPECIAL and skill totals now use the shared source formulas and constant
trait/apparel effects. Continue with actual damage, death, corpse loot and limb
destruction, tested through ordinary flat input before the simulator. Hit volumes
do not establish physical limb response or completed combat.

The cell review includes ordinary exterior traversal,
responsive sprint/jump and stepping, retained adjacent cells and distant LOD.
The opening, source door exit/return, HUD interaction prompts and item/container
transfer now have bounded ordinary evidence. Cell-boundary walking and cold
Continue outside also pass. The requested default optional open-door destination
view and physical traversal are still unimplemented; loading transitions remain
the working path. Finish that shared cell/door presentation owner, native Pip-Boy,
actor/creature behavior and combat before claiming the requested gameplay loop.
Current evidence and remaining failures are in current-work.md; the earlier
handoff below describes the starting baseline, not the current executable.

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
Preserve unrelated user work. Implement on a `codex/` feature branch, publish
through a checked and merged PR, then synchronize `main` with `origin/main`.
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

The user's current priority is couch -> original choices -> Pip-Boy -> outside
-> all cells -> combat -> crafting. Dialogue, barter, loot and containers are
included as shared gameplay systems. Pull their dependencies into the sequence
where the active source path requires them; component audits do not close any
ordinary or parity requirement.

Deliver complete shared behaviors in the following initial order. Each batch
must reach the ordinary runtime before starting the next broad subsystem;
pull dependencies forward when the actual failing chain requires them. Verify
flat/OpenXR state sharing and relevant presentation with each batch. Final-eye
comparison is part of integration, not work postponed until every system exists.

The September 6 corpus command report identifies 8,818 records with result
scripts, 930 with trigger-enter blocks and 728 with activation blocks. It finds
SetStage in 1,651 records, AddItem in 975 and EvaluatePackage's EVP alias in 789.
These are source occurrences in admitted bodies, not executed paths, distinct
unlocked features or predicted parity percentages. They support prioritizing
shared event/condition/effect ownership over further opening-specific handlers.

1. **Make the blocked interaction chain executable and resumable.** Connect
   reference lifetime, model-less trigger volumes, activation/default-action
   dispatch, actual player/NPC furniture state, script events, dialogue choices
   and their results. Use the same C# state, query and effect owners from object,
   quest, stage, dialogue and package scripts. Preserve each context's reference
   scope, event order, timing and pause rules. Replace the opening-specific
   progression decisions as this chain becomes authoritative. Generalize saves
   beyond stage 200 by restoring the actual opening phase, pending interaction,
   actor state and script clocks; removing the stage check alone is insufficient.
   The first ordinary result is couch activation through the original
   questionnaire, with cold continuation and unrelated furniture/script cases.
2. **Expand script and interface behavior by shared dependency.** Complete the
   query/condition semantics and effects reached by quests, rewards, inventory,
   enable/disable, packages, conversations and installed DLC startup. Bind
   original dialogue and menu definitions to those owners; finish character
   creation, farewell and Pip-Boy through them. Establish compiled SCDA contracts
   and feed decoded execution into the same owners. SCTX parsing alone cannot
   settle source/compiled disagreement, missing source or extension opcodes;
   retain those failures. Do not delay ordinary integration for a complete
   bytecode rewrite, or create a second script gameplay implementation.
3. **Complete actor operations across source families.** Resolve actor/creature
   templates, outfits, skeletons, animation groups, root motion, blending,
   equipment and attachments together with package travel, collision and combat.
   Exercise idle, walk, sit, draw, attack, hit, death and interruption on real
   runtime instances. Cluster failures by shared assembly/animation/procedure
   cause and prove each correction on unrelated actors. This supplies the
   general operations required by Sunny, Cheyenne and the tutorial combat.
4. **Complete ordinary world travel.** Replace selected-door routing with
   source-selected XTEL travel and exterior active-cell streaming. Connect LAND,
   collision, navigation, environment and reference enable state; retain mutable
   world state while presentation resources unload. Discover the full Goodsprings
   scope from the winning graph and door/exterior connections. Traverse, return,
   save, cold-load and continue across its interiors and exteriors, including
   Easy Pete, the full Sunny route and affected winning plugin content.
5. **Close rendering and audio differences by shared pipeline.** Decode the
   resources consumed by these cases and group failures by NIF block, material
   flags, controller, collision shape, UI operation and audio behavior. Fix
   geometry/skin/attachment ownership before tuning downstream appearance.
   Correct common material passes, lights, shadows, fog, image-space effects,
   particles, voice/LIP and spatial sound against matched moving evidence.
   Reuse in-process source-bound decode products. A fix should propagate to all
   consumers of that behavior without changing per-location parameters.

Use the existing development lab to minimize the time from a failed ordinary
action to a reproduced owner failure. Extend it only where the selected chain
needs an operation: actual contact/furniture, dialogue execution, actor motion,
resource decoding or a fresh-process save continuation. Keep full-corpus failures
visible, but replay affected cases during edits and sweep broadly after a shared
behavior change. Run the existing full gate before publication.

Choose the next batch by the number of independently failing behaviors that
share its cause, dependency order and measured implementation/replay cost. Count
source fan-out only as a prioritization hint. Include expression queries and
conditions when extending the corpus inventory; command names alone miss them.
Reduce failures to a small reproducible case, fix the earliest incorrect owner,
then run unrelated instances, a source override and negative cases. Track the
remaining failing identities. Batch throughput means fewer actual divergences,
not more parser passes, instantiated references or captured frames.

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
