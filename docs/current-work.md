# Current work

## Active objective

Complete ordinary TTW play from Fallout 3's birth through every Vault 101 quest,
including the G.O.A.T., Megaton, the authored Union Station power/ticket/train
route, New Vegas and Benny, then continued campaign play. After Megaton, use the
Fallout bot's ordinary movement and activation adapters with source collision and
door rules. Verify Benny Humbles You and Steals Your Stuff's selected deleveling,
gear confiscation/recovery, progression and cold continuity.

Complete all nine JAM modules, MCM, TTW's reached dependency behavior and compatible
recommendations from the current Best of Times and Wasteland Survival Guide.
Preserve all ten original mod targets plus Benny and the broader objective of
Nexus script/dependency compatibility. Registration and isolated fixtures are not
complete mod support. Follow [mod compatibility](mod-compatibility.md), the
[implementation plan](implementation-plan.md), [flat work order](flat-gameplay-plan.md)
and [JAM/MCM plan](jam-luna-max-plan.md). Coordinate the user-authorized parallel
camera, UI and mod investigations while keeping live input and publication under
one integration owner.

Prioritize flat play and system polish while retaining shared VR gameplay and
saves. Classic flat presentation uses winning Fallout/mod screens and controls;
the optional Nikami experience adds enhancements to those owners. Supply
code-addressable checkpoints of actually reached states with complete quest,
script, actor, inventory, mod and source identity restoration. Validate cold
continuation, then use slow ordinary flat/VR input for focused checks. Retain the
continuous route/event log and produce the requested journey video with labeled
excerpts, side-by-side moments and wipes. All 36 broad recovery requirements remain
open. Preserve the complete campaign, release and headset objectives in the plan;
no whole-game or retail-parity completion is claimed.

## Verified ordinary opening

New Game reads the winning configured starting QUST and executes its stage/menu
programs before constructing a player. Its source MoveTo chooses the ordinary
CELL builder while retaining executed prefixes, controls and shared quest state.
Ordinary flat New/Yes reaches TTW's holding CELL and Capital/Mojave question.
Capital starts the owned Fallout 3 intro; Escape interrupts it through the source
continuation. Source loading/character-generation policies, numeric settings,
player camera packages, screen blood and baby-cry activation feedback execute.
Mom and Dad have independent owned voice/lip/result/completion channels. Parent
package arrival retains their authored poses through the gender question.

Ordinary Girl/name input reaches TTW's source gene projector and race/sex menu.
Next/Done/Yes accepts Hispanic selection and closes it. MatchRace and four
MatchFaceGeometry commands execute. Twelve speeches lead to the
[owned trait screen](authored-trait-menu.md), using winning XML, fonts, artwork,
descriptions, counters and Reset/Done controls. The modal wrapper pauses gameplay;
an extended ordinary hold preserves speech, package time, quest progress and the
draft. Done accepts zero traits and releases progression. Menus beyond these
bounded checks and matched retail/XR presentation remain open.

