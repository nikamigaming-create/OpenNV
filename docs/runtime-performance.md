# Runtime performance and CPU scheduling

## October 18 source inflation hotspot

Resolved managed stacks from the native appearance fixture place roughly 2.0
seconds beneath strict DEFLATE extent validation while publishing two NPCs'
materials. The validator decoded each Huffman symbol one bit at a time before
framework inflation. This is source-read work on the scene thread, not measured
GPU cost. Short-lived traces without resolved stacks were discarded as evidence.
The optional `--appearance-profile` audit mode keeps the host alive for thirty
seconds after the same audited work, allowing symbol rundown before normal exit.
It neither records frames nor changes gameplay.

The validator now resolves codes up to nine bits with a bounded prefix table and
reads bit fields from byte windows. It advances only the actual symbol length.
Long codes, incomplete prefixes and short tails retain the canonical reader;
final bit/byte extents, reserved symbols, output/window bounds, zlib envelopes
and the distinct record/archive checksum policies remain unchanged. Independent
vectors cover code lengths through fifteen for literals and distances at all
byte alignments, every truncation of the long-literal frames, fixed/stored/mixed
blocks and generated streams across framework compression levels. Both original
record and BSA readers decode those vectors, including their existing refusals.

A private Release helper compares 23,200 deterministic generated, mutated and
truncated cases with main 8448d81's validator. Admission and exact successful
extents agree. It then reads the largest 32 compressed character DDS members
below four MiB per member from each owned archive: 27,536,230 compressed bytes,
38,452,616 decoded bytes in this installation. Actual old/new archive output is
byte-for-byte equal; input files are opened read-only and no asset data is written.
After warmup, five alternating validation-only passes have medians of 523.77 ms
before and 194.05 ms after. These timings exclude archive IO, framework inflation
and native upload and do not establish whole-game speedup.

The same Debug/Vulkan profiled NPC fixture constructs both selected actors with
exact prepared source vectors, cancellation and abandoned-node checks passing.
The worst part step falls from 751.66 to 223.83 ms. Debug profiling changes cost;
this is supporting hotspot evidence, not an optimized frame benchmark. Material
source preparation and native part publication still need bounded scene work.

Separate optimized Vulkan/Forward+ flat processes continue copies of the same
checkpoint, with builds, bots, recording and detailed snapshots off. Continue
wall time is 30.06 seconds before and 24.46 seconds after. The largest observed
loading draw interval remains 2,457.12 versus 2,408.73 ms. Median sampled settled
rolling p95 is 18.27 versus 17.24 ms; these overlap and are not whole-run or
headset percentiles. Five ordinary shots in the starting view retain no weapon
handling error; first-trigger preparation is 1.02 versus 1.61 ms, already warm.
Later rolling firing windows still reach 134.33 versus 107.64 ms without a cause
established by these samples. This ordered pair does not isolate OS/driver
caching, certify a combat encounter or meet VR timing acceptance. The remaining
loading freeze, explicit saves and physical headset CPU/GPU/missed-frame budgets
remain open.

## October 17 rejected-playtest repair baseline

Fresh optimized package-g and save-repair exports continued separate copies of
the same private FNV save on the RTX 4070 SUPER using Vulkan/Forward+, the safe
renderer and four content workers. Both report JIT optimization enabled. Builds,
bots and frame recording were off during these samples. Official content was
selected; this does not independently establish pristine installation files.

| Selected flat measurement | Package g | Save-repair candidate |
| --- | ---: | ---: |
| Continue button to observed ready, wall seconds | 42.86 | 37.10 |
| Settled median of rolling draw-interval medians, ms | 15.82 | 15.65 |
| Settled median of rolling p95 draw intervals, ms | 28.79 | 26.21 |
| Firing-sequence median of rolling p95, ms | 30.54 | 28.06 |
| F5 following firing | Failed: unowned active audio | Completed with finite audio receipts |

