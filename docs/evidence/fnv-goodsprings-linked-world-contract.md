# Goodsprings saloon/exterior linked-world contract

Status: **interactive bounded slice; visual parity unproven**.

## Source identity

- `FalloutNV.esm` SHA-256:
  `50991d36804b7d1e70df1afd7471b72f0e29d1b456ee2516a9717c002564e7c1`
- Worldspace: `000da726` (`WastelandNV`)
- Interior: `00106185` (`GSProspectorSaloonInterior`)
- Exterior grid CELL: `000daeb9`, coordinates `[-17, 0]`
- Exterior persistent CELL: `000846ea`
- Reciprocal doors: interior `0010618e` -> exterior `0010636f` -> interior
- Exterior terrain: LAND `000db010`

The interior door's XTEL arrival is the interior origin. The exterior scene uses
the reciprocal XTEL arrival as its origin. Runtime alignment joins the actual
rendered door planes, not merely the two reference origins.

## Promoted content

- 228 interior/exterior/held/LAND assets;
- 504 loaded enabled flat-mode placements;
- 379 decoded or derived texture artifacts and 476 material bindings;
- 27 authored lights and nine loaded doors;
- LAND height, normals, vertex colors, four base textures, and 24 alpha layers;
- enabled Sunny Smiles `00104e85`, settler `00104f08`, and Easy Pete
  `00104c80`; initially disabled Trudy remains state-gated.

The one selected `.spt` SpeedTree reference is retained as an explicit
`unsupported-model-format` exclusion. No proxy vegetation is substituted.

## Collision boundary

Static NIF collision is promoted only for the complete supported chain:

```text
bhkCollisionObject
  -> bhkRigidBody / bhkRigidBodyT
  -> bhkMoppBvTreeShape
  -> bhkPackedNiTriStripsShape
  -> hkPackedNiTriStripsData
```

The exporter preserves ordered body identity, subshape vertex ranges, material,
layer, flags/part number, and unknown filter short. Unsupported trees are not
mixed with partial authored output. LAND uses its decoded height grid. An
interactive object without supported authored collision may use an explicitly
labelled render-mesh fallback; decorative geometry does not become solid merely
because it renders.

## Runtime proof

The deterministic Godot portal gate requires:

- linked-scene and all artifact hashes pass;
- the XTEL pair is reciprocal;
- door-plane normal agreement is at least `0.999`;
- door-plane alignment error is at most `0.0001` metre;
- the closed ray hits the interior door;
- opening propagates to both door references and persists both states;
- an open fire ray is not blocked by either door; and
- the full player capsule traverses the opening in both directions.

The current local proof measures 332 collision meshes, 938 surfaces, 294034
vertices, normal agreement `1.0`, alignment error below `0.000001` metre, and an
authored-collision floor within `0.000001` metre of the
spawn origin, open ray clearance, and two-way capsule traversal.

This contract does not claim neighboring CELL streaming, SpeedTree, retail
weather/time, AI package simulation, full Havok shapes/dynamics/filter policy,
or retail rendering parity.
