# Current work

## Objective and boundaries

Complete the ordinary New Vegas opening through Doc, original character
creation, Vigor and psychology, the Pip-Boy handoff, leaving the house and
Sunny's completed tutorial, then verify cold Continue. Preserve the broader
NV/FO3/TTW objective in architecture.md. Owned files and shared C# gameplay own
state; Godot presents it. No replacement menus, named scene fixes or captured
reference values may become gameplay authority.

Work stays on main and in this task only. All three subagents are stopped at
the user's request. Do not restart delegation. Preserve unrelated work.
All 33 requirements in recovery-checklist.md remain open; scene-defects.md
tracks component discrepancies. Component results do not establish acceptance.

## Current verified state

- Ordinary New reaches the original name prompt, source night hour and
  Reflectron. Original XML, bitmap controls, dynamic FaceGen, source hair and
  voice/LIP are connected. The user accepted the current hair/age appearance
  for advancing. Preserve that improvement.
- Source root-placement correction removes the obstructing room module;
  19 selected native transforms agree. Clothing retains its own texture, and
  source opacity removes the gurney's black fill. Complete geometry, alpha,
  camera, shadows and collision still require matching.
- Original HUD gift icon, bracket, bitmap font and item text render during
  Classic Pack. Shared startup scripts produce 19 inventory grants. Radio,
  exact queue order, fading, glow, wrapping and loading transitions remain.
- Shared source quest clocks retain Float32 recurrence, overshoot, script
  identity and pre-New lifetime. All 252 selected initial native countdowns
  match exact bits. Source stage-global SETs publish the authored night hour.
  MenuMode execution, exact block admission, mutable delays, dynamic quest
  start/stop and complete result scripts remain unbound. See
  quest-script-timing.md; do not reopen established initialization without
  contradictory evidence. ForceWeather remains unbound.
- Geometry colour buffers enable the source vertex-colour binding. CELL
  directional rotations now reproduce the native emitted-ray axis. All
  25 selected light RGB/radius/dimmer inputs agree at the night checkpoint.
  Point-light creation preserves source radius without the unsupported 41
  percent expansion. Source colours, placement and HDR settings stay intact.
- Lit-material fog now uses projected vertex distance and vertex interpolation,
  with explicit game-unit conversion. Owned shader declarations and native
  fog inputs corroborate this correction. A GPU audit covers perspective and
  orthographic projection, both renderer clip conventions and unit scales.
  Fog alone changes the checked room regions only slightly; it does not close
  atmosphere or pixel acceptance.
- No-lighting angular opacity now uses the source smooth curve at each vertex,
  then interpolates it across the surface. Ten owned shader variants support
  this contract. Synthetic/GPU and selected owned model audits pass. In the
  room-62 to room-63 diagnostic, beam error falls from 33.97 to 14.79 and wall
  error from 19.91 to 5.11 colour levels; ceiling error increases from 13.25
  to 15.02. These are regional diagnostics, not aligned pixel acceptance.
- Trace inventory now discovers declared mesh instance parameters instead of
  enumerating selected names. Room-63 exposes 675 lit instances: 674 receive
  source fog, while a later ANIO attachment has zero defaults. A general cell
  environment owner now binds existing and newly attached geometry, follows
  cell transfers, isolates preview viewports and unsubscribes on removal.
  The GPU lifecycle audit passes. Ordinary room-64 now binds source fog inputs
  exactly on all 675 declared instances, including the late attachment. Actor
  skeleton and ANIO roots retain their source model provenance; both missing
  source-model trace entries are resolved. Trace errors and lost events are zero.
- Four source response gestures, finite release/resumption, chair exit/NAVM
  travel, IDLE repeats, KF sound dispatch, preview blink lifecycle and all eight
  original Vigor controls pass component audits. Complete ordinary movement,
  dialogue overlap, cigarette timing/smoke, audio and Vigor framing remain open.
- Source image programs, blur kernels, shared-clock double vision and the
  separate original menu-background effect are connected. Full opening haze,
  focus/DOF and matched GPU output remain unverified. Save v10 retains source
  global/calendar, script/inventory and sky/climate identity owners; complete
  cold progression and exterior weather remain open.

## Live comparison and evidence

The retained retail room-54 and fresh OpenNV room-64 processes are at VCG01
stage 10 / original name entry. Use the room-64 session configuration and
revalidate processes and state before control. Keep one instance of each game.
A shared quest/menu checkpoint is not exact camera, clock or animation phase.

The public harness rejects native.click callbacks and activates retail buttons
only through observed keyboard selection and Enter. Requested tap duration
reaches both local leases; a live 25 ms check passes. Use the private diagnostic
input bridge for ordinary input, skip the cinematic with a short Escape, and
stop on the first discrepancy. Never use OS/Computer Use input, forced stages,
menu-state writes, clock edits or teleports. Native observation uses the Win32
Ghidrust MCP in observe mode; helper calls are limited to attach/modules/read/
detach. Retail observations never become gameplay authority.

Use the harness state command or shared reads compatible with atomic file
replacement. Parse and select fields from large trace/state files; do not dump
entire JSON lines. Keep comparison hidden during implementation and open it
only for bounded checks. Record both native buffers and states at a checkpoint.

Switchable tracing links source ranges, decoded resources, meshes/bones,
materials, submitted image-space passes and viewport captures. Ten GPU surfaces
and constants for eleven passes place the remaining brightness discrepancy
before HDR. Selected native target/cinematic/tint/fade bytes agree with the
submitted constants. Unreadable destinations remain explicit. Native GPU
execution, per-pixel contribution, complete audio/events and final-frame joins
remain incomplete. Keep tracing off outside bounded evidence. All private
captures, native addresses and decoded resources stay outside the repository.

## Next owners

1. Preserve the source colour, radius, direction, fog and angular-opacity fixes.
   Remaining window composition needs the active native no-lighting shader and
   fog/blend toggles. The observed world camera far plane is 5000 game units;
   OpenNV still uses 200 metres. Bind its source owner before changing it.
   Light/shadow selection, fog toggles and ceiling material response remain;
   do not fit HDR or authored colours to the image.
2. Resume ordinary input through original creation and Vigor. Capture the HUD,
   full Doc motion/audio, cigarette timing, gurney transparency, opening haze
   and Reflectron preview/background at matching states. Fix the first failed
   owner and replay that boundary. Do not replay the already exercised movie.
3. Replace remaining psychology/tag/trait/farewell panels through original
   source owners. Complete Pip-Boy, room loot/skull/pool/physics, doors/exterior,
   Sunny dialogue/combat/tutorial and cold Continue. Six actor audit shutdown
   resources, broader gameplay/plugin behavior and integrated OpenXR remain.

## Required publication gate

The full gate and selected owned/GPU material checks passed on 2026-09-05.
Re-run when code changes require it, plus the selected owned-data audit:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

The optional NativeVertexFogAudit scene requires the normal Forward+ renderer;
the headless dummy renderer cannot create its local GPU device. See
material-fog-and-falloff.md for this bounded audit and remaining evidence lanes.
Read architecture.md, status.md, clean-room.md and parity-telemetry.md alongside
this file. A build or plausible frame is not gameplay acceptance.
