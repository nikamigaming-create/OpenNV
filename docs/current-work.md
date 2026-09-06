# Current work

## Objective and authority

Follow [implementation-plan.md](implementation-plan.md): general source-driven
NV/FO3/TTW gameplay, a separate aggressive lab, the complete opening and Sunny
route, every Goodsprings interior/exterior and installed winning DLC/plugin
behavior, cold persistence and shared flat/OpenXR state. All 36 requirements
in recovery-checklist.md remain open. No ordinary gameplay or parity acceptance
has been added by the headless work below.

Work directly on main. One implementation task; no subagents. Existing
first-party code is replaceable. Preserve owned inputs and clean-room boundaries.

## Current executable state

- tools/OpenNV.DevelopmentLab now inventories the complete loaded corpus,
  exercises arbitrary/all-cell reference lifetimes, and replays scripted events
  against the shared runtime. See its README for reproducible commands.
- The corpus pass read 628,395 winning records from ten official plugins and
  inventoried 182,177 members in 21 selected BSAs. It records layouts, script
  event types and every parser/declaration failure with its source identity.
  Asset contents, compiled bytecode and behavior are separate unverified lanes.
- Shared event parsing now supports filtered/unfiltered blocks together and
  preserves source order. The source-body parser failure count fell from 237 to
  183. Parsing does not establish executed branches or game support.
  Seven additional SCPT declaration conflicts are retained separately.
- Cell child indexing now groups winning references once per record type.
  The previous implementation rescanned the full type on every new cell.
  The indexed all-cell lifecycle sweep completed 44,517 cells: 44,516 passed
  reference-state checks, one failed on conflicting duplicate variable slots.
  Successful cases contained 421,226 references and 10,584 scripted instances.
  Every case repeats teardown/reassembly 30 times and checks a fresh-world JSON
  state restoration. Many CELL records are empty; these are not playable cells.
- Identical repeated variable declarations resolve to one local. Conflicting
  slot/name/storage declarations still fail closed. The remaining failed CELL
  is FalloutNV.esm:0846ea; preserve its failure for source/bytecode investigation.
- Reference locals have world lifetime, independent of meshes and cell residency.
  Ordinary quest/stage/activation query owners can address them. Campaign v12
  saves include reference locals and faults, checked against winning script
  hashes on restore. The existing stage-200 save restriction remains.
- The owned couch-before-Doc replay executes trigger guards, cross-reference
  writes, timers, control-command outputs and conversation-command output across
  a fresh reference/quest owner restore. Furniture facts and effect observation
  are lab boundaries. This does not establish ordinary sitting or dialogue.

## Ordinary runtime and next owner

The last ordinary playthrough remains the stage-80 run at 80c4db0. Original
creation/Vigor and selected NPC furniture behavior were exercised. Player couch
activation, original questionnaire, tag/trait/farewell presentation, Pip-Boy,
house exit, Easy Pete and Sunny remain unfinished. No ordinary stage-80 save
exists. Model-less reference presentation/trigger volumes remain unbound, even
though those objects now have an authoritative reference-state owner.

Next: use the whole-corpus failures and reusable event owner to bind physical
reference events, furniture and source dialogue to the ordinary runtime. Resolve
conflicting source declarations through compiled/source contracts without hiding
ambiguity. Extend the lab to actual actor/physics/animation/equipment operations,
full asset decoding and source-to-runtime coverage, and add ordinary intermediate
save restoration. Do not return to manual scene construction or call the
reference sweep whole-cell support. Exact independent evidence requirements and
all 36 acceptance conditions remain unchanged.

## Verification and publication

Synthetic reference contracts cover overrides, identical/conflicting duplicate
locals, per-instance isolation, filtered event order, cross-cell state, teardown,
Float64 cold state, persistent failures and changed-source rejection. Existing
script/save contracts pass. Selected owned checks are Doc, saloon, schoolhouse,
Hoover Dam power plant, and the couch-before-Doc replay. The broad corpus and
all-cell failures remain explicit machine-local reports under
`tmp/development-lab`; they are not distributed assets.

The AGENTS.md full runtime gate passed; its log is
`tmp/development-lab/runtime-gate-publish.log`. The selected owned checks passed,
and git diff --check passed. Rerun the required checks before later publication.
No ordinary games were launched in this work block; diagnostic reports are not
live retail/OpenNV sessions. Revalidate processes before further ordinary input.
