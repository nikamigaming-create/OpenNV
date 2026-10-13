# Current work

## Acceptance and ownership

Recover ordinary loading, gameplay and complete persistent saves for New Vegas,
standalone Fallout 3 and Tale of Two Wastelands with the selected mod stacks.
The product requirement covers all winning scripts, dialogue, quests, UI flows,
native modules and runtime entities. Binary framing and component passes cannot
establish campaign support. Headless, desktop and OpenXR must share authoritative
C# gameplay and persistence; Godot consumes state and adapts input/presentation.

The first gameplay milestone is three independent ordinary runs: New Vegas
completes the Goodsprings quest and leaves town; standalone FO3 completes Vault
101 and exits, then travels to and enters Megaton; TTW completes the Vault route,
reaches Megaton and completes a Megaton quest. At source-permitted checkpoints,
show a complete save, normal Quit and three cold Continue cycles, including
transitions. Deliver phone-friendly H.264/AAC fast-start video from real rendered
takes with readable labels and visible edit markers. Complete campaigns, other
mods and physical OpenXR remain separate acceptance work. No accepted gameplay
save or video exists for this recovery batch.

The user now authorizes publishing the complete source recovery through a checked
PR, merging it into main and verifying the GitHub backup. The candidate is on
codex/fo3-offline-clock, based on current origin/main. Preserve unrelated source
work and the private pre-recovery backup under local/fo3-recovery/source-before-recovery.
Owned assets remain read-only. The existing New Vegas save retains SHA256
f8304d443bca7d73648d4dd40d5a3406dd98be587ba4ad67fa5933d03eed95f6.
The development export under tmp/development-runtime/windows is stale and is not
an accepted candidate. Source publication does not qualify a playable release;
packaging and gameplay acceptance still require the ordinary routes and saves.

## Current gameplay failures

The user-selected toolchain is now official Godot 4.8-dev7 Mono
(4.8.dev7.mono.official.c971f93e7), Godot.NET.Sdk 4.8.0-dev.7 and .NET SDK 10.0.401.
All managed projects target .NET 10. Launcher, installer, CI and validation use
the shared runtime/godot-toolchain.json pin; archive hashes and actual versions
are checked. Matching Windows x64 export templates are installed and verified.
No new product export or publication was made. Release/Debug builds and focused
Main/player/plugin contracts pass. The coordinated 4.8 gate passes, including
analyzers, native Debug/Release domains, launcher and native Godot loading.
The private result is runtime-gate-godot-4.8-latest.log. The selected FO3 offline
audit has also been repeated on 4.8: 33 passes, one expected desktop-input
factory boundary and one real Player movement failure among 35 checks.
4.8's mip-level texture streaming is enabled, and live diagnostics distinguish
actual total/streamed/budget bytes. Direct owned DDS uploads remain resident;
the internal streaming registration/reload API is not exposed in the official
managed/GDExtension API. Actual FO3 Vulkan diagnostics report zero streamed and
streaming-budget bytes despite 1.37 GB of total textures in the birth cell and
1.68 GB after character creation. These Debug samples are diagnostics; owned
DDS integration and whole-game performance remain unaccepted.

First-person model replacement now uses a quiescent source-field boundary before
physical retirement; it does not require the whole Main/fade delivery to close.
Whole-process shutdown retains its stricter boundary. Authored checks reject
active calls, foreign threads and factory reuse, and preserve failed source
prefixes/save refusal through actual field replacement. Particle retirement
clears its instance buffer before its mesh. The fresh 4.8 Vulkan replay now
passes the reported stage-65 character-creation failure and reaches CG00 stage
100 through ordinary input. Normal Quit completes source-owner retirement with
no creation-replacement or null-multimesh errors. This is a selected lifecycle
check, not complete-game retirement or campaign acceptance.

Actual FO3 headless New Game reaches the owned Vault birth cell. Ordinary source
choices select Boy, confirm the entered name and finish character creation; CG00
reaches stage 100. The shared image-menu capture/restoration returns at stage 60.
Compiled TriggerScreenBlood enters the shared effect and consumes two original DDS
resources without GPU allocation or draws. Its menu-paused lifetime expires.
The latest raw KF sound repair routes NPC keys through the same source emitter
resolver as typed NIF keys. Actual Mom NPC voices now report ready PCM
continuations rather than anonymous-emitter refusal. Native cold proof retains
the exact 257-sample suffix, random state and source StopSounds lifetime.
After the quest disables Mom, those voices retain the separate missing disabled
emitter continuation. The latest ordinary character-creation repeat reaches
stage 100, attempts saving, cancels and exits normally with code zero.
The fresh product repeat has zero publication
failures and no compiled package callback errors. Pause/Quit Game/Yes exits zero after
reader and resource retirement. Recording is off. The previous native crash is
retained privately; complete-game teardown and cold Continue remain unaccepted.

