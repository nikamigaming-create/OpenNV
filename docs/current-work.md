# Current work

Updated 2026-10-22. Priority: fully playable vanilla Fallout: New Vegas flat mode.
The local integration coordinator owns builds, input and Git; user-authorized
writers have separate file scopes and do not run competing builds or tests. This
is greenfield development; old-save compatibility is not a priority. Owned retail
inputs remain read-only. Follow docs/implementation-plan.md, including terminals,
lockpicking, hacking and all 13 skills. No campaign or parity completion is claimed.

## Active block

The speech and diagnostics repairs are merged. The integration coordinator is
advancing the shared Sandbox idle-marker owner on codex/fnv-sandbox-marker.
Current population failures include placed markers 15ad26 and 16140d; their
winning IDLM declarations respectively contain four sequential idles with a
two-second collection wait and one smoking idle with a sixteen-second wait.
These waits are not Sandbox activity durations; the marker dispatcher does not
apply their patrol wait/DoOnce policy. Shared source-bound PACK/IDLM collection
validation preserves strict package save admission. The previous gameplay owner is qualifying marker
admission, approach, expiry/interruption and script-visible procedure contracts.
Furniture requires a nested physical continuation; NPC eating requires its own
consumption transaction. Neither can be omitted from mixed candidate sets.
The SWG execution window has ended. The serial OpenNV gate passes
(sandbox-marker-runtime-gate.log, exit0), including all four new contract groups.
The gated source manifest is unchanged. Recording remains off. Live equipment queries now share one-level
FLST matching, including null termination and first-match ordering. The focused
NPC/player equipment contracts and both owned IDLE equipment-list audits pass;
carrying, wearing, live changes, query immutability and cold state are distinct
assertions. Sandbox's script package code is 36; its phase-sensitive procedure
query remains unbound. Marker reservation and process-cursor primitives are
implemented but not dispatched by the live Sandbox owner.
The final owned equipment audit passes against the gated build
(sandbox-equipment-owned-final.log). The counted owned-body speech failure
regression also passes (sandbox-prerequisite-speech-counted.log), preserving one
committed result through cleanup, ticks/retry and save refusal; all ten owned
plugin hashes remain unchanged. None of these receipts proves full IDLE admission
or ordinary marker playback.
The existing four-request owned NAVM audit also passes against runtime module
6057edd3-b11b-40b5-9dfd-becd65846a5d (sandbox-prerequisite-owned-wander.log).

Preserve unrelated PR #242. Ambient speech startup now retires participant reservations
through the shared failure owner while retaining unresolved voice/results and
save refusal. The owned Trudy/Sunny HELLO failure fixture passes and fails its
negative control with cleanup disabled; both busy refusals and ticks/retry are
covered. Its selected INFO has no result script. A separate original synthetic
INFO/quest/script source proves one compiled-bound interpreter counter increment
before failure, retained at one through ticks/retry, with both save APIs refused.
The counted negative control detects the original reservation leak after that
result commits. All ten owned plugins and original synthetic bytes remain hash
identical. The final full gate passes (integration-final-runtime-gate.log, exit0),
including retained-window and timing contracts. Both speech audits pass against
that build (speech-final-counted.log and speech-final-hello.log); the counted
negative control is speech-ambient-counted-negative.log.
The comparator has explicit retained-window attachment with both channels' omitted
prefixes exposed; strict defaults and all subsequent loss checks remain intact.
Near/LOD publication diagnostics now separate exact charged step/source duration
and faults from batch wall time. Admission remains 2 ms; no rendered performance
improvement is established. Typed clocks, shared semantic fields/provenance,
event/audio/particle and final-frame joins remain required for retail comparison.
Frame recording remains off. The selected owned
10a210/074fe6 four-request navigation audit passes (integration-owned-wander.log,
exit0). git diff --check passes. The integration coordinator retains Git and
execution ownership; the previous gameplay owner is qualifying the remaining
marker movement/readiness contracts read-only without competing edits or jobs.

