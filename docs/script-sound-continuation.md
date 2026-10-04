# Reference script sounds and stopped instructions

`FalloutScriptSounds` owns PlaySound3D for the shared reference interpreter and
quest fallback. It validates a real placed reference or the engine player and
the winning SOUN before preparing audio. A native voice is parented to the
existing reference node, so moving that source moves the emitter. A missing
native node fails before publication. No proxy emitter or campaign placement
is introduced.

Mono WAVs use native 3D playback and the owned source attenuation curve,
distance bounds, gain and pitch. Stereo media and source 2D flags retain flat
playback. Owned loops, variant selection, pause, completion and reference-filtered
retirement share the existing sound owner. Environment reverb send and listener
submersion remain visible audio divergence. This does not establish complete
audio or matched retail parity.

New missing-command failures retain a source statement and a SHA256 identity
covering the complete SCPT bytes plus master-adjusted SCRO forms. Cold
restoration validates this identity before continuation. The newly supplied
PlaySound3D capability can resume that failed instruction and the remainder of
its invocation, with already consumed assignments, random draws and branch
guards retained. Completion advances the invocation clock once. A later failed
instruction retains the earlier completed continuation and its new failure.

A legacy error without a cursor is admitted only when it identifies a unique
GameMode command site with fixed compiled operands, no enclosing loop and no
ambiguous MenuMode site. The enclosing entered branches follow that reached
source instruction rather than reevaluating mutable guards. Legacy source
identity uncertainty remains explicit in the persistent receipt. Mutable
operands, ambiguous sites and unsupported loop frames remain stopped. This
capability does not clear arbitrary faults or retry complete invocations.

Synthetic checks cover typed reference calls, invalid targets, source flags,
consumed prefixes and guards, captured cursors, source mismatch, legacy
ambiguity, loop refusal, clock completion, historical faults and cold no replay.
The selected original TTW stopped invocation passes an isolated native
Godot/WASAPI audit using its actual source model, mono intercom WAV, attenuation,
root following and mixer completion. Checkpoint bytes and the entered stage
remain unchanged in that audit. Actual campaign advancement and full save
parity require ordinary input and separate observation.
