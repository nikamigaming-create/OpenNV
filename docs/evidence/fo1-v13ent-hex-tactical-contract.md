# Fallout 1 V13ENT tactical hex contract

Status: **exact source floor/hex/object authority retained; owned continuous
cave floor, 3D Vault Dweller, grounded giant rats, scale-seated cave props,
enclosed cave composition, and an embedded rock-to-Vault portal with the Vault
corridor/frame/gear door and corpse loaded from hash-pinned local game data;
optional exact hex overlay, conventional FPS mouse look, continuous
walk-mask FPS locomotion/hitscan combat, centered-hex shoulder commands,
one-AP tactical movement, local rat activation, combat HUD, shader cutaway,
save, native capture, and mobile-video gates passed; full Fallout simulation remains
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
- grid: 200×200 Fallout even-column-offset flat-top hexes; tile ID `y*200+x`
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
  rotatable local floor textures; deterministic multi-source edge padding
  makes every non-default rendered texture fully opaque for the optional source
  reference view
- one opaque, normal-mapped owned cave-floor mesh covers all 30,196
  non-default floor-backed movement hexes using 181,176 triangles; this exact
  topology set comes from `V13ENT`, while the material comes from hash-pinned
  locally owned New Vegas data
- 1,493 exact elevation-zero object placements from 115 unique
  FRM/frame/rotation artifacts; the mapped door and its source frame are the
  only two objects handled by the dedicated door lane
- all 20 source giant-rat placements instantiate the owned New Vegas giant-rat
  skeleton, six skinned surfaces, four textures, and five animation clips;
  source critter sprites remain hidden parity references
- all 1,494 source static/actor sprites share ground anchor `Y=0.015` with
  measured maximum anchor error `0`; 1,473 static cards disable billboarding
  and retain the authored `-45°` world yaw, while the 21 actor cards use
  fixed-Y billboarding
- player presentation is an owned animated 3D humanoid donor wearing the
  Classic Pack Vault 13 suit (`001735d1`): 15 surfaces, seven skins, 17
  textures, `Idle` plus `Forward`, and measured runtime height `1.8234525 m`;
  the donor face/body is explicitly not claimed as the Fallout 1 chosen one
- each giant rat is an entity using PID `01000030`, current MAP HP/AP/team/AI,
  and PRO-derived HP/AP/AC/melee/sequence values; highlight scaling preserves a
  measured ground error of `0 m`, and its hostile marker uses normal depth
  testing instead of drawing over the body
- 1,069 blocked central hexes derived from MAP object flags lacking
  `OBJECT_NO_BLOCK`
- 29,127 floor-backed, MAP-non-blocked candidate movement hexes; the owned 3D
  presentation footprint removes another 1,608 obstacle/Vault-side threshold
  hexes without covering the authored first-run spawn, leaving 27,519 runtime
  movement/grid hexes
- the optional `G` diagnostic renders those 27,519 legal hexes as one opaque,
  depth-tested mesh with 86,841 unique shared edges; it is hidden by default
  and its on/off proof passes
- no MAP objects were omitted for missing art, unsupported type, or hidden
  state in this source state
- mapped `VGearDoor01` leaf bounds: `4.384 × 4.317 × 0.916` metres
- exact `v13secr3.frm` frame presentation: `7.932 × 5.195` metres, scaled by
  matching the source-door FRM width to the mapped 3D leaf width
- 312 owned 3D instances are placed by source object/topology authority: one
  tactical-hidden terrain enclosure, 156 wall-ribbon segments, one continuous
  rock-to-Vault portal, 54 large rocks, 53 small rocks, seven stalagmites, 35
  cave-room modules, one Vault airlock, one source-axis cave-to-Vault frame,
  one hall, one hall cap, and the source entrance corpse; the full presentation
  resolves 97 material bindings
- all 114 large-rock, small-rock, and stalagmite instances are scale-aware
  seated from their measured runtime world AABBs into the cave floor; measured
  seat depth is `0.04569527-0.10202604 m`, maximum placement error is
  `0.000000022351742 m`, and the hard tolerance is `0.002 m`
- 257 cave/Vault instances participate in the shader camera melt, backed by
  439 shader materials; the final combat proof measured 13 target occluders
  while tactical mode sliced the enclosure and portal without disassembling the
  visible Vault frame
- the 1,420-edge/20,564-triangle topology blockout remains hidden behind `B`;
  it is a diagnostic, not the default environment

All PNG/glTF derivatives and retail captures remain in ignored local caches and
are not release inputs.

## Interactive proof

Private presentation: `fo1-v13ent-3d-presentation-20260825-r30`.

