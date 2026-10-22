# Current work

Updated 2026-10-21. Priority: fully playable vanilla Fallout: New Vegas flat mode.
Root owns implementation, builds, input and Git without delegated agents. This
is greenfield development; old-save compatibility is not a priority. Owned retail
inputs remain read-only. Follow docs/implementation-plan.md, including terminals,
lockpicking, hacking and all 13 skills. No campaign or parity completion is claimed.

## Active block

Branch codex/fnv-shared-sandbox, based on synchronized main
1901d2fa99e5ab84dd14d1141b8b4b4ff4a8f84f. The task PR uses this branch; preserve
unrelated PR #242. No game, build or watcher is running. Frame recording remains
off. Full runtime gate passes (sandbox-runtime-gate.log, session44635 exit0),
as does the selected owned10a210/074fe6 four-request navigation audit
(sandbox-final-owned-wander.log, exit0). git diff --check passes. Hosted checks,
review and main publication remain pending. Main has not been updated.

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

Complete branch review and hosted checks. The next implementation work is remaining
admission, activity execution and the independent movement request clock; do not revert unrestricted packages to
wander-only behavior. Exercise ordinary population, combat and quest transitions,
then save/cold continuation and selected owned checks before the full runtime gate.

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