Production Main still stops at source-Player-movement-current-actor-field-child-
unbound. Logical trigger membership now restores atomically from its qualified
source identities without replaying Enter or random draws. Its actual physical
contact producer, source phase/order and cold publication remain unowned and
refuse a playable save. Creature guard/idle owners also remain divergent; the
4.8 stage-100 replay additionally retains the reached editor-travel behavior,
search or target-owner refusal for Fallout3.esm:0300ef. Ordinary
Create New Save stays deferred behind the entered source invocation; Cancel
Save leaves no complete gameplay save. Complete cold Continue remains unexecuted.

The selected FNV and TTW stacks have now been retried headlessly on 4.8, retaining
all 10 and 18 selected plugins respectively. Both return exit code one and leave
no complete save. JIP passes Query/Load and the protocol-38 repair, then refuses
its actual callback-50 typed source-image mutation. TTW also retains the missing
JohnnyGuitar/Stewie/TTW callable imports. These are actual owned retries, not
native-mod compatibility passes. Their private logs are
headless-fnv-godot-4.8-latest.log and headless-ttw-godot-4.8-latest.log.

The actual FNV selected stack reaches native module admission. Headless USER32
now passes import binding. nvse_fnvxr completes Query and Load, then requests
unowned Console and PlayerControls interfaces. The source-backed Logging repair
passes the real authored native caller on the selected owned host, including
stable pointer/thread receipts, private diagnostic output and natural retirement.
Fresh FNV and TTW product retries pass the previous Logging null dereference.
ShowOff now completes Query and refuses Data function 1 because its actual native
class/cache/extra-data producer is missing. FNV retirement independently retains
the XR plugin's native child EOF during unload.
JIP completes Query and Load. Its original CALL identity now supports the actual
five-byte suppression write independently of the optional pointer-getter decoder.
The authored native write/protection/cache and fresh-domain tests pass. The fresh
FNV and TTW repeats expose an independent real cache-flush sequence: the source
helper attempts restoration with an invalid address, then flushes the genuine
site. The protocol 38 authored native focus now preserves the real failed SDK
restoration and output, actual RWX/shared-page protection, genuine cache flushes,
fresh cold domains and natural retirement. Private SDK failure tails are bounded
and remain separate from semantic faults. Fresh actual FNV and TTW repeats now
pass that cache-flush failure and refuse the next source-image protection request
because its typed mutation extent and producer remain unowned. No module is omitted.

The actual TTW setup resolves 18 plugins with no missing required packages using
D:/TTW/Installed plus the selected xNVSE, JIP, JohnnyGuitar, TTW NVSE and Stewie
folders. Its latest product launch passes selected sound startup and reaches
native module admission. JohnnyGuitar refuses missing Windows/CRT callable
owners; Stewie and TTW NVSE retain their independent missing callable imports.
Six exact JohnnyGuitar CRT imports now have genuine provider/IAT/export owners:
strncat_s, atoi, fmaxf, floor, round and the XMM0 tangent entry. Synthetic,
selected owned-module and actual authored native ABI/error/retirement checks pass.
The latest actual selected-stack repeat confirms nine remaining JohnnyGuitar,
84 Stewie and four TTW NVSE imports. The coordinated protocol 38 gate passes.
JIP retains its next typed source-image mutation refusal, ShowOff reaches the same missing
Data function 1, and nvse_fnvxr rejects this source version in Query. No module is omitted.
The selected DATA namespace passes owned startup/cold checks for all three games.
The launcher has no TTW selection yet. An earlier direct profile edit was rejected
by automatic approval review. Use the launcher's actual folder controls when the
desktop is available; do not bypass that decision with another file editor.

The development game's Windows session is locked. Foreground desktop and retail
input await an unlocked session; headless product play continues independently.
Engine-owned viewport readback has already worked while locked for the launcher.
The explicit --command-input route now selects shared command samples independently
of the renderer. Actual locked-session FO3 uses Vulkan, loads the Vault birth cell,
shows the source character-creation screen and accepts its ordinary page controls.
A fresh engine-owned 1280x720 character-creation frame was inspected and deleted
in its finally path. This 4.8 rendered repeat reaches CG00 stage 100 with no
presentation error and zero publication failures. Ordinary Create New Save
remains pending behind the entered source invocation; Cancel Save leaves no
complete save. Normal Quit completes, with the actual Player failure retained.
The private logs are godot-4.8-rendered-out-latest.log and
godot-4.8-rendered-error-latest.log. The live summary uses typed save-queue status
and CELL summaries instead of copying complete process bundles. The bounded
frame client reads and captures normally. A selected final Debug sample still
spends 35.93 ms constructing its snapshot and 4.74 ms serializing it; this
diagnostic cost remains material and whole-engine performance is unaccepted.
Full detailed state and every observed save/failure prefix remain available.
--headless-play additionally requires Godot's --headless backend. Both routes
require --data-root, --campaign, an isolated --save-path and an absolute --live-harness. Ordinary
numbered keyboard/pointer/text commands use the shared gameplay control owner.
A headless input receipt does not certify physical desktop or headset input.

## Current implemented boundaries