- presentation-manifest SHA-256:
  `970bb74acbb4989f5abee13226e30ec685db5155b5b8acef48bf076c37b2b5c1`

Private scene: `fo1-v13ent-hex-20260826-r52`.

- scene SHA-256:
  `da6e72212c1d34fd57f92e2daaa3253460bde6dc8e8af5f843bf443747cc83db`
- embedded runtime-profile ID/SHA-256:
  `fo1-classic-3d-runtime-v1` /
  `9e5bc4a347bd94249cd1943f9190209b37c3d3fb9ef92f5d827a2712adb699ac`
- proof schema/status: `opennv-fo1-tactical-proof/v1` / `pass`
- movement: `17690 -> 17490`, one adjacent metre, exactly `1 AP`
- selected source rat: serial `466`, PID `01000030`, HP `6`, AP `7`, AC `4`,
  melee damage `3`, sequence `12`, team `1`, AI packet `12`
- one provisional 10mm attack cost `5 AP`, killed the 6-HP proof target, and
  reduced the living source-mob count from `20` to `19`
- 20 hostile hex markers and health-label nodes were present; `Tab` selected
  and framed a living hostile and activated the screen-space target reticle
- end turn: turn `2`, AP restored to `10`
- owned player runtime: 15 meshes, one skeleton, two imported animations,
  source sprite hidden; the movement proof played `Forward` and returned to
  `Idle`
- owned rat runtime: 20 skeletons/animation players and 80 intact-state gore
  cap meshes explicitly hidden
- selected-rat death presentation: corpse visible, measured corpse-ground
  error below `0.00000002 m`, hostile marker depth tested
- source AI packet `12` is retained; activation distance is six exact hexes and
  the headless rat-turn proof confirms whole-cave aggro is prevented
- continuous owned floor: 30,196 exact source-backed hexes, 181,176 triangles,
  one visible mesh, 97 total owned material bindings
- optional hex overlay: hidden by default, on/off toggle passed, 27,519 legal
  hexes, 86,841 unique edges, opaque and depth tested
- camera cutaway: 257 candidates, 439 shader materials, and 13 combat
  occluders; the enclosure and portal melt around the tactical focus while the
  Vault frame remains visually coherent
- `C` cycles tactical orthographic, third-person, and first-person over the same
  authoritative session; shoulder commands terminate at exact hex centers,
  while FPS movement is continuous, source-walk-mask constrained, and consumes
  no tactical AP
- first-person gate: 1.66 m eye height, 68° FOV, zero eye-position error,
  horizontal player-forward alignment above `0.9999`, local player suppression,
  and all 2.5D source-reference cards suppressed
- synthetic mouse-up gate: pitch `-1.1459156° -> 7.1046767°`, forward Y
  `0.12368247`; conventional non-inverted FPS look passed
- MMB orbit changed yaw `-45° -> -57.376°`
- MMB tilt changed pitch `-52° -> -58.188°`
- wheel cursor zoom changed size `22.0 -> 9.5` metres after target framing
- RMB drag pan moved the camera focus `8.297` metres in the combined input proof
- proof report SHA-256:
  `763a56fdd1cebb3926c62079dfcc62ce82e3998382068eba53ee79c1fb1e8810`
- Windows app control, foreground activation, and injected foreground input:
  all `false`

## Native visual evidence

Private capture:
`fo1-v13ent-rock-grounding-portal-20260825-r4/native-capture`.

- schema/status: `opennv-fo1-hex-capture/v1` / `pass`
- renderer/projection: Godot Forward+ / orthographic
- UI frame SHA-256:
  `eccfa187612894bf9c6ce0ab720e361ab4e24aaa705b22a74bfd750845acbdfc`
- selected-rat combat frame SHA-256:
  `12f0eb2e95e4bc578d4a466e8eca83214ba9e7625aeeac5cd92923a5300bfa75`
- optional exact-hex-overlay frame SHA-256:
  `bb692c8a0fbff2fcdbbc2e72739386b63a766c2974c7f439431a5f7114620d86`
- clean 3D rat frame SHA-256:
  `c04d35612f91d4f5341c636796880540bcdeadecb0c0d01a15e702bef1db674b`
- clean 3D Vault Dweller frame SHA-256:
  `1b34f4c561e2411a4b244f100f598d6d640b87bbf68d56849710cd4f9c4e1383`
- clean Vault-door frame SHA-256:
  `2b1b96cefd819f71dd5bdd043ec78229844558ba9342d25d511057b128dfe45e`
- map frame SHA-256:
  `e9ce3f057884bcb4c3efd35e4b2f010851abbfe11416b255fc3f44fef0997bd8`
