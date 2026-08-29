# Fallout: New Vegas owned door-articulation contract

Status: **producer and runtime contract implemented; route acceptance pending**.

This boundary preserves an owned NIF door controller without rotating the whole
placed reference or inventing an OpenNV hinge. It applies only when a DOOR model
contains one controller manager with exact `Open` and `Close` sequences, both
resolving uniquely to the same source node. Unsupported controllers, ambiguous
targets, missing sequences, incomplete joins, and contract/hash drift fail
closed.

## Neutral contract

Static NIF sidecar v3 and CELL scene v14 carry identical canonical
`opennv-controller-door-articulation/v1` data:

- source block joins for manager, sequence, controller, interpolator, transform
  data, and target;
- the target's closed local translation, quaternion, and scale in Godot space;
- independent Open and Close initial/terminal transforms, duration,
  interpolation names, and source-key SHA-256;
- stable IDs and generated node names for every moving visual surface;
- source block IDs and generated node names for every moving authored collision
  body; and
- a canonical SHA-256 repeated by the static sidecar and CELL asset row.

Runtime independently reconstructs and validates the canonical hash and all
members. It removes the verified generated visual and collision wrappers,
reparents only their exact descendants beneath one articulation pivot, and
samples the source sequence terminal with its authored duration. Restore applies
the correct terminal directly and does not replay an animation. The placed REFR
and all non-target siblings remain fixed. A controller-free door may use the
configured fallback angle only when its visual and collision each contain
exactly one mesh.

## First exact owned gate

The Goodsprings route blocker is REFR `0010757e`, base `0010664e`, model
`meshes\clutter\fence\nv_fencepickburntgate01.nif`. The owned model SHA-256 is
`5bdde726ef76cec2637c02c57e89106ed8d718b1929caa413f3b492912ccf4c5`.
Its root has sibling targets `BGate` and `BPosts`; only `BGate` is controlled.
The decoded contract preserves:

- target pivot translation in source space
  `[-52.2645378113, -0.000003814697, 56.1037864685]`;
- target pivot translation in Godot space
  `[-52.2645378113, 56.1037864685, 0.000003814697]`;
- Open duration `1.0` second and source Z rotation from `0` to
  `1.3788100481` radians;
- Close duration `0.9666666985` second and source Z rotation from
  `1.3743162155` to `0` radians;
- moving `BGate` visual surfaces and the collision body targeting `BGate`; and
- static `BPosts` visuals and collision.

The Close initial angle differs from the Open terminal by
`0.0044938326` radians, so synthesizing Close by reversing Open would discard
owned behavior. Two exact local exports were byte-identical and produced
articulation hash
`998387cd8e7b94ffd2864ab069415693069261a551251ff91f5dddc7c049ec5b`.
This is contract evidence, not a passed gameplay route or visual-parity claim.

## Current unresolved owned pattern

The first whole-route cache build with this contract stopped on
`meshes\dungeons\nv_craftsmanhomesinterior\nvcraftsmanrmdooranimated.nif`
because its controlled target did not join to the currently supported collision
form. The owned NIF SHA-256 is
`6d3c9586c988a746db1c62e5d18c0d48664c98a613b7d942fec1167f3ca9ff3a`.
Root `Point01` block 1 reaches `Point01 NonAccum` block 17 and animated target
`OffDoorHotelSm` block 18. Both Open and Close control block 18. Its visual is
block 23; collision object 20 targets the same block and owns mass-zero
`bhkRigidBody` block 21 plus eight-vertex `bhkConvexVerticesShape` block 22.
There is no static sibling/root collision.

The gap is bounded: static collision export currently admits MOPP packed
triangles, while the dynamic-physics path recognizes convex vertices but skips
mass-zero bodies. The next producer slice must admit this exact authored static
convex form, preserving target-local ownership, body/filter/radius/points, and a
deterministic runtime convex representation beneath the same articulation
wrapper. Unsupported convex variants remain fail-closed. It must not relax the
articulation requirement, reassign the body to root, manufacture collision, or
rotate the whole REFR.

The run exited once with code 2 after 245.365 seconds, atomically emitted no
sidecar or install manifest, and therefore admitted no cache or compiler-family
closure. The partial output is disposable and must not be restored. After the
bounded convex-body fix, one new unique cache build and an ordinary route/cold-
Continue pair are required before this path can be promoted.
