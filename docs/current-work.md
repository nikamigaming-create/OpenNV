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
Ordinary TTW startup, rendered transfer, opening choice, campaign and train travel
remain unverified. The next owners are configured startup and quest MenuMode
dispatch, followed by general player/character-creation and campaign state.

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
checks pass. Parser 7 preserves all 446 owners in the genuine owned checkpoint
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
script clocks, failures and progression through parser 1 to 7 and another cold
restore; its file remains unchanged.
The selected JAM source audit now has 11 parser failures among 52 scripts.
Its JBT initializer registers the winning render function and mutates its perk
parameters before reaching hit-event registration; the source execution
audit does not establish a working bullet-time module. JHM reaches the missing
hit-event owner.
Complete JAM/MCM remains unverified.
The TTW source audit admits all but nine of 1,263 entry-plugin scripts;
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

Replace the New Vegas startup assumptions with the winning configured starting
quest, quest MenuMode blocks and shared player/campaign state. TTW's source
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
The current New Vegas startup assumes the Doc Mitchell opening, including its
start CELL, quests and character creation. Replace that assumption with winning
startup scripts before claiming a Fallout 3 start under TTW.

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
The requested flat screenshot is extracted from the retained September 27
companion clip; it does not show the new September 30 code.

Do not change `local/playtest-20260927-world/save.json` or
`local/playtest-20260920-companion/save.json`. Both still hash to
`800A37CC6C97C543393A544229C4E71ACB213B45C12FC4584EF3E70E94E2FBB7`.
Do not retry the previously rejected temporary-image deletions.
