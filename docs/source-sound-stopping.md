# Source sound stopping

The winning TTW CG00 stage 90 result stops QSTBirthRoomLP before playing its
fade sound. Stage 100 then removes the player's scripted camera package.
The current ordinary run passes the former StopSound fault, completes seventeen
speech commands and reaches stage 100. [Package removal](player-package-removal.md)
now cancels the pending change; [shared youth state and trait pause](player-youth-appearance.md)
execute. The [source sound-path owner](source-sound-paths.md) executes
SetSoundSourceFile; ordinary input enters CG01 stages 0 and 5. SetPCToddler is
the next fault before the player transfer.
The gurney exit and subsequent campaign remain open.

## Source contract and owners

The [JIP command declaration and handler](https://github.com/jazzisparis/JIP-LN-NVSE/blob/main/functions_jip/jip_fn_sound.h)
define a SOUN argument and an optional object-reference filter. Selection uses
source sound identity across the playing-instance map. An optional filter also
requires an associated native object under that reference. It does not filter
by the command caller or media path. The command requests immediate stopping
and does not return a stopped-instance count. This implementation uses an
independent behavior contract; plugin code is not included or executed.

FalloutSoundVoices is one transient C# registry per loaded plugin graph.
Canonical winning SOUN keys and optional source-reference attachment stay
separate. Reference/result/stage programs, pre-world execution and fallback
quest scripts share the StopSound command and its zero expression result.
Omitted or zero optional filters select all matching registered instances.
Invalid types fail before the script suffix; a failing stop retains its applied
prefix and latches the unresolved suffix instead of retrying each frame.

The existing Godot script, animation/NIF, response-SOUN, 2D/menu and spatial
factories register with that owner. Spatial attachment follows actual source
reference metadata through the emitter's ancestor chain. Flat sounds retain no
reference attachment. Pausing does not retire a registered instance or exempt
it from stopping. Finished, emitter/tree exit and explicit stopping release
registrations. Session retirement clears transient voices; they are neither
save-baked nor replayed cold. Script requests still awaiting MenuMode admission
have not started a registered instance.

Script PlaySound now admits source loop/envelope declarations through the same
stop owner. Each WAV loop has its own validated stream and sample bounds;
decoded resource reuse remains immutable. Explicit StopSound stops immediately,
independently of the existing KF StopSounds envelope-release operation. A stopped
response-SOUN delivers its presentation completion once, without inline INFO
result execution.

## Bounded proof and remaining work

Synthetic contracts cover winning and case-folded identity, equal-path distinct
forms, multiple owners, reference filtering, paused instances, retired leases,
typed command arguments, zero results, source suffixes, failure-prefix retention,
script loops, fallback sharing and cold non-replay.

The isolated owned native audit executes the winning stop command over three
real birth-loop instances from script, animation and menu owners. It checks
independent loop streams, nonzero mixer samples, stopping while paused,
subsequent silence, completion once, repeated stop and clean retirement. Two
spatial instances under actual owned Dad and Dr. Li bodies prove reference
filtering and global stopping. Reverb and submersion gaps remain in telemetry;
no frame recording is enabled. The audit does not advance a campaign.

The ordinary flat opening uses New/Capital, gender/name input, the gene projector
and the owned trait menu. It reaches stages 90 and 100 and stops three actual
birth-loop instances. Package cancellation and subsequent youth/pause behavior
are tracked in the [removal contract](player-package-removal.md) and
[appearance contract](player-youth-appearance.md); CG01's sound-path mutation is
the next fault.
Telemetry publication interval overruns remain visible in this run.

Retail instance-map lifetime, queued-menu admission, exact command/mixer timing,
all ambient/3D sound production, complete volume routing, endpoint audio and
physical XR remain unverified. These checks establish the bounded sound owner,
not campaign, audio or audiovisual parity.
