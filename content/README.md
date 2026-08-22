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
REFR-to-base many-to-one relationships, incoming XTEL placement, and 14
structural/door NIF assets. It does not yet export authored NIF collision
blocks, animation, actors, or a campaign.

The fidelity slice directly extracts 22 recipe-referenced DDS members from the
two owned texture archives, decodes them into hash-pinned PNG cache artifacts,
converts DirectX normal-map green channels for Godot, and emits 66 explicit
surface material bindings. Environment and mask slots remain inventoried but
are not yet rendered.

The committed NIF fixture is synthetic. Owned retail inputs and generated glTF
outputs remain local and ignored.