C# owns Main clocks, the shared world, scripts, state and persistence. Four source
clock channels and the renderer clock remain independent. Compiled qualified
assignment and XTEL No Alarm retain source order and state. The Player prefix
owns HUD/reference updates, thread entry, actual input getter, common-process
queries, scene scalar stores, reference queues, timed actions, post-action timers,
distance sorting, idle updates and movement-word clear. Eight source-declared
status descriptors retain separate mutable/effect pools and Float32 getter order.
Unknown callbacks and changed-state producers refuse before mutation. Common
process state is v8 and standalone Player scene state v15; unchanged NV v7 remains
readable. The production body does not reach its successful thread epilogue or
balanced scene decrement and cannot supply a completed-frame save receipt.

FalloutActorSourceModel now gives actual source assemblies fresh C# factories.
FalloutNifPhysicalModel reads original ordered scene slots, wrappers, bodies and
constraint registrations with an explicit linear traversal and indexed identity
lookup. Owned FO3/FNV/TTW third-person skeletons have 18 bodies and 17 registrations;
first-person skeletons have no bodies. These declarations do not supply a dynamic
world, utility registration, contact callbacks or historical systems. Current
native static-body/Area3D presentation cannot substitute for those owners.

The source image-menu caller now uses the same manager/list as Main, the normal
duplicate-admitting factory, two updates without clock advancement, immutable
receiver field capture, and form-wide removal/restoration. The C# receiver is
shared with the GPU-free headless adapter. Duplicate removal uses one ordered
linear compaction. The independent auxiliary interface gate remains unknown and
refuses when live auxiliary instances make its branch consequential. Constructor
sentinels, source declaration bytes, failed prefixes and cold receipts are tested;
rendered world capture and final pixels remain unaccepted.

FalloutSoundSearchNamespace preserves direct-before-DATA-prefix lookup and a
separate installation auxiliary input while resolving the selected winning DATA
layers. Original source archive registration and cold reconstruction use those
bytes. Merged native loose-directory enumeration still refuses when reached.

Native discovery uses FOSE for FO3 and NVSE for FNV/TTW. A discovered FOSE module
still requires its independent host ABI. Native imports validate original provider
and signatures, then require a window only when the reached operation needs one.
Headless USER32 owns the actual product thread and SDK keyboard layout/conversion;
Win32 message-queue keyboard state and window operations remain explicit refusals.

Compiled trigger callbacks retain original SCRO/SCRV filters, engine PlayerRef,
action context and failed-prefix/cold local state. Owned winning-header audits
pass for 809 FO3, 1,379 FNV and 2,218 TTW callbacks. Event instruction slices use
one linear construction and constant-time identity lookup. These audits do not
execute all branches or certify native contact parity.

Atomic save publication retries bounded Windows destination-sharing conflicts
after flushing the complete candidate; it never replays gameplay writers/effects.
Component rollback, damaged Continue and concurrent-save preservation pass.
Complete gameplay saves remain refused for the unfinished authoritative frame.
The mobile video converter passes format checks only; no gameplay take exists.

## Verification and next work

The required combined gate passes for the menu-image, physical-model, headless
USER32 and selected sound namespace batch. Owned image-menu declarations,
physical models and sound startup/cold checks pass independently. The subsequent
compiled package callbacks and PCM negative-capability observation pass the fresh
FO3 product run. Owned winning package header checks pass for all three selections.
Native Logging source/ABI, actual caller and private output checks pass. Its final
provider LastError receipt repair is included in the passing coordinated gate.
Release and Debug builds, analyzers, contract probes, launcher and native Godot
checks pass. The expanded FO3 offline run has 33 passes, one expected desktop-input
factory boundary and one real Player movement failure among 35 checks. It still
refuses gameplay acceptance.
Private logs are under local/fo3-recovery; none are campaign or pixel acceptance.
The required command is scripts/Test-GodotRuntime.ps1 with the pinned Godot 4.8-dev7
Mono console executable, followed by the selected owned audits and git diff
--check. Frame recording stays off during development and headless checks.

The required coordinated runtime gate passes for protocol 38 suppression and
the six CRT owners, raw KF/bone/static-node sound continuation, logical trigger
membership, command-input/backend selection, physical constructor inputs and
collision groups. The constructor projection retains eighteen ordered Float32
points, nonzero center XY asymmetry and distinct radii. Collision groups preserve
source truncation, zero-wrap reset, thread and cold identity. These owners do not
publish a body, canonical hull or production world receipt. The selected FO3
offline audit and live-summary failure-retention focus pass their component lanes;
the final combined gate for the summary change passes. Complete gameplay remains refused.

Next, repair the Player physical world and living utility as one cohesive
owner with real ordered body registration, contact/constraint classification,
point-velocity/impulse semantics, lifetime and cold state. Do not qualify its
constructor-empty arm from the first-person skeleton or a null presentation
ragdoll. Bind the later movement/control, camera, equipment and context owners
until repeated Main frames return and complete saves become eligible. In parallel,
implement reached native mod interfaces, genuine inventory-reference construction,
CRT callable imports and exact suppression receipts. Close physical trigger
contact production alongside the shared world. Then execute the requested opening routes and cold cycles,
and record/render proof only after actual gameplay can finish them.
