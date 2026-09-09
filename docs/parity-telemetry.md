# Live parity telemetry

OpenNV exposes an experimental C# telemetry producer with canonical binary
state, a Windows shared-memory transport, exact-byte comparison, typed deltas,
loss-detecting traces, a Godot three-panel view, and divergence-centered video
evidence. The reviewed private retail observer now publishes a live engine
timer, active CELL identity and attach state, player identity, and normalized
player position through the public packet ingress. Event-boundary identity,
complete gameplay fields and final-frame identity remain unconnected. A separate
live drive harness now sends ordinary input to each engine and presents native
frames; matched simulation and draw correspondence remain unestablished.

## Safety boundary

The retail producer is an observe-only private tool. It may publish neutral
measurements but may not modify retail state, inject input, ship target
addresses, or enter the public repository. OpenNV never consumes retail state as
gameplay authority.

The separate private diagnostic input adapter supplies native device input.
The CLI harness can duplicate key and relative mouse input, while a person can
take priority and release all controls. It does not write quests, actor poses,
camera state or other gameplay outcomes. Native input receipts mean delivery;
subsequent engine state and pixels establish what actually happened.

The live drive view uses bounded latest-frame mailboxes and revision waits.
It records preview replacements and rejects stale captures. This responsive
view does not retain every rendered frame and must not be described as a
lossless event or frame trace. The exact comparison protocol below remains a
separate lane.

## Packet v1

Every integer is little-endian. The packet envelope is:

| Offset | Bytes | Meaning |
|---:|---:|---|
| 0 | 8 | ASCII `ONVPTL01` |
| 8 | 2 | protocol version, currently `1` |
| 10 | 1 | engine: retail `1`, OpenNV `2` |
| 11 | 1 | reserved zero |
| 12 | 8 | producer sequence |
| 20 | 8 | signed simulation tick |
| 28 | 8 | signed monotonic nanoseconds |
| 36 | 8 | semantic event ordinal |
| 44 | 4 | canonical-state byte length |
| 48 | 32 | SHA-256 of canonical-state bytes |
| 80 | variable | canonical state |

Canonical state begins with a two-byte UTF-8 state-key length, the state key, a
two-byte field count, then fields ordered by category and stable ID. A field is
category `u16`, kind `u8`, reserved zero `u8`, stable ID `u64`, byte length
`i32`, and exact value bytes. Duplicate identities, invalid UTF-8, noncanonical
order, bad hashes, trailing bytes, and malformed numeric widths fail closed.

A stable ID is the first little-endian `u64` of SHA-256 over the UTF-8 field
name. Field names therefore remain implementation-neutral while packets remain
compact.

Value kind `6` is Float32: exactly four original IEEE-754 little-endian bytes.
The retail JSON ingress represents this kind as eight hexadecimal digits in
memory byte order (`0000803F` for 1.0). It never converts these fields through
decimal text, Float64, or unit scaling. Signed zero and NaN payloads survive
the packet round trip. Existing normalized-coordinate fields remain separate;
the observer also publishes raw source positions and rotations.

## Comparison

Envelope sequence and local time do not participate in exact state equality.
The comparator requires the same state key and the same nonzero observed event
identity, compares complete canonical
state bytes, records the first differing byte, and then expands differences by
field identity. Each difference retains both value kinds and full value bytes
as hex, including missing fields. Numeric deltas are additional diagnostics;
numeric equality never overrides different bits. Unknown or different event
identity stays unaligned even if the sampled bytes agree.

State keys and event ordinals must identify equivalent authored state. A camera
frame from one dialogue node is not comparable with a nearby frame from another
node merely because their wall-clock timestamps are close.

## Live transport

`--parity-channel <name>` enables the OpenNV physics-frame producer. The named
Windows mapping is `Local\OpenNV.Parity.<name>`. It uses 128 one-megabyte slots
by default, keeps a monotonically increasing ring sequence, and commits the
payload before publishing the new sequence. Readers request every sequence in
order. If a requested sequence has already been overwritten, the read fails;
telemetry loss is never reported as parity.

Adding `--parity-capture <new-private-directory>` samples at the render
boundaries instead. Each native viewport readback is saved as unchanged
`.pixels` bytes plus a PNG preview, with separate before/after `.onvpacket`
files. `frames.jsonl` records the native format, dimensions, source draw count,
timestamps, hashes, and whether the observed state changed across the draw.
This mode requires a rendering display and records every observed draw until
the runtime exits. The directory is temporary: inspect the selected frames
during the check; disposal or a capture failure removes them. Readback and
disk writes affect performance; their run is
not an uninstrumented frame-timing measurement. No game input is generated.
The native Godot viewport was observed to return RGB8. Capture preserves that
format instead of manufacturing an alpha channel. Retail-frame correspondence
remains explicitly unobserved.

