# FNV ESM cell contract evidence

OpenNV's first cell slice is based on a legal retail master with SHA-256
`50991d36804b7d1e70df1afd7471b72f0e29d1b456ee2516a9717c002564e7c1`.
No commercial bytes or decompiled engine code are retained in this repository.

## Confirmed observations

- `GSProspectorSaloonInterior` is CELL `00106185`.
- Its cell-child graph contains 452 REFR records; the playable bounded recipe
  selects 348 visible references backed by 153 unique NIF assets. Full converted
  rotations are promoted for gameplay item types; 50 non-item arbitrary
  rotations remain outside the contract.
- Exterior door `0010636f` points through XTEL to interior door `0010618e` and
  supplies arrival `(132.9728088, -821.6999512, 3456.0)` in game units.
- After `(x,y,z) -> (x,z,-y)` conversion and origin subtraction, the Godot
  spawn-floor ray hits at Y `0.0`.
- Closed entry door `0010618e` blocks the proof ray with the dense authored cell
  loaded. The identical short ray has no hit after the door and its collision
  rotate open.
- `Fallout - Textures.bsa` SHA-256
  `68c0f4beb00e07cc06361e3a5be0909873220731db3bd43bc013e85544b67578`
  and `Fallout - Textures2.bsa` SHA-256
  `bdaa85989b30a68c2c9ce79a07b167ecd72942df47f2e58c4a0299b016410dc2`
  supply 255 exact recipe texture members. They decode into hash-pinned PNGs and
  bind 332 source surfaces; the actor-free Forward+ capture retains the no-control
  flags in `opennv-godot-environment-capture/v1`.
- XCLL supplies the cell ambient/directional/fog state. Twenty-four placed LIGH
  references supply data-derived positions, RGB colors, radii, and FNAM
  intensity multipliers. A declared Godot energy calibration converts those
  authored values without replacing their placement or identity.
- The saloon exposes 97 authored pickups and five containers. WEAP `0008f216`
  contributes damage `26`, clip size `6`, and ammo form `001537e3`. The promoted
  route takes that revolver, fires once, takes Beer `00015197`, transfers six
  `SSBottleFull` items from resolved container `0010873e`, opens entry door
  `0010618e`, saves, exits, and cold-restores objective stage 4 with five rounds.

## Contradiction retained

Door `00107c77` overlaps solid wall reference `00106c19`. It fails the traversal
contract in both states and is not used as proof. This remains an open semantic
question rather than being hidden by a special-case portal.

Door `00108bc8` passed while its surrounding shack reference was outside the
first structural recipe, but correctly failed once dense authored references
were loaded because `00108bc9` blocks the same segment. The promoted oracle is
therefore the authored main entry door, not a coverage-dependent shortcut.

The implementation-neutral contract and test map are summarized in
[architecture.md](../architecture.md).
