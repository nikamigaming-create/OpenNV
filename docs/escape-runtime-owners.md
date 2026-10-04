# Escape runtime owners

Reached original TTW scripts need shared faction relationships, current actor
queries, independent package weapon visibility and radio conversations. These
capabilities operate on winning source forms; no quest-specific handler or
actor placement is added.

## Factions and actor queries

SetAlly and SetEnemy validate two FACT forms and both directional flags before
changing either relationship. Winning XNAM reactions supply unchanged defaults;
shared combat queries read the same mutable overlay. Save v38 retains both
winning faction hashes and directional reactions, rejects invalid/duplicated
relations atomically and reads v36/v37 checkpoints.

GetDistance and the reached GetAV queries share self, package-target and explicit
reference scopes. Resident actors publish their actual body placement. Source
Variable01 through Variable10 use retained reference values; missing player pose,
unowned values and unknown scopes remain errors. Travel's WeaponsUnequipped flag
controls presentation independently of running/once-per-day flags and inventory.

## Radio and dialogue

StartRadioConversation validates a source radio TACT/REFR and radio DIAL. The
station supplies condition identity; INFO ANAM independently supplies its voice
actor. Source results execute once, voice completion precedes link selection,
and each linked topic evaluates current state. Goodbye ends the sequence without
changing broadcast mode or dispatching an invented SayToDone event.

Native speech retains one voice/cursor per transmitter. Actual resident ACTI
radio references and the Pip-Boy receiver determine an audible listener; an
out-of-cell receiver is never materialized through that lookup. The transmitter
clock continues without an audible receiver. Continuous broadcast scheduling,
interruption, receiver toggles/attenuation and active-radio saves remain visible
missing owners. Ordinary scripted Say/SayTo requests wait behind player dialogue
and select after its completion; multiple pending requests and competing package
conversations still refuse unowned arbitration.

## Exact player locations and evidence

A native body installed by an authored MoveTo can lose a float bit when divided
back into source units. Player package membership compares the source marker's
forward projection with the actual body in the same coordinate units. Zero-radius
locations remain exact; neither a tolerance nor a traversal success is invented.

Full-reader synthetic contracts cover faction source precedence, directional
changes/cold state, shared combat, scoped actor values/current pose, Travel flags,
radio station/voice identity and fresh links, refused interruption/continuous
generation and exact projected player locations. The selected owned Escape audit
executes original stage 18 faction commands, reads the guard's Variable02 and
Amata's Travel flags, and finishes all five original PA INFO results with exact
owned voice bindings. It is an isolated audit. Ordinary native continuation and
matched retail evidence remain separate requirements; these checks do not
establish vault, campaign, audio or actor parity.

## Dialogue trigger and ordinary continuation

PLDT describes the speaker's wait location; PLD2 independently describes the
dialogue target's trigger. A zero-radius speaker wait cannot bypass the target
trigger. The selected owned audit retains both source declarations and verifies
that the original player bed lies outside the ambush radius. The ordinary native
run now completes Escape 0, 2, 3, 4 in that order, removes the camera/control lock,
finishes Amata's conversation and reaches 18 with completed faction results.
Original radio audio advances through complete lines and links. The player remains
inside the vault with source actor, geometry and effective-door-lock failures.

## Loading and reactive control waits

Ordinary startup and cell/save transfers present an animated spinner with phase
and elapsed time. Cell construction yields after its 8 millisecond work budget
between complete source references; individual reference creation remains atomic.
Detached trees retain existing source ownership/publication order. Cross-cell
MoveTo completion follows the awaited transfer; failed moves retain their fault.
Measured native cold load: 31.5 seconds total, 11.7 seconds cell assembly, 329 draw
yields; the largest single-reference stall was 914 milliseconds. This measurement
does not establish a comparison speedup or original loading-screen parity.

The bot separates real tree pause/loading from unpaused modal/source-control
waits. Loading suspends goal clocks. Thirty seconds without quest/objective,
menu, scene or control progress releases held input and reports the blocker.
Repeating audio and timer-variable writes do not renew that interval. Typed
execution faults stop immediately. Focused contracts cover stalled controls,
paused response clocks, source progress and cancellation failure; the native run
reports the bounded modal wait. Campaign decision-making remains unowned.