Frame recording is off by default. Use `record.start` or **Record ON** only
for the specific check being performed. `record.stop`, **Record OFF / discard**,
stream failure and closing the harness finalize the writer and delete its
temporary frames, indexes and timeline. To keep a video, use `record.export`
with the SBS configuration while recording: it finalizes, exports the requested
interval, then deletes the temporary recording in a finally block, including
when export fails. Save the video outside the temporary recording directory.
Do not stop and expect the discarded frames to remain available for export.
No duration quota or periodic recording is used. Captures are development
scratch data; retain only a requested deliverable or a selected diagnostic
that is still needed, and clean it up when the check is finished.

The OpenNV producer publishes configuration identity, renderer method,
authoritative current CELL identity and reference count, player-root position
and quaternion, and active-camera position, quaternion, FOV, near plane, and
far plane. Door streaming commits the new C# CELL owner before subsequent
telemetry capture; the startup CELL is not reused as active state. The live
active-CELL observation registry also
publishes every source-discovered reference, every observed runtime presence,
coverage counts, and a deterministic digest of missing identities. Source
discovery is not presentation or parity: an actor reference without a real
runtime actor remains missing.

Actor, bone, animation, package, quest, dialogue, inventory, effect, audio, UI,
material, input, renderer-submission, and final-frame fields must be published
by their authoritative runtime owners. A maintained side list is not an
acceptable denominator.

The private observer sends one strict JSON snapshot per line to
`OpenNV.ParityRetailPublisher`. The public ingress accepts only neutral field
names and values, assigns the retail producer sequence itself, encodes packet
v1, and publishes it to the same loss-detecting ring. Unknown JSON properties
fail closed so private addresses, process handles, and observer-specific layout
data cannot accidentally cross into the public protocol.

`OpenNV.ParityLiveComparator` opens distinct fresh retail and OpenNV channels,
reads every ring sequence in order, verifies each packet's engine and producer
sequence, and joins frames FIFO by exact `(state key, event ordinal)`. A ring
overrun, producer gap, wrong engine, or bounded unmatched-state overflow fails
closed. Each candidate pair receives the canonical exact comparison and may be
written with both original packet traces and a v2 JSON report containing all
field deltas. Partial reports and traces are retained on timeout or failure.
FIFO candidate pairing is not proof of simulation or final-frame alignment.

```powershell
dotnet run --project .\runtime\tools\ParityLiveComparator\ParityLiveComparator.csproj -c Release -- `
  --retail-channel fnv_retail_01 --opennv-channel opennv_01 --pairs 120 `
  --output D:\private-proof\matched-run-01
```

Start the comparator before either producer and use new channel names for each
run. The current retail event ordinal is deliberately zero because the
authoritative event boundary has not been recovered. A joined zero-ordinal CELL
sample proves live transport and state-key connectivity only, not event parity.
The observer can retain its neutral input stream with `-OutputDirectory
<new-private-directory>`. It records the beginning and end of each memory-read
interval and a second engine-timer observation; it does not claim an atomic
snapshot or events that occurred between samples.

## Traces and video

Binary `.onvtrace` evidence stores each original packet with an additional
SHA-256 and validates every packet when read. A bounded frame buffer retains
frames before and after the first divergence. The C# evidence writer saves
hash-verified PNG sequences, encodes retail-left/OpenNV-right H.264 with ffmpeg,
validates the result with ffprobe, and emits `parity-clip-report.json` containing
source-frame and video hashes. Video is diagnostic evidence; it does not replace
matched state or exact telemetry.

The dashboard independently compares every byte of equal-size RGB8 or RGBA8
readbacks, including RGBA alpha. It does not resize, align, recolor, or threshold
the inputs. State and pixel results are shown separately; sampled telemetry
equality cannot turn a different image into an exact result. The dashboard
still has no live retail-frame feed or proven final-frame correspondence.

## Switchable source-to-render inspection

The CLI live harness supports `trace` with `target: "opennv"` and `enabled: true`
or `false`, `trace.capture`, `trace.inspect`, and `trace.compare`. Tracing is off
by default. Enabling attaches source-read observers and schedules one capture;
disabling detaches them and clears queued source events. Normal reads do not
hash or copy trace payloads while tracing is off.

Each private trace contains source disk extents and decoded payloads, winning
and observed record ranges, NIF block ranges, scene transforms and projections,
mesh buffers, skin binds and bones, materials, shader programs and uniforms,
texture readbacks, viewport images, and gameplay state before and after draw.
Immutable SHA-256-addressed blobs preserve their exact bytes. Retained source
resources are identified as current observations, not a reconstructed history.
The bounded source-event queue reports overflow explicitly.

An explicit trace also arms one compositor observation. It retains the actual
submitted image-space pass order, view and draw counters, sampler-resource
links, exact push-constant bytes, compute program and readable GPU destination
surfaces. Readback runs together on the render thread after drawing; there is
no intermediate GPU readback on ordinary frames. Each surface identifies its
last writer and allocation dimensions, format, mip count and usage. A later
write to an input resource must not be mistaken for the earlier input pixels.
Unreadable destinations and extent mismatches remain in the report and live
trace status. The final engine scene target can lack readback usage; the native
viewport capture remains a separate output lane. Submission/readback evidence
does not establish native per-draw execution or a retail/viewport frame join.

