# Reference lock access and ownership

The C# reference world owns nonterminal lock access independently of difficulty.
Winning XLOC declarations admit the owned 12/20-byte extents, adjusted KEYM
identity, raw difficulty and flags. Source XLOC creates an initially locked
reference; byte FF retains difficulty 255. Unlock retains its difficulty/key.
Lock with omitted/zero difficulty retains it; a nonzero signed integer writes
the difficulty byte and locks the same placed instance. Without source data,
Lock creates a default lock; Unlock does not create one. Key activation uses
the same access owner and does not consume the key or change door motion.

A teleport reference without XLOC admits an unlocked pair when both winning
REFR records have reciprocal, master-adjusted 32-byte XTEL declarations, actual
DOOR bases and CELL ancestry. Both transforms must be finite and flags must be
zero. Neither side may supply opposite source XLOC or a retained dynamic lock.
An existing script lock on the calling side still blocks access; Unlock retains
that side's difficulty. Querying the other side then refuses visibly, including
after Unlock, because lock propagation and opposite-side difficulty precedence
are not yet owned. Directed one-way portals remain an explicit access boundary.
Inspection does not instantiate the destination or change cell residency.

Shared reference, quest, dialogue and PACK result execution binds Lock/Unlock
before native-command fallback. GetLocked and GetLockLevel read that same state
through ordinary and typed postfix receivers. An inactive source branch does not
query or mutate it. These commands do not activate, open/close or advance a quest.
The existing native HUD and door/container interaction adapters consume the
world's effective access state.

SetOwnership selects a typed NPC_ or FACT base form; omitted/zero selects the
engine player base. This changes only the reference ownership override. Pickup
reads it instead of static XOWN, retaining source rank/global, count and health
extras. Container CNTO/COED extras remain independently owned inventory state;
this component does not implement CELL inheritance or crime reactions.

Reference snapshots append optional lock/ownership overrides. Each pins the
winning record bytes and ordered master/name context, since identical bytes can
refer to different keys after a master-table change. Restoration validates in
the existing temporary world before committing any instance. Difficulty, owner
type, source drift and contradictory legacy flags fail closed. Legacy Unlocked
migrates access while retaining winning difficulty/key; an absent lock remains
absent. Campaign v26 admits preceding v25 state and refuses new overrides under
older schemas. Reached source failures retain their prefix; no Lock failure is
automatically recovered or replayed.

Source-less reciprocal teleport overrides additionally hash both winning
reference declarations and their master contexts. The absence of opposite lock
data is part of their admission. Destination source drift rejects restoration,
even when the calling reference bytes are unchanged. Cold access validation is
independent of snapshot order and creates no unsaved opposite instance. Existing
single-reference/terminal hashes and legacy absent-lock migration retain their
earlier contracts; the previous owner admitted no source-less teleport override.

Synthetic contracts cover FF/defaults, byte conversion, absent data, actual
callers, typed/lazy queries, per-instance isolation, adjusted keys and typed
owners, default player ownership, pickup extras, key access, serialized cold
state, legacy unlock migration, changed record/master context and atomic failed
restoration. Campaign checks admit/upgrade v25 and reject legacy override writes
without replacing existing save bytes.

Linked-door synthetic contracts cover reciprocal unlocked ESP-local links,
actual script locking/unlocking, opposite source/dynamic lock refusal, malformed
and missing links, both cold snapshot orders, legacy absent locks, destination
source/master-context drift and atomic restoration. The generic selected owned
pair audit reads caller-supplied identities and proves source-unlocked/cold access
plus disposable self-lock/refusal behavior through the shared world. It does not
perform ordinary activation, native travel or complete cold Continue.

The selected owned fixture executes the complete winning CG01 stage18 and72
results through the existing stage/result interpreter. It proves the actual
room's Lock100, subsequent Unlock/default ownership, the main door's Lock100,
once-only selected results, unchanged owned source and cold access state. Native
door effects and the source request for stage20 are explicitly collected. The
fixture does not execute nested/preceding campaign stages or establish ordinary
route, animated motion, complete actor cold continuation or matched parity.
Recording remains off; owned files remain read-only.

Linked-door effective lock inheritance, encounter-leveled difficulty
and positive optional public-CELL side effects remain visibly unbound. Broken
lock attempts, picking, inherited ownership and crime behavior remain unverified.
Source lock bits and leveled difficulty are separate queries: retaining a
leveled declaration does not establish its computed level.

Published command references: [Lock](https://geckwiki.com/index.php/Lock),
[Unlock](https://geckwiki.com/index.php?title=Unlock),
[GetLocked](https://geckwiki.com/index.php?title=GetLocked),
[GetLockLevel](https://geckwiki.com/index.php?title=GetLockLevel) and
[SetOwnership](https://geckwiki.com/index.php?title=SetOwnership). The two optional
integer Lock arguments and optional integer Unlock argument follow the owned
native declaration; their reached positive CELL side effect remains unbound.
The [GECK reference contract](https://geckwiki.com/index.php/Reference) describes
authored teleport links as bidirectional and defines lock access on references;
it does not establish opposite-side lock precedence. The unlocked-pair admission
uses that contract and both complete winning source declarations.
