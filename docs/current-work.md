# Current work

## Active objective

Make standalone Fallout: New Vegas, Fallout 3, TTW and the selected mod stack
playable from user-owned files in flat/OpenXR. JAM, JIP LN, JohnnyGuitar and their
actual dependencies remain required. The current direction prioritizes working
code, main updates and usable builds. Previous OpenNV saves and schemas do not
constrain implementation. Compile and run practical smoke checks after meaningful
code batches; do not substitute proof reports or new test projects for gameplay.

## Current implementation

The launcher accepts an installed game's folder and selects the matching game,
remembers that selection, and displays launch errors immediately. Enabled mod
packages now enter their base game's runtime after actual source/dependency
resolution; descriptive mod-readiness metadata no longer disables Play. Missing
files and reached unsupported behavior still report their actual errors.

The isolated `codex/retail-gameplay-replay` batch brings the existing retail input
recorder and native C# replay onto current main without the unfinished native
integration branch. Keyboard replay includes all number keys and Ctrl; the retail
adapter covers the standard physical keyboard and five mouse buttons. Mouse
leases release on expiry, stop and runtime retirement. The replay CLI selects the
recorded tape's mode automatically; a recording without a matched checkpoint does
not claim matching gameplay state.

Finite menu feedback continues during pause and does not block a world save.
Actor, dialogue and looping audio keep their own continuation requirements.

## Live result and remaining work

Ordinary launcher Play and Continue loaded the owned FNV installation. Ordinary
input advanced the opening to stage 110, released furniture and restored movement
and looking. The run exposed a save interruption caused by an active Vito-Matic
menu cue; the batch fixes that presentation/audio boundary. The source parser
still rejects the broken SMG's stale, unbalanced SCTX. Its original compiled
program must be executed through the general compiled-script path.

The external retail diagnostic loader reached a Steam startup error. A normal
Steam launch opened retail, but no fresh recording/replay pair has completed.
Complete FNV, FO3, TTW, native DLL compatibility, all mod behavior and headset
playability are not established by this opening run.

The larger `codex/full-runtime-owner-integration` checkout is still being edited
by the separate active chat. Its unfinished native startup, compiled scripts and
world changes are not merged merely because they exist. Publish working changes
through a PR, update main, and advance the runnable test package. The next work is
ordinary progression and the reached compiled-script/native-mod failures.
