# OpenNV clean implementation boundary

OpenNV's Godot runtime is implemented from legally owned retail data, published
format facts, synthetic fixtures, and targeted observations of the retail game.

## Admitted evidence

- hash-pinned ESM, ESP, BSA, NIF, DDS, KF, XML, audio, and FOS files supplied by
  the user;
- locked retail camera captures and xNVSE telemetry;
- published format documentation with recorded provenance;
- synthetic tests and Godot runtime evidence;
- targeted retail disassembly or debugging distilled into neutral behavior
  contracts when observation and format data are insufficient.

## Quarantined evidence

- output from modified third-party engines;
- third-party scene, actor, animation, collision, or save exporters;
- generated artifacts whose source, compiler, output hashes, schema, or
  unsupported semantics are unknown;
- pseudocode or distinctive implementation structures copied from another
  engine.

Quarantined material may explain historical experiments but cannot enter
`runtime`, `content`, a release bundle, or an acceptance oracle.

## First direct slice

The first authored local check uses the owned retail file
`meshes\landscape\nv_rocks\nvn_rockcanyon12.nif`, SHA-256
`5eb48addaead13852606870843b3da4592904e0a91bf7298149ff42dc5181b2b`.
It is extracted directly from `Fallout - Meshes.bsa`, SHA-256
`054e299829ff24fd4bd4edf69f6424346b400c87379cee39bec02e4d082bf85a`,
without a third-party engine or pre-generated asset cache.
The direct exporter produces one glTF surface with 106 vertices, 133 triangles,
normals, tangents, UV0, vertex colors, texture-slot identities, and material
metadata. Its Havok blocks are inventoried and explicitly reported as not
exported. Godot Forward+ loads the result and independently validates the glTF
and binary-buffer hashes from the sidecar.

This proves only static geometry transport. It is not a rendering-parity claim.

## First data-driven cell slice

The next contract directly scans `FalloutNV.esm`, SHA-256
`50991d36804b7d1e70df1afd7471b72f0e29d1b456ee2516a9717c002564e7c1`,
and selects `GSProspectorSaloonInterior` through a committed recipe. CELL group
ownership, REFR NAME/DATA, base MODL, XCLL, LIGH, and incoming XTEL fields
produce a neutral cell-scene manifest. The playable recipe exports 154 visible/
held NIFs, places 348 references, binds 266 textures and 339 materials, exposes 97
pickups and five containers, and places 24 authored lights. Godot verifies every artifact,
derives the spawn from XTEL, and runs floor and open-door collision rays. See the retained
[cell-contract evidence](evidence/fnv-esm-cell-contract.md).

This is not a general ESM, world, collision, or gameplay compatibility claim.
It is a playable, persistence-gated Goodsprings sandbox contract.
