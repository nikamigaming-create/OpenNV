# Runtime repair design notes

Retained technical context from the prior implementation plan. The active work
order and coordination contract are in [implementation-plan.md](implementation-plan.md).
Historical status and work-order statements below are not current assignments.

## System design and retail comparison

The same batch must address the current-location Sandbox source used by the
observed mantis. The selected winning declaration has radius 512. Private
static retail research indicates the location belongs to the actor, is retained
independently of an individual activity, and can be explicitly updated; a
location lookup initializes it from the actor when absent. It does not yet
establish all package-selection, interruption or save/load transitions. Trace
and verify those transitions and how search candidates and wander destinations
share the location. The Sandbox reader and search/wander location now consume
the shared actor owner. New live NPC and creature starts capture their native
pose; a detached NPC start waits for tree attachment, and restoration requires
the retained point. Verify native detached staging and complete the creature
pending-start and interruption owners, then compare
their event timing with retail before accepting gameplay. The offline Sandbox
capability graph keeps source identity, admission and refusal separate from
runtime proof.

The selected creature `Find` refusal is not the NPC furniture-only case. The
winning `VCrGeckoFindFood` record (`1651b1`) has procedure 0, location type 2
with radius 2048, and a `PTDT` target type 2, Food category 18, count 2. Its
type-specific flags include `0x0200`, identified in the
[PACK layout](https://tes5edit.github.io/fopdoc/FalloutNV/Records/PACK.html)
as Allow Killing for Find. An inventory-only food search cannot cover that
permission or its combat and acquisition consequences. Its general flags also
include Allow Falls, so a complete implementation needs the actual airborne,
landing and interruption owners rather than treating a projected route as
executable. The
[Find package description](https://geckwiki.com/index.php/Find_Package) uses a
standard search location and target; it describes pickup for inventory objects,
activation for activators, and relationship-dependent interaction for actors.
Source identity and category alone do not establish the selected creature's
search, transfer, eating or event order. A
general Find batch must decode the complete target and flag fields, resolve
current-location anchoring and candidate eligibility against winning records,
then own approach, item acquisition, count, package events, interruption and
save/restore. It must preserve failed searches and unsupported categories as
visible outcomes, not choose a named food or award synthetic completion. Reuse
the existing NPC furniture, inventory and actor motion owners only where their
contracts actually match. Before the one ordinary smoke, run source-backed
offline transitions for selection, count, failure, interruption and cold
restore; matched retail observation remains required for timing and behavior.

The selected 4K continuation exposed a 491 ms main-thread creature publication
step, including 475 ms for one source body part. Exterior creature preparation
now decodes source inputs on workers and advances detached geometry through the
grid queue. The existing NPC part assembly can split
hardware-skin tree and partition publication. It now carries creature external
animation-target names for eligible parts; non-streaming creation still drains
it synchronously. Eligible source-bone attachment trees and their particle setup
are staged as well; unsupported attachment cases retain the direct path. A
creature streaming batch must:

1. Decode immutable creature skeleton, model and KF inputs on bounded content
   workers, with source identities/hashes and cancellation tied to the current
   stack and grid generation. No worker may advance actor clocks, random state,
   package selection or runtime callbacks.
2. Build detached native skeleton and part trees in bounded scene-thread slices,
   preserving source order, external animation targets, material channels,
   metadata, collision and every supported fallback. An indivisible native
   upload remains measured and visible; no geometry or actor may be dropped to
   fit a frame budget.
3. Revalidate source selection and actor state, then publish AI, sound and
   spatial owners only with the actual live actor. Cancellation, replacement
   and failed publication must dispose all partial resources without changing
   the prior live owner or replaying source effects.
4. Compare source-part topology, material/animation and lifecycle against the
   existing synchronous path in offline and owned-data cases; then exercise a
   single ordinary 4K continuation through cold streaming, combat and saves.
   Measure each native slice and all over-budget frames. Matched retail visual
   and audio evidence remains a separate requirement.

For creature animation sound enums, the selected retail static path reads the
actor reference's base form and follows its CSCR sound-source links to a sound
table; TPLT is a separate source field in this path. Retain the placed actor's
base identity, resolve every link against the winning plugin stack, and reject
cycles, deleted or non-CREA targets, ambiguous links and simultaneous local
and inherited sound declarations before any random draw. Once a single
candidate is bound, keep the existing shared process chance draw and audio
owner. Runtime template substitution and matched retail playback remain
unverified; no parity claim follows from this static contract.

Creature Travel flag `0x00080000` is identified as Allow Falls by the
[package-flag reference](https://geckwiki.com/index.php/SetPackageFlag). The
current source reader refuses this flag and the fall candidate search is not
an execution owner. Before admitting it, join package state and source route
to supported ground departure, airborne collision, landing, damage and
save/restore; establish retail thresholds and interruption behavior. A
candidate trajectory alone is not a traversable or completed package, and
the flag must not remove collision or cause a synthetic teleport.

Combat input verification must distinguish a player killing impact from an
already-dead actor struck after another participant's lethal hit. After a
conserved ordinary shot, a matching, error-free impact whose before and after
health are nonpositive and whose damage and death flag are zero is an
unattributed death, not a player kill or a missing receipt. Stop that skill
without credit and let the existing campaign takeover reevaluate live threats.
Missing, mismatched or contradictory impact receipts still fail closed.

The campaign input driver must retain an interaction approach to a resident
source target across distance changes. The reference skill already routes to
its standoff and requires the ordinary activation ray. A distance threshold
must not alternate travel and interaction while the target moves, cancelling
the in-flight route each time. Nonresident and cross-region targets still use
source portal or travel selection; actual follow, combat, changed objective,
and target disappearance keep their existing owners. This is automation policy,
not a change to authored quests or retail actor behavior.

NPC speech may reuse decoded winning DIAL/INFO source declarations within the
speech owner's source-stack lifetime. Every attempt must still evaluate the
current participants, conditions, quest and said state, random selection,
suppression and relationship checks in their original order. Cache no selected
response, state-dependent decision, failed decode or voice playback. A stack
reconfiguration clears the cache before installing a stack. Source decode failures
remain visible at the same call boundary; no timing or parity claim follows
until measured in gameplay.

Use [runtime-capability-map.md](runtime-capability-map.md) for the current
cross-system source-to-owner gaps and evidence requirements. Its incomplete
joins must remain visible as implementation work.

Maintain one authoritative line of work on `main`, including this plan and the
implementation. Keep existing local work intact. Working-tree changes are not
published or in `origin/main` until they have passed the required gate and are
explicitly integrated.

Before implementing a batch, write a source-bound contract for each affected
system: winning records and resources, inputs, state, transition order, units,
clock and random owners, outputs, failure cases, and save/restore behavior.
Separate verified source facts, retail observations, mathematical consequences
and unknowns. Where the retail rule is unknown, expose the gap rather than
inventing a value. Keep the model in C# and executable without a rendering
window where its inputs do not require native physics or a GPU. Use synthetic
boundary cases plus the actual locally owned record graph. Give each batch one
ordinary gameplay smoke and repair its failures; the required repository gate
and the focused offline checks are separate from that smoke.

| System | Contract and offline execution | Retail comparison and acceptance |
| --- | --- | --- |
| Records and resources | Winning ESM/ESP and loose/BSA precedence, FormID adjustment, exact source bytes and decoded fields; explicit unknown layouts. | Same installation and load order; compare complete identities, canonical bytes and semantic fields, not counts. |
| World and persistence | CELL/worldspace topology, enable state, inventory and package ownership, events, time, RNG and complete snapshots. Model transitions as `S_next = F(S, input, dt)`; validate atomic failures and cold restore. | Matched starting state and ordered event consequences, including untouched and changed entities. A save must restore every owned continuation without replay. |
| Player motion and physics | Source units and coordinate transforms, input, capsule, support, stepping, gravity, portals and object forces. The headless model predicts candidate motion; actual Godot collision remains the authoritative native check. | Compare matched trajectories, contacts and object interactions over time; include stairs, doors, the Doc skull and exterior transitions. Do not equate a NAVM path with a traversable route. |
| AI, population and combat | Package selection, conditions, schedules, path constraints, hostility, weapon/animation events, hit resolution, health and death. Model damage and timing only where source rules are known; retain RNG and interrupted owners. | Match actors, equipment, positions and triggers; compare event sequence, damage, reaction, death, loot and save/reload for multiple actor families and negative cases. |
| Quests, dialogue and actions | Execute scripts, conditions, topic selection, inventory, locks, terminals, crafting, skills and Caravan against deterministic state. Preserve result ordering, menu ownership and consequences. | Match dialogue node, quest stage, actions and changes on both sides, including failure and repeat interactions. Unsupported commands remain visible. |
| Presentation and sound | Decode source scene/mesh/material/animation/audio declarations; track clocks, transforms, source identities and voice lifetimes. Offline contracts validate resource and pose construction. | Compare equivalent camera, FOV, time, weather, actor/animation phase and renderer state; independently compare geometry, materials, light, motion, effects, audio and final pixels. |
| Streaming and performance | Account for source preparation, residency, uploads, simulation, render and synchronization on their owning threads; do not add overlapping timings. Measure cold and warm loads, motion and combat separately. | Flat 3840x2160, scale 1: every measured gameplay frame <= 16.667 ms. Physical VR at 144 Hz: every delivered frame <= 6.944 ms, with compositor misses, eye size and CPU/GPU time reported. Record hardware and workload; no content reduction. |

Exterior prototype retirement may be deferred after the atomic residency
commit: remove cold entries from the cache at the same point as before, retain
the detached prototype owners in a queue, and admit at most one native free per
frame through the shared publication budget. A subsequent grid snapshot cannot
mistake a queued prototype for a cache entry. Session shutdown still drains all
prototypes synchronously. Live instances retain their existing ownership; a
native free remains indivisible and no frame-time result follows until ordinary
gameplay is measured.

Reference registration can reuse a single source-ordered snapshot for dynamic
bodies and controller discovery. Preserve physics-before-animation restore
order, ambiguity checks, capture bindings, retirement and failure behavior.
This reduces duplicate hierarchy traversal inside publication; it does not
defer residency, hide a missing reference, or make the commit atomic across
frames. Verify saved object continuation and source animation ownership before
attributing any live timing change to it.

Static NIF prototype assembly may yield between source hierarchy nodes only
when the source contains no controller, skin, particle, billboard, sort or addon
dependencies that require the original complete build ordering. Leaf addon nodes
retain their ordinary builder as one indivisible step; their source decode and
resource preparation are reused in memory and warmed on bounded workers. The detached
assembly must preserve all source children, collision and final binding, retire
partial resources on failure/cancellation and enter the cache only when complete.
Unsupported hierarchies keep the ordinary builder. Individual geometry, texture
and collision operations remain indivisible until separately redesigned; no frame
budget is claimed from yielding alone.

An eligible hierarchy with a single particle system and no particle master,
node controllers or other order-dependent owners may construct that system's
node during detached staging and bind its particle controllers only after all
source nodes exist. A cached prototype with live owners must still receive a
fresh source-built instance for every reference; it cannot share controller,
particle, random or sound state. Multiple particle systems and particle
masters remain on the ordinary builder until their ordering is established.
Compare direct and staged source hierarchy, controller bindings and independent
particle instances against owned models, and reject partial publication on
failure. Final GPU creation and particle configuration remain indivisible.

NPC hardware-skin construction now yields between eligible source hierarchy
blocks and partitions, while rigid attachments and individual geometry work
remain indivisible. An earlier outdoor run attributed a 106 ms step to an NCR
armor part, rather than to the actor as an undifferentiated whole. The actor construction owner
must preserve the source hierarchy's depth-first order, inherited transforms
and visibility, cycle detection, skin root and bone palettes, partition order,
materials, morphs and attachment paths. It must hold the partial part detached
until all partitions and material channels are complete, and retire every
partial resource on cancellation or failure. Split hierarchy traversal and
partition submission into measured native steps; decoded source data may be
prepared on workers, while Godot resources stay on their owning thread. A
single partition or material upload can still exceed the frame budget, so
measure each rather than assuming a yield makes it bounded. Compare complete
direct and incremental output against owned head and armor models, then measure
the ordinary streaming run; do not publish incomplete bodies as gameplay.

For initial cell construction, completed detached NPC steps may share a
two-millisecond wall-clock slice before yielding to the next draw. A pending
worker yields immediately. No partial actor is published; one native
operation can still exceed the slice. Measure both the longest individual
operation and total load time, because reducing one-frame work by waiting once
per source node can make loading unacceptably slow. Creature construction
still uses its original synchronous path; stage timings distinguish source
appearance, skeleton, idle, each model, animation and sound before it is
restructured. Timings are diagnostic, not evidence of parity.

Creature staging must retain the selected winning template, reference and
animation continuation for the entire preparation. Resolve the source
appearance and prepare the actual skeleton, idle KF and model dependencies on
bounded workers without mutating the reference or advancing its clock. On the
scene thread, construct a detached skeleton and each eligible model in source
order, preserving KF-controlled external targets, material channels, shadow
flags, bone and attachment bindings. A completed body may enter residency only
after its original idle and sound owners are bound, and template/enable state
must still match the selected source. Cancellation or a failed dependency must
retire the partial native tree and leave the existing gameplay and saved clock
untouched; unsupported source paths retain their explicit existing failure.
Compare direct and staged skeleton, skin, animation targets and state on owned
creatures, then measure both load latency and streaming frame time. Source
decode caching alone does not bound a native model, material or GPU operation.

Decoded NPC, creature skeleton and part, and addon NIF sources may be shared only within their live content
owner and keyed by the winning resource identity and payload instance. Keep at
most 256 cached entries; a replaced payload must not reuse an old decode and
eviction must not mutate models already handed to actors or workers. Source
geometry and appearance-specific morph results retain their own owners. This is
an in-memory reuse path, not a generated launch asset.

After archive indexing completes successfully, the index accounts for every
selected archive member: an absent canonical path is an archive miss without
another full archive scan. Loose-file precedence is still checked on every
lookup, preferred-archive validation remains mandatory, and an incomplete or
failed index retains the ordinary fallback. A missing winning resource remains
an explicit failure. Verify indexed hits and misses against the same owned
archive stack before measuring the affected creature upload.

The draft Caravan match snapshot must retain each source card instance and its
owner, ordered stock/hand/discard zones, all six lanes and modifier ordering,
turn, phase, winner and the supplied opening-draw policy. Cold restoration must
reject missing, repeated, foreign or wrongly interpreted instances before
publishing state; derived lane and winner values must agree with the retained
cards. This only makes the current model resumable: the model's unverified
rules, retail menu/AI/wager and campaign ownership remain separate gaps.

When a retained partial bot route fails its coarse native search, try an
alternate authored corridor before spending another extended search on the same
nearby prefix. The alternate remains a preference, not a portal exclusion, and
every resulting edge still requires the same native collision and floor checks.
If there is no distinct source corridor, retain the existing refined and wide
fallbacks. A search rejection is not evidence that a portal is impassable.

For a candidate airborne descent, the player's current fixed-step integration is
`v_y[n+1] = v_y[n] - g*dt`, followed by native movement using the horizontal
input and `v_y[n+1]*dt`. This determines clear airborne motion only; starting
support, sweep recovery, side collisions, floor snap, landing support, source
floor and the player's real follow-through must each be checked separately. The
owned fixture observes the same integration, but does not establish collision or
retail equivalence. Do not add an airborne planner edge solely from the formula.

For each matched comparison, bind the retail executable and content identities,
cell, save/quest state, player position, camera/FOV, time/weather, actor and
inventory state, animation/event phase, settings and input sequence. Use the
private x86 observe-only retail producer and ordinary input under the repository
boundary. Compare semantic events and raw bytes separately from timing, audio,
UI and final pixels; no input receipt or visually plausible frame is a parity
pass. The existing producer exposes cell, position and engine time, but shared
semantic event identity, complete state, synchronized audio and final-frame
identity are not yet connected. Those are explicit implementation work, not a
reason to claim a match. Track unmatched rows as unknown and fix the earliest
owner that causes a measured divergence.

### Current missing-owner map

This is a work queue, not a support percentage. Each item must join its source
record/resource, C# state owner, Godot adapter, ordinary input, persistence,
source/retail evidence and offline executable case. A decoded record or parsed
statement alone is not a working feature.

| Missing or unverified owner | Why it fails or remains unknown | Implementation and offline direction |
| --- | --- | --- |
| Script and dialogue execution | `ShowCaravanMenu` is unowned. The selected INFO calls it before its quest-variable increment. Source parsing is not compiled execution, and branch-dependent commands and conditions remain unverified. | Inventory every winning SCPT and embedded result, source and compiled extents, references, event/statement branches and exact runtime dispatch owner. Add stateful execution and persistence before connecting UI; verify script and menu timing against retail. |
| Caravan | No game-state, deck, AI, turn, wager, menu or result owner is present. | Resolve the winning card/deck and INFO/script inputs; model rules, legal moves, turn progression and settlement offline, then connect the shared UI and save/restore. Compare a matched retail game before claiming parity. |
| NPC/creature packages | Find, Search and several Travel/Sandbox variants reach unowned source procedures; a stopped Sandbox failure can be interrupted by combat/death. | Map PACK declarations, flags, conditions, targets, event order, motion, interruption and cold state by source identity. Exercise each owner with deterministic state and native collision where needed; do not create proxy actors. |
| Streaming and saves | A source corpse, its shot audio and an earlier package failure have independent lifetimes; stale teardown previously blocked saving. | Retain exact source events and stopped history separately from the live corpse and finite voices. Validate cold restore and repeated entry without replay. Zero pending counters alone do not prove a save. |
| World geometry and collision | Distant-world materials, SpeedTree geometry, non-finite sign UVs, shaped water phantoms and some constraints remain unresolved. | Map every winning resource and referenced block to decoder, material, collision and live-instance owners; prove source transforms and failure paths offline, then compare matched visible scenes and physical traversal. |
| Audio and rendering | Sound source events can exist without proven listener submersion, reverb, mix or event-to-frame synchronization; appearance and animation differ or remain unmatched. | Map winning SOUN/media and NIF/KF/texture/material/lighting inputs through playback, timing and render outputs. Verify ordering and retained state offline, then use matched retail audio and final pixels. |
| Frame time and stalls | The observed 4K exterior exceeded the frame budget, with creature packages, collision work and streaming commits among measured owners. The bot can spend minutes on bounded searches. | Attribute complete frame and background work without double-counting; use reproducible source workloads and bounded in-process reuse, then measure ordinary movement, combat and streaming at full settings. |

The executable `script-capability-map <quest-graph-audit-directory>` development
command streams the existing statement audit, groups source-body identities by
operation and inspection disposition, and also groups each event's statements
by operation. Both views reconcile statement, uninspected and error totals
against the audit summary. It writes its compact JSON to stdout, not a
transformed game cache.
Parse-failed bodies are joined separately to their program scopes and compiled
extents, with their own reconciled denominator; they cannot disappear merely
because they produced no statement rows.
The map also indexes literal command guards in the current reference dispatcher,
quest fallback and native host, with file, line, guard and source hashes. It
reports the audit/runtime build identities and flags a stale runtime build.
Those syntactic sites are candidate owners, not proof that arguments, callers,
continuations or effects execute correctly. Dynamic guards and expression
dispatch are separate unknowns. Joining to executed branches remains required.
Its example sources are pointers into local owned data, not public game assets.

The 2026-10-27 local winning-graph audit inspected 628,463 record identities
(including 68 deleted winners) and found 14,854 parsed source bodies out of
14,866, 12 source parse failures and three compiled-extent failures. It reported
1,460 statement-inspection errors and 51,518 statements whose dispatch owner was
not inspected; neither figure is a count of confirmed unsupported runtime
commands. Compiled instructions, effects and state-dependent behavior were not
executed. Eleven of the source failures involve branch structure and one an
unbound token; source text is evidence to investigate against compiled bytes
and retail, not an instruction to loosen the parser. A repeat with the current
runtime build at that time returned the same counts and found 29 source statements named
`ShowCaravanMenu`, none with an indexed literal dispatch site. A later repeat
against the current runtime build retained those structural failure and
statement totals. The event view accounts for 1,336 OnAdd statements across
213 source bodies, including non-item contexts; its uninspected operations are
not a confirmed unsupported-command count. The private active audit is
`local/system-map-20261027-extents`; it contains owned-derived diagnostics and
is not a public input or a parity report. Subsequent runtime changes require a
fresh audit for a current-build match.

The Caravan source audit resolves the winning CCRD cards and CDCK decks through
the ordinary record stack, retaining raw fields and ordered card references.
The observed `FalloutNV.esm:157f6c` result script selects a CDCK deck and two
numeric arguments for the current opponent, then increments a quest variable.
The selected 1.4.0.525 executable declares `ShowCaravanMenu` with a required
calling reference and three required parameters labeled Caravan Deck, AI TYPE,
and BET PERCENTAGE (deck, integer, float types). A reader now checks the
declaration from the owned executable, and a resolved request retains the deck,
calling reference and numeric values without imposing unverified AI or wager
ranges. The owned declaration and synthetic request contracts pass. The
descriptor does not establish opponent validity, AI or wager behavior, or
whether the following statement waits for the game; these remain unverified.
The source script dispatcher resolves the deck and evaluates both numeric
expressions before forwarding the request; a synthetic host contract verifies
the values and that an unbound host stops execution. The native host validates
the resolved request and still fails closed because the match and menu are not
bound. This dispatch has not yet been checked in a gameplay continuation.
The [published game manual](https://cdn.akamai.steamstatic.com/steam/apps/22380/manuals/fnv_gfw_manual-10-steam.pdf)
describes number-card ordering, face-card effects, and sold totals from 21 to
26. `FalloutCaravanRules` now models number-card admission, king multiplication,
and the three opposing-pile winner rule over already interpreted cards; it also
selects Joker removals across the board and models queen direction/suit changes.
A focused synthetic contract passes. This is an offline primitive, not a card
decoder, full game, native behavior assertion, or live menu. In particular,
retail opening/draw behavior, deck construction, wagers,
opponent play and script continuation remain unowned.
An offline draft board retains source-card instances and attached modifiers,
validates numeric placement and instance reuse, removes Jack and Joker targets,
and recalculates lane totals, suit and direction. Static inspection of the selected
retail scoring path shows that the last numbered cards and queens attached to
the last card determine current direction and suit; the earlier draft incorrectly
applied every queen globally. Runtime equivalence is not yet verified. The draft
is not yet a complete match owner or save representation.
An offline match draft partitions supplied interpreted decks into an eight-card
hand and ordered stock, tracks both players' discard zones, and alternates
opening placements, numeric/king/queen plays, discards and disbands. Opening
draw policy is an explicit input, not a retail claim; exhausted-stock behavior,
card interpretation and opponent decisions fail closed.
A synthetic lifecycle contract passes; no UI, script or save state uses it yet.
In the current owned stack, the 324 card records group into four 78-card
groups with first INTV 1–4 and second INTV values 1–10 and 12–14, plus 12
records with first INTV 5 and second INTV 15. Selected labeled spade queens
have (2, 13) and a labeled spade king has (2, 14). This correlation is a lead,
not proof of executable field semantics or permission to infer a card from its
display name. The owned card loader stores those ordered fields separately;
static inspection of the retail scoring path uses the second as numeric rank,
tests 14 for the king multiplier and 13 for queen behavior, and uses the first
as the current suit identity. Other face-card labels are corpus correlations.
The source adapter now maps those admitted values to match-card identities and
fails closed for unowned combinations. The selected corpus audit interprets all
324 records and all 13 winning decks: 240 number cards, 24 each of Jack, Queen
and King, and 12 Jokers. Deck interpretation preserves source order and gives
repeated card records distinct caller-supplied instance identities.
This static and corpus evidence does not prove retail action behavior or mod
variants; compare matched runtime games before asserting parity.
The source audit now groups all winning cards by both fields and their distinct
display labels: the current stack reconciles 324 cards into 53 groups. Those
labels are diagnostic evidence only and are never gameplay authority.
In the current installation it decoded 324 of 324 cards and 13 of 13 decks.
The cards reference two winning scripts; each has a parsed source body and
compiled bytes, with execution still unknown. This identifies inputs; it does not implement card semantics, rules, the game
state, opponent, wager, modal continuation, menu or save behavior. The selected
dialogue therefore still cannot complete a Caravan game.
The base-game card script also requests `AddCardToPlayer` and `RemoveMe` on
inventory addition. The shared reference-script executor now resolves a real
calling reference's CCRD base into the saved player collection for
`AddCardToPlayer` and its source alias; a synthetic source-dispatch contract
checks identity and duplicate handling. `GetContainer` and `RemoveMe` now
dispatch to an explicitly supplied inventory-instance owner, with a synthetic
identity/destination contract. No live instance owner or inventory event delivery
is bound, so both commands still fail closed in gameplay. Verify
pickup and card-collection ownership, exact ordering and persistence, not just
the opponent's deck and menu.
Static inspection of the selected executable's add-card command shows that it
checks both player card lists for the source card and adds an absent identity
at the head of the first. `FalloutPlayerCaravanCards` implements this as an
offline collection owner, preserves the two lists on snapshot/restore, and
retains unknown legacy history instead of fabricating an empty collection.
Its contracts pass. Campaign schema v62 now captures and restores this owner;
v61 is admitted with unknown collection history. The save-schema and
collection contracts pass, but full campaign cold-load remains unverified. It
is not connected to inventory event execution or a menu, and the lists' full UI
roles remain unverified.
The actual v61 gameplay checkpoint passes the read-only campaign reader and an
in-memory v62 unknown-history roundtrip without changing its hash. This does not
prove a newly written save or gameplay cold-load.
An owned scan finds `OnAdd` declarations on 583 of 4,700 winning item
definitions, across 204 distinct scripts. The shared item event owner must cover
world pickup, container transfers and script-driven inventory changes as their
retail semantics are established; it cannot be a Caravan-only shortcut.
The source inventory also finds local declarations and multiple event filters.
Current stack variants have no item-script instance or container owner, while
reference event dispatch requires a resident world reference. Specify and verify
identity, stack splitting/merging, actor filter, execution ordering and cold
restoration before sharing this event path across inventory operations.
The shared inventory owner now rejects nested additions during a prepared
transfer and provides a staged addition with a reversible external-owner
lease. Its focused contract checks rollback, HUD publication and nested
mutation. This transaction is a prerequisite, not `OnAdd` execution: arbitrary
script effects, instance identity and `RemoveMe` are not yet bound to it.
The inventory can now remove a selected current variant transactionally,
rejecting stale revisions and partial worn-instance ambiguity. Its synthetic
contract checks condition/ownership preservation, rollback, HUD and cold
inventory restore. Variant selection does not establish a durable item-script
instance or select a retail `RemoveMe` target.
The shared item-event dispatcher can select source blocks and recipient filters
and invoke the existing VM with a supplied staging owner. Its synthetic test
and an owned audit of all 324 card definitions exercise the player branch and
command order. Those tests do not establish live item-event delivery, script
effect rollback, instance semantics or retail behavior.

The first integrated block covers the opening through Goodsprings and the
current Primm continuation: source-driven interaction and physics; actor
package/combat/death persistence; dialogue/script/menu consequences including
Caravan; and streaming/frame pacing. Map the live failing states to the
contracts above, execute the offline models, compare equivalent retail states,
then run one continuous gameplay smoke and burn down failures. The remaining
campaign, DLC, supported plugins, all actors/actions, full audiovisual parity,
and physical XR remain part of completion; this first block does not close them.

## Playable-game batches (2026-10-26)

The user directs substantial integrated batches, each followed by one gameplay
smoke and a burn-down of the failures that smoke exposes. A passing smoke is a
gate for the next batch, not a claim of whole-game or retail parity. The required
repository gate remains in addition to the gameplay smoke before publication.
The Goodsprings-to-Strip objective includes every encountered creature, loot
interaction, available action and character, including their source behavior,
consequences and persistence. Arrival or one successful route is not sufficient
coverage; unavailable, unobserved and divergent interactions remain open.

1. **Opening and Goodsprings:** finish the Doc sequence, tutorial prompts and
   physical interactions; make the quest, dialogue, NPC/creature, combat,
   inventory and exterior transition owners support the ordinary Goodsprings
   lessons. Smoke the route from New Game through the tutorials, saving and
   continuing along the way. Fix the route's blockers before advancing.
2. **Open world and progression:** complete shared streaming, encounters,
   schedules, travel, combat, quests and persistence needed to travel through
   Primm/ED-E and onward to the Strip. Smoke that continuous route, including
   cold continuation, and burn down its blockers.
3. **Remaining game systems and content:** finish the shared mechanics and
   source-driven campaign, DLC and supported content behavior exposed by broad
   play, including all skills, equipment, crafting, dialogue and consequences.
   Smoke representative connected play across those systems and burn down the
   failures; expand coverage before claiming the whole game works.
4. **Presentation, stability and performance:** close missing world rendering,
   audio, input/session lifecycle, hangs and frame-time problems without removing
   gameplay or visual content. Smoke ordinary interior/exterior movement,
   streaming, combat and transitions at the user's flat and VR targets, then burn
   down failures. Measure the physical headset separately from the simulator.

These batches overlap where a failure blocks play: reliability, performance,
presentation and save fixes belong in the currently active batch when needed.
The smoke uses normal gameplay and reports its coverage and remaining failures;
recording is enabled only for a specific visual question and then cleaned up.

Current user requirement (2026-10-26): **native 4K must not dip below 60 FPS**.
At 3840x2160 and scale 1, the frame budget is 16.667 ms. An average or median
above 60 FPS is insufficient: report maximum frame intervals and counts above
16.667 ms across ordinary movement, streaming, combat and transitions, with
loading measured separately and kept visible. This flat minimum does not replace
the VR 144 Hz / 6.944 ms target or permit visual/gameplay reductions.

Current user override (2026-10-26): **1:1 retail and 144 Hz or bust**. Keep one
maintained main. The user clarifies that **144 Hz targets the VR headset**.
Desktop draw cadence is a shared-runtime diagnostic, not headset acceptance.
Require the physical runtime's supported/active refresh, actual eye dimensions,
CPU/GPU frame times and delivered/missed compositor frames through ordinary
outdoor movement, streaming and combat. Simulator cadence cannot certify this.
Cheyenne must retain continuous source-owned poses while idle
and moving, including outside and after cold Continue. A bounded snapping fix
does not establish matched retail animation, presentation or campaign parity.
Required frame budget:
6.944 ms. The prior 49 FPS/720p diagnostic is rejected as a performance
handoff. Optimize measured runtime owners while preserving gameplay, visual
content, shadows, particles, collision and saves. Report CPU/GPU and actual
draw-interval median/p95/p99, late frames, resolution and display refresh for
ordinary movement, exterior streaming and combat. Distinguish uncapped engine
throughput from physical 144 Hz headset presentation; stationary or low-resolution figures alone
do not pass this target.

Updated 2026-10-23: the user directs complete-game playability in flat Fallout:
New Vegas before repeated deep optimization.
Start with correct Doc Mitchell positioning and the original questionnaire,
physical object handling including the skull, every Goodsprings tutorial,
shooting, creature combat and campfire cooking, then Primm/ED-E and the Strip.
Repair shared runtime behavior from owned records and actual gameplay. Continue
to remaining campaign behavior; a selected successful route is not full game
support. The active owner is in [current-work.md](current-work.md).
Ordinary play should preserve retail behavior while improving measured frame rate,
distance and streaming. Track exterior prefetch, transition readiness, cold/warm
loading and rendered frame stalls. Preserve cell, script and save semantics;
interior presentation must retain authored teleports/transitions and memory
ownership rather than assuming all interiors are spatially contiguous. Seamless
presentation is an acceptance goal, not an established blanket no-load capability.
The user authorizes coordinated writers. The local integration coordinator owns
builds, live input and Git; writers use separate scopes and serialize native tests.

The user explicitly includes terminals, lockpicking, hacking and all 13 NV skills:
Barter, Energy Weapons, Explosives, Lockpick, Medicine, Melee Weapons, Repair,
Science, Guns, Sneak, Speech, Survival and Unarmed. Include their progression,
temporary modifiers, perks, dialogue checks, crafting and combat consequences.
Acceptance requires ordinary successful and failed interactions and their real
world/quest consequences; loading skill values or displaying a menu is insufficient.

The accepted lockpick runtime connects keyless DOOR/CONT activation to manual
mouse/rebound-control input and Force outcomes for its admitted unowned,
unscripted, known-history plain-pin scope. Shared unlock, XP and resident
activation are owned; ordinary campaign and owned-model presentation acceptance
remain open. Locked TERM activation now connects owned puzzle generation,
interactive selection, source lockout/perk admission, XP/statistic outcomes and
delayed contents; ordinary campaign acceptance remains open. Complete the unsupported ownership,
script, threshold-cache, variant/history and XR lanes from source evidence;
never unlock on a skill threshold alone or bypass required quest effects.

The following inventory preserves FNV repair requirements from the rejected
physical VR playtest of package g. Shared gameplay defects also block flat mode.
Classic plans remain preserved in [classic-fallout-plan.md](classic-fallout-plan.md)
and [classic-world-realization.md](classic-world-realization.md).

## Acceptance reset

Package g is rejected as a playable VR candidate. Its earlier component and
simulator passes remain bounded evidence only. Rendering the world, hands or a
menu does not establish functioning people, combat, progression or persistence.
The requested 90-second private edit is selected development footage, not an
acceptance video. No runtime repair is implemented by this planning change.

The private 46:25 recording was surveyed across its full timeline at 20-second
intervals, with denser samples around selected interactions and visible defects.
This is a sampled review, not uninterrupted normal-speed or every-frame review.
Timecodes below refer to that original recording, not the highlight edit. Session
logs provide independent implementation failures; they are not synchronized to
video frames, and repeated error lines are not distinct affected actors.

Selected native source checks now cover deathclaw/coyote attack-group binding,
real event-key hit/miss range and retained cold attack phases. Ordinary F5 during
a qualifying coyote limb reaction and separate-process Continue also pass, with
the exact saved phase, injury/health and resumed combat. Ordinary terrain
deathclaw combat, older off-cell audio continuations and physical VR acceptance
remain open. Details and the current owner are in current-work.md.

## Evidence and repair inventory

| Priority / item | Evidence and boundary | Runtime owner and required repair | Acceptance before closing |
| --- | --- | --- | --- |
| P0: damaged combat state blocks saving | Session reports hit-reaction NIF user version 2 = 27 unsupported and an actual failed manual save. | NIF layout reader, actor hit reaction, player package capture and campaign snapshot: establish corpus-backed version layout and retained reaction continuation. Do not discard the affected actor or reaction to save. | Hit the source creature, save during and after reaction, cold-load and compare actor pose, health, combat and quest state. Original user save stays untouched. |
| P0: creature attacks fail to bind | About 16:08-16:27 a deathclaw approaches and overlaps the view. Logs reject deathclaw h2hattackleft_a animation-group binding on multiple references. Exact impact/damage timing remains unproven. | FalloutActorAnimationResources, NativeActorCombatAnimation and RuntimeNativeActorCombat.Attack: source variant/group selection, animation clocks, attack windows, reach and contact. | Ordinary terrain encounter visibly winds up, hits at the source event, applies damage and recovers; moving out of range genuinely avoids the hit. Test another creature family and cold continuation. |
| P0: armed and embedded attacks | User rejects combat. Logs also reject embedded weapons with multiple ProjectileNode emitters and report unsupported impact materials. | RuntimeNativeActorCombat.Weapons, attack-node selection, ranged combat and shot effects: bind authored emitter selection, ammo/reload/projectile events and impact material. | Source-armed NPC detects, aims, fires, reloads and damages through real collision; blocked shots stay blocked. Exercise multi-emitter creature separately. |
| P0: NPCs do not perform their behavior | User report; NPC response area around 20:56-21:22. Logs require travel/procedure and dialogue-location owners, linked patrol starts, patrol flags and AI condition ownership. Sunny walking around 34:44 is only a bounded positive example. | RuntimeNativeNpc.PackageSelection, PendingPackageSelection, DialoguePackage, shared package schedule/target/procedure owners: resolve full winning conditions and targets before execution, including interruption and resumption. | Observe multiple ordinary NPCs execute eligible travel, patrol, dialogue and idle/schedule changes; provoke, disengage and resume. No named-actor exceptions. |
| P0: combat relationships/assistance | Logs report conflicting enemy/friendly faction disposition priority. User reports poor response. | FalloutReferenceWorld relationships and RuntimeNativeActorCombat acquisition/engagement/assistance: implement source disposition arbitration and assistance eligibility. | Mixed-faction positive and negative fixtures plus actual combat show the correct actors retaliate, assist, remain neutral or flee. No universal-hostility fallback. |
| P0: first-person rig is visibly malformed | About 32:10-32:48 the rifle pose produces large deformed/penetrating hand and forearm geometry. Other weapon transitions need dense review. | RuntimeNativePlayerActor.OpenXR/HandContacts, NativeXrArm, NativeXrHandContact and NativeXrWeaponSupport: inspect bind spaces, bone weights, grip targets and pose publication. | Both final eyes retain coherent arms and grip contact during independent head/hand motion, support-hand release, rifle/pistol switching, aim, fire and reload. Physical comfort and alignment need a real headset retest. |
| P0: reliable launch/render/session lifecycle | Earlier D3D12 run was white; visible local launcher plus Vulkan Continue displayed the world. Logs contain shader compilation, view-count and 2D-versus-2DArray errors near session end. Vulkan recovery is not proof D3D12 is fixed. | Launcher process visibility, renderer resource lifetimes, multiview shaders and OpenXR session transitions. Reproduce the errors before assigning a common cause. | Repeated launch, Continue, New Game, menu return, tracking/focus loss and quit show actual pixels without white/black hangs, bad view counts or stale resources; test each advertised graphics path. |
| P1: creature procedures and cross-cell routes | Logs reject creature procedures 0/9/12/13 and additional travel inputs; marker travel lacks nearby authored floor. Prior saloon creature portal and Sunny return failures remain open. | Shared creature package binding, CellNavigationGraph, portal/capsule planning, streaming residency and deferred continuation. | Actor reaches source target over real slopes, narrow doors and cell boundaries, survives unload/reload and resumes without teleport, proxy or fabricated target. |
| P1: dialogue, quest scripts and activation | Around 29:20-30:22 and 43:20-44:10 conversations/UI are exercised. Logs contain AddReputation without owner, unbound conversation conditions, Invalid script else and generic Sequence contains no matching element failures on activation/GameMode/triggers. | Shared script interpreter/control flow, semantic command/condition owners, reference events and conversation results. Replace opaque lookup errors with exact failing source operation context. | Reach the affected conversation/trigger normally; observe real reputation, quest, inventory and package consequences once, then save/cold-load. Synthetic control-flow and absent-target fixtures must fail meaningfully. |
| P1: survival and actor values | Logs reject LeftMobilityCondition base/formula and an ability effect. | Actor-value formulas and source effect application/persistence. | Damage, cripple/heal and ability changes produce the correct authoritative values and locomotion/combat consequences before and after save. |
| P1: wrist UI, radio and input release | About 08:00-08:09 visible wrist pages change. Logs reject tuning without station broadcast scheduler. This does not establish every page or action. | NativeOwnedPipBoy, shared menu transactions, native radio/audio scheduling and NativeXrRig input ownership. | Stats/items/quests/map/radio execute genuine actions; tuning plays the selected station on its source schedule. Close, tracking loss and menu changes release held trigger/stick state. |
| P1: body/creature/model omissions | Logs reject absent creature resources, a protectron part with no presented surfaces, beard texture index -1, NIF skin/head binding restrictions and nonfinite UVs. | Winning resource selection, general NIF/model/skin/material readers and actor construction. Inspect actual source bytes; do not hide missing actors. | Each affected winning source is either correctly presented and animated or has an explicit source-integrity failure. Genuine unsupported layouts get corpus-backed tests, not guessed coercion. |
| P1: content identity | Official ESM selection is logged, but a missing creature path contains MMM_ghoulvariant_v1. This alone does not establish why the path won or prove the installation pristine. | RuntimeLiveContentSource, loose/BSA precedence and plugin record provenance: trace winning record and every resource layer, privately. | Record exact source winners; distinguish altered owned inputs, missing files and engine defects. Preserve installation and saves; test verified vanilla separately from intended mod support. |
| P1: collision and physical objects | Logs reject shaped phantoms and model constraints. | Source overlap/contact, Havok joint/constraint and Godot physics owners. | Triggers fire on genuine enter/leave; objects retain constraints through contact and save. Phantom data never becomes fake solid floor. |
| P2: vegetation, lighting, LOD and effects | Logs repeatedly reject present SpeedTree geometry, missing cactus/agave resources, light flags, shaders, world material for LOD, morph inputs, BSValueNode behavior, impacts and sever effects. Scenery in the edit is incomplete. | Resource readers and rendering/LOD/material/light/effect owners; fix resolution failures before decoding failures. | Traverse the same cells across lighting/LOD transitions and inspect both eyes, including distant draws, vegetation, shadows and impacts. Retail pixel parity requires matched retail evidence. |
| P2: audio and shutdown | Radio scheduler is unbound; prior NPC fixtures had playback shutdown warnings; this session ends with an OpenXR RID leak. Source recording contains audio, but this review does not certify mix/sync. | Audio voice/scheduler ownership, session retirement and XR resource disposal. | Correct event-bound voices/music/radio, complete finite voice termination and repeated clean shutdown with no dangling runtime resources. |
| P0: frame pacing and loading | The user requests immediate loading/combat measurement and repair. Fresh optimized flat runs retain roughly 26-31 ms rolling p95 draw intervals and a streaming upload above 200 ms. The firing-sequence window also contains a detailed diagnostic snapshot, so its 181-200 ms peaks are not isolated weapon timings. Desktop cadence is not headset timing. | Source loading, actor/animation ticks, particles, native wrapper lifetime, renderer and telemetry owners. Use recording-free traces to select each repair; preserve source behavior and complete gameplay state. | Measure cold/warm loading, ordinary movement and genuine combat separately, then headset CPU/GPU times and missed frames. Set the refresh-rate budget before claiming performance. |

## Work order and reviewable blocks

The active flat sequence is Doc Mitchell -> Goodsprings tutorials -> Primm/ED-E
-> the Strip, fixing each reached gameplay blocker at its shared runtime owner.
The inventory below supplies those owners. Physical VR repairs do not block a
flat repair; they remain required before a new VR handoff.

1. Preserve the failing save/profile privately and bind evidence to source commit,
   content winners, graphics driver and session. Inventory all observed failure
   classes; keep unobserved campaign coverage explicitly unknown.
2. Complete whole-game playability before repeated deep optimization. Measure
   performance immediately when stalls or resource failures block playability or
   trustworthy validation; keep builds and recording off during measurements.
   First save block:
   reproduce the hit-reaction-to-save failure on the
   affected owned creature. Repair the general NIF/reaction continuation owner,
   then prove ordinary hit -> manual save -> cold Continue. This prevents combat
   investigation from leaving the player unable to preserve progress.
3. Next blocks: deathclaw attack variants and event timing; embedded/ranged attack
   ownership; faction assistance. Require real damage and negative collision/range
   cases. A single attacking gecko or Viper does not close combat.
4. Repair NPC and creature package execution, schedule/conditions and cross-cell
   route ownership. Include interruption by combat/dialogue and authoritative
   resumption. Background actors must continue through the shared world owner.
5. Repair the malformed tracked rig and render/session lifecycle before another
   physical VR candidate. Audit both eyes through the complete motion sequence.
6. Close script/dialogue/reputation/actor-value, radio, content construction,
   physics and rendering defects from the inventory, ordered by actual route
   blockers. Retest persistence after every new state owner.
7. Run a fresh ordinary-input New Game -> Doc exit -> Goodsprings tutorial/wells
   -> Primm/ED-E route, with real combat, dialogue, doors, loot, crafting where
   required, fast travel, save and cold continuation. Survey the encountered
   population, not selected successful actors. Expand beyond this route before
   making broader game/campaign claims. FO3, mods and TTW remain later work.

Keep this work on the single maintained `main` branch as the user directs.
Retain unrelated work, add focused synthetic and owned-data proof, and run the
required runtime gate before publishing. Verify the resulting local and remote
main state after publication. No asset, save, private capture, address or
decompilation output enters the repository.

## Flat delivery acceptance

The flat handoff needs ordinary gameplay outcomes, full source-state save/cold
continuation, and actual flat audiovisual and input review across the requested
route. Whole-game completion additionally requires the remaining campaign and
supported content. A new VR handoff separately requires final-eye temporal review
and a successful physical headset retest.
Keep an explicit pass/fail/unproven entry for population, combat, quest changes,
input release, rendering, audio, loading and saves. Log every unsupported owner
and every missing entity; do not turn an exception into nominal success.

Before pushing a runtime or claim change, run the selected owned-data audit and:

~~~powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
git diff --check
~~~

Capture only a specific visual check, then stop recording and remove its new
temporary frames after inspection, including failure paths. Preserve the user's
original recording and requested highlight. Do not retry earlier cleanup that
was blocked by automatic approval review. A passing build or selected highlight
cannot override a rejected playtest.
