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

## Contradiction retained

Door `00107c77` overlaps solid wall reference `00106c19`. It fails the traversal
contract in both states and is not used as proof. This remains an open semantic
question rather than being hidden by a special-case portal.

The implementation-neutral contract and test map are summarized in
[architecture.md](../architecture.md).