The shared NPC/creature Sandbox owner now moves actors along source NAVM requests
and retains the selected activity separately from individual destinations. Its
sampled duration, game-hour start and preference history survive animation updates
and package-motion serialization. Selection expires using the source game-hour
calculation, including its unusual signed-byte minute components; do not replace
this with frame seconds or normalized midnight subtraction. Arrival does not end
an activity. Ordered weighted Wander/Dialogue selection now dispatches ambient
attempts through the shared speech owner. Refused attempts retain selection; a
started exchange reserves participants and selects the next activity. Furniture,
eating, markers, daily-window lifecycle, request retarget timing and complete
admission remain unfinished.

Latest ordinary Continue: sandbox-dialogue-dispatch-live. Snapshot5 shows10a20f
selecting10a210 as a dialogue partner, with attempts and no started exchange or AI
error. Snapshot11 shows it back in Wander with a new duration/preferences and a
changed position, still without an AI error;10a210 also continues moving. This
verifies refusal-to-movement continuation, not voiced exchange, sustained population,
completed paths, cold gameplay continuation or parity. No frames were recorded.
The live job has ended (handle absent, no Godot process, source-owner retirement
logged); do not restart it from its old handle. Default ReferenceScriptContractProbe
passes (sandbox-dialogue-dispatch-contracts.log, session7970 exit0), including
serialized dialogue targets and preference history. Final Debug build passes zero
warnings/errors (sandbox-dialogue-dispatch-final-build.log).

Live well geckos select PACK074fe6 (editor location, radius512, unrestricted
activities). Creature15ef63 selects026ad5 (radius16, exclusions31), whose defensive
combat flag400000 remains explicitly unbound. The previous10a210/026ad5 owned
query was a fixture, not its live selected package.

## Implemented owners and remaining integration

- Wander search uses source edge permutations, newest-first ties, noncumulative
  cost, best avoidance fallback, barycentric sampling and clearance35. Regional
  queries start from current spatial placement. Accepted requests retain NAVM
  identity, destination and arrival through package motion, saves and interruption.
  The separate movement request timer and traversal-state lifecycle remain open.
- Search-door access uses source empty-context rules and DOOR FNAM bit8 cost100.
  Linked ownership reads destination-local fields without reciprocal XTEL; lock
  inheritance keeps its separate reciprocal rule. General ownership rank/global
  fallback and explicit overrides have selected contracts. Search does not unlock.
- Candidate collection uses resident source references, current position, region,
  radius and activity exclusions. Food admission reads ALCH/INGR food flags and
  ALCH effect classification without executing effects. Inventory contributes one
  eating candidate. Owned ingestible and existing Stimpak checks pass
  (sandbox-food-owned.log); package-data/travel checks pass (sandbox-food-contracts.log).
  Furniture/marker usability, complete ownership admission and eating execution
  remain open. Unknown activities are still explicit failures.
- Conversation suppression initializes from source GMSTs, advances through zero
  and persists. Recent-partner history retains both participants before an attempt,
  including no-voice/no-INFO outcomes; its separate timer advances on source candidate
  evaluation, not unconditional frame ticks. These states survive actor retirement.
- Shared conversation admission binds disabled/dead/knockdown/combat, physical
  sitting, current-package restrictions and bidirectional hostility using live
  aggression independently of confidence. Teammate/perk branches remain explicit
  failures. Commanding actor, greeting, paralysis, geometry/detection/process gates
  and temporary engine-generated package overlays remain unfinished.
- Shared NPC opening selection uses built-in HELLO and applicable GOODBYE fallback,
  preserving default versus explicit-topic semantics and passing selected INFO once
  to playback. TryStartNpcConversation resets social/history state before voice/INFO
  refusal. Sandbox now calls this owner. Ambient exchanges are distinct from
  scripted/package commands and emit no invented SayToDone receipt or package
  completion. Linked GOODBYE resolves the built-in identity. Started voiced
  exchanges and their interruption/cold lifecycle still need live validation.
- Daily sleep/meal windows and six-category preferences/duration/weighted ticket
  owners exist. Daily-window runtime lifecycle and non-dialogue activity execution
  still need binding. Dialogue search includes the source extra range. Actor energy comes from the effective AIDT template owner.

## Next work

