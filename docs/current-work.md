# Current work

## Active objective

Complete the combined TTW campaign from Fallout 3's opening through the authored
train-station route into New Vegas and continued campaign play. This takes
priority over the remaining JAM/MCM work. Complete TTW's reached dependency
behavior as part of that route; preserve all ten requested mod targets. Folder
registration and isolated scenes do not meet this objective. Prioritize flat
play while preserving shared VR behavior; detailed VR presentation follows.
Classic flat presentation must use winning Fallout/mod screens and controls;
the optional Nikami experience adds enhancements to the same gameplay owners.
Work without subagents. Follow the [mod implementation](mod-compatibility.md),
[implementation plan](implementation-plan.md) and [flat work order](flat-gameplay-plan.md).
All 36 broad recovery requirements remain open; no whole-game or retail-parity
completion is claimed.

## Verified runtime

New Game now reads the winning configured starting QUST from selected-profile
settings and executes its source stage/menu scripts before constructing a player.
The first queued source move chooses the ordinary CELL builder and player
placement; executed prefixes, control masks and shared quest state survive that
handoff. Required TTW settings stay in the selected profile, leaving owned INIs
unchanged. Ordinary flat New -> Yes reaches TTW's authored holding CELL and its
actual Capital/Mojave message. Selecting Capital starts CG00's owned Fallout 3
intro movie; ordinary Escape interrupts it and resumes the source result program.
That continuation now executes SetLocationSpecificLoadScreensOnly and SetInCharGen
through shared session state, then reaches CG00 stage 5's player script package.
That package now accepts its winning explicit reference location, starts the owned
birth camera clip and lets the subsequent source player move load Vault 101's
birth CELL. Both reached SetNumericGameSetting commands now change the shared
session settings, and the package camera advances. CG00's stage-6 PlaySound now
plays and completes its owned birth WAV. Its stage-8 same-package request now
plays the outgoing source OnChange camera clip and retains the pending assignment
until that clip completes. The subsequent source sequence stops at the unbound
TriggerScreenBlood command, before its baby-cry sound request.
The loaded CELL reports 102 missing runtime references on entry
and 83 after ordinary reference processing; the camera clip reports two unbound
non-camera targets. The first manual save attempt fails because world persistence
requires a prior native save. Source save eligibility and the first Fallout 3
campaign snapshot remain unbound. TTWStart
retains a separate TTW_EnableRadioFix fault. Fallout 3 character creation,
the Vault exit, train station, travel and continued campaigns remain unverified.
The requested current-build choice and unfinished birth-room screenshots are
retained privately. The birth frame exposes lighting and HUD presentation gaps;
it does not establish scene parity. Recording is off. Synthetic bootstrap,
conditional stage execution, winning renamed skill
identity and owned TTW/cold-save checks pass, as does the complete required
runtime gate. The final flat build retains TTWStart's startup identity across
background quest stage changes. Both protected saves remain unchanged.

Character-generation policy is shared by reference/results, fallback quest scripts
and pre-world startup, and retains cold with a false legacy default. Deferred XP
can exceed the current level threshold without changing level or disappearing
during SPECIAL/vitals derivation. Exiting with earned levels fails visibly at the
unbound level-cap/allocation/LevelUpMenu owners before clearing the flag or executing
the result suffix. XP reward commands, modifiers and leveling remain incomplete;
this policy and owned component audit do not establish player advancement support.

PlaySound now shares transient C# voice ownership across reference/results,
startup and fallback quest execution. Winning SOUN declarations supply WAV
identity, gain, pitch, chance and variants; stream preparation fails before
committing a request. Normal requests queue in MenuMode, while system sounds
play through paused menus. Concurrent voices complete and retire independently.
These transient voices are not save-baked and do not replay on cold restoration.
Synthetic prefix/typed-form/random/cleanup checks, an isolated owned native
mixer check and the complete required runtime gate pass. The ordinary Capital run also plays and completes the birth
sound. Reverb, submersion and stereo/LFE presentation gaps remain explicit;
loops, timed scheduling, 3D/reference-node playback, complete volume routing and
matched voice timing remain unbound. This is partial audio presentation, not
audio or campaign parity.

