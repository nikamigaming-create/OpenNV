# OpenXR runtime contract

Status: **software/package pass; connected-headset hardware gate pending**.

OpenNV uses Godot's built-in OpenXR path. The runtime is started with
`--xr-mode on` before Godot's `--` separator and receives `--vr` as an OpenNV
argument. Flat mode explicitly uses `--xr-mode off`; both modes load the same
cell manifest and `opennv-sandbox-save/v2` state. The runtime retains a
read-only migration path for v1 saves; every new save is v2 so pool motion and
pocket state share the same persistence boundary.

## Promoted software evidence

- One bounded action set contains seven actions: aim, move, turn, activate,
  fire, save, and haptic.
- The only declared interaction profile is the locally testable Meta/Oculus
  Touch profile. Unavailable hardware profiles are not guessed.
- The rig contains `XROrigin3D`, `XRCamera3D`, left/right `XRController3D`
  trackers, a controller-mounted `Label3D` HUD, and live-runtime-gated
  `OpenXRRenderModelManager` controller geometry.
- World scale is `1.0` Godot unit per metre and XR physics is 90 Hz.
- Controller actions call the same pickup, container, door, fire, inventory,
  objective, and save code exercised by the flat gameplay/cold-reload gate.
- The asset-free exported executable reloads the compiled action map from its
  PCK and passes `opennv-openxr-rig/v1` without initializing a fake XR runtime.

## Deliberately unpromoted

A registry entry or installed OpenXR runtime is not headset evidence. VR is not
`hardwareValidated` until a connected device proves stereo output, head and both
controller poses, action edges, haptics, room-scale collision, readable wrist
HUD, runtime controller models, stable frame pacing, and the complete saloon
route/save result. That run must retain telemetry and a stereo-safe visual
review; a headless node-layout report cannot satisfy it.

The present package target is Windows PCVR. Standalone Quest is a separate
promotion requiring a pinned Android/Quest export toolchain, final-APK native
library inspection, signing identity, first-run asset import, and on-device
performance evidence; the desktop archive does not imply those are complete.
