# Classic script events

The native classic runtime is not yet a complete campaign script host. The
shared INT interpreter and event owners must connect to authoritative gameplay,
3D presentation and persistent state; decoding an instruction is not execution
of its complete gameplay effect.

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
Cancellation was merged through checked PR #232. The subsequent native timer
block is under validation; it is not yet campaign or matched-retail acceptance.

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

## Acceptance

The complete product target is all FO1 content working in our 3D runtime, followed
by three complete problem-free ordinary-input playthroughs. Current qualifying
playthrough count: **0 of 3**. Component and source-admission audits do not count.