Numeric GMST mutation now belongs to the loaded C# stack, shared by reference,
result, startup and fallback quest commands/functions and all numeric readers.
Winning declarations and admitted owned executable defaults provide typed storage;
unknown/non-numeric setters return failure, and undefined conversions fail before
mutation. Existing skill, weapon damage/spread, ingestible and armor calculations
read current values. Derived vitals/health, jump/limb/actor movement, blink/head
clocks, HUD/quantity, casing and weather consumers still retain coefficients; mutations
that would invalidate those copies fail visibly before writing. These remaining
refresh boundaries are exposed in telemetry. Warm owner replacement retains
settings; a new stack restores owned defaults rather than baking mutations into
saves. Synthetic contracts, live skill/damage checks and isolated owned TTW
commands pass. The ordinary Capital run also performs both authored karma-setting
writes. Complete karma behavior and other unimplemented setting consumers remain
unbound; setting storage does not establish those gameplay systems.

Player script-package assignment, source identity, idle phase/cursor and elapsed/wait
clocks retain in the saved shared session. The outgoing OnChange camera event now
owns deferred replacement; a later request replaces the pending assignment without
restarting that event. Same-package completion returns to ordinary idles without
replaying OnBegin. PACK accepts both owned byte and UInt32 idle-count declarations.
Synthetic and isolated owned native checks preserve cold change clocks, camera
samples and frame remainders, reject changed pending packages, and pause clocks
while source movement is pending. The ordinary Capital run reaches and completes
the stage-8 change clip. Explicit-location idles require the matching CELL and
source radius; unreached traversal still fails visibly. End/removal animations,
change cancellation, nonempty event scripts/topics, editor-location semantics,
body targets and matched interruption/blend/event timing remain unbound.

Loading-screen eligibility now resolves winning LSCR direct CELL/WRLD and signed
world-grid identities, with the shared location-only policy retained cold. Native
door and queued player transfers present eligible owned images and LSCT tips while
paused, then restore the previous input/pause scope. Retirement drains both transfer
tasks. Synthetic policy/override/deletion/layout checks, the owned Vault 101 pool
and a rendered paused image/tip fixture pass with no retained frames. LoadingMenu's
ancillary NIF, progress/statistics widgets and matched selection/fade/layout timing
remain explicit gaps. This component evidence does not establish campaign travel.

Quest scripts now execute unfiltered and filtered MenuMode blocks through the
shared SCPT clock. Multiple GameMode blocks share source order, locals, budgets
and Return; menu filters read their current local value at execution. A shared
C# menu frame owns queries and identity; absent filtered-menu identity fails
visibly. Native message and supported paused panels publish their codes, retaining
underlying panels when a source message opens. Claimed opening menus retain their
existing handoff; exact open-menu scheduling remains unverified.
Synthetic reference/fallback-quest checks pass source order, filters, failure
prefixes, stopped clocks, scoped handoff and cold faults. The winning TTWStart
owned fixture now runs through the shared quest clock with owned/default cadence,
queues its holding-cell move once and publishes the actual two-choice message.
Cold restoration retains its prefix and pending choice without a transient menu
frame. The complete runtime gate passes with recording off. Parser 8 retains all 446 original saved owners and admits one previously
rejected multi-block owner; both protected saves remain unchanged. The TTW source
audit retains five parser failures among 1,263 entry-plugin scripts. Campaign
progression and train travel remain unverified.

Player MoveTo now queues requests in the shared C# world owner, so the following
source statements finish before native movement. Typed destinations and optional
offsets resolve against current reference placement. The native adapter consumes
that queue and uses the ordinary CELL/exterior builders for cross-cell transfers;
door and script transfers update the active player CELL before binding events.
Failures retain the request and error without automatic retry. Saves reject
unsettled movement rather than discarding its continuation. Synthetic and native
fixtures check suffix execution, offsets, mixed rotations, residency, self moves,
failure retention and retirement. The winning TTWStart MenuMode block passes an
explicit owned fixture: its holding-cell request and following statements execute
once. The full runtime gate and unchanged 446-owner cold-save audit pass.
These movement fixtures do not establish campaign or train travel. General
character-creation and campaign state remain open.

