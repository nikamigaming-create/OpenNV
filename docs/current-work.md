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
and [JAM/MCM plan](jam-luna-max-plan.md). Work without subagents.

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
CG01 enters stages 0 and 5. Its next SetPCToddler has no owner; the nested failure
remains visible on calling CG00. The player stays in birth CELL
Fallout3.esm:028138 before the authored player scale/move suffix. Do not rearm,
reset, teleport or bypass this retained prefix. The later current-build video
reaches nineteen speeches and background CG01 stage 10 while the player remains
in the birth room; speech reports `Dialogue condition 14 RunOn 0 has no actor/quest
owner.` This background progress does not establish the blocked player transfer.
Childhood-room entry and the gurney exit are not accepted. Ordinary Quit drains
all source readers.

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

The selected owned audit and full required runtime gate pass for the sound-path
patch. Publish after the final diff check, obtain all required exact-head CI checks,
merge and synchronize clean main. Recording is off during development and gates.

## Visible gaps

- The current birth CELL retains 83 missing runtime references and two reached
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
  JAM/MCM remain partial; 11 of the selected 52 JAM scripts are rejected by the
  parser. Folder registration is not full module/dependency behavior. Benny 13.05
  is privately available but is not mounted or runtime-proved.
- Missing source resources and unsupported behavior fail visibly. All broad
  [recovery requirements](recovery-checklist.md) remain open at their full scope.

## Next owner

Implement reached SetPCToddler through shared source-validated player animation
policy and its actual flat/XR consumers. Trace owned locomotion/camera behavior;
do not add a flag-only bypass. Preserve youth appearance, modal pause, package
cancellation, independent sound voices and path invalidation. Rerun ordinary input
through the source movie and player transfer, then repair the next reached owner,
including the visible dialogue condition context failure. Continue the complete
TTW/JAM/Benny objective above.

## Private continuation

Current ordinary video run: `tmp/development-lab/ttw-sound-paths-video-20261001/`.
Reached state: `tmp/sound-path-video-reached.private.json`.
The bounded source-prefix check is
`tmp/sound-paths-ordinary-reached.private.json`.
Checks: `tmp/sound-path-reference-contract.log`,
`tmp/sound-paths-owned.stdout.log`, `tmp/sound-path-runtime-gate.log`.
Owned audit helper: `tmp/Run-SoundPathsAudit.ps1`.

Private source analysis: `tmp/player-young-cg01.private.jsonl`,
`tmp/phy-baby-rattle.private.jsonl`, `tmp/set-pc-toddler-command.private.txt`,
`tmp/set-pc-toddler-handler.private.txt`, `tmp/set-pc-toddler-owner.private.txt`
and `tmp/toddler-paths.private.jsonl`. The private source reader/retail decoder
remain under `tmp/`; raw owned bytes and private addresses are never public inputs.

The requested current trait screenshot is
`local/recordings/ttw-current-opening-20261001/TTW-owned-traits-flat-20261001.png`.
The requested current-build video is
`local/recordings/ttw-current-opening-20261001/flat-traits-birth-handoff-20261001.mp4`.
It contains forty seconds of ordinary trait Done input, late birth dialogue and
camera release, with stereo audio. The 1280x720, 30fps export retains repeated
latest frames (816 unique draw IDs across 1,200 frames); exact audiovisual timing
remains unverified. The player stays in the birth room throughout. Recording is
off and temporary inspection frames are removed. The complete journey reel is
pending ordinary campaign progress.

Both protected saves (`local/playtest-20260927-world/save.json` and
`local/playtest-20260920-companion/save.json`) must remain unchanged, with SHA256
`800A37CC6C97C543393A544229C4E71ACB213B45C12FC4584EF3E70E94E2FBB7`.
Use fresh private runs and preserve all visible failure state.
