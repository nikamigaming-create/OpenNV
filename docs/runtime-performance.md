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

Windows is the measured platform. Synthetic budget tests cover small, large and
memory-constrained process limits. Physical-headset timing, other operating
systems, integrated GPUs and constrained machines remain unverified.

September 20 measurements on an i7-14700F / RTX 4070 SUPER / 32-GiB Windows host:

| Selected sample | FPS | Median / p95 frame interval |
| --- | ---: | ---: |
| Flat stationary baseline, prior runtime | 15 | 57.93 / 80.61 ms |
| Same flat checkpoint, current safe renderer | 60 | 16.67 / 17.60 ms |
| Current OpenXR simulator, safe renderer | 45 | 22.21 / 28.23 ms |
| Intermediate OpenXR build, separate renderer | 83 | 11.21 / 20.08 ms |

These samples keep recording/builds off. Other GPU applications were active;
the host was not isolated. The XR comparison is not an exact build A/B and the
simulator does not establish physical-headset timing. A flat cell crossing
reached 175.25 ms for one upload and 69.57 ms for commit before returning to
60 FPS. Streaming stutter remains a release limitation.