Keyboard/mouse control queries and remaps now share a profile-owned C# table.
Winning installation INI bindings remain read-only; changes swap occupied keys
within their device lane and persist in a separate profile overlay on session
retirement. Reference and fallback-quest commands use the same owner. Native
movement, activation, firing, reload, grab, jump, Pip-Boy, quick-save and aim/POV
actions consume that table. Remapping clears affected held actions; rejected
physical keys leave the owner and native map intact. A native input fixture and
synthetic script/cold-profile checks pass with recording off. The full runtime
gate and unchanged 446-owner cold-save audit also pass. Source-bound flat input
no longer intercepts Q/H for the diagnostic wheels. The owned JAM
initializer passes GetControl and now reaches SetOnHitEventHandler. Joystick/
gamepad adapters, missing executable binding defaults, remaining stock actions,
Classic/Nikami selection and complete mod input remain open. The next shared
owners remain hit-event context and actor-effect lifecycle, remaining perk
consumers and authored HUD/MCM. TTW startup and campaign now take priority.

Winning perk parameters now have a shared C# owner for indexed numeric reads and
writes, including independent two-value slots and byte-sized quest stages.
Mixed ability/entry-point lists retain their source indices. Cached ability
readers, player traits/acquired perks and actor perk entries project live values;
a synthetic winning-override check changes actual weapon damage through the
ordinary resolver. Reference and fallback-quest commands accept typed form
variables; grouped form arguments retain identity instead of display names.
Invalid writes preserve existing values, and owned source files remain read-only.
Changes live with the loaded source stack, outside campaign snapshots. The owned
JAM initializer publishes all 16 bullet-time and two hit-marker parameter writes.
JBT then reaches hit-event registration; JHB reaches Dispel. Native
source activation updates the same cached perk reader, including reactivation.
The complete runtime gate and unchanged cold-checkpoint audit pass. Complete
entry-point consumers, conditions and JAM gameplay remain unverified.

Source UI interpolation now owns the four documented SetUIFloatGradual modes,
including stop/replacement forms and signed command arguments. Dependent XML
traits read current script overrides. A monotonic native UI clock keeps animations
running during menu pause and zero gameplay time scale; unload/reset clears them.
The native HUD's supported source tiles read the same C# float values and redraw
on their revisions. A rendered owned-reticle fixture changes actual pixels through
that bridge while gameplay is paused, retaining no frames. Synthetic mode,
dependency, lifetime and reference/fallback-quest checks and the complete runtime
gate pass. The owned JAM audit advances its actual repeating HUD trait through
the shared owner. JHM initialization now passes interpolation and reaches
SetOnHitEventHandler. The rest
of the authored HUD, complete hit markers and MCM remain unverified.

Default JohnnyGuitar render callbacks now retain source identities in the shared
C# event owner. The native adapter invokes them before drawing, including paused
menus, once per global frame rather than per viewport. A rendered fixture with
two additional viewports checks source expressions, inactive sessions, cold
owner replacement and disconnection on retirement, with recording off.
Registration is idempotent; removal takes effect during dispatch, and failures
retain their executed prefix without frame-by-frame retries. Nonzero flags that
select additional retail render phases remain visibly unbound.
NVSE integer remainder, bitwise AND/OR, shifts and compound assignments now
use signed 64-bit truncation; binary/hexadecimal literals retain their 32-bit
contract. Focused execution, precedence, undefined-operation and migration
checks pass. Parser 8 preserves all 446 original owners in the genuine owned checkpoint
through cold restoration, with both protected source saves unchanged.

Script arrays now belong to the shared C# value store. Packed lists, numeric
maps and string maps preserve typed elements, alias identity and nested graphs.
Indexed expressions, core construction/mutation/copy commands and array function
arguments/returns execute through ordinary script owners. Recursive and failed
function frames release temporary roots; cold restoration rejects malformed or
unowned graphs. Save v23 retains array identities alongside v22 object-animation
state; earlier supported saves still load. Focused scalar/array/function and
native activation/key-callback/cold-reference checks pass, as does the complete
required runtime gate. The genuine owned checkpoint retains all 446 quest
script clocks, failures and progression through parser 1 to 8 and another cold
restore; its file remains unchanged.
The selected JAM source audit now has 11 parser failures among 52 scripts.
Its JBT initializer registers the winning render function and mutates its perk
parameters before reaching hit-event registration; the source execution
audit does not establish a working bullet-time module. JHM reaches the missing
hit-event owner.
Complete JAM/MCM remains unverified.
The TTW source audit admits all but five of 1,263 entry-plugin scripts;
its opening, campaign progression and travel remain unverified.

