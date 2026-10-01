# OpenNV architecture

OpenNV has one runtime architecture: C# readers consume a legally owned game
installation in place and publish authoritative state to Godot.

## Boundaries

- Retail files are read-only. OpenNV never edits the selected installation.
- No Bethesda asset, executable, save, or converted derivative is committed,
  packaged, uploaded, or distributed.
- The Godot product entry accepts `--launcher`; direct diagnostic launch accepts
  a live installation root and a campaign identity.
- Loose files override archives through a case-insensitive source namespace.
- Active ESM and ESP records are resolved in load order with master-aware
  FormIDs. BSA members are resolved in memory.
- NIF, DDS, KF, audio, string tables, records, DAT, MAP, PRO, and FRM data are
  interpreted by C# owners inside the runtime.
- Gameplay and save state are authoritative and shared by flat and OpenXR
  presentation adapters.
- Unknown binary layouts and unsupported behaviors fail closed.
- Godot's bundled Jolt backend owns native contact solving in both modes.
  Source NIF/Havok shapes, masses and constraints remain the inputs; no route
  bypass or replacement collision geometry is used to escape a contact.
- Jolt ray face indices are enabled so a mixed-material NIF contact selects its
  original triangle's Havok material. This adds roughly 25% to concave-shape
  memory according to [Godot's setting contract](https://docs.godotengine.org/en/4.6/classes/class_projectsettings.html#class-projectsettings-property-physics-jolt-physics-3d-queries-enable-ray-cast-face-index).
  DODT retains its raw flag byte while interpreting only the three declared
  low bits; nonzero reserved bits in owned thrown/melee impacts are preserved
  in decal telemetry. Unknown semantic fields still require explicit owners.

## Main owners

RuntimeNativeOpeningStageDriver captures the same authoritative campaign state
for ordinary saves and code-addressed checkpoints, including the first settled
Fallout 3 snapshot. Active movies, furniture, speech, creation/trade/crafting
menus, unsettled transfers and death remain rejected. RuntimeSaveSlotCatalog
protects existing GUID identities, derives metadata from saved state and looks
up a selected slot independently of unrelated corrupt files. RuntimeCoordinator
validates the complete selected save before preserving Continue and draining
readers for an in-process scene reload. Diagnostic checkpoint loading may open
the shared pause menu before world gameplay advances. The live harness reports
the last completed restored slot separately from command delivery; checkpoint
requests remain diagnostic preparation, followed by ordinary flat/XR input.
Complete actor clocks, active continuation restoration and source save eligibility
remain unbound. See [save recovery](patrol-session-recovery.md).

TTW's race-menu device selection reads owned plugin registrations and bounded
copy/import/shared-buffer/native-forward associations. The original executable
model consumer and initialized buffer agree with the normal selector's default
path; the gene selector supplies its own owned relative NIF. No plugin code is
executed. RuntimeNativeOpeningStageDriver opens the same creation owner with the
selected model, and standard ShowRaceMenu restores its default selection. The
winning model, XML, font, source surfaces and shared draft supply presentation and
input. Acceptance closes the menu and retains ordinary source continuation.
Source drift and unsupported overlapping requests fail visibly. Synthetic
associations, native model/input/default restoration and ordinary TTW opening
checks cover this bounded owner; matched
portrait/effect/close timing and XR final-eye presentation remain unverified.

GamebryoRootMotionTravel advances the winning NAVM corridor using owned KF
displacement. It retains arrivals reached by the initial zero-distance sample;
RuntimeNativeNpc consumes them after package binding and on later frame arrival,
then selects stationary locomotion and delivers completion once. Cancellation
retires pending delivery. NPC package-change events use the shared owned IDLE
clock and source-priority layers; forever-loop poses do not impose an invented
replacement barrier. The package's script still precedes its idle, and failed
events retain their executed prefix and failure latch. Synthetic travel contracts,
owned Dad/Dr. Li package changes and ordinary birth input cover this bounded
repair. Actor cold restoration, animation group interruption/blending, Must
Complete end-idle waits and matched event/pose timing remain unbound.

RuntimeNativePlayer retains the shared source control mask supplied by the
opening/session owner. NativeOwnedGameplayHud applies its movement flag to the
winning HP/AP/reticle tile visibility. Target text requires both rollover and
ordinary world activation to be enabled; the source movement flag also disables
activation, so a ray hit cannot advertise an unavailable Talk/Take/Open action.
Rollover suppression no longer removes unrelated gameplay branches or messages.
The same owned XML/font/atlas renderer supplies pixels; control re-enabling
restores the existing branches. Synthetic mask cases through owned native pixels
and ordinary birth input cover this bounded flat owner. HUD override extensions,
other branch rules and matched retail/XR presentation remain unbound.

FalloutReferenceWorld owns NPC-base race overrides independently of resident 3D.
MatchRace reads winning RNAM/YNAM/ONAM links, preserves the target's age tier in
the source family and leaves an exact same-race request unchanged. Invalid typed
links/cycles reject before committing. NPC/RACE source hashes retain in saves;
prior supported saves still load without inventing overrides. Existing,
warm and newly assembled native bodies read the same state. Revision stamps avoid
frame-by-frame source/template scans. RuntimeNativeNpc prepares actual source
parts, FaceGen and material channels before replacing its body parts, preserving
the shared skeleton, animation/face clocks and non-body objects. Changed material
targets rebind on revision; bounds and reference fade geometry refresh. Dialogue
race/child conditions query authoritative state. Player-target race transitions,
different skeleton rebinding, complete actor cold clocks and
matched retail/XR pixels remain unbound.

The same world owns NPC-base MatchFaceGeometry independently of resident 3D.
Current source appearance, marked/fallback presets and native case-sensitive
tie order supply a scaled source-minus-preset displacement. The owner preserves
target affine age, subtracts the target race's male default and retains texture.
It commits once after finite/extent validation, then invalidates native appearance
without replacing actor/skeleton/animation state. Save v25 retains model/RACE/CTL
hashes and independently owned coefficient arrays; v24 and earlier supported
schemas remain readable. Scripted player targets and active template changes
remain unbound. See [face geometry](actor-face-geometry.md) for the bounded proof
and remaining dialogue/cold-load failures.

[Say](https://geckwiki.com/index.php?title=Say) and
[SayTo](https://geckwiki.com/index.php?title=SayTo) share RuntimeNativeSpeech's
winning INFO selection and owned voice/lip path with actual resident actors.
FalloutDialogueConditions owns actor traits and shared quest predicates;
GetPCIsSex reads current player state. The admitted optional forced-subtitle
integer is true only when positive. INFO end results run before typed
[SayToDone](https://geckwiki.com/index.php?title=SayToDone) delivery to the actor's
source script. Header filters resolve DIAL identities independently of action
references; actor locals retain cold and transient speech is not replayed.
Missing actors, conditions, presentation or event bindings fail visibly.

NativeOwnedSubtitles draws the winning HUDMainMenu Subtitles branch and its
owned text template/font. Admitted executable child/global/placement/template
associations supply numeric operands, integer screen centering and Info-relative
safe-zone placement. General-subtitle settings and source forcing control
admission independently of activation prompts. The presenter is prepared before
initial stage execution and follows shared script-UI revisions. Synthetic
association/command/event/cold-local checks and owned native pixels cover this
bounded owner. Ordinary TTW birth input reaches gender selection, Mom's Say,
continued parent dialogue and owned name entry. Additional Say actor/audio
arguments, SayTo's look flag/other listeners, concurrent voices, styling, queue/
hold/fade, matched timing/scaling and XR final-eye presentation remain unbound;
neither dialogue nor campaign parity is claimed.

[SetNoActivationSound](https://geckwiki.com/index.php?title=SetNoActivationSound)
and [ClearNoActivationSound](https://geckwiki.com/index.php?title=ClearNoActivationSound)
share FalloutNoActivationSound across reference/results, startup and fallback
quests. The source executable's clearing-command/global-slot/lazy-lookup
association supplies the default SOUN editor ID; no fallback identity or build
address is substituted. The selected winning SOUN and payload hash retain in
FalloutScriptSession. Cold restoration validates both before admission and never
replays transient voices. Ordinary failed/blocked flat and XR activation outside
modal input uses the shared owned sound player, suppressing repeated input while
its selected voice remains active. Reset leaves existing voices intact and
resolves the owned default on the next feedback request. Synthetic contracts and
an isolated owned command/input/native-mixer fixture cover selection, reset,
completion, malformed associations, failed prefixes and cold identity. Retail
precache/handle reuse, matched activation eligibility/timing and endpoint audio
remain unverified; calling an XR input adapter without a headset is component
evidence only.

TriggerScreenBlood requests belong to FalloutScreenBlood in the shared reference
world, with the same owner available to startup/results and fallback quests.
Owned compiler associations admit byte Boolean INI defaults, including source
names with spaces and numeric prefixes; installation/profile INIs override them.
The blood enable flag is captured at presentation binding. Winning numeric and
string GMST readers supply remaining active capacity, geometry/opacity ranges,
duration/fade and DDS identities. Texture preparation precedes geometry/random
commitment. Groups retain their captured duration, read live fade-start and
advance on the shared menu-aware game clock. Retirement releases all geometry;
campaign saves never bake or replay these transient effects.

NativeOwnedScreenBlood provides a partial flat presentation using the owned
two-by-two alpha/tint atlas and color map. Its independently written fragment
equation combines per-drop opacity, group fade and mask alpha; source base
blending multiplies the underlying image. The owned native fixture verifies
changed pixels, fade and exact restoration of its baseline after expiration,
without writing frames. Directional color-UV offsets, additive flares, color
transfer and matched layering/timing/random stream remain explicit telemetry
gaps. XR shares authoritative requests but retains an unbound final-eye adapter;
no effect, campaign or retail parity is established by this component check.

Non-locational [PlaySound](https://geckwiki.com/index.php/PlaySound) requests
belong to FalloutScriptSounds in the shared reference world; fallback quests use
the same owner. Winning SOUN records and the shared source selector provide gain,
pitch, chance and variant identity. NativeOwnedScriptSoundPlayer prepares a hashed
owned WAV before request commitment, reports actual completion and releases each
concurrent voice on retirement. Normal requests queue until GameMode; system
sounds continue through paused menus. Active normal voices pause without replay.
The pause timing remains unmatched against retail. Voice state is transient,
outside campaign snapshots, with no cold replay of an executed script prefix.
Environmental/submersion and stereo/LFE presentation gaps remain explicit in the
request, while unsupported loop/timed/codec behavior fails before the source
suffix. 3D/node routes, stop ownership and complete audio volume/output behavior
remain unbound; a native mixer fixture does not establish endpoint audio parity.

- `runtime/src/Content`: installation detection, plugin/archive readers,
  strings, media, records, and live source precedence.
- `runtime/src/Formats`: NIF and engine-family binary interpretation.
- `runtime/src/Gameplay`: authoritative inventory, stats, crafting, settings,
  and save state.
- `runtime/src/World`: cells, actors, collision, movement, interactions, and
  streaming.
- `runtime/src/Campaigns`: source-backed campaign progression.
- `runtime/src/Presentation`: Godot rendering, UI, character creation, and XR
  adapters.
- `contract-tests`: C# synthetic contracts and explicitly selected owned-data
  audits.
- `desktop`: retained JavaScript contract and migration compatibility coverage;
  it is not part of the normal product launch path.
- `tools/OpenNV.DevelopmentLab`: separate headless corpus, reference-lifecycle
  and event-replay operations that call these runtime owners directly.

The Godot launcher is a `Control` owned by `RuntimeCoordinator`. It raises a
typed route request, and the coordinator validates the selected owned install
before calling the existing campaign dispatch owner. The launcher is hidden and
released after dispatch; it never starts another executable or engine process.

Placed-reference script locals belong to `FalloutReferenceWorld`, including
references with no model and initially disabled objects. Resident cell membership
is separate from retained world state. Bindings resolve winning base/script
attachments and declared slots; instances share immutable declarations without
sharing mutable locals. Campaign reference snapshots carry script identity and
hashes; restoration rejects changed declarations. The ordinary native player ray
and source primitive contacts now dispatch through FalloutReferenceScripts.
Reference enable state and source enable-parent relationships share that world
lifetime. Native models may be built on demand without creating a second source
path. Per-instance texture changes remain transient with presentation lifetime.
Managed object animation binds that same reference to its winning NIF hash and
controller block. Shared script owners select source groups and query their
manager; v22 snapshots retain selected clocks and pending groups through cold
restoration, warm residency and presentation replacement. Actor skeleton groups
remain owned separately and fail visibly until that binding exists.
Reference, result and quest script local lookup resolves the retained world
instance's attached script, including actor templates, rather than rereading
only SCRI on its base. Qualified local reads use the same resolver.
Typed string and array values have one C# store shared by those script owners.
Array locals retain identities by instance/slot; nested elements and transient
function frames retain the same graph. Execution scopes protect intermediate
values and release unreachable graphs after calls finish. Save v23 retains
typed arrays and rebuilds roots from winning quest/reference declarations on
cold restoration; earlier supported snapshots remain readable.
Weapon hits and scripted actor deaths share the health/death inventory owner;
an unknown killer remains nullable through delayed events and saves. Native
combat presentation observes that shared transition and activates source bodies.
Leveled actor templates retain a reference-owned random choice, source hash and
selection level in the same snapshot. Appearance, inventory, script attachment,
voice, factions and combat consume that selection. Reentry cannot reroll a
creature or outfit. Encounter zones retain their first admission level across
connected cells and saves. Reference/CELL/WRLD assignments and source difficulty
multipliers feed the same actor selection; persistent exterior references use
their spatial CELL. Save v17 carries zone state, while earlier saved actor choices
remain unchanged. See [encounter admission](encounter-admission.md); resource
preparation alone does not establish visible or functioning actors.
FalloutConversation owns INFO selection, results and choices; native source menu
and voice adapters report input/completion. Actual player furniture and posed
actor query contacts now connect to reference events. Complete event/physics,
dialogue presentation and cold interaction restoration remain incomplete; see
reference-events.md and dialogue-and-furniture.md for the bounded contracts.

The player keeps one complete world skeleton in flat and OpenXR. Camera
visibility, shadow casting and source bone query volumes are independent;
first-person visibility never removes anatomical contacts. XR shows the complete
world outfit and excludes only head geometry from its eye cameras. Source FaceGen
eyes define the head anchor, independent of the authored flat weapon camera.
Grounded pelvis/spine/leg constraints preserve source lengths and report excess
reach. The posed shoulders also anchor first-person weapon/device attachments;
the duplicate first-person body draw is masked. The actual wrist device casts its enlarged shadow; its duplicate outfit
device stays hidden. Self rays exclude the player's capsule and bone volumes.
These contacts provide hit queries, not completed limb rigid-body response.

Creature package movement and combat share the source envelope, root motion and
native collision owner. Follow/Dialogue select their source target without an
invented start location. Package position, animation identity/clock and talk
history are reference-owned save state. Presentation eviction cannot reset them.
Source schedule selection reads the shared game calendar before evaluating
package conditions. Resident actors react to hour changes and periodic condition
reevaluation; resident package motion does not require a player object. Complete
package procedure/event lifecycle and retail evaluation cadence remain unbound.
Collision readiness excludes initial scene construction and door handoffs;
an absent follow/dialogue target is a visible residency wait, not a permanent
procedure failure.
Recruitment effects retain reference/base faction scope, perks, flags and combat
style in the shared world. Creature weapons and teammate target selection reuse
ordinary source contacts and damage. Door transfer composes destination residency
and requires an authored NAVM arrival with native capsule clearance. These paths
retain shared player state across CELL replacement. Source player MoveTo has a
C# request queue; following statements execute before the native adapter resolves
current target placement and builds the destination through the same scene path.
The active player CELL changes before destination event binding. Failed transfers
retain their source request and stop automatic retries; saving unsettled movement
requires a continuation owner and currently fails closed. These paths
have separate acceptance levels; [companion gameplay](companion-gameplay.md)
and [creature packages](creature-packages.md) record the bounded evidence.

The first-person XR owner separately binds those source hand shapes and the
equipped weapon's Havok into persistent motion-query bodies. Collision-resolved
wrist targets publish to both existing skeletons before muzzle evaluation.
Resident world collision constrains translation/rotation; a bounded virtual
spring transfers impulses to real dynamic props at their contact points. Actor
hit areas still require a response/damage owner. Source two-hand aim poses define
the support socket; proximity dwell and release rules do not create another
weapon or inventory. Tracking loss and unavailable collision disable firing.
The free-hand grip frame uses the palmar normal: OpenXR +X points out of the
left palm and into the right palm; -Z runs from little to index finger
([OpenXR pose conventions](https://registry.khronos.org/OpenXR/specs/1.1-khr/pdf/xrspec.pdf#page=110)). The
source closing animations independently check that sign on both skeletons.
Wrist-distance agreement alone cannot detect an inverted hand. Free hands retain
independent grip, index trigger and thumb-touch source finger channels. A held
weapon retains its complete authored grip regardless of untouched controls.
The tracked holding frame and source grip palette stay fixed during source flat
action clips; internal model animation and authoritative timing continue.
Anatomical reach limits targets before contact publication; a retained contact
that the body can no longer reach reports divergence and blocks that gun's shot.
Source elbow bend planes and authored twist helpers distribute wrist roll while
the prepared full-body torso supplies consistent shoulder anchors.
Animation layer reduction belongs to each skeleton, with reusable arrays and
value/stack-based sampling rather than per-frame channel-object graphs.

ReactiveReferenceBot and ReactiveSteering own an extractable, engine-independent
C# observation-to-input policy. RuntimeCoordinator supplies resident references,
source navigation and authoritative interaction observations; RuntimeLiveHarness
feeds ordinary flat inputs or a simulator adapter with bounded controller leases.
No bot path writes player transforms, inventory, quests or collision results.
Source-portal A* supplies coarse intent; native swept queries refine bounded
route segments with the actual player capsule. A distant portal's midpoint alone
cannot reject its entire width. Failed corridors expire and
trigger alternate-route searches. Segment execution observes actual movement
before replanning. Dead-actor aiming uses current physical body centers and
checks the first collider. Campaign decisions and combat tactics remain unbound.

Player SPECIAL and skill queries evaluate owned GMST formulas, tags, constant
abilities, conditional trait effects and equipped apparel effects against live
inventory/global state. They are also used by the Pip-Boy rows. Inventory weight
is reused until inventory revision or hardcore mode changes. Ingestible DATA
supplies weight and ENIT supplies value; ammunition weight applies in hardcore
mode. FalloutPlayerIngestibles preflights selected ALCH effects and consumption
audio before committing shared inventory/vitals changes. Supported health and
limb effects use winning skill settings; timed healing advances on gameplay time
and preserves its remaining duration and source hashes in campaign saves.
Other actor values, scripts, addiction and unbound conditions fail explicitly.
Full damage, actor-value modifier pools and advancement are incomplete.

Quest stages, INFO results, reference events and quest GameMode share the same
statement interpreter. Compiled bindings are cached per owner/script. A stage
retains each QSDT entry's condition and reference scope; blocking presentation
suspends its statement iterator. Nested stages publish in source order and a
reached failure retains its executed prefix. Running quest admission, stopped
clocks, source inventory grants, dialogue history and active quest selection
remain C# state. Inventory expands LVLI into real item variants and retains its
random state in saves. Godot supplies playback, input and completion callbacks.
The original source program now advances the ordinary opening; a predicted
stage edge cannot replace its execution.
Multiple GameMode blocks share the SCPT invocation, including source order,
locals, instruction budget and Return. Quest MenuMode blocks use the same clock;
filtered blocks evaluate their current integer argument when reached. Reference
and fallback-quest queries share a C# menu frame published by the native adapter.
Missing filtered-menu identity remains divergence. Supported paused panels and
source messages publish their codes; transient menu frames are not saved. The
claimed opening path retains its legacy acceptance handoff. New Game resolves
General/SCharGenQuest against the winning load order. A C# bootstrap activates
that quest, executes authored stage-zero/menu programs and carries their state
into the ordinary scene selected by the first queued player move. Selected mod
settings overlay owned installation settings in memory; they never edit the
installation. The TTW configuration follows its
[installation guide](https://thebestoftimes.moddinglinked.com/essentials.html).
Blocking native movies retain their continuation and expose playback failures;
unbound pre-world commands remain visible. Runtime stage programs choose their
conditional destinations; a static opening graph cannot reject or select them.
The generic save/diagnostic startup label remains stable across background quest
stage changes; the shared quest owner retains all actual stage progression.

Character-generation policy belongs to the same saved script session. Reference
and fallback quest commands/queries share it; pre-world startup may enter it but
cannot leave without a player advancement owner. XP is authoritative vitals state,
and may cross a level threshold during chargen without being discarded by validation
or SPECIAL derivation. Leaving with earned levels fails before changing the flag
until source level-cap, skill/perk allocation and LevelUpMenu behavior are owned.
Reward commands and their modifiers remain unbound. This bounded policy follows
the primary [SetInChargen contract](https://geckwiki.com/index.php/SetInChargen);
it does not certify complete leveling behavior.

Numeric game settings have one mutable owner on the loaded winning plugin stack.
Their typed declarations resolve winning GMST bytes and admitted owned executable
defaults once; session overrides feed the ordinary float/integer readers and source
commands/functions. They stay outside campaign snapshots, following the primary
[SetNumericGameSetting session contract](https://geckwiki.com/index.php/SetNumericGameSetting)
and [NVSE numeric type/return contracts](https://github.com/xNVSE/NVSE/blob/master/nvse/nvse/GameSettings.cpp).
Unknown/non-numeric setters return failure. Non-finite and undefined storage
conversions fail before mutation. Skill, weapon damage/spread, medicine, armor and
death-delay calculations read current values. Consumers that still copy coefficients
register a named refresh boundary; an invalidating write fails before changing the
setting, with those boundaries exposed in telemetry. Derived player/NPC values,
movement, face/head clocks, HUD/quantity, casing and weather refresh remain incomplete.
Default Boolean GMST/unsigned initializer layouts and complete gameplay consumers
remain unadmitted; the admitted Boolean INI reader does not certify those numeric
GMST defaults or gameplay behavior.

Player script-package assignment and idle phase/cursor/elapsed/wait clocks live in
the saved shared session. Outgoing OnChange camera playback retains a pending
assignment until its clip completes; subsequent requests replace that pending
assignment without restarting the event. Same-package requests resume ordinary
idles without another OnBegin event. Native restoration validates the current and
pending winning package hashes, event kind and owned clip, then samples the retained
clock without replay. Legacy event snapshots retain their OnBegin interpretation.
PACK idle counts admit the owned byte and UInt32 layouts with exact list extents.
Package clocks pause while queued source player movement is pending; ordinary idles
require their explicit reference CELL/radius, while event camera playback may start
at assignment. Unreached traversal, editor origins, nonempty event scripts/topics,
end/removal animations, change cancellation and non-camera body channels remain
explicit gaps. Interruption, blending and event timing remain unmatched with retail.
This component boundary follows the primary [PACK layout](https://tes5edit.github.io/fopdoc/FalloutNV/Records/PACK.html)
and [AddScriptPackage behavior](https://geckwiki.com/index.php?title=AddScriptPackage).

Loading policy belongs to that shared script session, including pre-world source
results and cold restoration. LSCR location filters resolve each winning record's
direct CELL/WRLD identity or signed world-grid coordinates before native selection.
Godot projects its owned images and LSCT tip bounds/font/color while cell transfers
pause gameplay. Native door and player transfer tasks drain before source retirement;
closing their loading scope preserves an existing pause or retirement. Source
LoadingMenu ancillary animation/progress components and matched selection/fade/layout
timing remain explicit divergence. Layout contracts follow the primary
[LSCR](https://tes5edit.github.io/fopdoc/FalloutNV/Records/LSCR.html) and
[LSCT](https://tes5edit.github.io/fopdoc/FalloutNV/Records/LSCT.html) definitions.

Built-in tag-skill slots bind to their base-master record identities. Their
winning AVIF editor IDs, labels and fields may change without renumbering those
engine slots. Player skill queries and source menus use the same slot owner.

Numeric NVSE assignments and eval expressions use these same script state
owners. Their logical operators retain numeric operand values independently
of vanilla boolean expressions. Parser-version migration preserves admitted
owners, locals and clocks; unknown functions and value families stay explicit.
Integer operators truncate numeric operands to signed 64-bit values; based
literals retain their unsigned 32-bit contract. Render registrations retain
source identities in process-owned C# state. A shared native adapter dispatches
default callbacks before drawing and resolves the current world executor rather
than retaining retired delegates. Scene retirement disconnects the global draw
subscription; separate retail render-phase flags remain unbound.
Source UI interpolation and live trait dependencies share the C# component
store. A native monotonic clock advances menu animations before scripts, even
under pause or zero gameplay time scale. Supported HUD tiles project those
float overrides by source path and redraw on their revision; complete authored
menu branches, dynamic components and string presentation remain unbound.
Winning perk parameter changes belong to the selected C# source stack. Decoded
ability readers project current indexed values rather than caching private
mutations. Their lifetime crosses world replacement but does not write source
files or campaign snapshots; missing entry-point consumers remain divergence.
Keyboard/mouse control bindings have a shared profile-owned C# table. Script
queries and remaps read the installation's control settings and swap occupied
bindings within a device lane. The native adapter updates the player action map
and releases affected held actions. Retirement commits an atomic profile overlay;
source INIs and campaign snapshots are unchanged. Missing executable defaults,
joystick/gamepad mappings and remaining stock actions stay explicit gaps.
Script admission retains older saved owners with unsupported trailing Else text;
the reached executor still refuses that syntax until its behavior is bound.
See [NVSE script runtime](nvse-script-runtime.md) for the implemented boundary.

Destroyed references reject ordinary activation. MarkForDelete retains a
tombstone and commits deletion across residency teardown/reload. Neither a new
model nor an enable request can resurrect that state. Pip-Boy selection and
inventory invalidation have a shared owner. NativeOwnedPipBoy presents the source
menu on the owned arm-mounted NIF, with one camera/screen input projection;
equipment changes and quest/map state remain C# authority. Unspent character
creation state can be saved, while active interaction
continuations still fail visibly instead of being discarded.

RuntimeNativePlayerActor owns the player's equipped source skeleton, body, hand
variants, weapon model and KF palette. First/third-person views share inventory
and physics state. Animated equipment cannot also run dropped-object physics.
Equipped NIF subtrees with an authored anatomical parent bind to that skeleton
bone independently of the hand attachment. First-person duplicates have no eye
draw; the world body owns the visible pack. Both subtrees follow model visibility
when NPC combat and package presentation alternate. Shaderless export geometry
retains its source identity without acquiring Godot's default material draw.
Source accumulation is separate from local bone poses; third-person collision
movement owns world translation. The first-person render target has an explicit
output color conversion and source FOV. Pip-Boy presentation uses the source Hit
hold point and winning arm IDLE. Source draw/reload clips, magazine state and
animation sound events share the gameplay owner. FalloutWeaponShot resolves
winning weapon/ammunition/projectile declarations. Source attack Hit events
publish semi-automatic hitscan rays from the posed weapon node, while the shared
weapon/inventory owner atomically consumes rounds and retains recovery random
state. XR and flat input enter this same path. Actual source bone contacts now
resolve BPTD regions and damage constant-health creatures through the shared
reference owner. Winning skill settings, NV weapon condition and unconditional
abilities supply the bounded damage calculation. NPC health, selected armor
condition, aggression/assistance and bounded ranged/melee attacks now use the
shared world and damage owners. Critical/sneak modifiers, complete resistance
ordering, reactions, AI tactics, hit scripts and XP remain unbound. Pending death
events and elapsed gameplay time belong to reference injury state. The native
event adapter dispatches source OnDeath blocks after the winning dying delay and
speech completion, once per new death, with the actual killer as its filter.
Death switches that actor from its idle clock to its source skeleton's
rigid bodies and joints; corpse activation uses the existing container owner.
Saved injury, death inventory and physical bone poses share reference persistence.
Cold death construction waits until cell attachment completes. The current
authored-corpse path decodes ordered XRGD local bone transforms in game units.
Duplicate Havok part numbers retain skeleton traversal order. A saved physical
pose takes precedence over initial source placement. XRGB accumulation-root
rotation remains explicitly unbound. The current
Godot angular envelopes do not reproduce every Havok solver parameter, and limb
severing/explosion presentation remains incomplete.
Muzzle presentation resolves source addon indices through winning ADDN records,
indexed from the runtime's existing plugin stack before presentation begins.
One effect clock advances source controllers and emitters through short emission
windows, including long host frames, and retains surviving particles. The
growth/shrink modifier blends from its base-size endpoint through the smaller
of its generation-specific envelopes; a zero endpoint does not erase particles.
Completed nonphysics impacts retain at most one reusable graph. Reuse resets
controllers, particle state and emission remainders only after all particle and
audio tails finish; overlapping effects keep independent state.
The posed ShellCasingNode ejects the original WEAP MOD2 with source shell settings and its
own collision body. Original PROJ/LIGH supplies muzzle illumination. Actual
collision shapes and packed triangle materials select WEAP IPDS/IPCT models and
sounds. LAND retains LTEX Havok declarations; ambiguous material blends remain
unbound. These are presentation owners and cannot establish gameplay damage.
Exterior lighting registers transient meshes and particle draws within the active
world, excludes separate viewports and unregisters expired objects.

Native OpenXR has one visible source world body and an authored first-person
attachment rig for weapon and Pip-Boy action channels. Both use the same prepared
shoulders and collision-resolved wrists. The wrist UI borrows the existing device; it never
creates another player. Flat and XR share source triangle/UV picking and native
menu/gameplay state. Grip focus keeps the world live. The whole original wrist
device scales around the anatomical forearm centerline and rolls only about
that axis toward the reader. Release restores its source pose by the shortest
angular path. Owner replacement tears
down UI subscriptions before binding the new equipment. Physical acceptance,
room-scale collision following and complete menu actions remain required.

Particle draws pack the same source transforms, RGBA and atlas coordinates into
one reusable MultiMesh buffer per effect. Active quad bounds prevent render-side
bounds reconstruction from the uploaded data. Conservative bounds expand when
quads escape and refit once per simulated second; empty effects skip draw work.
Native action identifiers and particle distance comparers are reused.
Opt-in live diagnostics expose viewport CPU/GPU timing, a bounded 512-frame host
cadence window and the actual loaded C# optimization flag. Snapshots and JSON
freeze on the engine thread; one background writer replaces the complete file
atomically, preserving open readers. Backpressure and write failures remain
visible. Lightweight coverage queries do not replace canonical capture.

The winning cell-child index is built once for each requested record signature.
Subsequent cell queries visit only that cell's indexed children. The lab's
all-cell sweep exercises the same index and lifetime owner as ordinary loading.

Exterior residency follows the player's source grid coordinates. Background C#
reads prepare LAND and NPC NIF/EGM/TRI geometry; Godot stages new terrain and reference uploads, retains the
overlap, then commits cell membership and lighting. Source ancestry remains
independent of the active grid. Overlap retains event contacts, OnLoad state and
actor animation owners. Collision availability guards the edge of the resident
grid. Source LOD uses the owned quadtree, terrain morph data and a mask of actual
detailed LAND. Available parent/finer coverage is retained during replacement.
Reference events reuse the presentation owner's resident index; warm geometry
cannot retain activation bindings. Source discovery and runtime observations
still cover the full resident denominator, with canonical fields encoded into
one sized buffer rather than temporary per-field arrays.

Streamed NPC templates and equipment resolve from reference state on the scene
thread. Bounded workers prepare source geometry, then detached native skeletons
and body parts assemble over queue visits. Complete bodies receive gameplay and
physics bindings through the ordinary reference owner. Cancellation disposes
unfinished native bodies; source results waiting for assembly share the same
bounded admission count. Equipment changes invalidate pending appearance work.

Source byte reuse has a 512 MiB LRU; decoded NIF declarations are immutable and
reused within each file. Eviction removes the cache's ownership without invalidating
live readers. Decoded CELL metadata has a separate 128-entry LRU. Scene prototypes
and detailed texture entries expire with residency;
inactive LOD resources have a separate estimated budget. These caches are disposable
in-memory implementation details, never transformed launch inputs. Flat sprint and
step climbing are explicit OpenNV controller options; source jump height and shared
player control gates still govern ordinary movement.

## Launch sequence

The Windows development launcher defaults to a native ExportRelease build in
tmp/development-runtime/windows. Its manifest retains all campaign declarations
and selects the existing packaged-executable route. Explicit Debug uses the
Godot project runner, which always loads Debug OpenNV/GodotSharp assemblies.

1. The launcher validates the selected installation and campaign.
2. `NativeGameInstallation` identifies the game and content root.
3. `RuntimeLiveContentSource` resolves plugins, loose files, and archives.
4. C# readers build record/resource relationships in memory.
5. Campaign and world owners create Godot entities from those relationships.
6. Save files contain gameplay state and source compatibility identity only.

For NV/FO3, plugins.txt selects explicit activation; TES4 master flags and file
timestamps determine order. FNV NAM sidecars additionally activate matching
ESM/ESP files. Installed inactive masters are excluded. Archive admission uses
the Archive INI list, admitted plugin archives and FNV's base update archive;
unconfigured inactive archives do not become resource winners. Live retail's
admitted plugin array, rather than plugins.txt alone, verifies a comparison stack.

## Promotion rule

A parser count is not gameplay. A rendered cell is not a campaign. Support is
claimed only for behavior exercised by ordinary input and persistent state.

## Live parity evidence

The parity subsystem uses one canonical little-endian telemetry contract for a
private, read-only retail observer and the public OpenNV runtime. Each producer
publishes hash-bound frames through a Windows shared-memory ring. The comparator
joins equivalent state keys, checks canonical state bytes exactly, expands the
first mismatch into typed field deltas, and retains a bounded in-memory frame
window around divergence. Godot can show retail, OpenNV, and absolute-difference
views; the C# evidence writer can encode selected retail-left/OpenNV-right MP4
clips with hash-bound reports. Retail observation is one-way and never supplies
gameplay state to OpenNV. A diagnostic bridge may duplicate timestamped user
input into both games, but neither producer may use the other game's state to
advance simulation.

The comparison denominator is discovered from live source and runtime owners,
not a maintained checklist. A source identity without a runtime entity, an
unpublished event, a lost packet, or an unmatched final frame is divergence.

Exact source Float32 fields retain their original bits. Optional OpenNV frame
capture records native viewport bytes and state at both draw boundaries, with
PNG previews and file hashes. These outputs do not establish retail frame
correspondence. The live join currently produces candidate sample pairs;
unknown event identity stays unaligned, and full event/present observation is
still required. No sampled-packet or startup-capture result certifies gameplay.
