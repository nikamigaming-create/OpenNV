# Current work

Updated 2026-10-23. Priority: fully playable vanilla Fallout: New Vegas flat mode
before repeated deep optimization. Follow docs/implementation-plan.md, including
terminals, lockpicking, hacking and all 13 skills. Owned retail inputs stay read-only;
no campaign or parity completion is claimed. The coordinator owns builds, native
input and Git; no competing jobs or denied writer contact are permitted.

## Active block

Accepted main/origin-main is05b4366deaf366b13e629d31111a5e8e9389b0f1 (PR287),
merged15:29:18 UTC after independent source review and allfive exact-head checks.
Local main matched origin/main and was clean. Completed feature refs were removed
only after exact squash-tree equality and independent bundle recovery/integrity
verification. Three refs remain apart from this new feature: main, unique classic
sound PR242 and held combat-control PR269. Original saves remain unchanged.

Accepted NOTE membership is shared by script queries, CTDA382, native adapters,
Pip-Boy and schema59. Legacy missing history remains unknown through resave.
AddNote and positive player AddItem join membership and qualified source-formatted
HUD/audio requests. The full unchanged owned ED-E GameMode passes controlled note
fixtures through both executors; the owned HUD selects/registers/completes an
actual folder variant without cold Presented replay. Those are component proofs,
not ordinary recruitment, acquisition, campaign or retail presentation parity.

Active slice: codex/fnv-remove-note. Ordinary non-console RemoveNote now binds both
script executors to typed compiled NOTE identity and the same first-entry removal
owner. Native effects invalidate only that exact current selection before removal,
even when membership is absent. No playback stop, menu close, inventory mutation
or ItemRemoved HUD notice is introduced. Unknown membership refuses before UI
mutation; duplicate saved identities lose only their first match.

Selection belongs to the actual menu lifetime. It survives refresh/visibility
changes of that owner, clears on matching removal without selecting another row,
and retires when the owner is destroyed. A newly constructed flat menu starts a
fresh selection; a retained XR menu keeps its own lifetime. Source diagnostic
output is conditional on console invocation, not a required ordinary-script HUD
side effect. Console invocation/output remains unsupported rather than inventing
its eligibility, text lifetime or nested-context behavior.

Corrected full gate PASS at15:56:53 UTC, Release/Debug zero warnings/errors,
formatting/analyzers/contracts/launcher/native Godot checks passed. Allfive
selected checks pass: both-executor removal, full quest-query suite, unchanged
owned ED-E GameMode fixture, actual native adapters and owned Pip-Boy UI.
All2216 frozen source/config hashes and original save hash remain unchanged,
diff check0. Only this ledger is finalized afterward.

Disabled command bindings reproduce the original unsupported operation separately
in fallback and shared execution, with exact source restoration. Two additional
negative controls reproduce navigation-reset selection loss and empty-list
selection suppression. The corrected implementation preserves those existing
behaviors. Tests cover first-match duplicates, absent/unknown membership,
canonical/compiled argument refusal, cold state and retained failure prefixes.
Actual native tests cover exact selection, invalid-input atomicity, replacement/
retirement and playback-owner noninterference. Owned UI tests click actual NOTE
rows and verify live removal, no forced reselection and cold membership. Existing
radio/Aid negative paths remain intentional. The slice is ready for checked PR
publication and independent exact-head review; main remains authoritative.
Ordinary placed-note pickup and world/container transfers remain refused before
mutation. They require the separate ownership/Karma predecessor and reversible
reference retirement/relationship owners; neither Taken=true nor queued
SetEnabled(false) is substituted. HUD readiness/drop, fade/glow timing,
cross-kind tail deduplication, reverb and full console presentation remain
visible differences. No ordinary RemoveNote playthrough or parity claim.

The coordinator owns serialized builds/runtime/Git. No denied outbound writer
contact is retried or relayed; incoming sealed evidence is independently assessed.
Private receipts: local/fnv-wells-fixed/remove-note-20261023/. Standing checked
publication direction remains active. PR269 stays draft/native-and-merge-held;
no third retail or bot encounter attempt. Recording is off.
Shared reputation state retains source-hashed float axes in schema v58. Fresh axes
are zero; absent legacy history stays unknown per axis. CTDA573/574/575 share that
owner through native quest/message, conversation, speech and NPC/creature/unloaded
package adapters. Selection and package lifecycle are unchanged. Component and
owned query proofs do not establish ordinary campaign or package execution.
All three getters preserve known/unknown history and canonical source identity.
Null context is source-permitted; malformed nonzero explicit references refuse
because raw-ID frontend fixup stays unqualified; run-on3+ stays unbound. Nonfinite
ratio/setting values fail visibly; exceptional retail/FPU behavior is unclaimed.
The separate SCTX percentage/threshold commands and broader rank/disguise remain
unbound. Existing latched errors are neither cleared nor replayed.

Generator reference MenuMode scheduling is parked after two bounded approaches.
Selection publishes a consumptive button result; source closure has a separate
transition owner. The common-frame ordering of reference execution, input and
visibility retirement is unqualified. No synchronous click callback or guessed
closing timer is substituted. Snuffles remains parked at its distinct base/damage
history and locomotion-resource transition boundaries; no scalar setter bypass.

Notification/witness qualification remains parked after two approaches. Missing
owners include retained combat-group membership/counts, UI transition predicates,
nested player serialization/warm-load ordering, witness membership, detection and
concrete process responses. No generic in-combat/modal substitute, empty witness
set, guessed history or omitted side effect is added. Add/Remove, crime-triggered
reputation and native hacking remain closed. No ordinary dialogue/speech/radio,
campaign, final-pixel or retail parity completion is claimed.

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

- Current P0: finish the checked collected-note integration, then take the nearest
  qualified ordinary route blocker. Hacking outcome admission remains closed.
  Shared faction arbitration is implemented; actual eligible/ineligible assistance
  remains unverified. Do not repeat the retired encounter admission.
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