Fresh ordinary input completes seventeen speeches, enters CG00 stages 90 and
100, stops three actual birth-loop instances, clears current/pending camera
packages, disables Dad and stops CG00. The [shared saved youth policy](player-youth-appearance.md)
executes SetPCYoung. The [source sound-path owner](source-sound-paths.md) executes
SetSoundSourceFile and changes PHYBabyRattle to its owned baby-rattle directory.
The [shared toddler/scale owner](player-toddler-animation.md) now executes
SetPCToddler, the source One Year Later movie, player scale 0.4 and MoveTo to
CG01PlayerStartMarker. Birth and playroom share CELL Fallout3.esm:028138; reached
marker, pose and actual room pixels establish the ordinary toddler transfer.
CG01 reaches stage 10. Scripted speech now receives the existing actor-value
owner, and eligible random INFO groups use the saved shared script RNG. The fresh
run completes 28 speeches without the previous speech or opening-driver errors.
The autosave stays pending during active continuation, then writes the actual
campaign snapshot. Complete actor/interaction cold continuation is not accepted.
Cold Continue returns to the actual playroom pose and source toddler/scale state,
but retains visible unrelated actor/ragdoll failures. Normal E activation opens
the source playpen gate. Ordinary movement through its actual clearance fires
the reach-Dad trigger and enters stages 12/14. Dad reaches his next marker, where
the authored On End `setstage CG01 16` exposed the limited package-script host.
The [shared package-result owner](package-event-results.md) now passes synthetic
scope/prefix/cold-value checks and the owned native arrival/result fixture.
The resumed ordinary route executes Dad's On End result and enters stage 16.
Its QSDT starts Dad's next voice. The [source door-state owner](source-door-state.md)
now executes `CG01PlaypenGateREF.setOpenState 0` in both the owned native fixture
and the resumed ordinary route, with its actual source collision. Dad then selects
CG01DadCloseDoor (FalloutNV.esm:0457c4). A second fresh ordinary route again
reaches stage 16 and reproduces its nonzero-location-radius refusal. The
[source marker-radius owner](source-marker-travel-radius.md) now passes the
radius 25 to owned NAVM, refuses endpoints outside its radius and completes the
actual package in the selected native fixture. The zero-radius arrival/result
regression still passes. This package's own result scripts are empty; its
attached actor script separately declares OnPackageDone and the next stage.
That typed reference-package event bridge remains unowned. Ordinary continuation
with the candidate, subsequent stage programs and complete cold travel remain open.
The gurney departure, toddler quest completion and Vault exit remain open.

The [source camera repeat owner](player-package-camera-loops.md) now runs each
selected IDLE intro once and repeats its authored inner interval. Infinite event
poses no longer block package handoff; identical outgoing/incoming poses retain
their phase. Source text keys use the actual player sound owner. Synthetic and
three owned native fixtures pass repeat, cold camera, atomic rejection, removal,
sound-child reuse and voice-retirement checks. The mixer fixture observes nonzero
output without recording frames. Fresh ordinary input reaches CG01 stage 10 with
current/pending camera packages cleared and no camera or speech error. The new
110-second stereo take shows the source movie and actual playroom, but the camera
still remains under the birth-room lamp until the fade. Proper visible gurney
room departure is not accepted. A separate fresh live clock/pose observation
shows the actual current viewport camera advancing 6.74496 metres through the
owned path before removal. The travel channel and source anchor are bound.
The [quest elapsed-time repair](quest-script-clock-elapsed.md) fixes a general
overdue-clock path that discarded new frame time from GetSecondsPassed. It
retains scheduling debt, initial dispatch and saved clock fields. Synthetic and
owned CG00 timer checks pass at 30/60/90 Hz and with a 750 ms frame, including
modal suppression, once-only result and serialized overdue cold continuation.
Visible departure, fade/speech alignment and matched retail timing remain open.

The reviewed [tag skill menu](native-tag-skill-menu.md) uses winning Fallout XML,
AVIF text/icons, bitmap fonts and source controls. Draft values, Reset,
complete-only Done and live refresh have synthetic and isolated native proof.
Acceptance, cancellation, failure closure and tree exit restore prior pause,
mouse and player modal input once while retaining the source control mask.
The [typed postfix call owner](nvse-postfix-reference-calls.md) retains actual
reference identity and evaluates receivers/arguments once, with lazy inactive
branches and source compiled bindings. The selected JAM source audit admits
46 of 52 scripts, previously 41; eight MCM initialization scripts each execute
360 clean headless frames. Full module/player/MCM behavior remains unproved.
JBTMCM's attached object-type script lacks quest-clock admission and remains
visibly unowned. The combined gate and selected owned audits pass, with native
tag-menu stderr empty and its temporary PNG deleted after inspection. All five
exact-head checks passed; the tag/postfix block is merged. Clean main equaled
origin/main before the fresh radius/clock integration branch.

## Shared owners and checks