Settled rows use the ten sampled windows after fifteen seconds from readiness.
The windows overlap and are not independent whole-run percentiles. This single
ordered pair does not isolate OS/driver caching or establish a speedup. The
firing sequence followed a detailed state request; its 181-200 ms largest draw
intervals cannot be attributed solely to weapon preparation. Shots were initially
obstructed before the selected creature, so this is firing coverage, not an
accepted encounter benchmark. A completed save adds actual snapshot/write work
that the failed baseline did not perform.

A subsequent ordinary-input bot approach was outside the timing comparison.
During its genuine exterior transitions, streaming's own timers recorded a
203.19 ms maximum upload and an 82.84 ms commit, including 33.86 ms of reference
publication. A later ordinary shot damaged the resident coyote by 14.40 health;
the following F5 retained its injury and active attack state. This shot hit the
torso, not the version-27 qualifying limb-reaction path.

A separate twenty-second managed sample identifies particle publication and
collision work plus substantial native-wrapper finalization. Its all-thread
sample includes sleeping workers; those percentages are not CPU utilization or
GPU timings. Next repairs must measure individual publication/activation steps,
reduce wrapper churn, and bound loading work without deleting source entities,
events, particle behavior or saved state. Physical-headset timing remains open.

A follow-up optimized run isolates first firing without a detailed state request,
build, bot or recording. Preparation takes 90.18 ms: 59.05 ms for muzzle resources
and 21.57 ms for the animation clip. Shot publication takes another 36.10 ms,
including 20.97 ms audio work and 11.19 ms collision work. The first draw window
peaks at 182.97 ms. Four subsequent preparations take 0.54-1.43 ms; their shot
publication takes 2.26-6.43 ms. This is firing into the ordinary starting view,
not a damage/range acceptance encounter. It identifies cold resource work to
move into equipped-resource preparation without selecting an attack variant,
advancing random state, consuming ammunition or starting a voice/effect.

The subsequent optimized repair moves 121.64 ms of selected weapon-resource
preparation into equipment construction before gameplay input. In the same
five-shot protocol, first-trigger preparation is 2.23 ms and shot publication
15.49 ms, including 0.88 ms audio and 11.33 ms collision. The first rolling draw
window peaks at 38.01 ms. Later preparation is 0.65-1.21 ms and publication
2.09-2.85 ms. A later rolling window still contains a 179.35 ms interval;
the small weapon timers do not explain it. This is one ordered pair, not
an encounter or headset-performance pass. Owned preparation checks preserve
ammunition, handling/random state and audio history, with no native voices,
shots, visible muzzle or particle births before input.

Loading now reuses detached incremental NPC assembly, bounds landscape source
reads with the shared worker budget and yields between native publication
steps. One optimized Continue reports 29.52 seconds internally; cell construction
still includes a 1,015.6 ms main-thread step and terrain a 49.8 ms cell. These
remain stalls, and the loading changes do not establish a uniformly responsive
loading screen. Closing during active NPC preparation exits with code zero,
drains source readers, and leaves the original checkpoint unchanged; expected
session cancellation is distinguished from a failed load. This is a scoped
cancellation test, not whole-session resource acceptance.

Repeating the earlier firing/save protocol on this export observes Continue
readiness after 36.30 wall seconds (28.47 seconds inside the load timer). Settled
rolling-window median is 15.98 ms and median p95 is 24.43 ms; firing median p95
is 25.08 ms. These are overlapping windows and the firing sequence still follows
a detailed snapshot and obstructed aiming, so they do not isolate combat speed.
F5 completes, spending 109.03 ms capturing and 121.05 ms writing state, with a
345.79 ms largest save-window interval. Explicit saving remains a measured stall.

Selected native checks use `NativeNifInstanceAudit.tscn --particle-buffer` with
Vulkan (the dummy headless renderer cannot validate GPU buffer readback), and
`NativePlayerPresentationAudit.tscn <owned-Data> <checkpoint> --weapon-preparation`.
The latter covers owned weapons 1735d4 and 08f21e without firing. Source-worker
retirement during initial loading is covered by
`Test-NativeSessionRetirement.ps1 -Cases loading-window-close`; the normal cases
remain the default. Run these alongside the complete runtime gate, with recording
off, and measure optimized exports separately from builds and diagnostic fixtures.

## Embedded script declarations during loading

