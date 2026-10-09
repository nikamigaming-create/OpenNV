# Recorded input and repeatable comparison

The native diagnostic owner records physical keyboard/mouse input and delivered
harness input to a private JSONL segment. It obtains a complete checkpoint from
the shared campaign save owner before recording. Active procedures or result
continuations without a save owner still refuse; a tape cannot replace them.

`input.record.start` accepts an absolute, new `.jsonl` path. Its receipt and
live-state `checkpoint.slot` identify the captured save. `input.record.stop`
releases held input and closes the segment. Keyboard renewals preserve holds
longer than one lease. Physical input, bot steering and observed button clicks
use the same ordinary adapters. Simulator steering without a recorded adapter
ends the recording with a visible failure. Frames remain off.

The versioned header binds checkpoint SHA256, initial scene and ordered plugin
names/hashes. Each delivered input retains an ordinal, relative monotonic clock
and observed scene. The footer binds the complete journal digest, duration and
input count. Duplicate/unknown fields, corrupt or truncated journals, abandoned
recordings and gameplay-edit/console operations are refused before playback.
Rejected recording commands fail the segment rather than silently omitting an
action. The format can describe retail input, but this change does not establish
a delivered retail recorder, matched checkpoint or game-wide coverage.

Replay requires a cold checkpoint load. Intervening physical or harness input
invalidates that preparation. The runtime validates the entire tape and source
binding before input. Every action checks its recorded scene and timing budget;
button playback also checks the currently visible caption/path and clickable
viewport center. Wrong scenes, late delivery, unavailable controls, player
intervention, stop requests and retiring owners stop playback and release input.
The cursor advances only after successful dispatch.

Run a segment against an already running native session with:

```powershell
.\scripts\Invoke-RecordedInputReplay.ps1 `
    -CommandDirectory '<private native input directory>' `
    -InputTape '<private absolute segment.jsonl>' `
    -CheckpointId '<captured save-slot GUID>'
```

The command cold-loads the actual slot, waits for its restored owner, runs timed
input, checks fresh process/state evidence and returns completion or the stopped
instruction. Publication is tied to the replay-start request so an older completed
snapshot cannot satisfy a new run. Timeout/failure sends the existing stop request.
Delivery completion establishes an input run only. Resulting source outcomes,
cold state and retail comparison require their own evidence.

The live parity comparator accepts `--tolerances <private-json>` with an array
of `{ "category": "Camera", "stableId": 1, "maximumAbsoluteDelta": 0.001 }`.
Rules apply only to explicitly named, same-type Float32/Float64 fields. Reports
retain exact-byte equality, both byte strings and every delta independently of
the tolerance assessment. Missing fields, integer outcomes, changed types and
unobserved event alignment cannot pass a tolerance. No rules means exact fields.

Synthetic contracts cover timing, binding, rejected delivery, integrity and raw
delta retention. The native Godot regression exercises physical holds, renewal,
observed button activation, recorder save refusal, playback and owner retirement.
It can use a selected owned plugin stack, while its checkpoint callback remains
an explicitly synthetic save-owner fixture. These checks establish the input
capability, not a campaign checkpoint, retail parity or the requested journey.

## Retail input into the native runtime

Build `tools/OpenNV.LiveHarness` and run `--record-retail-input <configuration.json>`
against the existing private native device adapter. The configuration supplies
`retailCommandDirectory`, `retailProcessId`, `retailExecutablePath`, `actionsPath`,
`inputTapePath` and `receiptJournalPath`. Use `actionsPath: "-"` for ordinary JSON
input on stdin. A file contains `{ "microseconds": 0, "input": { ... } }` rows.

Physical input includes `key` with `key`, `pressed`, `leaseMilliseconds`; `look`
with integral `dx` and `dy`; and `mouse` with `button`, `pressed`,
`leaseMilliseconds`. Buttons are Left, Right, Middle, Xbutton1 and Xbutton2.
Leases are 20–1000 milliseconds; renew a held input before it expires. EOF releases
held inputs. The standard keyboard includes letters, digits, modifiers, function
keys, navigation and keypad keys. Both `3` and `Key3`, and Ctrl/Control, address
the same physical key.

Use `--replay-input <configuration.json>` with `openNvCommandDirectory`,
`openNvProcessId`, `inputTapePath`, `transportJournalPath` and
`deliveryJournalPath`. The command selects the tape's mode automatically. A bound
tape also needs `checkpointId`; an unjoined retail tape plays from the game's
current state and does not restore or manufacture a checkpoint. Both use ordinary
Godot input. File validation and actual delivery errors remain visible. Frame
recording is independent and stays off.