Source sound paths resolve canonical winning SOUN forms, preserve getter text,
and invalidate only changed form revisions. Script, animation/NIF/response and
menu consumers refresh descriptors while prepared/playing voices retain their
source and media. Synthetic typed/getter/setter/argument/prefix/cache/graph-scope
checks pass. The owned native fixture executes TTW's change and reset, primes
script and animation caches, retains queued old media, observes nonzero native
mixer output with zero discarded samples, restores the path and retires voices.
It records no frames. Form-path mutations live with the selected graph, outside
campaign snapshots; retail cold continuity remains unmeasured.

[Source stopping](source-sound-stopping.md), [package removal](player-package-removal.md),
[scripted speech completion](scripted-speech-completion.md), shared race changes
and [face geometry](actor-face-geometry.md) retain their bounded synthetic/owned
checks. Youth selection uses winning RACE.DNAM defaults without replacing stored
customization; male/female first/third body checks pass. Trait input/reset/pixel
restoration, pause/resume, prior-pause preservation and cleanup checks pass.
Source movement/activation controls suppress HP/AP/reticle and target prompts
during the opening while retaining subtitles. Further capabilities and exact
limits are in [status](status.md); component checks do not establish campaign,
actor, rendering, audio or physical-headset parity.

Synthetic toddler/scale, numeric actor-value and random-dialogue checks pass.
Owned native male/female body/camera/movement/clear/scale checks and two actual
encouragement voice/result/completion checks pass. The full required runtime gate
passes; that block is merged and main was synchronized before the next branch.
The package-result block passed its owned fixture and full gate, was merged after
all five CI checks, and main was synchronized before the fresh door-state branch.
The door-state synthetic and owned native checks pass, including cold motion,
source collision, duplicate requests, unbound-owner refusal and source-drift
rejection. Its required runtime gate and all five CI checks pass; the block is
merged and local main equals origin/main before this camera integration branch.
The integrated camera gate and all three owned audits also pass, with empty
native stderr. All five exact-head CI checks passed, the block is merged, and
clean local main equaled origin/main before the fresh tag/postfix integration.
Recording is off during development and gates.

## Visible gaps

- The current cold toddler CELL retains 109 missing runtime references and two reached
  GetLinkedRef operand failures. Camera body targets, actor cold clocks,
  interaction continuation, subtitle competition and matched timing remain open.
- PlaySound's reverb, listener submersion, complete output/volume routing and script
  3D/node behavior remain incomplete. Voice/lip/output clocks and camera cadence
  require measurement; publication overruns remain visible rather than hidden.
- Saves write v25 with source-validated face state and admit v24. Reached-state
  slot creation and in-process loading preserve their tested root state, and the
  owned cold fixture retains 641 saved script owners before execution. The
  post-face cold native load exposes an unbound ragdoll accumulation-root rotation.
  Complete actor/interaction restoration and source save eligibility remain open;
  that checkpoint is not fully reusable.
- TTWStart retains TTW_EnableRadioFix failure. Complete TTW/FO3 character creation,
  Vault exit, train travel, New Vegas/Benny and continued campaigns are unverified.
  JAM/MCM remain partial; six of the selected 52 JAM scripts are rejected by the
  parser. Folder registration is not full module/dependency behavior. Benny 13.05
  is privately available but is not mounted or runtime-proved.
- Missing source resources and unsupported behavior fail visibly. All broad
  [recovery requirements](recovery-checklist.md) remain open at their full scope.

## Next owner

Publish the integrated source marker-radius and quest elapsed-time repairs after
the combined gate, selected owned checks and exact-head CI, then resume the
ordinary toddler/Vault route. Bind actual source OnPackageDone events through the
typed PACK filter and existing reference-script owner; do not substitute quest
results for arrival. The UI owner implements the source SPECIAL book's native
NIF/input/session foundation with explicit permanent-read/base-write callbacks.
The mod owner implements deferred source-string argument binding and graph-owned
numeric defaults. Preserve
the real inventory-reference identity gap; do not replace it with a base form.
The source trigger sphere and gate clearance have now executed through normal
input; preserve closed-door collision and reject unknown clearance.
Camera body/blend targets and matched finite-event/audio timing remain unbound;
the camera fixtures do not establish ordinary departure or retail parity.
Preserve shared youth/scale, modal pause, camera cancellation, independent sound
voices and path invalidation. Continue the complete TTW/JAM/Benny objective above.

