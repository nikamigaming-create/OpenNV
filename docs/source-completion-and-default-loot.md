# Source completion, default loot and manual save requests

Default reference activation and attached script execution have separate owners.
A successfully parsed winning script with no OnActivate block leaves the existing
native default action available, even when another invocation has stopped. The
ordinary collider index, residency, enabled state and pending input guards still
apply. Opening corpse inventory uses the original container menu and shared item
transfer owner. Neither opening nor looting recovers or replays the stopped VM.
An unreadable script or an authored OnActivate block cannot establish this
independent admission. Native default failure has its own visible result.
New activation and trigger-entry input cannot clear an authored invocation's
fault and replay its already consumed prefix. Only the existing admitted recovery
or typed continuation owner may recover that invocation.

RewardXP uses the shared saved player's XP pool independently of its calling
object, quest or function. It reads the current source level cap, converts the
signed amount through Float32, rounds upward and clamps to the source cap's XP
threshold. It does not advance a level or heal the player. An active XP perk
entry requires its dispatch owner before publication; unknown rank/priority
and condition behavior cannot award unmodified XP. LevelUpMenu, allocation and
XP presentation remain separate owners. Signed-total overflow and negative pools
refuse before publication rather than being concealed by cap clamping.

Numeric GMST names share a case-insensitive registry. Different winning FormIDs
may declare the same name; source plugin load order, then serialized declaration
order, selects its value. A later override of an older FormID must take priority
over a new FormID from an earlier plugin. This follows the
[xEdit GMST identity contract](https://tes5edit.github.io/docs/18-whatsnew.html#1847---whats-new-in-xedit-223)
and [xNVSE name-based lookup](https://github.com/xNVSE/NVSE/blob/master/nvse/nvse/Commands_Game.cpp).
Aliases share the existing typed mutable session value; cold loading reads the
source value again. No source bytes are changed.

The bot uses the same strict independent-default admission before scoping a
target's retained script/selection/package fault. It requires a parsed source
without OnActivate, actual resident/enabled native binding and an owned default
interaction branch. Living dialogue, furniture and activators retain their
existing target fault guards. Observation consumes no input; ordinary collider,
queue, range, obstruction and transfer ownership remain independent. Global
execution, movement and presentation faults still stop the bot.

Finished speech gets a durable receipt only after actual audio retirement and
committed INFO results. It binds the speaker, winning INFO/DIAL, generation and
source scopes. Native counters and completed generation history survive cold
restoration without restarting audio. A stopped source invocation retains its
exact block, instruction, elapsed time, action reference and original prepared
detection request. An owned fixed-argument suffix can resume that instruction;
earlier guards, writes, result effects and random draws stay consumed. Historical
snapshots without that invocation cannot acquire a reconstructed cursor.

A successful package-event callback retires the consumed event marker only
after INFO results and notification complete. If the callback starts another
response, the new generation retains its own state. If it fails, the old marker
and fault remain capture blockers. The original owned package-event voice
passes actual audio retirement and cold-history checks; failing-callback and
reentrant-response checks preserve consumed counters without replay. Ordinary
F5 also writes a complete Escape checkpoint after this voice finishes.

CreateDetectionEvent retains source caller placement, CELL, signed sound level,
requested type provenance and Float32 simulation time. The proven High process
replaces its one pending event; proven lower tiers allocate none. The native
binding currently admits only the reserved player's proven High class. Expiry
reads the current shared numeric lifetime and uses strict greater-than age.
The setting can come from a winning GMST or the existing read-only owned
executable-default reader. Creation does not own receiver detection, hearing,
LOS, searching or alert consumption. Those boundaries remain visible after
restoration and cannot become a false GetDetected answer.

ForceSave queues a new ordinary manual slot. Entered source invocations finish
their original suffix before a later engine frame may invoke the shared complete
save writer. Suspended invocations, menus and loading retain the request; writer
failure retains a visible failed receipt. The global command also works inside a
source function with no placed caller, preserving its null calling reference.
An asynchronous writer failure does not stop unrelated later source invocations
or retry its writer. The original failed receipt, source hashes and ended
invocation remain visible. Complete capture and another ForceSave still refuse
that unowned failure history; successful writing is never inferred from source
script completion.
Concurrent AutoSave/ForceSave ordering and active request restoration remain
unowned. IsHardcore reads the existing saved player session. SetAlert changes a
source-bound persistent actor flag independently of weapon and combat state.

## Explicit player/menu save preparation

The synchronous pause-menu callback was a separate failure owner: it invoked the
complete writer while `SceneTree.Paused` kept the original finite audio paused.
The F5 wait could independently remain pending because running model/actor
source clocks kept emitting overlapping finite generations. The shared
transaction routes both surfaces through `RuntimeManualSaveRequests`, retaining
one request identity, one slot, coalesced input counts and all failed/cancelled
history. Diagnostic checkpoint and source ForceSave ordering stay separate.

A transient `RuntimeNativeManualSavePreparation` observes the settled session,
source graph, player, driver, CELL and menu identities, then keeps authoritative
world/input/source-producer clocks paused. Its input lease changes only modal
ownership: it does not cancel weapon actions, clear velocity or invent settled
gameplay. The status/menu says **Saving**, disables competing menu actions and
offers cancellation. There is no created-save feedback before the writer returns
the matching committed slot and that slot exists.
The source-script subtree acquires exact process-mode leases as well:
ordinary pause alone does not stop its Always-mode MenuMode, input and pre-draw
callbacks. Pre-draw source invocation now honors its node's process eligibility.
Those leases freeze source scripts/UI clocks without changing ordinary menu
execution, permanently toggling event registrations or touching the independent
image-space owner. Pending/failed script-sound continuations still refuse.
The input adapter continues observing genuine physical key edges without
dispatching gameplay callbacks, so a real release cannot become a stuck key
after cleanup; paused callbacks are neither executed nor replayed.

The existing source sound registry prepares a fixed set of already observed
finite native voices. Each lease binds the original node, playback, stream,
winning SOUN, source generation and owned media hash. Only those exact audio
nodes temporarily acquire `ProcessMode.Always` and unpaused playback. The source
host, actor/model clocks and completion continuations are not resumed; a voice
with a gameplay completion callback cannot acquire this independent lease.
This is explicit save preparation, not a global paused-audio policy. Pause
notifications cannot start a false mixer-EOS window.

Only the original native `Finished` handler settles a prepared generation.
`Playing=false`, registry removal, wall time, cancellation and authored Stop
cannot supply that receipt. The prior finite dispatch-window limits remain;
the complete transaction additionally refuses after 60,000 monotonic
milliseconds. Missing Finished, opaque/cancelled history, native/source loops,
unknown audio and unsupported actor, movement, conversation or source
continuations still refuse. A new registration/generation, resumed or replaced
playback, source/media drift, reload, loading, death or session/menu drift
cancels the transaction rather than migrating its lease.

The shared ordinary writer recaptures every existing authoritative predicate
after the entire fixed set genuinely finishes. It runs once, with no active
audio added to the save schema. `finally` cleanup restores the prior tree pause,
modal ownership, mouse mode and surviving audio process/playback flags, including
partial preparation and native teardown failures. The slot catalog rolls back
the prior Continue bytes and removes the candidate slot if writing, validation
or committed metadata fails. A rollback/cleanup failure remains visible.

Pure preparation/source-ordering contracts, actual native spatial/flat positive
and negative transactions, and the unchanged owned broken-door/real paused-menu
component pass. Ordinary paused Create New Save writes a complete Atrium slot;
ordinary quit and cold Continue restore its genuine stage-18 state at 120 HP and
12/24 rounds. F5 independently creates another complete slot through the same
owner. Post-combat actor/physics/procedure capture and ordinary exterior source
ForceSave remain separate evidence boundaries. These results do not establish complete
campaign saving or retail parity. Run focused checks serially with recording off:

```powershell
dotnet run --project .\contract-tests\ReferenceScriptContractProbe -- --manual-save-preparation-contracts
& $Godot --headless --path .\runtime 'res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn' -- --paused-save-transaction
& $Godot --headless --path .\runtime 'res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn' -- --sound-paused-save-transaction $OwnedFNV 'ttw' $OwnedTTW 'Fallout3.esm:0aba56' 'FalloutNV.esm:06eb6f' @OwnedDependencies
```

The pure selector covers fixed-set receipts, bounded missing completion,
source/native/media/generation drift, unknown/loop refusal, writer-once/cold
state, truthful feedback and Continue rollback. The native selector uses real
spatial and flat finite playback, overlapping generations, genuine mixer
Finished while the world is paused, an actually advancing producer clock that
must remain frozen, one committed component slot and cold non-replay. Negative
cases cover user cancel in both pause contexts, unsupported continuations,
actual unknown native audio, new source/native registration, timeout, native
replacement, session/death invalidation and deliberately missing source Finished.
Their previous Continue bytes and pause/modal/mouse/audio flags must survive.
The owned selector binds an original source NIF sound key and the actual typed
create-save menu callback with owned fonts, saving/busy/cancel UI and committed
browser refresh. Every component capture is removed in `finally`. Ordinary F5,
whole-campaign cold Continue, Vault exit/Megaton/train/Mojave and retail
comparison still require their own evidence.

One pre-existing pending ForceSave may now precede the ordinary player/menu slot
under the same owned preparation. `RuntimeManualSaveSourceOrder` binds the
original generation/GUID, requested phase, source hashes/sites and actually ended
requesting invocations, plus the exact manual session/source/origin/slot. It
requires the original owner's real engine-phase binding; it does not advance a
phase or retire a cursor. Only a later phase, unchanged paused producer/input
leases, every real finite Finished receipt and otherwise complete admission
permit the original `FalloutScriptManualSaveRequests.Drain` to run.

The coordinator's original source blocker accepts this specific quiescent
lease only during that ordered drain, never an arbitrary pause or menu. The
original bound writer commits its original slot with its existing
`WritingRequestedSlot` self-capture exception and source validation. Then
ordinary complete admission is checked again and the F5/menu writer commits its
different slot once. Receipt/UI state retains both outcomes; a committed source
slot cannot be hidden by a later manual failure. Source failure remains failed
without retry, and prevents the ordinary writer.

Unknown, entered/suspended or failed source requests, unowned stopped suffixes,
new source generations, phase/binding drift and concurrent AutoSave still refuse
or invalidate the lease. No request is cleared/cancelled/superseded and no consumed
prefix/suffix is replayed. Already ended typed source faults keep their original
error receipt and still require the original writer's existing whole-state
capture admission; ordering supplies no missing continuation. The focused pure selector covers both origins, exact
original source receipts, self-capture, two writers once in order and source
failure isolation. The native paused-save selector additionally covers actual
overlapping spatial/flat finite Finished while paused, the ordered source slot
and ordinary slot, both cold captures, frozen producers and restored modes.
These added pure checks and the 21-case native selector pass after integration,
including two distinct original-source-then-manual commits and both cold
captures. Ordinary exterior ForceSave, complete actor continuation and matched
retail ordering remain separate evidence.

Closed quest-stage results retain their exact source identity, entered stage,
consumed step count and failure through schema v41 saves. Restoring a failed
stage does not execute it; entering it again refuses before predicates and
effects. Other suspended stage iterators retain their actual execution leases
and remain save blockers when one sibling fails. A typed closed source failure
quarantines that result instead of stopping unrelated stage entries, manual save
drainage, radio and player updates. Its full error remains visible; unrelated
native failures still stop the driver. Detection queries in failed results are
still unowned and their absent result cannot be supplied by this fault scope.

Synthetic contracts cover cold generation/source joins, consumed prefixes,
paused recovery, malformed restoration, live numeric lifetime changes, default
activation ordering, unavailable defaults, ordinary save phases and global
function callers. The selected native corpse check uses an actual source guard,
ragdoll collider, original container XML/font and three source item kinds; empty
inventory and the original OnDeath error survive retirement and cold restoration.
The native speech check plays original owned voices, applies the original Look
result to the real source NPC, restores its ended failure off-cell, consumes the
original suffix once and admits a new generation independently. Its target point
is an explicit component fixture. Head targets retain all six independent flags,
stored references, stale cache, exact negative timer overshoot and revisions.
Source-bound native head state retains current/previous/authored quaternions,
easing, float override, BPTD, skeleton, bone/parent and settings identities.
Child-first retirement precedes source-reader disposal; cold attachment restores
the raw state after child initialization. Missing or failed required capture stays
visible. The owned original INFO Look fixture passes that complete target/pose
receipt and source drift checks; synthetic active-pose tests also preserve the
next publication. Whole campaign checkpointing, ordinary traversal and matched
retail pixels remain separate checks. Recording stays off for these audits.
