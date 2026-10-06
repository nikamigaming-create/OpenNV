# Source animation event history

Native source sounds keep per-reference request generations separate from the
number of live Godot voices. Selection retains the winning SOUN hash, request
path and variants, selected owned media hash, output/pitch, consumed random
states and unsupported presentation lanes. Only the matching native Finished,
authored Stop or selected chance skip settles that request. Child retirement,
zero voices or a cleared dictionary cannot supply completion.

Save v43 retains completed animation sound history and stopped pure hit-reaction
query history. Cold validation checks source identity and consumed selection on
an independent random-state copy. Native sound binding additionally validates
the owned media. It resumes no finished voice and removes no partial audio lane.
Active, cancelled, failed and unreceipted requests retain visible save refusals.
Older saves supply no invented completion history.

A finite source voice keeps its actual native node under the persistent source
scene owner. It follows its real emitter while resident, then retains the last
real position until native Finished. Model retirement alone supplies neither a
Stop nor cancellation. Source graph/node retirement remains cancellation.
An exact source name absent from the complete NIF string domain uses the original
root-position fallback; finite missing-name voices do not follow later movement.
Declared names with missing native adapters still refuse. The default lookup is
AttachSound. Loops retain their separate attachment and stop owner.
Actor skeletons and each current assembled part expose their complete source
domains. Real source bones bind one posed BoneAttachment3D shared by sound
players; each dispatch and following update reads the current skeleton pose.
Absent names use the actual source-root bone frame. Current equipment removal
retires its names/adapters, while unknown domains, duplicates and declared
unadapted nodes remain refusals. Binding consumes no selection RNG or source key.

Live save admission validates every retained reference ledger, including
nonactors and off-cell voices, against the matching native generation and owned
source/media. Finite waiting changes no capture predicate and writes only after
genuine completion. The transient receipt belongs to the shared content sound
owner, independently of manual save requests. An asynchronous source writer
failure retains its request and complete-capture refusal without stopping
unrelated future script frames or retrying the failed writer.

## Native completion dispatch boundary

Godot's [4.7.2 native playback owner](https://github.com/godotengine/godot/blob/4.7.2-stable/scene/audio/audio_stream_player_internal.cpp)
reads mixer activity for `Playing`, but removes ended playback and emits
`Finished` during its later node-processing notification. Spatial players use
the [native physics notification](https://github.com/godotengine/godot/blob/4.7.2-stable/scene/3d/audio_stream_player_3d.cpp).
Mixer inactivity alone therefore cannot supply either completion or refusal of
an otherwise proven live finite wait.

The proposed completion-wait owner retains the original native node, playback,
stream, source/media hashes and generation, after observing that playback
playing. `awaiting-native-finished` admits only the same registered, resident,
processable, unpaused, nonlooping playback. It returns the unchanged transient
receipt for at most two subsequent native processing phases and 1,000 ms.
Either bound expiring is a visible refusal, never a synthesized event. Missing
startup, replacement bindings, resumed playback without completion, source
drift, cancellation, opaque history and loops remain refusals. Neither the
retained ledger nor the complete writer's capture predicate changes.
`soundVoices.active[].finiteCompletion` exposes the phase, original binding and
timeout cause without changing history or admitting a checkpoint.

Focused selectors extend the existing pure, native finite-lifetime and owned
emitter/cold audits. Run these serially with recording off; they have not been
executed as part of this isolated proposal:

```powershell
dotnet run --project .\contract-tests\ReferenceScriptContractProbe -- --finite-sound-completion-wait-contracts
& $Godot --headless --path .\runtime 'res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn' -- --finite-sound-completion-wait
& $Godot --headless --path .\runtime 'res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn' -- --finite-sound-lifetime
& $Godot --headless --path .\runtime 'res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn' -- --sound-finite-completion-edge $OwnedFNV 'ttw' $OwnedTTW 'Fallout3.esm:0aba56' 'FalloutNV.esm:06eb6f' @OwnedDependencies
```

Use the selected legally owned installation and unchanged dependency folders for
those variables. The owned selector resolves its exact winning sound from the
reference's original NIF text keys, repeats the isolated component on fresh
owners, and requires genuine native inactivity before `Finished`, strict active
capture refusal, one later complete reference-world capture and cold non-replay.
It neither executes campaign input nor writes the selected source installation.
The native component deliberately holds only its own main-thread dispatch while
the real mixer reaches EOS; elapsed time supplies only failure deadlines.
Disposable component captures are removed in `finally`.
This establishes no ordinary failed-F5 attribution, complete campaign save,
cell-stop policy, endpoint audio or retail parity. A current immutable receipt
must additionally bind the refusal's exact generation to its native playback
phase; an older registered nonplaying voice is not that same observation.

## Independent stopped continuation

A hit-reaction read failure retains the exact source condition ordinal, visited
sources, successful predicate/random prefix and failed query. It is historical
state rather than an active pose or resumable script cursor. Cold restoration
neither reevaluates its mutable predicates nor supplies the missing query result.
Opaque physical failures and active reactions still require independent owners.

A selected script, package-event or ended dialogue IDLE can continue after a
package begin fails. Its independent snapshot retains the winning IDLE/KF,
selected repeats, exact phase, future text-key boundary, revision and uncovered
raw bone components. The separate base clock, stopped package selection/error,
random state and retirement remain retained. Cold native binding restores the
selected phase after the source base clock without replaying selection or result
effects. A live response, attached animation object, weapon, active sound or
independent physical/combat pose is not admitted by this stationary owner.

Before teardown, an already admitted stopped NPC selection/package fault can
prepare an immutable nonaudio pose/procedure copy. Exact original finite
source/media generations and the native binding lease must remain unchanged.
The normal writer commits the copy only after every generation reports native
Finished; it never invokes the retired model's capture callbacks. Current script
locals/inventory remain current. Replacement bindings, new generations, changed
poses/clocks/faults, cancellation and SourceStopped after preparation refuse.
The transient lease is not saved; cold state retains genuine completed history.

Pure contracts cover source drift, missing completion, cancellation, mixed pose,
atomic rejection and future repeat/text-key suffixes. Owned native components
remain separately labelled; these contracts do not establish full audio output,
ordinary campaign saving or retail parity.
