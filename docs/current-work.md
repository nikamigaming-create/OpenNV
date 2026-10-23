# Current work

Updated 2026-10-23. Priority: fully playable vanilla Fallout: New Vegas flat mode
before repeated deep optimization. Follow docs/implementation-plan.md, including
terminals, lockpicking, hacking and all 13 skills. Owned retail inputs stay read-only;
no campaign or parity completion is claimed. The coordinator owns builds, native
input and Git; existing writers have disjoint scopes and run no competing jobs.

## Active block

Baseline main/origin-main is 5a7b14b893f95dc651f18156bc5bd80b68abc464 (PR268,
checked and merged). Work is on codex/fnv-bounded-combat-control. The current
block adds optional health and simulation-time stop limits to the existing
explicit-combat bot owner. Direct physics observations must stop input before
evidence persistence can block; the session clock excludes pause, loading and
modal control and does not reset between targets. Absent limits retain existing
defaults. A stop requests ordinary protective pause once; only observed source
pause establishes that it took effect. Pending shots cannot become successes.
The unchanged 250 ms publication-loss guard remains independent. The writer owns
bot/admission/contracts; the coordinator owns builds, native input and publication.
Debug compilation has zero warnings/errors, and the focused bot contracts pass,
including exact HP/time boundaries, default behavior, invalid admission, original
fault precedence, pending-shot refusal and release before throwing/blocking
persistence. The optional clock is advanced only by the harness physics callback
at early physics priority; ordinary process ticks add no time. Duplicate/regressed
physics samples, process/physics same-sample ordering, excluded states and default
callbacks have focused checks. Default bot/skill process cadence is unchanged.
The corrected full runtime gate passes at 00:38:21 UTC: exit 0, all 2,186 frozen
source/configuration hashes unchanged, and git diff --check clean. The earlier
process-clock snapshot did not satisfy this owner. Hosted checks are pending;
native claims and merge admission remain held for parent review.

Native acceptance is blocked after two failed private-driver admissions; the bot
never started in either. The first waited incorrectly for pause after Continue
resumed. The second passed offline readiness checks against retained real states,
then failed on a raw observation file-sharing error while loading. Its settled
Stop command released input but did not request a new pause because the loading
tree was already paused; ordinary Continue later resumed and the player was
defeated. Both runs exited normally, recorded no frames or publication losses,
made no saves, and preserved source/copy hashes. No optional-limit native outcome
or campaign progress is claimed. No third encounter: diagnose the private read
and loading/cleanup lifecycle before another attempt. Product proof and this
driver failure remain separate; do not alter gameplay or weaken telemetry guards.

PR268 fixes directed faction aggregation priority ally 2 > friend 3 > enemy 1 >
neutral 0 and removes an invented shared-membership ally input. Explicit self
reactions and live directional overlays remain authoritative. Synthetic and
genuine saved coyote/gecko pair/cold checks, the old-runtime refusal negative
control, full local gate and all five hosted checks pass. The selected pair is
not joined to the historical logged victim. Native assistance, its unchanged
range/LOS/package/aggression guards, the separate player-enemy faction flag and
retail parity remain unverified.

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

- Current P0: implement and validate qualified mixed-faction aggregation, then
  verify actual eligible/ineligible assistance when ordinary control is reliable.
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