A later optimized loading trace attributes 8.31 seconds of sampled main-thread
time to `FalloutPluginStack.CountEmbeddedQuestScripts`, called by quest-host
construction. The complete scan is necessary: unknown winning record types can
contain embedded script headers, and unsupported/inactive declarations still
determine script initialization phases. It cannot be replaced by a whitelist of
known supported records.

The candidate warms that same stack-owned count inside the existing indexing
worker, after archive warmup. It does not construct clocks or execute gameplay
there. Session retirement cancels the scan between records, drains its task, then
disposes source handles. Failed and cancelled scans never cache a partial count;
synthetic checks cover unknown winners, deleted overrides, concurrent reuse,
cancelled worker retry, independent clocks and disposed sources.

Fresh optimized flat Continue runs from separate copies of the same checkpoint
sample lightweight rolling draw intervals throughout startup and loading. Builds,
bots, detailed snapshots and recording are off. The largest observed loading
interval falls from 11,863.78 to 3,169.69 ms. Preparing characters and controls
takes 11.92 versus 2.84 seconds. Full Continue wall time is 39.22 versus 36.35
seconds; reading game data grows from 8.32 to 15.39 seconds as the 7.83-second scan
moves there. This is a responsiveness repair, not evidence that the scan's work
disappeared or a controlled total-loading speedup. Both hosts retain seven
embedded declarations, 496 initializations and default delay five; those counts
do not establish gameplay coverage. The remaining three-second stall is open.

The owned `index-window-close` retirement case interrupts the active scan,
publishes no completed index, exits zero with readers drained, and leaves the
original checkpoint unchanged. A subsequent targeted loading trace attributes
approximately 0.94 seconds to activation-parent indexing and 0.93 seconds to
quest-script restore, including approximately 0.89 seconds indexing source radio
stations. Its trace overhead and selected time window are not a
new frame-performance benchmark; they identify the next owners to investigate.

## Current engine and streaming boundary

The selected engine is official Godot 4.8-dev7 Mono
(`4.8.dev7.mono.official.c971f93e7`), with Godot.NET.Sdk 4.8.0-dev.7 and .NET 10.
`runtime/godot-toolchain.json` is the shared engine/archive/SDK pin. Installation
verifies the official archive SHA256; launch and gate scripts reject a stale
engine or SDK mismatch. The 4.8 snapshot is a development release. The earlier
measurements below do not qualify this engine's performance.

4.8 adds GPU-feedback-driven mip-level texture streaming through
StreamedTexture2D and TextureStreaming. The project enables the feature. Its
official public API currently loads imported `.stex` resources; it does not
expose the internal RID/format/reload-callback registration to C# or GDExtension.
NativeDdsTexture continues to upload direct owned DDS bytes, preserving authored
levels and BC1 alpha; NativePartialMipTexture preserves incomplete authored
pyramids. These uploads are resident and are not registered with the new engine
streamer. Enabling the project flag alone does not close that integration.

The live harness reports the actual engine counters for total texture bytes,
streamed texture bytes and the streaming budget's current usage. Zero streamed
bytes must remain visible. A complete owned-data integration still needs actual
camera/GPU demand, bounded asynchronous source reads, compatible texture
replacement, source alpha/mip fidelity, native lifetime and cancellation, and
measured memory/frame behavior. Persistent transformed assets cannot become
launch inputs. C# CELL/reference/LAND streaming remains independent of texture
residency and authoritative gameplay.

The actual 4.8 Vulkan FO3 Debug run uses the RTX 4070 SUPER, 28 available logical
processors and the shared four-worker content budget. The birth-cell sample
reports 1,373,634,048 total texture bytes; the final CG00 stage-100 sample reports
1,679,149,568 bytes. Both report zero streamed and budget-used bytes. The final
live snapshot costs 35.93 ms to construct and 4.74 ms to serialize, with a
selected 512-interval window at 26.99 ms median and 82.94 ms p95. These are
unoptimized Debug diagnostics with the live harness enabled, not Release or
moving-campaign performance. No matched retail or before/after timing claim
follows from them. Recording was off apart from one inspected-and-deleted frame.

