# Fallout 1 V13ENT tactical hex contract

Status: **exact source floor/hex/object placement rendered; mouse camera and
one-AP movement interactive proof passed; full Fallout simulation unproven**.

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
- floor grid: 100×100; `floorIndex=(hexY/2)*100+(hexX/2)`; four movement
  hexes per floor-art tile
- entry: hex `20090` (`90,100`), elevation `0`, rotation `0`
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

Private scene: `fo1-v13ent-hex-20260823-r5`.

- scene SHA-256:
  `d8d687ccb09708693ceef3c19ac9b8f816e6b03d6938ee97334d0e3eccf374e3`
- proof schema/status: `opennv-fo1-tactical-proof/v1` / `pass`
- movement: `20090 -> 19889`, one adjacent metre, exactly `1 AP`
- end turn: turn `2`, AP restored to `10`
- MMB orbit changed yaw `-45° -> -57.376°`
- MMB tilt changed pitch `-52° -> -58.188°`
- wheel cursor zoom changed size `30.0 -> 25.8` metres
- RMB drag pan moved the camera focus `2.386` metres
- proof report SHA-256:
  `7022d20feb9277b91ff165b1daf1124743c99e6ae90ba1e92158ef69da2d0b6f`
- Windows app control, foreground activation, and injected foreground input:
  all `false`

## Native visual evidence

Private capture: `fo1-v13ent-hex-capture-20260823-r5`.

- schema/status: `opennv-fo1-hex-capture/v1` / `pass`
- renderer/projection: Godot Forward+ / orthographic
- UI frame SHA-256:
  `2baf53c601cc3d7f804a735215abedaea3b373c07201357f8bf0d284b96338fa`
- map frame SHA-256:
  `27a2a456ccd9350c0f32ae96f92c3d43ca0ac18186294995f6dba34065ce8fd7`
- UI mean luminance/deviation: `0.061120 / 0.094223`
- map mean luminance/deviation: `0.055512 / 0.058397`

## Explicit gaps

- Floor walk-mask bytes are not decoded; floor-art presence is the current
  base walkability input.
- `OBJECT_MULTIHEX` secondary footprints are not resolved. This map state has
  no multihex blockers, but the general rule remains unsupported.
- Roofs, dynamic wall cutaways, sprite direction changes while orbiting,
  source lighting, and animation playback are not promoted.
- The 3D gear-door controller, Et Tu 15-frame script state, opening collision,
  and persistence are not implemented.
- Rat AI, sequence order, attacks, damage, RNG, dialogue, quests, inventory,
  character statistics, and full Fallout saves are not connected.
- Packaging, retail visual parity, and physical OpenXR acceptance are unproven.
