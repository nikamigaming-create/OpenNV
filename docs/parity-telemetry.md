# Live parity telemetry

OpenNV exposes an experimental C# telemetry producer with canonical binary
state, a Windows shared-memory transport, exact-byte comparison, typed deltas,
loss-detecting traces, a Godot three-panel view, and divergence-centered video
evidence. The reviewed private retail observer now publishes a live engine
timer, active CELL identity and attach state, player identity, and normalized
player position through the public packet ingress. Event-boundary identity,
complete gameplay fields, matched input, and final-frame identity remain
unconnected, so this is diagnostic infrastructure rather than a parity claim.

## Safety boundary

The retail producer is an observe-only private tool. It may publish neutral
measurements but may not modify retail state, inject input, ship target
addresses, or enter the public repository. OpenNV never consumes retail state as
gameplay authority.

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
the runtime exits. Readback and disk writes affect performance; their run is
not an uninstrumented frame-timing measurement. No game input is generated.
The native Godot viewport was observed to return RGB8. Capture preserves that
format instead of manufacturing an alpha channel. Retail-frame correspondence
remains explicitly unobserved.

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
