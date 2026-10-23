# Current work

Updated 2026-10-23. Priority: fully playable vanilla Fallout: New Vegas flat mode
before repeated deep optimization. Follow docs/implementation-plan.md, including
terminals, lockpicking, hacking and all 13 skills. Owned retail inputs stay read-only;
no campaign or parity completion is claimed. The coordinator owns builds, native
input and Git; existing writers have disjoint scopes and run no competing jobs.

## Active block

Baseline main/origin-main is 621bb7e7f1b095bc773fa464598a4dac8026e1da (PR271).
All five hosted checks and independent review passed; local main matched origin.
The coordinator is on codex/fnv-terminal-hacking-admission. PR269 remains draft
at e7a459b5 with native/merge acceptance held. No third encounter admission,
retail attempt or denied writer contact is permitted.

The active implementation closes two hacking-generation prerequisites. The owned
dictionary is space-delimited with no newlines; the old newline-only decoder
refused the actual resource. The source file reader uses a space delimiter and a
32-byte buffer, retaining at most 31 bytes per call. Decoding now preserves byte
case, delimiter consumption, empty spans, partial final tokens and long-token
chunks. It does not use arbitrary whitespace splitting or invent replacement words.
The source candidate-derived attempt calculator now runs inside production
candidate generation instead of an arbitrary caller callback. It uses positional
likeness groups and recursive retained subsets, then the existing four-attempt
floor. Requested and retained candidate counts remain distinct.

The native terminal opener still refuses locked access without a password.
Ordinary hacking requires remaining shared RNG/clock lifetime, reference-aware
admission/retained outcomes, native presentation/input, success followups and
persistence. This block does not unlock access, grant XP, acknowledge success or
claim ordinary hacking. Terminal-specific retained difficulty is distinct from
placed XLOC; no XLOC refusal was removed on unrelated evidence. Formatting and focused synthetic/owned-dictionary checks pass. Nine real
word-length sets pass, including calculated allowances of five for the controlled
four- and five-letter sets. The source recursion retains its candidate-count
outcome-index limit even when longer words have higher likeness classes.
The renewed full runtime gate passes at 02:31:47 UTC: exit 0, all 2,189 frozen
source/configuration hashes unchanged, and git diff --check clean. Release/Debug
builds have zero warnings/errors; native locomotion, render-trace and recorded-input
audits pass with recording off. Hosted checks and independent review remain pending. Builds and native checks remain serialized, recording off.

The accepted campfire event proof uses actual shared OnTrigger admission,
compiled bindings, quest state and canonical saved inventory. Powder paths reach
an explicitly refused native conversation boundary; production StartConversation
already has a command and host. This is not a missing product command. Existing
normal Restore clears the exact retained Parse-prefixed fault after source checks;
no new recovery policy or real save mutation was added. The probe's cold retry
checks post-restore versus post-retry state; broader prefix restoration relies on
existing restore contracts. Physical contact, conversation, crafting and campaign
progress remain unverified.

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

- Current P0: publish and review the qualified campfire branch/save-admission
  repair, then continue the remaining-wells route only through reliable ordinary
  control. Shared faction arbitration is implemented; actual eligible/ineligible
  assistance remains unverified. Do not repeat the retired encounter admission.
- Tutorial: complete remaining wells, delayed death counters, loot, ingredients
  and campfire cooking through ordinary input, F5 and separate-process Continue.
- NPC packages: marker current/queued/outgoing animation and physical occupancy
  owners remain missing. Furniture and eating execution, voiced/cold behavior,
  admission and movement-request clocks remain open. Never substitute clip
  completion for qualified native readiness.
- Access: terminal pages/results exist, but missing keys/passwords stop before
  ordinary lockpicking/hacking. Candidate/attempt/bracket primitives are
  prerequisites; admission, UI/input, retained outcomes, qualified rewards,
  authoritative generation callbacks and owned tape still need integration.
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
