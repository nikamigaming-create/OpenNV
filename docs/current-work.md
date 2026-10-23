# Current work

Updated 2026-10-23. Priority: fully playable vanilla Fallout: New Vegas flat mode
before repeated deep optimization. Follow docs/implementation-plan.md, including
terminals, lockpicking, hacking and all 13 skills. Owned retail inputs stay read-only;
no campaign or parity completion is claimed. The coordinator owns builds, native
input and Git; no competing jobs or denied writer contact are permitted.

## Active block

Accepted main/origin-main is 20b03986a8d4ce6445c1311e46fd78fa98991684 (PR279),
merged at08:41:21UTC after explicit release, independent production-adapter re-review
and all five exact-head checks passed at b8e3c90327da9becb3a602e794474989689b9c15.
Local main equals origin/main. PR269 stays draft/native-and-merge-held. No third
retail or bot/encounter attempt is permitted; recording is off.

Accepted shared reputation state retains source-hashed float axes in schema v58.
Fresh axes are initialized zero; missing legacy history remains unknown per axis.
Explicit SetReputation establishes only its own axis, without clamp/notifications.
GetReputation CTDA573 uses this same owner through native quest/message, ordinary
conversation and scripted speech adapters. Its raw selector chooses positive only
for1, otherwise negative. Actual installed-delegate composition, old-callback
negative controls, cold/unknown-state and owned14-condition/12-INFO checks pass.
The repaired full gate passed08:27:19UTC with all2204 source/config hashes unchanged.
These are component proofs, not ordinary dialogue/quest or retail-parity completion.

Active branch: codex/fnv-reputation-percentage, now based on accepted main.
The current independent change adds CTDA GetReputationPct(function574) through the
same world owner and all three production condition adapters. Source arithmetic
is float32 axis/capacity: no multiplication by100 or clamp. Capacity remains
immutable winning REPU DATA, with no new save field. Known/unknown history,
source identity and context refusal remain shared. Nonfinite quotients refuse;
exceptional-result retail behavior is unclaimed. The separate SCTX GetReputationPct
command, threshold/rank and disguise semantics remain unbound.

Focused arithmetic, signed/unclamped values, finite-result/unknown-axis refusal,
master-order, comparison and cold tests pass. Unchanged installed conversation and
speech adapters reproduce exact missing574 refusals; repaired native composition
passes both with current/cold/legacy state. The typed owned-data audit exercises
two winning INFO conditions(selector1) with controlled known axes and no source/save
mutation. The stopped reflection helper was not retried or used as corpus evidence.
The full gate passes08:49:46UTC: exit0, all2204 source/config hashes unchanged,
diff check0 and recording off. Keep the published head frozen and merge held for
independent review and hosted checks. Evidence:
local/fnv-wells-fixed/reputation-state-20261023/percentage-focused.log,
percentage-native.log, percentage-owned.log, percentage-negative-native.json and
percentage-final-result.json.
Neutral percentage contract SHA256:
88AFF097879A1166A5A96730CB1A92390EC1154D6D551E5A8E039C08D03F8FC8.

Primary CTDA dispatch permits null subject/context for these subject-independent
commands. Nonzero missing/wrongly typed explicit references refuse because raw-ID
frontend fixup remains unqualified; run-on3+ stays unbound. Source context contract
SHA256 B4676FCC6F2C43C7146B336AEAA59DB3D131DB3ECDE4D310A1BC27A34018E85D.

Notification and witnessed hacking qualification stay parked after two approaches.
Exact missing owners remain retained combat-group membership/counts, UI transition
predicates, nested player serialization/warm-load ordering, witness membership,
detection and concrete process responses. No generic in-combat/modal substitute,
empty witness set, guessed history or suppressed side effect is added. Add/Remove,
crime-triggered reputation and native hacking remain closed until required effects
are owned. Existing latched script faults are neither reset nor replayed. Original
campaign and retail inputs remain read-only; no campaign, ordinary conversation,
speech/radio playback, final-pixel or parity completion is claimed.

