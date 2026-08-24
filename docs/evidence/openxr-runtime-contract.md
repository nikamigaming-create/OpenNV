# OpenXR runtime contract

Status: **layout/package pass; repo-local simulator pass; physical-headset gate pending**.

OpenNV uses Godot's built-in OpenXR path. Live XR starts with `--xr-mode on`
before Godot's `--` separator and receives `--vr` as an OpenNV argument. Flat
mode uses `--xr-mode off`. Both load the same owned-data cell manifest, first-
person rig, `GameplaySession`, and `opennv-sandbox-save/v2` state.

## Promoted software evidence

- One action set contains nine actions: aim pose, grip pose, move, turn,
  activate, fire, reload, save, and haptic output.
- Meta/Oculus Touch and the OpenXR 1.1 generic-controller fallback are declared.
  Input meaning comes from each suggested OpenXR component path, never from an
  application action-name guess.
- Each hand has one visible provider: a hash-verified skinned first-person hand
  compiled locally from the player's legal `lefthand1st.nif` or
  `righthand1st.nif`. Separate grip nodes publish hand transforms; separate aim
  nodes own interaction and projectile rays. Runtime controller proxies are not
  rendered over the retail hands.
- The right-hand weapon mount is derived from the retail first-person skeleton's
  `Weapon` frame. The initial 10mm identity, damage, clip, ammunition, model, and
  muzzle position are resolved from the owned master/recipe/model chain.
- World scale is one Godot unit per metre and physics runs at 90 Hz. Eye height
  calibrates to 1.68 metres and is judged against an authored floor only when
  the floor ray agrees with the capsule's supported foot plane; ledge/fall
  distance is not misreported as standing-height error.
- The wrist HUD is attached to the left grip. Locomotion, door activation,
  firing, reload, inventory, objective, pool, and save outcomes use the same
  gameplay state as flat mode.
- `opennv-openxr-rig/v3` is an explicitly layout-only package gate. It proves
  the action/resource hierarchy without pretending that direct diagnostic calls
  are controller input or headset evidence.

The repo-local OpenXR simulator gate is a higher evidence tier. It launches the
real Vulkan/OpenXR runtime with an isolated simulator data directory and drives
actual tracked poses and suggested bindings. The accepted 2026-08-24 run proves:

| Contract | Result |
| --- | --- |
| Touch profile / tracking | both active and tracked for more than 400 frames |
| left/right hand travel | at least 1.57 m / 1.49 m |
| left-stick locomotion | at least 1.40 m |
| right-stick snap turn | two turns; 0.000000 m maximum HMD pivot error |
| supported eye height | 0.0200 m maximum error against 1.680 m target |
| squeeze / trigger / B / X | door open / fire / reload / save accepted |
| presentation | both retail hands, 10mm, muzzle feedback, and wrist HUD present |
| process control | no Windows app control or foreground input injection |

The native stereo projection, engine report, save, logs, owned-data validation,
and simulator/runtime/input hashes are retained by
`scripts/Test-OpenXrSimulatorControls.ps1`. Godot 4.7.1
still emits two upstream teardown-only diagnostics after a successful explicit
OpenXR uninitialization; the driver allowlists only those exact known issues and
rejects any other engine error:
[interaction-profile RID lifecycle](https://github.com/godotengine/godot/issues/122239)
and [spatial-discovery signal disconnect](https://github.com/godotengine/godot/issues/122238).

Flat mode has its own blocking `opennv-flat-controls-acceptance/v1` gate. It
feeds configured physical key/mouse events through Godot's InputMap and proves
mouse capture/look, WASD movement, E door activation, left-click 10mm fire, R
reload, F5 save, both compiled hand resources, weapon feedback, and desktop HUD.

## Deliberately unpromoted

A simulator, registry entry, or installed OpenXR runtime is not headset
evidence. `hardwareHeadsetValidated` remains false until a connected device
proves stereo final-eye output, head and both controller motion, comfort and
room-scale collision, all controller edges, haptics, readable wrist HUD, stable
frame pacing, and the same door/fire/reload/save route. That run must retain a
stereo recording and telemetry.

The package target is Windows PCVR. Standalone Quest requires a pinned Android
export, final-APK native-library inspection, signing identity, first-run owned-
asset import, and on-device performance evidence; the desktop archive does not
claim those gates.
