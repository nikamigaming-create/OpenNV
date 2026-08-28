# Fallout 1 Classic Diorama presentation contract

Status: **interactive camera presentation proof over an owned donor CELL; Et Tu
turn simulation is not connected**.

`Classic Diorama` is a flat-screen presentation adapter over the same OpenNV
CELL root and `GameplaySession` used by the existing first-person and OpenXR
paths. It does not create proxy scenery or a second gameplay state.

## Promoted software behavior

- `Camera3D` with orthographic projection.
- Initial orthographic size: `18.0` metres.
- Bounded zoom range: `6.0` through `64.0` metres.
- WASD camera-relative pan at `7.5` metres per second at the initial zoom.
- `Q` / `E` rotate the view in exact `60` degree increments.
- Mouse wheel zooms; `Home` resets pan and zoom; `F5` uses the same save owner.
- Initial framing is derived from the complete transformed CELL mesh bounds,
  not from a hand-authored camera target. The authored XCLL fog color and power
  remain intact while the depth envelope expands deterministically from camera
  distance and cell span so an orthographic camera outside the first-person fog
  range can still see the CELL.
- The tactical adapter raises ambient energy by a fixed `1.25` multiplier and
  adds one camera-aligned, non-shadowing fill derived from the authored CELL
  ambient color. This is an explicit presentation light, not retail lighting
  parity and not a replacement material.
- The classic adapter and OpenXR adapter are mutually exclusive presentation
  modes over one gameplay/session owner.
- A no-retail software gate constructs the actual camera hierarchy, injects the
  real rotation and zoom input events, and verifies the shared save schema.
- Native UI captures retain the standard `0.05` luminance-deviation gate.
  Environment-only diorama captures use an explicit `0.035` deviation gate
  because the wide dark cave composition has less histogram spread; mean
  luminance must still exceed `0.035` and dark pixels must remain below `60%`.
- Interactive launches create the owned-data loading layer before verification,
  keep it visible for at least `0.85` seconds, and dismiss it only after the
  verified CELL returns. Automated capture launches bypass the minimum delay so
  the loading UI cannot contaminate evidence frames.

## Explicitly unsupported

- Et Tu turn order, AP, combat, RNG, hex selection, pathfinding, AI, or scripts.
- Fallout 1 actor/object placement in the donor cave.
- final UI art, target outlines, wall cutaways, roof hiding, or cinematic cuts.
- tabletop OpenXR scale/interaction.
- retail parity, headset acceptance, or package promotion for this mode.

The runnable `SLGoodspringsCaveINT` smoke proves only that the presentation can
load and navigate a deterministic owned FNV CELL. `V13ENT.MAP` remains the
authoritative future placement source. No donor CELL placement is presented as
Fallout 1 content.

## Native donor-CELL evidence

Private capture: `classic-diorama-capture-20260822-r6`.

- schema/status: `opennv-classic-diorama-capture/v1` / `pass`
- renderer/projection: Godot Forward+ / orthographic
- transformed CELL bounds: `49.006 x 17.819 x 63.296` metres
- data-derived initial camera size: `30.382` metres
- verified content: 44 assets, 44 textures, 161 material bindings, 126
  references, and 15 authored lights
- UI frame SHA-256:
  `549d6fc209443676bfc16ff4b99ed34fcb9aebd5cf8aa8e3b29d27c579833ab0`
- environment frame SHA-256:
  `1b6e684b70fdee6f9f7db218ac906fc7fdd955e1a1520a8875580d99a48c7e3c`
- Windows app control, foreground activation, and injected foreground input:
  all `false`

This promotes the donor CELL as **rendered** in Classic Diorama presentation.
It does not promote Fallout 1 placement, turn-based interaction, retail parity,
packaging, or headset acceptance.