Primary references: [4.8 texture-streaming release notes](https://godotengine.org/article/dev-snapshot-godot-4-8-dev-5/),
[TextureStreaming API](https://docs.godotengine.org/en/latest/classes/class_texturestreaming.html),
and [the selected engine's registration implementation](https://github.com/godotengine/godot/blob/c971f93e7/modules/texture_streaming/texture_streaming.cpp).

## Scheduling and previous measurements

Native bot capsule refinement now uses the actor navigation owner's shared
two-millisecond physics-thread budget. Pending requests release ordinary input;
changed player poses, cells, source-scaled bodies and target goals invalidate
obsolete work. Source NAVM loading remains separately measured. This bounds
collision search work, not cell streaming or the complete frame duration.

Source dialogue/head tracking uses resident reference indexes instead of whole
scene searches. Reference-owned base clocks retain independent NPC phases for
shared clips. The selected birthday replay has distinct phases; full rendering
cadence and matched retail timing remain unverified.

Content preparation sizes its concurrency from the processors available to the
process and the memory limit reported by .NET. The budget is one quarter of
available logical processors, clamped to one through four workers. A memory
limit below 8 GiB caps it at two. This is a scheduling policy, not a hardware
minimum or a promise that a particular frame rate is supported.

Exterior terrain/reference/NPC preparation and distant LOD share one admission
limit. The LOD upload queue holds at most twice that worker count and applies
backpressure. Obsolete grid requests and departing scene owners cancel queued
work. Already-running source reads finish at a cancellation boundary. Startup
plugin-header and archive-index scans each use the same bounded budget.

Initial exterior loading, door travel and source MoveTo drain that same LOD
queue at the destination view before releasing gameplay. Selected fine children
cannot be evicted while siblings are still staged behind a coarse parent. The
512 MiB LOD cache target applies to its conservative resource estimate; active
and staged demand can exceed it, while dormant entries are evicted. Terrain and
object reads/coverage remain independent, and camera morph position uses one
shared shader update rather than one call per resident material. These changes
do not establish a frame-rate improvement; whole-model uploads remain indivisible.

Workers read and prepare owned data. The main thread owns scene publication,
gameplay and physics interaction. The operating system and .NET schedule the
jobs across available cores; there are no hard-coded P-core/E-core indices or
affinity masks. GPU selection and renderer support remain engine/platform
decisions. Godot's internal job pool is distinct from the content budget.

Resolved float/integer GMST values belong to the immutable winning plugin stack.
Combat no longer scans source declarations every tick. Exterior weather publishes
shared shader constants; interior/independent viewport environments retain local
instance values. Direct3D 12 and Vulkan pixel checks verify equal local/shared
lighting, weather updates and interior rebinding. HDR texture names are reused.
Diagnostic render timing is collected asynchronously on the render thread.

Separate rendering was exercised with actual flat and OpenXR gameplay on the
earlier engine. That build reports a render-thread `finalize` error on shutdown in both D3D12
and Vulkan, including the small pixel fixture. The safe renderer shuts that
fixture down cleanly and remains the release default. Engine argument
`--render-thread separate` is available for explicit development comparisons;
it is not a supported-performance claim for another graphics driver or platform.
4.8 needs its own repeated native shutdown and frame measurements before changing
the renderer default.

The live harness reports process CPU/memory availability, OS, architecture,
renderer, graphics adapter, content budget, frame intervals and asynchronous
render measurement age/errors. Performance checks keep recording and builds
off. Moving gameplay must be measured separately from loading and stationary
views. Whole-object uploads still can exceed the nominal three-millisecond
publication budget; bounded CPU preparation does not solve that stall by itself.

Actor navigation retains exact source triangle projection but first orders mesh
bounds by their minimum possible distance. Queries stop when remaining bounds
cannot beat the nearest triangle, including deterministic equal-distance ties.
The synthetic selection check agrees with exhaustive per-mesh projection for
400 near/distant queries over stacked floors. One owned world graph contains
4,036 admitted meshes and 589,546 triangles; 40 warm repeated queries on the
selected Primm route measured median 1.81 ms and p95 2.00 ms. Unbound source NAVM
records remain diagnostics; the counts are not world-navigation acceptance.

Native actor capsule searches yield between node expansions under one shared
two-millisecond physics-thread budget; each search admits at most 512 nodes.
The budget can overrun by an expansion, and source graph preparation/projection
is measured separately. Source reads use content workers; native physics queries
remain on their owning thread. A selected simulator route request measured
2.71 ms total, 1.63 ms for the source corridor and a 1.08 ms largest native slice.
Earlier unbounded requests in this encounter measured 52-142 ms. Other actors,
large custom meshes and moving-world contention still need profiling.

Windows is the measured platform. Synthetic budget tests cover small, large and
memory-constrained process limits. Physical-headset timing, other operating
systems, integrated GPUs and constrained machines remain unverified.

September 20 measurements on an i7-14700F / RTX 4070 SUPER / 32-GiB Windows host:

| Selected sample | FPS | Median / p95 frame interval |
| --- | ---: | ---: |
| Flat stationary baseline, prior runtime | 15 | 57.93 / 80.61 ms |
| Same flat checkpoint, current safe renderer | 60 | 16.67 / 17.60 ms |
| Earlier OpenXR simulator sample, safe renderer | 45 | 22.21 / 28.23 ms |
| After navigation changes, second encounter, safe renderer | 82 | 11.30 / 20.03 ms |
| Intermediate OpenXR build, separate renderer | 83 | 11.21 / 20.08 ms |

These samples keep recording/builds off. Other GPU applications were active;
the host was not isolated. The XR comparison is not an exact build A/B and the
simulator does not establish physical-headset timing. A flat cell crossing
reached 175.25 ms for one upload and 69.57 ms for commit before returning to
60 FPS. Streaming stutter remains a release limitation.

## Incremental NPC admission

A subsequent copied-save flat walk isolated a 182.38 ms NPC construction step;
scene collection and disabling took under one millisecond. The same ordinary
movement leases now prepare NIF/EGM/TRI source geometry on the bounded content
pool, then assemble one native skeleton or body part per queue visit. Incomplete
bodies stay detached. Only complete actors receive contacts, AI and residency.
Equipment is checked against current reference state before each step. Grid
cancellation frees unfinished nodes and cancels source work. At most the worker
budget's number of NPC preparations, including completed source results waiting
for publication, can remain in flight.

The selected owned audit compares every prepared geometry/expression vector with
direct source evaluation, checks worker execution and cancellation, rejects
partial publication and verifies abandoned native nodes are freed. Synchronous
construction uses the same assembly owner. This does not establish every actor
or outfit's admission.

| Same checkpoint and movement leases, safe flat renderer | Before | After |
| --- | ---: | ---: |
| Largest streaming upload slice | 183.46 ms | 59.67 ms |
| Largest observed draw interval during the sampled walk | 197.09 ms | 81.33 ms |
| Highest rolling p95 during the sampled walk | 26.15 ms | 33.67 ms |
| Last cell commit | 44.55 ms | 53.50 ms |

Both runs complete the grid with 49 resident cells, 620 retained references and
63 terrain cache cells. Recording and builds were off; host GPU activity and
combat timing were not isolated. The reduced worst stall is accompanied by more
distributed construction work; p95 and commit did not improve in this sample.
Individual armor/material uploads, cell commit and LOD remain performance work.
These finite measurements do not support a claim of consistently smooth VR.

An Elliott Tate simulator run using ordinary thumbstick movement from the same
copied checkpoint completes the same two cell transitions and source NPC body.
Its largest upload slice is 53.79 ms and last commit 46.63 ms. The sampled walk's
largest draw interval is 159.72 ms and highest rolling p95 is 31.23 ms; larger
frame spikes remain outside the upload slice. The final settled sample is
72 FPS with p95 22.07 ms. This is a separate simulator smoke test with a
level head pose, not a matched flat/XR performance comparison or headset result.

## Cell commit ownership

Live-harness-only phase timing separates cell commits into source/world state,
reference admission, terrain, events, observation, lighting, LOD and eviction.
Another ordinary flat walk of the same checkpoint completed both transitions at
49.05 and 60.12 ms. Interaction-binding reconstruction took 10.75 and 15.94 ms;
runtime observation took 4.21 and 14.13 ms. These measurements identify commit
work independently of the preceding upload queue.

Event residency now consumes the presentation owner's existing resident index,
excluding its warm cache, instead of rediscovering every source identity through
native node metadata. Existing bindings retain OnLoad and contact membership;
late materialization subscribes once and reentry restores ordinary activation.
The source observation encoder preserves the canonical length-prefixed UTF-8,
field order and Float32 bits in a single sized buffer. A 512-record synthetic
sample allocates 299,008 bytes versus 593,920 with the previous stream encoder.
Discovery scope replacement also reuses its string prefix.

Native synthetic contacts and owned reference materialization/warm-residency
checks pass, as does the complete runtime gate. A subsequent exported flat walk
from the same checkpoint and look angle completes both transitions at 29.85 and
39.48 ms. The last commit spends 4.91 ms rebuilding bindings and 2.30 ms on
runtime observation, versus 15.94 and 14.13 ms before. It retains the same 49
resident cells, 620 warm references and 63 terrain cells with an empty queue.
The early draw-interval window includes initial Continue loading, so it is not
used for a gameplay frame-time comparison. Commit and individual upload stalls
remain; these samples are not whole-route performance acceptance.

## Weapon action saves

The user's flat playtest log exposed a separate stall: a completed shot invoked
the full campaign writer. Its snapshot took 26.57 ms and validation/serialization/
file publication took 131.16 ms, for a roughly 15 MB save. The same call also ran
after reload, equip and holster animations.

Weapon action completion now leaves updated ammunition, condition and random
state with the shared gameplay owner. Existing explicit, scripted and transition
save actions capture that state. A save is no longer requested by animation
completion. This removes the repeated save trigger; explicit save capture and
writing remain synchronous and can still pause the game.

Fresh ordinary flat and Elliott Tate controller runs each fire four shots and
reload without changing the saved file. Explicit saving persists the final
12-round magazine. Cold Continue in the other mode verifies the magazine,
carried ammunition and exact shot-random state. This is selected
weapon/save evidence, not all-combat or physical-headset acceptance.

## Explosion target lookup

The reported Fat Man landing delay came from synchronous target classification.
Destruction lookup enumerated every ancestor's children for each overlap shape;
the large source blast returned 3,028 shapes belonging to 65 distinct colliders.
Destructible reference roots now register their owner for their tree lifetime.
The blast deduplicates colliders before classification and reuses actor lookup.
Damage targets and the complete overlap query remain intact. Native source car
checks verify lookup before and after wreck replacement and reject an adjacent
player collider as a destruction owner.

An ordinary flat shot from the same copied checkpoint, without recording, spent
1,729.77 ms inside detonation processing before the repair and 128.01 ms after.
The repaired sample spent 10.54 ms querying overlaps, 3.93 ms classifying them,
and 4.03 ms applying blast damage. Explosion model construction still took
91.32 ms. Projectile simulation elapsed 1.978 seconds in both samples; wall time
through completed detonation fell from 3.656 to 2.011 seconds. This separates
the impact stall from projectile travel and does not establish retail parity.

The corresponding recorded flat shot spent 172.19 ms in detonation processing
and 2.435 seconds through the 1.967-second simulated flight. Capture overhead
and effect assembly remain visible; neither game speed nor video speed was
changed to conceal them. Timing telemetry retains the completed projectile and
separates contact, detonation, effect construction, playback and sound phases.

Two exported flat diagnostic runs completed the shot but Windows reported
native heap corruption after the ordinary Quit Game action. The September 30
session repair drains source workers and releases detached model prototypes
before native engine shutdown. Ordinary copied-checkpoint Quit now exits with
code 0; main-menu return, another Continue and fresh-title Quit pass too.
Cell owners retire compositor GPU resources before the engine's rendering
device, and a four-replacement native fixture passes with retained effects.
This addresses the selected shutdown paths; successful shots, captures and
these checks do not establish broad session stability or retail parity. One
repeated reload also crashed during native triangle-mesh construction before
resuming gameplay; a later complete audit passed. The intermittent construction
failure remains open.