## Private continuation

Current ordinary run: `tmp/development-lab/ttw-toddler-continuation-20261001/`.
Reached state: `tmp/toddler-continuation-reached.private.json`.
The actually written stage-10 autosave remains in that private run; it has not
passed complete cold actor continuation. Checks:
`tmp/toddler-followthrough-reference.log`, `tmp/toddler-owned.stdout.log`,
`tmp/speech-actor-values-owned.stdout.log`, `tmp/toddler-runtime-gate.log`.
Package result checks: `tmp/package-results-contract.log`,
`tmp/package-results-owned.stdout.log`, `tmp/package-results-runtime-gate.log`.
Door-state checks: `tmp/door-state-contract.log`,
`tmp/door-state-owned.stdout.log`, `tmp/door-state-runtime-gate.log`.
Integrated camera checks: `tmp/gurney-runtime-gate.log` and
`tmp/gurney-{camera,change,package}-owned.stdout.log`.
Tag/postfix integration gate: `tmp/tags-postfix-runtime-gate.log`.
Radius checks: `tmp/npc-radius-navigation.log`,
`tmp/npc-radius-owned.stdout.log`, `tmp/npc-radius-exact-regression.log`.
Radius/clock integration gate: `tmp/radius-clock-runtime-gate.log`.
The combined required gate passes. Final integrated owned checks are
`tmp/radius-clock-owned-timer.log`, `tmp/radius-clock-owned-travel.log` and
`tmp/radius-clock-owned-zero.log`; all pass with recording off.
Owned audit helpers: `tmp/Run-ToddlerAudit.ps1`,
`tmp/Run-SpeechActorValuesAudit.ps1`.

Private source analysis: `tmp/player-young-cg01.private.jsonl`,
`tmp/phy-baby-rattle.private.jsonl`, `tmp/set-pc-toddler-command.private.txt`,
`tmp/set-pc-toddler-handler.private.txt`, `tmp/set-pc-toddler-owner.private.txt`
and `tmp/toddler-paths.private.jsonl`. The private source reader/retail decoder
remain under `tmp/`; raw owned bytes and private addresses are never public inputs.

The requested current trait screenshot is
`local/recordings/ttw-current-opening-20261001/TTW-owned-traits-flat-20261001.png`.
The requested fresh transfer video is
`local/recordings/ttw-current-opening-20261001/flat-toddler-transfer-20261001.mp4`.
It includes the source movie and actual playroom entry, with stereo audio.
The new follow-through take is
`local/recordings/ttw-current-opening-20261001/flat-toddler-walk-20261001.mp4`;
ordinary movement approaches the playpen and Dad's encouragement continues.
The selected playpen still is
`local/recordings/ttw-current-opening-20261001/TTW-toddler-playpen-walk-20261001.png`.
Exports retain repeated latest frames; exact audiovisual timing remains
unverified. Recording is off and temporary inspection frames are removed. The
complete journey reel is pending ordinary campaign progress.
The fresh camera-build take is
`local/recordings/ttw-current-opening-20261001/flat-gurney-departure-20261001.mp4`:
110 seconds, 3,300 exported frames, 2,778 distinct source draws, stereo 48 kHz
AAC and a measured audio start offset of 0.0190942 seconds. It reaches Baby Steps
but does not establish proper gurney room departure or exact audiovisual timing.
Recording is off and all temporary review frames/pixels were deleted.

Both protected saves (`local/playtest-20260927-world/save.json` and
`local/playtest-20260920-companion/save.json`) must remain unchanged, with SHA256
`800A37CC6C97C543393A544229C4E71ACB213B45C12FC4584EF3E70E94E2FBB7`.
Use fresh private runs and preserve all visible failure state.
