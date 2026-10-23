# Current work

Updated 2026-10-23. Priority: fully playable vanilla Fallout: New Vegas flat mode
before repeated deep optimization. Follow docs/implementation-plan.md, including
terminals, lockpicking, hacking and all 13 skills. Owned retail inputs stay read-only;
no campaign or parity completion is claimed. The coordinator owns builds, native
input and Git; no competing jobs are permitted. The existing writer is explicitly authorized.

## Active block

Accepted main/origin-main is b6836fecb8847f2d743c3561c959db003bbce92c (PR295),
merged after exact-head independent review and all five hosted checks. Both script
executors now share GetDestructionStage with explicit known/unknown object-health
presence, separate from rendering Stage/ModelStage. Actor maximum health, complete
trap activation and DamageObject scheduling remain outside that query repair.
Local main was synchronized; the completed branch has a verified complete Git
bundle with an identical tree to the accepted squash commit and is removed.

Active branch: codex/fnv-terminal-hacking. Locked keyless TERM activation now
connects to the existing owned-dictionary/shared-RNG hacking transaction through
a first-party mouse/keyboard character grid. Both columns, word/bracket selection,
dud removal, attempts, cancel and terminal outcomes remain transaction-owned.
Success publishes the independent terminal high bit, shared player XP and the
Computers Hacked statistic/CHAL handoff, then opens actual terminal contents after
the source success delay. Lockout increments the retained result byte once.
Generic Unlock and row-zero result execution are not substitutes. Active puzzles
block saves; cancellation and external layer retirement release input and restore
pause/mouse state, including teardown during the success delay. Teardown neither
opens contents nor saves a retiring campaign. Outcome failures retain consumed
state and diagnostics instead of replaying XP/statistic callbacks.

Initial admission is flat, effectively unowned, nonleveled/non-overridden TERM,
known result/stat history, ASCII dictionary and no acquired generation perk
entries. Crime/witness, Science-bonus entry context, unknown legacy history and XR
remain visible refusals. IgnoreLockedTerminal uses entry20 only at failure count1.
Fresh statistic counts start at zero; missing saved counts stay unknown through
restore/resave. Existing saves are not inferred from CHAL progress or inventory.

The existing writer delivered NativeHackingMenu.cs and FalloutMiscStatistics.cs;
the coordinator owns integration, Git and serialized builds. The connected runtime
compiled with zero warnings/errors. Selected hacking contracts, including new
statistic prefix/cold/unknown controls, pass; changed-file formatting passes.
Earlier local NU1900 failures were package advisory transport,
not gameplay failures. The required publication gate passed; a final menu-retirement
guard is being closed against the amended runtime. No capture or route-only audit stream is
introduced. No ordinary hacking, campaign, XR or retail presentation acceptance is
claimed. Recording is off; original saves remain unchanged. Private receipts:
local/fnv-wells-fixed/hacking-integration-20261023/.

Static ACTI trap activation remains a separate parked owner. A real production
DefaultActivate route exists, but the selected controller-free trap cannot supply
its currently required Open/Close motion. Source default activation deliberately
can suppress attached-script dispatch; no forced OnActivate, no-op or guessed
actor propagation substitutes for the unqualified base action/lifetime effects.
Ordinary physical NOTE pickup/transfers likewise retain their separate reached
ownership/Karma and immediate reversible reference/relationship requirements.
Neither Taken=true nor queued SetEnabled(false) is substituted.

The coordinator owns serialized Git/build/native work. No denied outbound writer
contact, competing job, retail launch or third combat admission is introduced.
Recording remains off. Private receipts are under
local/fnv-wells-fixed/owned-jukebox-composition-20261023/ and
local/fnv-wells-fixed/lockpick-runtime-20261023/.
Shared reputation state retains source-hashed float axes in schema v58. Fresh axes
are zero; absent legacy history stays unknown per axis. CTDA573/574/575 share that
owner through native quest/message, conversation, speech and NPC/creature/unloaded
package adapters. Selection and package lifecycle are unchanged. Component and
owned query proofs do not establish ordinary campaign or package execution.
All three getters preserve known/unknown history and canonical source identity.
Null context is source-permitted; malformed nonzero explicit references refuse
because raw-ID frontend fixup stays unqualified; run-on3+ stays unbound. Nonfinite
ratio/setting values fail visibly; exceptional retail/FPU behavior is unclaimed.
The separate SCTX threshold command and broader rank/disguise remain unbound.
Existing latched errors are neither cleared nor replayed.

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

- Current P0: connect and validate the owned lock/pin presentation and
  validate ordinary admitted DOOR/CONT interaction, then close the next reached
  gameplay owner. Collected-note integration is already on main. Hacking outcome admission remains closed.
  Shared faction arbitration is implemented; actual eligible/ineligible assistance
  remains unverified. Do not repeat the retired encounter admission.
- Tutorial: complete remaining wells, delayed death counters, loot, ingredients
  and campfire cooking through ordinary input, F5 and separate-process Continue.
- NPC packages: marker current/queued/outgoing animation and physical occupancy
  owners remain missing. Furniture and eating execution, voiced/cold behavior,
  admission and movement-request clocks remain open. Never substitute clip
  completion for qualified native readiness.
- Access: the working lockpick slice connects admitted keyless DOOR/CONT input,
  manual/Force outcomes and shared XP/activation; ordinary campaign acceptance
  and its explicit unsupported lanes remain open. Missing terminal passwords
  still stop before native hacking. Candidate/attempt/bracket primitives are
  prerequisites; hacking UI/admission and complete outcome ownership need integration.
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
