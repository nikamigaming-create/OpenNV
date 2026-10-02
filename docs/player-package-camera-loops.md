# Player package camera repeats

The native camera now consumes the selected winning IDLE's timing DATA and the
shared source KF playback clock. The intro runs once, the authored
StartLoop/EndLoop interval repeats, and finite repetitions retain their outro
and unused frame time. The source value 255 remains an indefinite repeat. This
follows the [GECK idle animation contract](https://geckwiki.com/index.php/Idle_Animations)
and the already implemented actor idle clock.

A forever-loop package event supplies an ongoing pose. It does not impose a
completion barrier on the next script assignment. When the outgoing change and
incoming begin choose the same active source IDLE, the new assignment retains
that instance, phase and selected repetitions. Same-package change events can
retain their explicit event pose independently of the ordinary package idle
list. A different incoming IDLE selects its own source clock. Existing finite
event waits are retained; their exact interruption and retail timing are not
established by this change.

The owned birth departure camera has a travel intro followed by a short repeat
interval near its final pose. Replaying the complete KF as the idle previously
returned the camera to the room and repeated that travel. Its winning IDLE now
keeps the authored final interval. No quest, actor, location or stage identity
selects this behavior, and no player transform or quest outcome is fabricated.

Ordered interval crossings publish source text keys, including Sound and
Enum: StopSounds, through the existing native sound owner on the actual player.
The repeat clock emits boundary keys once per crossing and excludes intro keys
from later inner loops. Unsupported keys, including an unbound Blend directive,
remain visible in camera telemetry. Non-camera body targets, blend behavior,
audio reverb/output routing and matched event/audio timing remain incomplete.
Removal cancels current and pending camera assignment and releases the flat
camera through the shared player adapter. Independent transient voices retain
their existing source stop/completion/retirement policy.

The shared session snapshot now retains the selected IDLE source hash, KF hash,
chosen and remaining repetitions, exact source phase, completed repeats and
boundary admission. Elapsed time is checked against that phase. The animation
sound random stream also retains its current state. Cold restoration validates
the winning package, timing and repeat interval before sampling the camera; it
does not dispatch old text keys or replay transient audio. Earlier snapshots
admit only deterministic IDLE loop selection and reconstruct their saved elapsed
prefix without consuming random state. Variable legacy selection fails visibly.

Synthetic checks cover fixed/random/infinite repetition admission, source
intervals, finite outro remainder, cold future-key equivalence, cycling clocks
and atomic rejection of malformed snapshots. The selected owned native fixtures
check same-package pose retention, shared change/begin handoff, long repeated
camera clocks, cold camera equality, movement deferral, malformed/source-drift
rejection and removal without later reinstall. The camera/audio fixture checks
the actual departure KF, one intro sound and stop, nonzero native mixer output,
retained final repeats and cold continuation without sound replay. Rejected live
restoration leaves the assignment, source clock, camera, session and sound child
unchanged. Live sound-stream restoration preserves the RNG object used by an
existing child, repeated removal reuses that child, and player exit retires its
native voices and registrations. Recording
stays off. These are component fixtures, not an ordinary gurney departure,
campaign completion, physical-headset acceptance or retail parity claim.
