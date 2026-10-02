# Player package removal

The owned RemoveScriptPackage command accepts the actor as its caller and no
arguments. Its private retail handler completes removal and resets the player's
animation control without an invented wait for an outgoing change clip. The
winning TTW CG00 stage 100 uses it while the birth camera still has a pending
script-package replacement. That source transition previously failed here.
Private executable data and addresses are not public implementation inputs.

RuntimeNativePlayerPackage now admits removal during that pending change.
It validates the outgoing package's reached exit behavior before mutation,
then clears both current and pending assignment, phase, idle, cursor, elapsed
and wait state. The shared session publishes no player package, and the native
player releases its source camera through the existing flat/OpenXR adapter.
Later frames cannot finish the discarded change or reinstall its pending
assignment. Repeated removal remains harmless.

Nonempty exit scripts/topics and authored exit animations still fail before
discarding state. Their deferred execution owners remain separate work. No
actor, location, quest stage or particular package identity selects cancellation.
The existing source hashes and cold pending-change validation remain in place;
the save schema does not change.

Synthetic session checks retain winning source identity, phase/pending validation
and null removal across serialization and replacement of an older live change.
The isolated owned native change fixture removes both same-package and different
pending assignments. It checks released camera transforms, no reinstall after
sixty seconds of advancement, repeated removal, cold cleared state and isolation
from other owners. Existing change-clock, latest request, remainder, movement
pause and source-drift checks also pass. Recording remains off.

A fresh ordinary flat New/Capital run completes seventeen dialogue commands,
reaches CG00 stage 100, removes the package/camera, disables Dad and stops CG00.
The shared saved SetPCYoung policy and modal trait pause now execute; ordinary
input executes the [sound-path command](source-sound-paths.md) and enters CG01
stages 0 and 5 before its unsupported SetPCToddler. See
[player youth and modal traits](player-youth-appearance.md). Telemetry interval
overruns remain visible. No child race change,
next-cell transition, gurney-exit acceptance, campaign or retail/XR parity is
claimed from these component and prefix checks.