Source object animation now binds PlayGroup and IsAnimPlaying to each resident
reference's authored NIF manager. Queued and immediate selection, authored loop
starts, source text-key order, callback changes and independent clocks have native
checks. Alternative looping sequences no longer reject the selected window.
Actor skeleton groups and ambiguous/absent object groups remain explicit failures.
Save v22 retains selected object clocks, consumed start events and pending groups;
v21 and earlier supported saves still load. Cold restoration, warm eviction,
replacement presentation and source mismatch checks pass.

Conservative recovery resumes only exact legacy missing-PlayGroup faults whose
unchanged block proves that the failed command preceded any other mutation.
All 51 selected saved plant failures recover in the owned-data audit. All six
source script/model families grant their authored rewards once through native
activation, retain destroyed state and reject duplicate rewards across cold
restoration. The selected window supplies the seventh tested source model.
The complete runtime gate and reference-presentation regression check pass.

Kill/KillActor with an optional killer now use the same health/death inventory
transition as combat. An omitted killer remains unknown, including delayed
OnDeath and cold saves. Repeated calls on a corpse add no loot or death event.
Native presentation detects a scripted death and activates its source ragdoll.
The owned native fixture recovers all 14 selected old Kill failures and checks
source corpse scripts, a living creature's ragdoll and its cold continuation.
Inherited actor script locals, including qualified reads, now resolve through
the retained world template owner. Synthetic checks cover both ownership paths,
filtered death events, conserved loot and rejected earlier mutations. Essential
recovery, player script death and limb/cause parameters remain visible boundaries.

An ordinary exported flat Continue on a copy of the genuine Primm checkpoint
recovers ten resident plant faults and 23 prior read faults. Ordinary traversal,
mouse aim and activation harvest reference FalloutNV.esm:157e35 once, adding one
Coyote Tobacco Chew. Manual saving retains its destroyed flag, source local and
completed Forward animation in v22. Cold exported Continue restores all three;
another ordinary activation attempt leaves the inventory count at one.
A later exported flat Continue also resumes 14 resident Kill faults; their
corpse locals and destroyed flags complete, and Tobacco remains at one.
The bot now observes destroyed state as an interaction outcome; some source
contact aiming and final navigation segments still need work.

Session retirement now drains title indexing and exterior source readers before
releasing the world, records and detached model prototypes. Ordinary exported
flat Quit on the copied checkpoint exits with code 0 after releasing 349
prototypes; the previous direct Quit reproduced native heap corruption.
Main-menu return, another Continue, fresh-title Quit and native window close
also exit cleanly, with the source checkpoint unchanged.
Cell destruction releases its compositor pipeline, shader and samplers on the
rendering thread even when managed references retain the effect. Four successive
synthetic rendered replacements and repeated/unused release checks pass without
GPU RID leaks. The full runtime gate and owned script-death regression pass.
Two ObjectDB exit warnings remain visible in the selected owned runs; their
owners still need diagnosis. One repeated exported reload also crashed during
native triangle-mesh construction before gameplay resumed; a subsequent complete
session audit passed. This intermittent construction failure remains open.

## Next owners

Implement the reached TriggerScreenBlood owner from winning settings and owned
resources, including shared transient state and visible presentation. Preserve
the completed stage-8 change event and its executed script prefix. Complete the
remaining script audio routes, loop/stop ownership, environment/submersion and
output routing without treating the audible birth voice as full audio support.
Trace source save eligibility and initialize the first source campaign snapshot
without requiring a prior New Vegas save or bypassing character creation. Complete
live refresh for the retained numeric-setting consumers exposed in telemetry.
Preserve CG00's stage-5/package and birth-CELL movement prefixes. Trace and repair
the loaded birth CELL's missing runtime references and two unbound non-camera clip
targets. Complete player package traversal, editor origins, event scripts/topics
and end/removal/cancellation animations without named-location success paths. Complete
the deferred XP level-cap, allocation, LevelUpMenu and reward
owners, plus TTW radio-worldspace dependency behavior, without discarding failed
prefixes. TTW's source
opening choice must lead into Fallout 3's authored character creation, exit,
train-station route and ticketed travel into the Mojave opening. Continue through
ordinary input and persistent saves; queued source transfers alone do not prove
that route. Implement each reached extension/effect/package/menu owner.

