# Source door state

`SetOpenState` selects a placed object's owned Open or Close sequence without
dispatching activation. The shared reference snapshot retains its target and
opening/closing completion state, while the existing managed NIF controller
retains source identity, elapsed time and text-key cursor. Flat and OpenXR use
the same state and controller bindings. Duplicate targets preserve the clock.
The getter distinguishes open (1), opening (2), closed (3) and closing (4).
A decoded object without these source animations returns none (0).
These are the [GECK getter states](https://geckwiki.com/index.php/GetOpenState);
the [setter](https://geckwiki.com/index.php/SetOpenState) uses a Boolean.

Native construction binds the capability when a source model declares Open/Close,
including activators. It requires one finite managed controller and rejects
ambiguous or incomplete declarations. Existing loading doors keep their portal
owner. A script cannot claim an animation target that has no resident binding.
Closed-door collision follows the source animated subtree; no extra geometry,
clearance bypass or quest-specific branch is introduced.

Cold restoration validates the retained target against its saved sequence and
model/controller identity. It restores the source pose and elapsed clock without
replaying crossed text keys. Legacy snapshots without the new optional motion
state retain their prior DoorOpen endpoint. Moving reversal, authored default-open
policy, source door-record sound routing and matched retail timing remain open.
Another animation replacing the door's selected sequence fails visibly.

Synthetic scripts check target selection, all four active states, duplicate
requests, invalid Boolean/arity, executed failure prefixes and atomic rejection
of inconsistent cold snapshots. The owned native fixture selects the actual
playpen gate, executes the winning CG01 stage-16 QSDT, observes moving collision,
restores a mid-opening pose/clock, closes it to its source endpoint and rejects
changed source identity. It records no frames and dispatches no activation.
The fixture does not establish campaign or retail parity.

In the resumed ordinary toddler route, normal movement again reaches Dad's
trigger and stages 12/14. His actual arrival result enters stage 16, plays the next
voice and now closes the gate. Dad's subsequent CG01DadCloseDoor package exposes
the next unsupported travel/procedure; the quest remains at stage 16. The gurney
departure, remaining toddler actions and Vault exit are still unverified.
