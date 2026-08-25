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

The whole-game inventory path is documented in
[`docs/whole-game-cell-parity.md`](../docs/whole-game-cell-parity.md). It merges
the pinned official plugin stack into stable CELL/child identities, accounts for
source anomalies and engine-implicit marker bases through the versioned recipe,
emits one pending review row per effective CELL, and validates an exact join to
the independent actor-placement corpus. This inventory is not a runtime or
fidelity claim. Its partitioned compiler currently has exact presentation
policies for supported `STAT` models and baseline placed `LIGH` records. Each
REFR subrecord outside the selected base policy becomes a named blocker; the
compiler does not silently discard it. Light placement, optional `XRDS` radius,
base RGB/radius/flags/falloff/FOV/intensity, transforms, and runtime conversion
are independently rejoined and validated before Godot can load the artifact.

Profile v2 also treats `LAND` as a direct CELL child rather than pretending it
has a base record. `landscape_stack.py` independently resolves the exact
corpus-bound source record and effective master-aware `LTEX -> TXST` winners;
`cell_landscape_compile.py` emits the height mesh, deterministic owned-texture
bake, collision declaration, and provenance; `cell_landscape_validate.py`
reconstructs that graph independently. `cell_static_resource_validate.py` owns
generic asset/texture/file closure, leaving `validate_cell_static_compile.py`
as CELL/plan/policy orchestration. The current baseline accepts only complete
four-quadrant LAND layouts and fails partial/default layouts closed.

`recipes/fnv-thirteen-area-capture-plan-v1.json` selects a fixed base-game and
story-DLC review surface with both interiors and exteriors. The selection is
compiled from the immutable CELL corpus by `tools/area_capture_plan.py` and
rejoined by `tools/validate_area_capture_plan.py`; no CELL transform, lighting,
child count, portal count, or runtime FormID is copied into the recipe. Retail
must be captured first, and Godot must consume its exact camera telemetry.
Until both native frames exist, every comparison remains explicitly blocked.