- all seven 1280×720 luminance/deviation/dark-fraction gates passed; capture
  report SHA-256:
  `219f79f24f10286effd39df4585f710221f7f8b5bd2c4aa9703d9aad3fd8b658`

## Deterministic full showcase video

Private video:
`fo1-full-showcase-20260825-r2-capture`, with delivery encodes in
`fo1-full-showcase-20260825-r3-video`.

- report schema/status: `opennv-fo1-new-game-demo/v4` / `pass`
- 2,188 native Godot/Vulkan frames, 1280×720, 30 fps, 72.933333 seconds;
  RTX 4070 SUPER Forward+ capture with no desktop automation
- the owned picker and custom creator, accelerated original opening, Pip-Boy
  2000, exact entry landing, open Vault corridor look-back, continuous FPS cave
  traversal, two FPS hits and one rat kill, shoulder orbit and center-hex
  movement, tactical transition/grid, two turn-based kills, and a slow wide
  pan/orbit/zoom map tour are visible
- native MJPEG/PCM AVI SHA-256:
  `cf8b455a2608dd993f841cfcf7f7e8b2692abbf30edffa8c1e7b6b9300cca833`
- phone-safe H.264 High/yuv420p/TV-range/AAC-LC 854×480 MP4, 3,678,915
  bytes, SHA-256:
  `44ce33f6083225782486bfee399bcf6ad569304d937c7451876e168cd80ee834`
- full H.264 High/yuv420p/TV-range/AAC-LC 1280×720 MP4, 32,267,136
  bytes, SHA-256:
  `e1b840472bfcfcba0e0806a870a4527bb485d4bd06fb2c28fe61521dbc84b462`
- showcase report SHA-256:
  `52f96a8dc8ab7222cdb78c24e2e0e867ba97d53ffbcc89914b127c43c7f1515d`
- native contact-sheet SHA-256:
  `23987cd70b08617dc5097bad75908e6c3743befbb5abe1bafdbb4df8180498bb`
- Windows app control, foreground activation, and injected foreground input:
  all `false`

The end-to-end new-game and source-bound character details are documented in
`fo1-new-game-character-opening-contract.md`.

## Whole-game coverage inventory

`fo1_campaign_inventory.py` parsed all 96 locally present Fallout 1 MAP files
(157 elevations; versions 19/20), with per-file/header/layout hashes and no
retail bytes in the output. The promotion ledger deliberately reports only one
map with object transport, owned 3D presentation, and tactical playability:
`V13ENT`. Quest-script execution, campaign autoplay, campaign-wide first-person
promotion, and OpenXR acceptance remain `0/96`; the bounded local V13ENT FPS
slice is independently proven above. Inventory SHA-256:
`2be8a280087c2b428f1df73526a650cda9758bf0a7902a576884fdbe338e5a44`.

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
  base walkability input. The continuous owned floor is a presentation mesh and
  does not add or remove authoritative movement hexes.
- `OBJECT_MULTIHEX` secondary footprints are not resolved. This map state has
  no multihex blockers, but the general rule remains unsupported.
- Roof state and source lighting are not transported. The enclosed owned cave
  composition and shader cutaway are promoted for this slice, but the cave
  pieces are New Vegas presentation mappings driven by Fallout 1 topology—not
  retail-authored Fallout 1 3D equivalents or visual-parity evidence.
- The local 3D gear controller opens for the shared landing, closes after the
  crossing, and persists normal runtime state; exact Et Tu 15-frame timing,
  sound, script-state parity, and collision parity are not claimed.
- The isolated tactical proof intentionally uses a provisional 10-AP profile;
  the new-game route applies the created character's live HP/AP/AC/Sequence and
  Fast Shot weapon cost. The bounded new-game route now decodes the starting
  knife, 10mm Pistol, ammunition stacks, stimpaks, and flares from the V13
  script and item/ammunition/weapon PRO records. Broader pickup, container,
  equipment, and inventory-screen behavior remains unconnected.
- Rat turns use source stats/AI packet with local activation and a bounded
  animated chase/melee proof, not full retail to-hit/critical/damage formulas,
  sound, path scheduling, or all AI-packet behaviors.
- Dialogue, quests, broader inventory/equipment behavior, complete retail
  to-hit/critical/armor semantics, OpenXR, and Fallout save-format parity are
  not connected. The bounded V13ENT route does provide shared FPS/tactical
  pistol ammunition and reload, ranged and knife attacks, animation, impacts,
  ricochets, grounded casings, deaths, continuous walk-mask FPS movement, and a
  local JSON save contract.
- The local launcher is verified, but public packages may not contain the
  generated owned-data caches or private rendered video. Retail visual parity
  and physical OpenXR acceptance remain unproven.
