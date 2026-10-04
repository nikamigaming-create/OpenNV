# Source actor procedure checkpoints

The reference world retains NPC procedure state independently of native body
residency. A save admits a procedure only when its capture owner can account for
the actual pose, animation clock, source selection, lifecycle, timers and random
state. Unowned movement, active conversation and animation overlays still refuse
saving. Capturing a stopped fault preserves its error; it does not supply the
missing behavior.

Find Furniture retains search, entry, occupation and exit state. The continuation
pins winning PACK, FURN, IDLE, NIF and KF identities, the selected NIF/MNAM seat,
exclusive reservation, initial disposition and actual body basis. Cold validation
restores all reference placements before checking reservations. Disabled occupants
keep their retained lease; restoring does not attempt a new reservation or select
a different idle. Native approach remains an unowned continuation.

Failed source predicate selection retains the candidate PACK and exact CTDA
ordinal, source fault, consumed random state, poll/schedule timing, stationary
base clock, blink queue and prior retirement. It admits no active procedure.
Restoration performs no predicate evaluation. A subsequent ordinary source poll
can reevaluate using the retained state.

Stopped initialization retains an explicitly declared package idle collection
and actor-wide replay cooldowns before procedure begin. Settled Dialogue retains
an unrequested, reached wait position, source movement mode and target/floor cache.
Cold restoration neither repeats package results nor initiates speech.

Package idle state contains its collection cursor, selection count, fractional
wait, completion, source-bound cooldowns and visible fault. Active overlays remain
separate save owners. Changing away from furniture retires its old Find/Dialogue
owner before the new procedure takes ownership.

Queued attached-script package events retain their typed actor/PACK membership,
kind, coalesced revision and winning source bytes/master context. Cold restoration
validates a fresh queue atomically. Consuming an event keeps a newer requeued mark;
failed script prefixes remain consumed.

Campaign schema v36 retains these owners and pending events. v35, v34 and v33
remain readable when their declared state is representable; an older schema with
new continuation fields or a nonempty event queue is rejected before replacing a
valid save.

Synthetic checks cover source/clock/lifecycle validation, exclusive seats,
disabled leases, cooldown timing, malformed atomic rejection and exact cold state.
Isolated owned native fixtures pass Furniture search, failed predicate selection,
stopped Travel initialization with a declared IDLE, and reached Dialogue waiting.
They compare actual body pose and subsequent clock advancement. These fixtures
establish only those continuations. Campaign checkpoints require separate ordinary
input and cold Continue evidence; neither fixture nor schema validation establishes
campaign, visual or retail parity.
