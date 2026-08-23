# V13ENT source/Godot orientation review

Status: **two confirmed transform/state defects corrected; source-bound scene
functional; executable retail pixel parity remains unproven**.

## Matched comparison row

| Field | Source reference | Godot |
| --- | --- | --- |
| Area | `V13ENT.MAP`, elevation 0 | `opennv-fo1-hex-scene/v1`, elevation 0 |
| MAP hash | `02b6987038a0c94c8226aa4df048f49bfcb59dfd9339a4a6a3b48da622b2a2d7` | same verified source |
| State | initial map, `map_first_run` | initial tactical scene |
| Spawn | `V13CAVE.ssl` override `17690 / 0 / 2` | `17690 / 0 / 2` |
| Door | object serial `129`, hex `16290`, rotation `0` | same source identity and hex |
| Presentation | deterministic owned MAP/FRM reconstruction | Godot Forward+ orthographic tactical view |
| Capture | 1280×720 crop around spawn/door | 1280×720 route capture |

The source side is not an executable screenshot. It deterministically composites
the original floor and object FRMs using the documented Mapper-compatible
screen projection. Therefore this comparison accepts topology/orientation and
placement conclusions, not lighting, camera, UI, animation, or pixel parity.

## Confirmed deltas

1. **Floor grid mirrored under objects**
   - previous owner: `floorIndex=(hexY/2)*100+(hexX/2)`
   - correct owner: `floorIndex=(hexY/2)*100+(99-hexX/2)`
   - cause: the 100×100 floor-storage X coordinate must be reversed before it
     maps to the 200×200 object-hex projection
   - confidence/severity: confirmed/blocking

2. **Player spawned at the far map-entry fallback**
   - previous state: MAP header `20090 / 0 / 0`
   - correct new-game state: `V13CAVE.ssl` `map_first_run` override
     `17690 / 0 / 2`
   - source script SHA-256:
     `02c84efeed93c78bd6077efc81586d55f2d81448e386868ab7370cfe6e1c5d65`
   - confidence/severity: confirmed/blocking

The corrected source-space door-minus-spawn vector is `[-112, -84]` pixels:
the Vault door is upper-left and the Vault Dweller exits down-right into the rat
cave. Godot now preserves that relation.

## Evidence

Private review: `fo1-v13ent-orientation-review-20260823-r5`.

- source-reference SHA-256:
  `5b00de590a29c8a199ee374aedacd39b74e3e4554c4504c64e07b9f182b4b2ce`
- corrected Godot frame SHA-256:
  `b58a614413b7ac5ef919779649cada1ad91f59f2664888e6bf8e200d40c9609a`
- side-by-side SHA-256:
  `78413d534f684d8eb011ecbcb066bca89485a65fae2394ea098257583d6711c8`
- review report SHA-256:
  `15a2a97dcb9b86ad25d87a0b81a0583a9c9f8213dbb8cd69e7b6c6db961e18b5`
- Windows app control, foreground activation, and injected foreground input:
  all `false`

## Remaining deltas

- Godot uses rotatable floor textures and camera-facing 2.5D sprites, not the
  fixed cavalier source projection.
- Sprite direction does not yet switch as the tactical camera orbits.
- The mapped 3D gear leaf does not yet play the source/NIF opening sequence.
- Source lighting, roofs/cutaways, effects, animation, sound, and UI are not
  matched.
