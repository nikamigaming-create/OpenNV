# Current work

Updated 2026-10-23. Priority: fully playable vanilla Fallout: New Vegas flat mode
before repeated deep optimization. Follow docs/implementation-plan.md, including
terminals, lockpicking, hacking and all 13 skills. Owned retail inputs stay read-only;
no campaign or parity completion is claimed. The coordinator owns builds, native
input and Git; no competing jobs or denied writer contact are permitted.

## Active block

Accepted main/origin-main is 729feec620937705ea8f51ae55a65e3b407298c5 (PR278).
PR278 merged at 07:38:07 UTC after explicit release, independent case-identity
re-review and all five exact-head checks passed at
3b2b26c6e39386e09460be7ef106253bd3257d28. Local main equals origin/main.
PR269 remains draft, native-and-merge-held. No third bot encounter or retail
attempt is permitted. The accepted effective-owner query includes encounter-zone
and current-cell inheritance; cold restore stages validated placement/ownership
before querying effective access. This does not admit hacking or crime effects.

Active branch: codex/fnv-reputation-notification-lifecycle. Accepted PR278
binds source GetReputation/SetReputation through one world owner, object and quest
script adapters, and ordinary campaign capture/restore. The setter writes one
source float axis directly without clamping or AddReputation feedback. Fresh
worlds have initialized zero; legacy missing history stays unknown per axis.
An explicit setter establishes only that axis. Schema v58 retains source-hashed
reputation state, admits v57/older without invented history and rejects old-schema
payload injection. Restore validates all entries before replacing the owner.

Debug builds, actual first-party OnActivate/GameMode shared-state contracts,
malformed-state refusal and cold retention pass. The owned audit verifies all13
winning REPU declarations, the actual v56 campaign reader and a temporary full
v58 codec roundtrip with unchanged references, original save and source hashes.
The case-identity review finding is reproduced by two old-runtime negative
controls. The repaired map, duplicate validator and staging use the established
case-insensitive form-key comparer; reads/writes/restores use canonical winning
keys. Fresh and legacy alias lookup, unknown axes, canonical cold serialization
and atomic duplicate-alias refusal pass. The repaired full gate passes at
07:25:53 UTC: exit0, all2202 source/config files unchanged and diff check0. The
repaired head is merged; hosted runtime passed at 07:34:35 UTC in run39917835438.
These are getter/setter and persistence proofs, not ordinary campaign or retail-parity completion.
Evidence: local/fnv-wells-fixed/reputation-state-20261023/case-final-result.json.
Neutral source contract SHA256:
F38D9AECF12D17B8B666B62252034F191A9992BFA2504E5A8B79F1BA57BC6789.

The current runtime change binds CTDA GetReputation (function573) through the
accepted shared reputation owner and the existing native condition adapter. Dialogue
queries reach that owner before speaker-specific fallback, including NPC listeners.
Its raw integer selector chooses positive only for1 and negative otherwise; this
is distinct from script-command selector validation. Fresh zero and legacy unknown
axes retain their existing meanings. Other reputation/rank/disguise functions stay
unbound. Synthetic source-master, dialogue/message, comparison, live/cold and
refusal contracts pass. The owned audit passes14 conditions in12 winning INFOs,
including selectors0/1/2, using controlled known axes without source/save mutation.
This does not establish whole-record eligibility or ordinary dialogue/quest progress.
The old dialogue dispatcher negative control reproduces NPC-listener refusal;
the repaired default contract passes. The full runtime gate passes at08:00:20UTC:
exit0, all2203 source/config files unchanged, diff check0, recording off. Merge
is held for independent review of the checked published head.
Evidence: local/fnv-wells-fixed/reputation-state-20261023/condition-focused.log,
condition-owned.log, condition-old-result.json and condition-final-result.json.
Neutral condition contract SHA256:
89E70EAC09BABF287BCE033A971B8E8C18A003F7BD212C3903D9395C6E0C5253.

AddReputation/RemoveReputation, crime-triggered reputation and notification remain
unbound. Notification qualification is parked after two finite approaches. The bounded notification qualification distinguishes
two immediate predicates from four deferred predicates and confirms a single
identity slot with overwrite semantics, consumed using current reputation values.
The combat predicate depends on retained combat-group membership/counts, not
current engagements. Dialogue/InterfaceManager and StartMenu transition owners,
nested player serializer effects and actual warm-load reset order remain open.
Revert clearing does not prove LoadGame clearing. Two finite approaches stop at
those exact joins; no approximation, save-field addition or mutation is admitted.
Evidence: local/fnv-wells-fixed/hacking-session-20261023/reputation-notification-contract.private.md
(contract SHA256 E9BEFD3583D56C9D0012B6B1B372A9EC07C06EE3DB99DE8FD3DB368DAB38FD15).
No notification runtime code changed in that qualification block. Existing latched
script faults are not reset or replayed.

Witness candidate membership, retained observer detection and concrete process
responses remain distinct unknown owners. No nearby-actors substitute, empty
witness set or counter-only crime effect is added. Hacking success ordering remains
result bit -> XP -> conditional owner/witness handoff -> statistic -> later contents.
Native hacking stays closed until required effects and input/lifetime are owned.
Recording is off and original campaign/retail inputs remain read-only.

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