The inspector follows node-to-resource-to-source links. Clicking a viewport
lists projected bounding-box candidates, clipping edges at the camera near
plane so surrounding room geometry remains selectable. It does not determine occlusion,
alpha contribution, skinned coverage or exact pixel provenance. The byte
comparator validates each blob's length and hash before reporting the first
different byte, total differences and a surrounding hex window.

Two live captures and observer detachment were exercised in paired run40.
Collection is expensive and intended for stopped discrepancy investigation.
Native GPU draw execution, exact per-pixel contributors, matched retail frame
joins and complete audio events remain missing evidence. Do not treat a
populated inspector or byte-equal source record as visual parity.

The native trace additionally captures MultiMesh instance buffers, live particle
state, controller clocks, CanvasItem properties, audio voice state and bus
routing/effects. Explicit captures install non-modifying audio taps and drain
them independently of the main thread. Float32 stereo samples keep their exact
bits; pushed, discarded and retained counts are independent fields. A gap or
zero observed samples stays a failure. Bus samples are before bus gain and are
not proof of physical endpoint output or per-voice sample contribution. Taps
are removed on completion, failure or cancellation. Traces use owned temporary
directories and are discarded when tracing is disabled; copy only a selected
diagnostic that is still needed before disabling.

Every NIF/KF block can expose its reader field map with absolute decoded-resource
offset, extent and storage encoding. The map preserves skipped ranges and the
unread suffix after a decode failure. Cached declarations do not manufacture
new read events. Consuming bytes proves a format read, not that every field has
a correct runtime effect; that later relationship remains a separate gap.

## Process audio and retail world observation

The private Win32 observe adapter now traverses the retail world root and its
native type ancestry, children, transforms, controller clocks, fade-reference
links, geometry headers and property identities. Separate audio-manager reads
retain voice resource paths, flags, gain, rate and placement. These are bounded
sequential read intervals. They do not freeze the game, collect intervening
events or imply that resident geometry contributed to a draw. The menu and
first-person roots are not covered by a world-root walk. Private addresses and
raw observations remain outside public repository inputs and packet ingress.

The development harness also supports a finite Windows process-loopback capture:

```powershell
dotnet tools/OpenNV.LiveHarness/bin/Release/net8.0-windows/OpenNV.LiveHarness.dll `
  --audio <process-id> <milliseconds> <new-absolute-private-directory>
```

This includes only that process tree's audio, never microphone input. The
report retains unmodified API packet bytes, source format, packet flags, byte
extents, QPC timestamps, waveform and hash. API-declared silence is identified
separately from copied packet data. Where the virtual client does not provide
a native format or device sample counter, the requested float32 format and
missing counter are explicit. Windows conversion may occur; the result is not
claimed as original decoder bytes or physical endpoint bits. Use
`--discard-capture <directory>` to remove a completed diagnostic through the
capture ownership/link checks. Failed captures clean themselves up.

Selected exterior observations established audible retail output and a silent
OpenNV output interval. Source model-event playback subsequently produced
nonzero OpenNV output at the same saved position. Neither observation was
matched to retail simulation time; these are coverage findings, not waveform
parity scores. Reference identity,
source reads, node counts, packet continuity and exact sample retention remain
independent from gameplay, event, frame and final-output parity.

## Source model sound events

The native controller owner retains each sequence's original text keys and
publishes crossed events in source time and original same-time order. Whole
loop crossings include the old endpoint before the new start; a seek changes
the cursor without replaying past sounds. Instances have independent clocks.
The reference world owns and saves the sound selection random state. Winning
sound names are indexed once per plugin stack, with ambiguity left as failure.

`Sound:` events resolve the source SOUN and optional named NIF emitter.
`Enum: StopSounds` addresses voices on that emitter. WAV sustain loops use the
source sample bounds. Fast envelope release jumps to the release section;
slow release disables looping and continues from the current sample. Each
looping voice has its own stream state, so one release cannot alter another
instance or the decoder cache. This follows the authored loop modes described
in the [GECK sound documentation](https://geckwiki.com/index.php/Sound);
native mixer timing, resampling and matched waveform output remain unverified.

Explicit traces retain the event observations, source SOUN record bytes,
selected media identity, runtime voice and bus output. Unsupported text keys
and acoustic lanes stay in the owner's divergence set after later events.
Currently unresolved cases include autonomous random scheduling, regional
ambience, placed emitters, reverb, authoritative listener submersion and
complete voice-to-output-sample attribution. This does not certify actor
animation, combat audio, every sound flag or retail event timing.

The live harness also counts failed state publications. A Windows reader
which denies replacement sharing can block a snapshot; the next successful
publication retains the failure count and last error. It does not silently
turn that gap into a successful observation or stop ordinary key-lease expiry.
