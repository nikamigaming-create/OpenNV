# Runtime performance and CPU scheduling

Content preparation sizes its concurrency from the processors available to the
process and the memory limit reported by .NET. The budget is one quarter of
available logical processors, clamped to one through four workers. A memory
limit below 8 GiB caps it at two. This is a scheduling policy, not a hardware
minimum or a promise that a particular frame rate is supported.

Exterior terrain/reference preparation and distant LOD share one admission
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
