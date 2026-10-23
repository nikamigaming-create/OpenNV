# Current work

Updated 2026-10-23. Priority: fully playable vanilla Fallout: New Vegas flat mode
before repeated deep optimization. Follow docs/implementation-plan.md, including
terminals, lockpicking, hacking and all 13 skills. Owned retail inputs stay read-only;
no campaign or parity completion is claimed. The coordinator owns builds, native
input and Git; existing writers have disjoint scopes and run no competing jobs.

## Active block

Baseline main/origin-main is 3528ad6428ebb780cac25255de6b26e7f72bfd94 (PR272).
All five checks and independent review passed; local main matched origin with
all 2,189 gated hashes unchanged. The coordinator is on
codex/fnv-hacking-native-session. PR269 remains draft/native-and-merge-held.
No third bot encounter, retail attempt or denied writer contact is permitted.

The active implementation adopts the qualified unsigned stream for hacking
preparation through GenerateFromProcess. One static process owner is retained
across content-source retirement; it lazily seeds on its first positive draw
from the actual Win32 GetTickCount boundary, uses the qualified raw 624-word
recurrence and mask/modulo rules, and supplies the source 100 ms retry window.
Zero bounds consume no draw or clock read. There is no per-menu seed, arbitrary
clock fallback, restore API, or conversion of existing saved RNG fields. Other
script/sound/actor streams remain unchanged; complete cross-system RNG routing,
save/load equivalence and retail stream parity are not claimed. Unqualified
platform clocks refuse explicitly.

The selected owned preparation audit exposed a genuine terminal source-reader
refusal: compiled references to engine-created player14 required a nonexistent
plugin record. Terminal results now use the existing script binder's exact
canonical runtime identity rule; other missing and foreign player-shaped forms
still require winning records. This repairs source binding, not a fabricated
player entity or a demonstrated native terminal interaction.

Deterministic contracts pass lazy seeding, zero/normal/mask bounds, raw block
transitions, process-factory identity, cancelled generation with no draw, clock
faults and preservation of existing saved-script RNG behavior. The complete
reference-script probe passes, including engine-player reference controls. Two
generations using the actual owned TERM/dictionary and native process clock pass
across content retirement with unchanged source hashes and one retained seed.
Their Science/perk inputs are explicitly controlled; they are not ordinary
player access. Observation-only counters report a timed pair of 1,523.975 ms /
15 windows / 2,631 attempts / 19,392,479 draws, then 1,572.670 ms / 15 windows /
2,443 attempts / 18,060,569 draws. Windows include the initial window; this is
consistent with timed adaptive search, not retail performance proof. Retry policy
and RNG ordering are unchanged. The renewed final runtime gate passes at
03:39:03 UTC: exit 0, all 2,191 source/configuration hashes unchanged, and diff
check clean. Release/Debug and native audits pass; recording remains off. Hosted
checks and independent review are pending; the source is frozen.

Native locked-terminal admission remains closed at both existing gates. Actual
session display/input, placement fallback and reference outcome/reward/statistic
handoff must be qualified before access or XP is committed. No native hacking,
campaign completion or retail parity is claimed. Recording remains off.

The campfire source/restore and event proofs remain accepted component evidence.
Production StartConversation exists; the controlled fixture's conversation
refusal is not a missing command. No physical contact, conversation, crafting or
new campaign progress is claimed from those proofs.

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
