# Fallout 1 V13ENT tactical hex contract

Status: **corrected source floor/hex/object placement rendered; source player
and rat presentation loaded; mouse camera, one-AP movement, target attack, rat
turn, and combat HUD interactive proof passed; full Fallout simulation
unproven**.

This slice replaces the donor-cave prototype with the actual `V13ENT.MAP`
coordinate space. It preserves source tile IDs and MAP placements while using
local owned-data derivatives only. It is not a claim that scripts, collision,
AI, or combat already match Fallout.

## Source spatial contract

- MAP SHA-256:
  `02b6987038a0c94c8226aa4df048f49bfcb59dfd9339a4a6a3b48da622b2a2d7`
- elevation-zero floor-grid SHA-256:
  `5ddcdaaf9cbe23247183c6424e55c77bc1c9a97d6c18ee389977d04fd4957336`
- object-contract SHA-256:
  `6c29b67fdb317f6f1a1efbcb95028c4b1fcc477927b0d912a6377135723b38ab`
- grid: 200×200 odd-row pointy hexes; tile ID `y*200+x`
- physical proof scale: one metre flat-to-flat per hex
- floor grid: 100×100; `floorIndex=(hexY/2)*100+(99-hexX/2)`; four movement
  hexes per floor-art tile
- MAP-header fallback: hex `20090` (`90,100`), elevation `0`, rotation `0`
- authored first-run spawn: hex `17690` (`90,88`), elevation `0`, rotation `2`,
  from `V13CAVE.ssl` `override_map_start_hex(17690, 0, 2)`; source SHA-256
  `02c84efeed93c78bd6077efc81586d55f2d81448e386868ab7370cfe6e1c5d65`
- Vault door: object serial `129`, hex `16290` (`90,81`), rotation `0`
- source frame: serial `130`, same hex and rotation

The source rules/manual record one metre per hex and one AP per walked hex.
The tactical proof uses 10 AP as an explicitly provisional player state until
the chosen character's statistics are transported.

## Owned presentation coverage

- 10,000 floor entries; 7,549 non-default floor placements
- 58 exact floor FRMs, affine-unprojected from their isometric diamonds into
  rotatable local floor textures
- 1,493 exact elevation-zero object placements from 115 unique
  FRM/frame/rotation artifacts; the mapped door and its source frame are the
  only two objects handled by the dedicated door lane
- all 20 source giant-rat placements are present as idle source sprites
- player presentation uses owned `hmjmpsaa.frm` male Vault-jumpsuit art; sex,
  appearance, and statistics remain pending character selection
- each giant rat is an entity using PID `01000030`, current MAP HP/AP/team/AI,
  and PRO-derived HP/AP/AC/melee/sequence values
- 1,069 blocked central hexes derived from MAP object flags lacking
  `OBJECT_NO_BLOCK`
- 29,127 floor-backed, non-blocked movement hexes
- no MAP objects were omitted for missing art, unsupported type, or hidden
  state in this source state
- mapped `VGearDoor01` leaf bounds: `4.384 × 4.317 × 0.916` metres
- exact `v13secr3.frm` frame presentation: `7.932 × 5.195` metres, scaled by
  matching the source-door FRM width to the mapped 3D leaf width

All PNG/glTF derivatives and retail captures remain in ignored local caches and
are not release inputs.

## Interactive proof

Private scene: `fo1-v13ent-hex-20260823-r8`.

- scene SHA-256:
  `e0871a3af52c23c6f2667d92b364a255fdb3115db7cc6623902e15fafd408298`
- proof schema/status: `opennv-fo1-tactical-proof/v1` / `pass`
- movement: `17690 -> 17489`, one adjacent metre, exactly `1 AP`
- selected source rat: serial `466`, PID `01000030`, HP `6`, AP `7`, AC `4`,
  melee damage `3`, sequence `12`, team `1`, AI packet `12`
- one provisional 10mm attack cost `5 AP`, killed the 6-HP proof target, and
  reduced the living source-mob count from `20` to `19`
- end turn: turn `2`, AP restored to `10`
- MMB orbit changed yaw `-45° -> -57.376°`
- MMB tilt changed pitch `-52° -> -58.188°`
- wheel cursor zoom changed size `22.0 -> 18.92` metres
- RMB drag pan moved the camera focus `1.750` metres
- proof report SHA-256:
  `11a8aca59f3bbe2958a0276abf20aa20f2894d972e1f18935a132f9e6b2ab031`
- Windows app control, foreground activation, and injected foreground input:
  all `false`

## Native visual evidence

Private capture: `fo1-v13ent-hex-capture-20260823-r10`.

- schema/status: `opennv-fo1-hex-capture/v1` / `pass`
- renderer/projection: Godot Forward+ / orthographic
- UI frame SHA-256:
  `b58a614413b7ac5ef919779649cada1ad91f59f2664888e6bf8e200d40c9609a`
- selected-rat combat frame SHA-256:
  `745d4d8de4d690d0fa7a212084c3852ddc6425df8bd48721df476daf5f57f983`
- map frame SHA-256:
  `fd30b1a3a97ec9164a785bc52bf345a49371e3ebff3efb43d29e6e22e0420f5e`

## Orientation and spawn review

Private comparison: `fo1-v13ent-orientation-review-20260823-r5`.

- source side is a deterministic owned `MAP + FRM` reconstruction, not an
  executable screenshot
- source door minus first-run spawn: `[-112, -84]` pixels; the door is
  upper-left and the player exits down-right into the rat cave
- confirmed floor defect: storage X was not reversed
- confirmed spawn defect: MAP-header fallback `20090` was incorrectly used
  instead of first-run scripted spawn `17690`
- source-reference SHA-256:
  `5b00de590a29c8a199ee374aedacd39b74e3e4554c4504c64e07b9f182b4b2ce`
- side-by-side SHA-256:
  `78413d534f684d8eb011ecbcb066bca89485a65fae2394ea098257583d6711c8`
- orientation-review report SHA-256:
  `15a2a97dcb9b86ad25d87a0b81a0583a9c9f8213dbb8cd69e7b6c6db961e18b5`

## Explicit gaps

- Floor walk-mask bytes are not decoded; floor-art presence is the current
  base walkability input.
- `OBJECT_MULTIHEX` secondary footprints are not resolved. This map state has
  no multihex blockers, but the general rule remains unsupported.
- Roofs, dynamic wall cutaways, sprite direction changes while orbiting,
  source lighting, and animation playback are not promoted.
- The 3D gear-door controller, Et Tu 15-frame script state, opening collision,
  and persistence are not implemented.
- Player stats and 10mm profile are provisional until character creation and
  starting inventory records are connected.
- Rat turns currently use source stats with a bounded chase/melee proof, not
  full AI-packet behavior, retail to-hit/critical/damage formulas, animation,
  sound, or sequence scheduling.
- Dialogue, quests, inventory/reload, and full Fallout saves are not connected.
- Packaging, retail visual parity, and physical OpenXR acceptance are unproven.