Trace JAM's remaining actor-effect, input-control and hit-event failures through ordinary
native owners. Chained reference expressions, lambdas, further operators and
remaining array operations still reject syntax/behavior. Typed strings/arrays,
INI and auxiliary state, UIO injection and UI component state already exist; they do not
establish a working MCM menu or any complete JAM module. Implement the next
reached missing owner, including ordinary input and persistent effects. Keep
winning HUD/menu rendering, original crafting/barter/character-creation screens
and Classic controls in scope; the float bridge covers existing supported HUD
tiles and does not yet draw every mod component or bind string overrides.
Bind remaining perk entry-point consumers, ranks and condition scopes rather
than treating successful parameter mutation as complete perk behavior.
Keep the complete TTW opening, campaign progression, travel and dependency
semantics in scope; do not remove launch gates on the strength of source audits.
Startup placement now follows the winning configured quest. The native creation
contracts still assume New Vegas's Doc Mitchell flow; replace those remaining
assumptions with shared source owners before claiming playable Fallout 3 creation.
Complete the remaining source LoadingMenu animation/progress components and
verify its selection, placement and timing against matched retail evidence.

Preserve the actual flat run's remaining script and actor failures. Essential
recovery and additional death-command parameters need their own source-backed
behavior. The reached GetReference Player compiled binding, NPC radio,
creature package condition 136 and further
patrol/sandbox/eat/sleep procedures remain visible failures. Preserve all quest,
combat, mod and campaign objectives while fixing those owners.

Weapons still need the Flamer's source strip-particle decoder, thrown recovery,
remaining projectile effects, mines/remote triggers, bare fists and ammunition
variants. Blast rules, hit events, death XP, leveling/perks, radiation/addiction,
crime, crafting/barter completeness, JAM/MCM and TTW remain open. Do not replace
these requirements with selected component passes.

Selected exported flat shutdown paths now pass; broader session stability,
streaming spikes and rendering/audio fidelity remain open. Recording stays off
during development except for a requested visual check.

## Candidate and private continuation

The public-facing local experimental candidate remains
`local/releases/OpenNV-0.1.0-experimental.20260927.5-windows-x64`, from runtime
commit `06ffd2cadd1f0e0a180882a666489503bea4aa30`. It predates these repairs.
Update the dated candidate after stable publication. Retain the requested September 27
weapon and companion reels; they are selected simulator/flat footage.
The refreshed Windows development executable is
`tmp/development-runtime/windows/OpenNV.exe` and includes the September 30 session
repairs; it predates the shared-array and render-callback changes.

