# Direct retail-content pipeline

`content` reads files supplied by the player and emits versioned, hash-pinned
artifacts for the Godot runtime. It does not invoke or consume another game
engine.

`tools/prepare_legal_assets.py` accepts a legal Fallout New Vegas `Data`
directory, hashes `FalloutNV.esm` and `Fallout - Meshes.bsa`, extracts the
requested member directly from BSA v104, and builds an isolated cache.

The first slice, `tools/export_static_nif_gltf.py`, supports opaque static
`NiTriShape` and `NiTriStrips` geometry. It exports positions, normals,
tangents, up to two UV sets, vertex colors, indices, texture-slot identities,
and Bethesda shader/material metadata. Collision is inventoried but not
exported. Controllers, skinning, alpha properties, and unknown surface
properties fail closed.

The cell slice adds a bounded TES4-family record reader and a hash-pinned
Goodsprings Saloon recipe. It resolves CELL-to-REFR one-to-many relationships,
REFR-to-base many-to-one relationships, incoming XTEL placement, 153 visible
NIF assets, 348 placements, 97 typed pickups, five containers, CELL XCLL
lighting, and 24 placed LIGH records. Item references retain full converted
rotations; unproven non-item arbitrary rotations remain excluded.

The fidelity slice directly extracts 255 recipe-referenced DDS members from the
two owned texture archives, decodes them into hash-pinned PNG cache artifacts,
converts DirectX normal-map green channels for Godot, and emits 332 explicit
surface material bindings. Environment and mask slots remain inventoried but
are not yet rendered.

The committed NIF fixture is synthetic. Owned retail inputs and generated glTF
outputs remain local and ignored.

## Fallout et Tu source profile

`tools/fo1_profile.py` owns one deliberately narrower cross-game contract. It
hash-validates Et Tu's `V13ENT.MAP`, verifies the exact Fallout: New Vegas
master and archives, and resolves cave, clean-Vault, gear-door, Vault-suit, and
giant-rat donor identities from the retail record/resource graph. It emits a
neutral JSON contract plus detached SHA-256 into a fresh disposable cache.

This proves only **transported source identity**. It does not decode MAP tiles
or placed objects, extract donor assets, generate a Godot scene, or claim that
the Fallout 1 opening is rendered or interactive. The bounded recipe is
`recipes/fo1-ettu-vault13-opening-v1.json`; retained evidence and explicit gaps
are recorded in `docs/evidence/fo1-ettu-vault13-opening-contract.md`.