## Gameplay and test-control checkpoint

The best committed checkpoint is v56 slot 4dc878ab017f4e75a41f80d213387284:
120 HP/eight Stimpaks after ordinary Aid/Use and F5. SHA256:
7B366B739C51102CCF62AD2C8CEE25D878EE8FAC7032A2C00B56AE36C9655F14.
Fresh-process Continue verifies its HP, inventory and paused Resume menu. The
original post-kill source remains unchanged. Actual delayed OnDeath reaches the
source two-second timer and changes well-2 counter 0 -> 1 exactly once. Later
unsaved fights reach counter 2, but the player is defeated before F5. Those kills
are run evidence, not committed tutorial progress; objective 50 remains incomplete.

The changed bounded-bot attempt uses the existing in-engine ordinary-input combat
owner, actual reload readiness and a reviewed private outer watchdog. Resume must
settle before starting the bot because subsequent external ordinary input stops it.
Cleanup cannot overtake an uncertain command; save acceptance requires a new
matching generation/slot completed while paused. PID365524 reaches gameplay at
120 HP and zero publication losses, then stops on an observation older than the
private watchdog's 500 ms bound. Stop acknowledgement takes 1,401.9 ms. Pause,
bot-inactive and 120 HP are observed; normal exit is 0 at 23:20:06 UTC. Source and
copy hashes remain unchanged; recording is off. One discharge hits terrain with
no verified contact, kill or new save. Review finds no concrete aiming mismatch:
readiness rays are unspread, while normal firing applies source spread and muzzle
parallax. The exact miss is not attributed to spread versus target motion.
Evidence: local/fnv-wells-fixed/tutorial-well2-bounded-bot-20261022/acceptance.json.

No identical combat retry. External observation/control cadence is a test-control
blocker, distinct from the unchanged 250 ms atomic-publication loss guard. Prior
747/942 ms atomic-write delays remain unexplained. A healthy Temp run is not their
fix. Do not relax deadlines, clear loss, force hits or write player/quest state.
Escape can dismiss a completed save notice without pausing; verify actual UI
ownership and pause separately. Retail startup remains held after two failed
attempts and normal cleanup; no third blind launch.

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

## Remaining completion gates

- Current P0: integrate qualified native hacking owners while preserving closed outcome admission, then resume ordinary tutorial progress with reliable control. Shared faction arbitration is implemented; actual eligible/ineligible
  assistance remains unverified. Do not repeat the retired encounter admission.
- Tutorial: complete remaining wells, delayed death counters, loot, ingredients
  and campfire cooking through ordinary input, F5 and separate-process Continue.
- NPC packages: marker current/queued/outgoing animation and physical occupancy
  owners remain missing. Furniture and eating execution, voiced/cold behavior,
  admission and movement-request clocks remain open. Never substitute clip
  completion for qualified native readiness.
- Access: terminal pages/results exist, but missing keys/passwords stop before
  ordinary lockpicking/hacking. Candidate/attempt/bracket primitives are
  prerequisites; native UI/admission, retained outcomes, qualified rewards and
  persistent outcome ownership still need integration.
- Combat: extend actual attack families, hit reactions, embedded emitters,
  assistance and survivable encounters. Selected gecko damage/kills do not
  establish complete combat.
- World/UI/save: continue authored transitions, inventory scrolling, bottle
  destruction and all reached crafting/menu paths. Preserve scripts, targets,
  RNG, sound/animation, cell semantics and exactly-once results across saves.
- Visual parity: corrected Strip shells and selected controller/particle owners
  have bounded proof; the Vault21 missing-shader preparation state remains
  unqualified. Do not fabricate a material or skip geometry for a prettier scene.
  Matched retail telemetry, audio and final pixels remain separate acceptance lanes.

Use docs/implementation-plan.md for the broader dependency ledger. Do not replace
implementation with perpetual telemetry or optimization audits. FO1/FO2 work is
preserved; physical FNV VR package g remains rejected and unaccepted.
