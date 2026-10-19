# Classic script events

The native classic runtime is not yet a complete campaign script host. The
shared INT interpreter and event owners must connect to authoritative gameplay,
3D presentation and persistent state; decoding an instruction is not execution
of its complete gameplay effect.

## FO1 campaign initialization

New Game executes owned startup with map-entry action 15; the source program
routes its own map-enter procedure. Level-one FO1 skill and trait queries and
instance-aware inventory transfers now let the opening's actual grants execute.
The owned three-premade Begin fixtures cover arrival, distinct inventory,
save/restore, drop/pickup/equipment, duplicate prevention and in-memory source
variation. Synthetic checks cover partial ammo, first-match stack order, cold
payloads and failed-script rollback. They are not full-game acceptance.

FO2 startup and save identity retain their existing path and pass regression.
FO1 new-game initialization does not complete map first-run persistence,
unload/export lifetimes, live skill/perk progression or general actor events.
Active item effects and retirement side effects without runtime owners fail
closed. Old FO1 preview saves lacking initialization identity remain unchanged.

## Object event cancellation

Original FO1 binary registration and diagnostic strings identify opcode 0x80f1
as object event removal. Static inspection shows one object argument, no script
return value, and removal of all queued entries owned by the target while
preserving unrelated entries. The original queue releases type-specific payloads.
The evidence and original target hash remain in the private research workspace;
this is static evidence, not a matched live-retail parity result.

OpenNV's represented script-timer queue removes every matching target event,
retaining its current tick, next insertion sequence and other targets' ordering.
The VM does this within its transaction. Null/unknown target handles fail closed;
an operation followed by a script failure cannot mutate the source queue. Other
retail queued-effect families need their own lifecycle owners before full event
queue parity can be claimed.

The synthetic ClassicMapInitializationProbe covers repeated target events,
unrelated events, clocks/sequences, no return-stack residue, cold restoration,
idempotent empty cancellation, invalid handles and failed-script rollback.
Cancellation and native timer work were merged through checked PRs #232/#233.
They are not yet campaign or matched-retail acceptance.

## Native FO1 timers and nested use

The native door event owner now retains one active-map script queue and shared
game tick. Static FO1 initialization establishes 264600 deciseconds, and ordinary
script updates advance one tick after 100 milliseconds rather than catching up
a stalled frame with several seconds of game time. Combat, dialogue time gates,
world travel and the complete calendar remain separate unfinished owners. FO2
timer admission remains closed pending its own lifecycle evidence.

Due events are removed in their unpublished transaction before dispatching the
target's `timed_event_p_proc` with action 22 and the saved fixed parameter. A late
callback observes current dispatch time; newly scheduled work uses that time,
not the old deadline. Equal deadlines retain insertion order. Negative
`game_ticks` input converts to zero, as observed in the original wrapper.

Script `use_obj` dispatches the target's use procedure and default action within
the same transaction. The suspended caller retains its value stack and local
frame while sharing program/script variables and world changes. Nested override
applies to its own use. Instruction budgets include nested execution. Unsupported
interaction owners and overlapping door animations remain explicit failures.
This owner does not yet supply all object event families or general animation
request sequencing.

Native v5 saves retain the clock, active map, queue, fractional update phase and
any retained callback failure. Prior v4 saves could not admit pending events and
start with an empty queue when upgraded. A current save missing its clock or
whose targets no longer bind to source objects is rejected. A failed callback
keeps its pending event, freezes the script clock and reports a persistent fault.
Static transition cleanup cancels script events when leaving a map; same-map
elevation transitions retain them. Ordinary save/reload retains queued work.

Owned fixtures exercise 18 eligible Vault 13 metal doors: source use schedules a
three-second event, cold continuation runs its nested use and closes the door,
and the subsequent source event drains. Additional fixture checks cover absent
clock data, misbound maps/targets, same-map versus map-departure cleanup, and a
retained synthetic callback-owner fault across cold reload. These are fixture
checks, not ordinary input, a campaign run or matched retail visual/timing proof.
The Vault entrance terminal remains unbound.

## Source module initialization

The native reader retains initializer instructions and their identifier/string
references, separately from the source procedure index table. Native startup
executes the initializer before its source startup jump. Integer globals may be
interleaved with export declarations, stores and other supported operations;
they are no longer inferred from a contiguous prefix of constant pushes. The
global stack frame becomes persistent program variables at startup transfer.
Unknown instructions, invalid frames and instruction-budget exhaustion fail
without publishing state. Nested object use during module initialization still
requires a suspended-global-frame owner and remains closed.
Initializer reference literals are currently admitted only as immediately
consumed export names. Other reference-literal uses require typed
value ownership and is rejected rather than becoming integer global state.

Static original-binary inspection identifies export declaration as creating an
integer zero, resetting an existing declaration from the same module, and
rejecting a different owner. Names and owning module identities fold ASCII
letters only. Export ownership follows nested event transactions and native v6
saves. Older saves retain their values as unowned; declarations cannot silently
claim those values. Assignment requires an existing exported name and reports
an undeclared-variable failure otherwise. Another module may write an existing
export without changing its declaring owner; assignment is not declaration.
The source opening map is connected through campaign initialization, but full
map/object module lifetimes, unload and revisit dispatch remain unbound.

Synthetic source bytes cover interleaved globals, computed values, initializer-only
references, source `Start` spelling, export reset/conflict, cold owner metadata,
nested ownership, undeclared assignment, cross-module writes, late
instruction-budget failure and rollback. The owned
opening-cave initializer produces six globals and six zero-valued exports.
The complete FO1 scripts.lst audit isolates initializers by replacing the startup
body with a diagnostic return: 880 pass, 62 entries have no owned resource, one
has an unsupported procedure identity/body and three require typed reference
globals. Those 66 remain visible audit
failures; this is neither full script execution nor campaign support.
FO2 startup, starting grants and cold continuation still pass for all three
premade characters. The 18 FO1 timer fixtures and 72-map door admission checks
also pass their existing boundaries, with four unbound door initializers.

## Map-entry prerequisites

The integer VM implements signed less-than-or-equal with left/right stack order
and integer zero/one results. It does not infer float or string comparison
support. The elevation opcode reads the authoritative world-object elevation;
the source null-handle result is zero, and an unknown non-null handle fails
closed. Synthetic checks cover signed extremes, equality, operand order, null
and missing objects.

The owned opening's first-delivery fixture with first-run=false executes its
startup dispatch and map-entry body (151 instructions), returning an arrival
override and light level. First-run=true still fails at single-item inventory
transfer. These are source-script fixtures, not live map-entry or campaign
acceptance. An ordinary-input opening check selects a character and writes a
save, but its empty starting inventory confirms that FO1 Begin still bypasses
source campaign initialization. Complete map lifecycle and item ownership are
the next runtime owners.

## Acceptance

The complete product target is all FO1 content working in our 3D runtime, followed
by three complete problem-free ordinary-input playthroughs. Current qualifying
playthrough count: **0 of 3**. Component and source-admission audits do not count.
