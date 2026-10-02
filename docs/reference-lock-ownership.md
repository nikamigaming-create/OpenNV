# Reference lock access and ownership

The C# reference world owns nonterminal lock access independently of difficulty.
Winning XLOC declarations admit the owned 12/20-byte extents, adjusted KEYM
identity, raw difficulty and flags. Source XLOC creates an initially locked
reference; byte FF retains difficulty 255. Unlock retains its difficulty/key.
Lock with omitted/zero difficulty retains it; a nonzero signed integer writes
the difficulty byte and locks the same placed instance. Without source data,
Lock creates a default lock; Unlock does not create one. Key activation uses
the same access owner and does not consume the key or change door motion.

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

Synthetic contracts cover FF/defaults, byte conversion, absent data, actual
callers, typed/lazy queries, per-instance isolation, adjusted keys and typed
owners, default player ownership, pickup extras, key access, serialized cold
state, legacy unlock migration, changed record/master context and atomic failed
restoration. Campaign checks admit/upgrade v25 and reject legacy override writes
without replacing existing save bytes.

The selected owned fixture executes the complete winning CG01 stage18 and72
results through the existing stage/result interpreter. It proves the actual
room's Lock100, subsequent Unlock/default ownership, the main door's Lock100,
once-only selected results, unchanged owned source and cold access state. Native
door effects and the source request for stage20 are explicitly collected. The
fixture does not execute nested/preceding campaign stages or establish ordinary
route, animated motion, complete actor cold continuation or matched parity.
Recording remains off; owned files remain read-only.

Terminal lock state, linked-door effective fallback, encounter-leveled difficulty
and positive optional public-CELL side effects remain visibly unbound. Broken
lock attempts, picking, inherited ownership and crime behavior remain unverified.
Source lock bits and leveled difficulty are separate queries: retaining a
leveled declaration does not establish its computed level.

Published command references: [Lock](https://geckwiki.com/index.php/Lock),
[Unlock](https://geckwiki.com/index.php?title=Unlock),
[GetLockLevel](https://geckwiki.com/index.php?title=GetLockLevel) and
[SetOwnership](https://geckwiki.com/index.php?title=SetOwnership). The two optional
integer Lock arguments and optional integer Unlock argument follow the owned
native declaration; their reached positive CELL side effect remains unbound.
