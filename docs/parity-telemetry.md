# Live parity telemetry

OpenNV exposes an experimental C# telemetry producer with canonical binary
state, a Windows shared-memory transport, exact-byte comparison, typed deltas,
loss-detecting traces, a Godot three-panel view, and divergence-centered video
evidence. The corresponding private retail observer is not yet connected, so
this is infrastructure rather than a claim of live retail parity.

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

## Comparison

Envelope sequence and local time do not participate in exact state equality.
The comparator first requires the same state key, compares complete canonical
state bytes, records the first differing byte, and then expands differences by
field identity. Numeric fields also report an OpenNV-minus-retail delta.

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

The OpenNV producer publishes configuration identity, renderer method, active
CELL identity and reference count, active-camera position, quaternion, FOV,
near plane, and far plane. The live active-CELL observation registry also
publishes every source-discovered reference, every observed runtime presence,
coverage counts, and a deterministic digest of missing identities. Source
discovery is not presentation or parity: an actor reference without a real
runtime actor remains missing.

Actor, bone, animation, package, quest, dialogue, inventory, effect, audio, UI,
material, input, renderer-submission, and final-frame fields must be published
by their authoritative runtime owners. A maintained side list is not an
acceptable denominator.

## Traces and video

Binary `.onvtrace` evidence stores each original packet with an additional
SHA-256 and validates every packet when read. A bounded frame buffer retains
frames before and after the first divergence. The C# evidence writer saves
hash-verified PNG sequences, encodes retail-left/OpenNV-right H.264 with ffmpeg,
validates the result with ffprobe, and emits `parity-clip-report.json` containing
source-frame and video hashes. Video is diagnostic evidence; it does not replace
matched state or exact telemetry.
