# OpenXR frame transitions and process exit

The runtime's viewport and current camera must agree on their view count for
every queued draw. A launcher request can arrive while a mono frame is still
queued. The launcher now waits for FramePostDraw before enabling the stereo
viewport and camera, matching the direct startup route's existing boundary.
The launcher's existing pending-launch latch prevents duplicate requests.

At process exit, PrepareNativeOpenXrExit releases the player's raw hand-query
bodies while physics is still alive and disables the tracked input adapter.
Its two callers immediately quit. The last queued frame keeps its stereo
viewport/camera contract. Godot subsequently retires scene, renderer and OpenXR
driver resources in its process cleanup order. Switching a live stereo viewport
to mono and manually uninitializing the interface immediately before quit caused
an additional view-count failure and a 2D/2DArray uniform mismatch.

The first Debug native launcher candidate reaches the owned world and ordinary
menu Quit with exit code zero, readers drained, source owners disposed and zero
view-count/type errors. Selected submitted-eye pixels still contain the world,
hands and weapon. This is a bounded simulator component result; it does not
establish continuous stereo, physical-headset acceptance or frame performance.
The final complete runtime gate passes. Both optimized owned direct and launcher
close checks also pass with zero startup/exit draw errors and unchanged original
checkpoint. Each still reports the engine's single interaction-profile leak.

The retirement script can exercise both direct and launcher entry with an
explicit local simulator runtime:

```powershell
.\scripts\Test-NativeSessionRetirement.ps1 -DataRoot $OwnedData `
  -Checkpoint $PrivateCheckpoint -Cases continue-window-close `
  -OpenXrSimulatorRuntime $SimulatorRuntime -Launcher
```

Omit -Launcher to exercise direct entry. XR simulator cases require D3D12.
The script copies the checkpoint into its temporary private run, observes actual
world readiness and calibration, waits for further drawn frames, then sends an
ordinary close request to the owned window. It retains the actual process
handle/exit code, compares renderer errors immediately before and after close,
restores process environment variables and checks the original checkpoint hash.
Its initial report is marked running so an unsuccessful rerun cannot leave an
older completed report appearing current. Continuous frame recording stays off.

The old optimized executable is a negative fixture: it exits zero and drains
readers but adds two renderer errors during retirement, so the check rejects it.
An XR draw-retirement pass covers that specific boundary only. Reports preserve
startup errors and profile-leak counts and explicitly set overallXrAccepted=false.
The flat Vulkan Continue/main-menu/Continue/quit regression also passes. Its old
player-exists predicate could send Escape during remaining LOD preparation; all
loaded-world cases now wait for both loading owners to clear and further drawn
frames before requesting ordinary input or close.

One OpenXR interaction-profile RID still leaks with either application teardown
order. The pinned Godot c971f93e7 action-map loader checks a local resource array
for RID membership instead of recording created RIDs in the member cleanup
collection; its free routine therefore does not see them. See the pinned
[profile registration](https://github.com/godotengine/godot/blob/c971f93e7/modules/openxr/openxr_interface.cpp#L303-L349)
and [cleanup](https://github.com/godotengine/godot/blob/c971f93e7/modules/openxr/openxr_interface.cpp#L606-L613).
That dependency defect remains visible and open. Do not free engine-owned RIDs
through private memory access, suppress the diagnostic, or describe this repair
as leak-free XR shutdown. The pinned engine also documents process-owned final
[driver cleanup](https://github.com/godotengine/godot/blob/c971f93e7/modules/openxr/openxr_interface.cpp#L670-L690).
