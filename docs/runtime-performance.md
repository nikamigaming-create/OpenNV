# Runtime performance and CPU scheduling

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

Separate rendering was exercised with actual flat and OpenXR gameplay. This
Godot build reports a render-thread `finalize` error on shutdown in both D3D12
and Vulkan, including the small pixel fixture. The safe renderer shuts that
fixture down cleanly and remains the release default. Engine argument
`--render-thread separate` is available for explicit development comparisons;
it is not a supported-performance claim for another graphics driver or platform.

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
