# FNV ESM cell contract evidence

OpenNV's first cell slice is based on a legal retail master with SHA-256
`50991d36804b7d1e70df1afd7471b72f0e29d1b456ee2516a9717c002564e7c1`.
No commercial bytes or decompiled engine code are retained in this repository.

## Confirmed observations

- `GSProspectorSaloonInterior` is CELL `00106185`.
- Its cell-child graph contains 452 REFR records; the bounded recipe selects 42
  structural/door references backed by 14 unique NIF assets.
- Exterior door `0010636f` points through XTEL to interior door `0010618e` and
  supplies arrival `(132.9728088, -821.6999512, 3456.0)` in game units.
- After `(x,y,z) -> (x,z,-y)` conversion and origin subtraction, the Godot
  spawn-floor ray hits at Y `0.0`.
- Closed interior door `00108bc8` blocks the proof ray. The identical ray has no
  hit after the door and its collision rotate open.
- `Fallout - Textures.bsa` SHA-256
  `68c0f4beb00e07cc06361e3a5be0909873220731db3bd43bc013e85544b67578`
  and `Fallout - Textures2.bsa` SHA-256
  `bdaa85989b30a68c2c9ce79a07b167ecd72942df47f2e58c4a0299b016410dc2`
  supply 22 exact recipe texture members. They decode into hash-pinned PNGs and
  bind 66 surfaces; the actor-free Forward+ capture retains the no-control
  flags in `opennv-godot-environment-capture/v1`.

## Contradiction retained

Door `00107c77` overlaps solid wall reference `00106c19`. It fails the traversal
contract in both states and is not used as proof. This remains an open semantic
question rather than being hidden by a special-case portal.

The implementation-neutral contract and test map are summarized in
[architecture.md](../architecture.md).