Current private flat checks are in
`tmp/development-lab/flat-polish-20260930-animation/` and its `-cold` continuation.
Selected native audit logs are `tmp/object-animation-owned.log`,
`tmp/object-animation-native-contract.log`,
`tmp/object-animation-presentation-regression.log` and
`tmp/object-animation-runtime-gate.log`. These are private diagnostics.
Script death checks are in `tmp/scripted-death-owned.log`,
`tmp/scripted-death-contract.log` and `tmp/scripted-death-runtime-gate.log`.
Session checks are in `tmp/native-shutdown-owned.log`,
`tmp/native-shutdown-runtime-gate.log` and `tmp/native-session-retirement/`.
Array checks are in `tmp/jam-array-contract.log`,
`tmp/jam-array-native-contract.log`, `tmp/jam-array-runtime-gate.log`,
`tmp/jam-array-owned-save.log`,
`tmp/jam-array-source.private.json` and `tmp/jam-array-execution.private.json`.
Render checks are in `tmp/jam-render-contract.log`,
`tmp/jam-render-native.stdout.log`, `tmp/jam-render-native.stderr.log`,
`tmp/jam-render-owned-save.log`, `tmp/jam-render-runtime-gate.log`,
`tmp/jam-render-source.private.json` and `tmp/jam-render-execution.private.json`.
UI checks are in `tmp/jam-ui-contract.log`, `tmp/jam-ui-native-clock.log`,
`tmp/jam-ui-native-pixels.stdout.log`, `tmp/jam-ui-native-pixels.stderr.log`,
`tmp/jam-ui-owned-save.log`, `tmp/jam-ui-runtime-gate.log` and
`tmp/jam-ui-execution.private.json`.
Perk checks are in `tmp/jam-perk-contract.log`, `tmp/jam-perk-script-contract.log`,
`tmp/jam-perk-owned-save.log`, `tmp/jam-perk-runtime-gate.log` and
`tmp/jam-perk-execution.private.json`.
Current transfer checks are `tmp/ttw-player-moves-native.stdout.log`,
`tmp/ttw-player-moves-native.stderr.log`, `tmp/ttw-player-moves-runtime-gate.log`,
`tmp/ttw-player-moves-owned.private.json` and `tmp/ttw-player-moves-owned-save.log`.
These fixtures do not establish a campaign playthrough.
Current menu checks are `tmp/ttw-menu-script-contract.log`,
`tmp/ttw-menu-owned.private.json`, `tmp/ttw-menu-owned-save.log`,
`tmp/ttw-menu-source.private.json` and `tmp/ttw-menu-runtime-gate.log`.
Current startup checks are `tmp/source-quest-startup-contract.log`,
`tmp/source-quest-startup-plugin-contract.log`,
`tmp/source-quest-startup-owned.private.json`,
`tmp/source-quest-startup-cold-save.log` and
`tmp/source-quest-startup-runtime-gate.log`. The ordinary flat session is
`tmp/development-lab/ttw-startup-20260930-final`, with the selected movie/failure
check in `tmp/development-lab/ttw-startup-20260930-run4`. Their retained failures
identify the next source owners. The requested current-build screenshot is
`local/recordings/ttw-startup-20260930/TTW-starting-choice-flat.png`.
Current loading checks are `tmp/loading-screen-owned.private.json`,
`tmp/loading-screen-native.stdout.log`, `tmp/loading-screen-native.stderr.log`,
`tmp/loading-screen-cold-save.log` and `tmp/loading-screen-runtime-gate.log`.
The fresh ordinary Capital run is
`tmp/development-lab/ttw-player-package-change-20261001`; both session flags are true,
CG00's package camera advances and its queued move enters Fallout3.esm:028138.
Both authored karma-setting writes execute, and the owned birth sound plays and
completes. CG00's stage-8 same-package request plays and completes the source
OnChange clip, then retains the missing TriggerScreenBlood owner before its
baby-cry request. The selected unfinished birth frame is
`local/recordings/ttw-birth-20261001/Vault-101-birth-incomplete-flat.png`; temporary
frame data is removed. It exposes lighting and HUD gaps, not scene parity.
The prior numeric-setting run's CREATE NEW SAVE attempt retains the prior-save
requirement failure; saving was not retried in this package check. The session
quit through ordinary input with source readers drained. One transient native
state-write loss remains retained; later state samples resumed. Audio checks are
`tmp/script-sounds-contract.log`, `tmp/script-sounds-owned-native.log` and
`tmp/script-sounds-runtime-gate.log`; the mixer fixture retains no samples or frames
and cannot establish endpoint audio or campaign parity. Numeric checks are
`tmp/numeric-settings-contract.log`, `tmp/numeric-settings-owned.private.json` and
`tmp/numeric-settings-runtime-gate.log`; cold setting checks are explicitly component
evidence, not a continued Fallout 3 campaign. Package checks are
`tmp/player-package-contract.log`, `tmp/player-package-owned-native.log`,
`tmp/player-package-cold-save.log` and `tmp/player-package-runtime-gate.log`.
Native cold/camera checks are explicitly isolated owned component evidence, with
recording off. Current change checks are
`tmp/player-package-transition-contract.log`,
`tmp/player-package-transition-baseline.log`,
`tmp/player-package-transition-owned-native.log` and
`tmp/player-package-transition-runtime-gate.log`. They cover cold change clocks,
source drift, latest pending assignments and frame remainders in isolated native
components, not a cold Fallout 3 campaign. Character checks are
`tmp/character-generation-contract.log`, `tmp/character-generation-owned.private.json`,
`tmp/character-generation-cold-save.log` and `tmp/character-generation-runtime-gate.log`.
The owned command audit is explicitly isolated component evidence. Recording is
off. The flat route
check in `tmp/development-lab/loading-policy-door-20260930` retained a bot capsule
route/segment failure near Nash before activation; it is not a successful transfer.
The copied interior checkpoint check in
`tmp/development-lab/loading-policy-interior-20260930` also stopped at navigation
before activation. Both sessions quit through ordinary input with source readers
drained. Broader transfer/loading acceptance remains open.

Do not change `local/playtest-20260927-world/save.json` or
`local/playtest-20260920-companion/save.json`. Both still hash to
`800A37CC6C97C543393A544229C4E71ACB213B45C12FC4584EF3E70E94E2FBB7`.
Do not retry the previously rejected temporary-image deletions.
