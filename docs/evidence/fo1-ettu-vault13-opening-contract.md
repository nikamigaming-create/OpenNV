# Fallout et Tu Vault 13 opening source contract

Status: **transported source identity only; not rendered or interactive**.

This bounded profile selects the Fallout et Tu `V13ENT.MAP` opening and a
strictly local Fallout: New Vegas donor-asset catalog. It does not extract an
asset, decode Fallout placed objects, create a Godot scene, or claim parity.

## Pinned source

- Fallout et Tu release: `v1.16.3771`
- Release ZIP SHA-256:
  `68777c2c32b911da902992c2d1cd5d4ce895059bd182909b579aa1eada0c686`
- `mods/fo1_base/maps/V13ENT.MAP` SHA-256:
  `02b6987038a0c94c8226aa4df048f49bfcb59dfd9339a4a6a3b48da622b2a2d7`
- Map index: `35`; entry tile/elevation/rotation: `20090 / 0 / 0`
- Fallout MAP layout reference:
  <https://github.com/rotators/fallout2-docs/blob/master/content/pages/map.md>

The source MAP is version `20`, has only elevation zero present, and resolves
through Et Tu's `Maps.txt` as `V13ENT`. The tool validates the exact header,
file hash, global/local variable arrays, and the complete 10,000-entry
big-endian floor/roof tile grid. Elevation zero's raw tile-grid SHA-256 is
`5ddcdaaf9cbe23247183c6424e55c77bc1c9a97d6c18ee389977d04fd4957336`;
it contains 58 floor IDs, 7,549 non-default floor entries, one default roof ID,
and no non-default roof entries. Scripts, objects, inventories, PRO
relationships, and actor placement remain explicit blockers rather than
inferred data.

## Pinned donor graph

- `FalloutNV.esm`:
  `50991d36804b7d1e70df1afd7471b72f0e29d1b456ee2516a9717c002564e7c1`
- `Fallout - Meshes.bsa`:
  `054e299829ff24fd4bd4edf69f6424346b400c87379cee39bec02e4d082bf85a`
- `Fallout - Textures.bsa`:
  `68c0f4beb00e07cc06361e3a5be0909873220731db3bd43bc013e85544b67578`
- `Fallout - Textures2.bsa`:
  `bdaa85989b30a68c2c9ce79a07b167ecd72942df47f2e58c4a0299b016410dc2`

Data-resolved donors:

- `SLGoodspringsCaveINT` (`00153159`): 55 qualifying cave/skeleton placements,
  28 unique bases.
- `2EOVault21` (`0010FDEB`): 866 qualifying clean-vault/furniture/console
  placements, 105 unique bases.
- `VGearDoor01` (`000041E9`): clean Vault gear-door mesh identity.
- `VaultSuit21` (`00104184`): male/female Vault-suit base meshes. A local
  Vault 13 number texture remains an authored identity delta.
- `NVCrGiantRat` (`000E8E95`): giant-rat skeleton identity.

The donor CELL placements are evidence for asset identity and coverage only.
They are not copied into the Fallout 1 scene. Future placement must come from
the decoded Fallout MAP object graph and an explicit per-prototype mapping.

## Why Fallout 3 is not admitted in v1

Fallout: New Vegas already contains the clean Vault, cave, Vault-suit, gear-door,
and giant-rat identities required for the first opening slice. Adding Fallout 3
now would enlarge source precedence without closing a demonstrated gap. A later
recipe version may admit a hash-pinned Fallout 3 master/archive only when an
automated coverage report identifies a missing role.

## Promotion boundary

Passing `fo1_profile.py` means **transported** only: exact source bytes reached a
neutral, deterministic, hash-pinned contract. Rendering, interaction, parity,
packaging, and OpenXR remain unproven.
