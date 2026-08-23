# Fallout 1 Vault 13 entrance presentation concept

Status: **native rendered concept passed; static door leaf only; not Fallout 1
placement, animation, interaction, gameplay, or VR parity**.

Superseded as the primary test route on 2026-08-23 by the exact
`V13ENT.MAP` tactical hex slice. This donor-cave composition remains only an
asset/presentation smoke; it must not be used as Fallout 1 layout evidence.

This slice answers one narrow visual question: can the exact Et Tu entrance
door identity be represented by a legally owned, locally imported 3D Vault
door and shown inside a bounded 3D cave with the Classic Diorama camera? Yes.
It deliberately does not claim that the donor cave or concept offset matches
the retail Fallout 1 scene.

## Exact source and presentation mapping

- Et Tu object contract SHA-256:
  `6c29b67fdb317f6f1a1efbcb95028c4b1fcc477927b0d912a6377135723b38ab`
- Source door: serial `129`, object ID `152`, tile `16290` (`90,81`), rotation
  `0`, FID `02000119`, PID `020000AD`, script `342`, `00000174.pro`,
  `v13secr2.frm`, 15 frames.
- Same-tile source frame: serial `130`, FID `0200011A`, PID `020000AE`,
  `v13secr3.frm`.
- Target owned-data identity: FNV FormID `000041E9`, EDID `VGearDoor01`,
  `meshes\dungeons\vault\roomu\vgeardoor01.nif`.
- Source NIF SHA-256:
  `c210e8cdf137d9e6f22e3cc3cc54583a3dff85cc7aeec480dbb5a0ab0d06ac97`
- The static concept exports only the two `VGearDoor*` leaf surfaces. Thirty-four
  particle/helper/drilling surfaces are explicitly inventoried and omitted from
  this closed-pose view; they are not silently treated as supported animation.
- glTF SHA-256:
  `111faa14419844933224120563fd82ff7c4c9c66d2d37b6157d22c60b5e8f772`
- binary buffer SHA-256:
  `ce7ee5a341a9e2e5b364bb2d1168109d4aa5f069eb17a21dae79e1826090d1a1`
- material-manifest SHA-256:
  `627ee23549aa69a44efb882c171815a41ef387c8d6da5bdd1267f8eff757cee9`

The composition replaces the donor cave's entrance reference transform, then
applies the recipe-labelled concept offset `[100, 0, -100]` Godot units so the
larger gear leaf can be inspected outside the rock. A warm authored concept
light is equally explicit. Neither value is a Fallout 1 parity observation.

## Native Godot evidence

Private capture: `fo1-vault13-entrance-concept-capture-20260822-r8`.

- schema/status: `opennv-classic-diorama-capture/v1` / `pass`
- renderer/projection: Godot Forward+ / orthographic
- composed scene SHA-256:
  `a0befa071b5a0478720759ecaa4374ec21f513cb1c417d5448e6b73027a552b8`
- verified content: 45 assets, 48 textures, 163 material bindings, 10
  references, and 16 lights
- UI frame SHA-256:
  `78f5e1c5f2fa452fa0cb661728b1913f8a12ef3be9ffc5278c0a0f1bf9987b60`
- environment frame SHA-256:
  `fef9f23e425cc53f380ba0074d0709b620accb5d10d6b202f872ae21b8820828`
- environment mean luminance/deviation: `0.099059 / 0.036394`
- Windows app control, foreground activation, and injected foreground input:
  all `false`

The interactive launcher shows the automatic owned-data loading layer before
the scene, then exposes WASD pan, wheel zoom, `Q`/`E` 60-degree rotation,
`Home` reset, and `F5` save.

## Explicit gaps

- The NIF `Open`/`Close` controller sequences and particles are not played.
- The Et Tu 15-frame timing and scripted state are not connected.
- The rendered cave is a bounded New Vegas donor presentation, not `V13ENT`
  geometry or placement parity.
- Door collision and open/close persistence have not been accepted for this
  mapped leaf.
- Fallout 1 AP, turns, hex pathfinding, combat, AI, dialogue, quests, and saves
  are not implemented by this concept.
- No package or headset acceptance claim is made.