The user prioritizes whole-game playability before repeated deep optimization.
The following finite ledger selects implementation by campaign dependency;
each row stays open until ordinary outcomes and cold state are proved. Routes
provide incremental evidence and must expand to the remaining campaign.

| Priority / pillar | Concrete open blocker | Next bounded outcome |
| --- | --- | --- |
| 1. NPC packages + persistence | Current population stops at IDLM admission/execution; phase, queued/current animation readiness, location-return and saved marker continuation are not owned. Furniture and eating also remain unexecuted. | Bind the next qualified shared marker lifecycle; competing actors, genuine approach/playback, interruption, save and separate-process Continue. Do not substitute clip completion for native readiness. |
| 2. Quests/scripts | Current campaign checkpoint is Sunny stage50 at the second well; later wells, ingredients and campfire cooking have no completed ordinary-play proof. | Continue the real tutorial, repair the first reached shared script/gameplay failure, retain its exact result prefix and save/cold outcome. |
| 3. Combat | Ordinary gecko/settler pursuit, damage and kills have bounded proof; healing/retreat, remaining encounters, attack families and reactions are still incomplete. | Complete the current source encounter with actual hits/misses/health, loot and survivable continuation; extend to the next reached family rather than claim all combat. |
| 4. Access interactions | Missing keys/passwords return before lockpicking/hacking; hacking also has a separate creation refusal. | Implement a source-qualified access transaction with ordinary input, skill/perk admission, success/failure consequences and retained per-reference outcome. The read-only access owner is finishing admission/result-commit qualification. |
| 5. World transitions | Existing door/exterior owners are present; complete campaign travel and transition persistence remain unproved. | Exercise the next required authored transition with script/cell/save state intact. Treat prefetch/loading stalls as immediate work only if they block play or validation. |
| 6. UI/inventory | Loot/healing owners exist; inventory scrolling, bottle destruction and all reached crafting/menu paths still need ordinary review. | Complete the next required inventory/crafting interaction with actual state changes and modal-input release, including cold continuation. |
| 7. Save/load across all pillars | Selected retained owners pass contracts; ordinary population, voiced exchanges and marker/furniture/eating state do not yet have complete save admission. | Require manual save and separate-process Continue after each new gameplay owner, preserving targets, RNG, scripts, sound/animation and exactly-once commits. |

The scoped equipment predicate repair is implemented and proved by synthetic
live inventory and selected owned-list audits. Marker reservation/selection
primitives are prerequisites, not closure of the first ledger row. Do not make
telemetry, streaming optimization or broad perpetual audits the primary work
while these campaign blockers remain.

The next implementation work is remaining admission, activity execution and the
independent movement request clock; do not revert unrestricted packages to
wander-only behavior. Exercise ordinary population, combat and quest transitions,
then save/cold continuation and selected owned checks before the full runtime gate.
Refusal suppression/history updates are source-supported per admitted attempt;
two process timers precede that admission and still need complete qualification
and runtime ownership. Do not infer retail cadence from OpenNV's per-tick retry or
deduplicate histories without binding those gates.

Private source analysis, scripts and unresolved semantics are in Ghidrust
workspace/evidence/fnv-1.4.0.525/sandbox-20261021/notes.md. Owned declarations are
local/fnv-wells-fixed/sandbox-records.private.json and sandbox-settings.private.json.
Use these to avoid repeating resolved source discovery; private addresses/assets
and decompiler output must not enter public repository inputs.

## Gameplay checkpoint

local/fnv-wells-fixed/prefetch-cold-resume.json identifies stage50 at the second well,
Sunny's marker travel complete. First-well geckos are dead and Gecko Meat was looted.
Merged baseline includes geometry-only NPC prefetch, effective enable-parent combat
state and blended-LAND impact material selection. Ordinary second-well play has
shown gecko pursuit, damaging melee, settler/gecko combat, rifle kills and player death.
Healing/retreat, remaining wells, gathering/campfire cooking and campaign progression
remain required. Inventory scrolling and bottle destruction still need ordinary review.
Physical FNV VR package g remains rejected; preserve FO1/FO2 work.
