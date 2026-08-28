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

`tools/fo1_profile.py` hash-validates Et Tu's `V13ENT.MAP`, verifies the exact
Fallout: New Vegas master and archives, transports the complete 10,000-entry
tile grid, and resolves cave, clean-Vault, gear-door, Vault-suit, and giant-rat
donor identities. `tools/dat2_archive.py` and `tools/fo1_map_objects.py` then
decode the owned Fallout 2 DAT2 index, MAP script table, placed-object graph,
PID/FID relationships, PRO filenames, and FRM art names without invoking a
Fallout engine.

The first rendered object mapping is the exact Et Tu Vault 13 door at tile
`16290` (`v13secr2.frm`). `tools/prepare_fo1_door_proof.py` maps it explicitly
to the owned New Vegas `VGearDoor01` identity and emits a filtered, hash-pinned
static door-leaf cache. `tools/compose_fo1_vault13_concept.py` places that leaf
in a bounded donor-cave entrance with a labelled concept-only offset and light.
Generated NIF/glTF/PNG data remains local and ignored.

This promotes a **rendered presentation concept**, not Fallout 1 placement or
gameplay parity. NIF controller playback, the Et Tu 15-frame door sequence,
collision/interaction parity, AP/turn order, hex pathfinding, scripts, quests,
packaging, and headset acceptance remain explicit gaps. The source and render
boundaries are recorded in `docs/evidence/fo1-ettu-vault13-opening-contract.md`
and `docs/evidence/fo1-vault13-entrance-concept-contract.md`.

## Fallout 1 V13ENT tactical hex slice

`tools/prepare_fo1_hex_scene.py` supersedes the donor cave as the primary
Fallout 1 test route. It transports `V13ENT.MAP` into its actual 200×200
movement-hex namespace, with the original 100×100 floor grid mapped four hexes
per floor tile and its storage X reversed before object-hex projection. Each
hex is one metre flat-to-flat. The tool resolves all 58
used floor FRMs, unprojects their isometric diamonds into rotatable local floor
textures, and emits 1,493 visible elevation-zero MAP-object sprite placements
from 115 exact FRM/frame/rotation artifacts. Owned/derived PNG and glTF files
remain in a fresh ignored cache.

The MAP header fallback is `20090`; the authored first-run `V13CAVE.ssl`
override starts the player at `17690`, rotation `2`, just outside the Vault.
Godot places the mapped gear door and
exact `v13secr3.frm` frame at source hex `16290`. A separate hash-pinned local
presentation recipe imports an owned animated Vault 13-suited humanoid, the
owned New Vegas giant-rat rig, and an owned cave kit without making those donor
assets layout authority. The default runtime therefore presents one opaque,
normal-mapped cave-floor mesh over all 30,196 non-default floor-backed movement
hexes, a 15-surface Vault Dweller with idle/forward clips, twenty animated 3D
rats with hidden gore caps, and 310 cave/Vault/corpse instances placed from
Fallout 1 object and topology evidence, including 35 owned cave-room modules.
The 58 unprojected source floor textures receive
deterministic transparent-edge padding for their optional reference view;
source floor/scenery and the exact-topology blockout remain hidden diagnostics
rather than the default art.

Rats are entities with MAP HP/AP/team/AI and PRO HP/AP/AC/melee/sequence
values. The runtime exposes click pathfinding, animated one-AP movement, target
selection, a bounded 10mm attack/rat turn, combat HUD, target-cycle framing,
end-turn AP restoration, save/reload, and a camera-driven cave cutaway. Its
tactical camera supports middle-mouse orbit/tilt (the Kenshi default),
right-drag map pan, mouse-wheel zoom toward the cursor, WASD/arrows, edge pan,
player focus, route reset, and a `C` cycle through tactical, shoulder, and
first-person play. Shoulder commands resolve to exact Fallout hex centers;
first-person uses conventional mouse look and continuous movement constrained
by the source walk mask without spending tactical AP. The shader cutaway keeps
the cave enclosed in perspective but removes the enclosure and Vault corridor
over tactical play. `G` toggles an opaque, depth-tested overlay of
27,519 currently legal hexes as one 86,841-unique-edge mesh; 1,608 additional
floor-backed hexes are masked by the owned 3D presentation footprint. `V` swaps
the owned floor for the cleaned source floor/scenery reference in tactical mode;
first-person suppresses all 2.5D reference cards and retains the continuous 3D
floor. Selected rats are rescaled and regrounded together, and hostile markers
remain behind their 3D bodies through normal depth testing.
`tools/fo1_campaign_inventory.py` also
produces an asset-free, hash-recorded promotion ledger for every locally
present Fallout 1 MAP file; it currently promotes only `V13ENT` as tactically
playable.

`tools/dat1_archive.py` now directly reads the original Fallout DAT1 directory
and LZSS blocks. `tools/prepare_fo1_character_start.py` verifies the owned
original `MASTER.DAT` and manual, decodes the exact 640x480 character-creator
chrome, preserves the original Overseer MVE/text/timing hashes, and builds a
deterministic private playback cache. The runtime connects name, age, sex,
SPECIAL, three tag skills, and up to two traits to the live HP/AP/AC/Sequence,
skills, weapon AP cost, save, and new-game report before handing off to exact
V13ENT tile `17690`, rotation `2`.

The opening can be watched or skipped; both routes use the same open Vault 13
threshold crossing, close the gear door, and settle on tile `17690`. Rats use
the source AI packet and activate only within six hexes rather than aggroing as
a whole cave.

This is a **playable new-game-to-V13ENT 3D vertical slice**, not the complete game.
Floor walk masks, complete multihex footprints, roof state, door animation,
complete trait effects, retail to-hit/critical/damage formulas, complete AI-packet
behavior, dialogue, quests, inventory/reload, and full save semantics remain
unpromoted. The owned cave/actor substitutions are presentation mappings, not
a claim that Fallout 1 shipped with equivalent 3D assets. See
`docs/evidence/fo1-v13ent-hex-tactical-contract.md` and
`docs/evidence/fo1-new-game-character-opening-contract.md`.

All core 3D adaptation values are now owned by the versioned
`recipes/fo1-classic-3d-runtime-v1.json`, which the V13ENT recipe references by
exact SHA-256. The preparer fails closed on a missing, escaped, malformed, or
hash-mismatched profile and embeds it in the immutable scene cache. See
`docs/evidence/fo1-data-driven-runtime-contract.md` for the source/donor/config
boundary, permitted-literal policy, linearity analysis, and remaining blockers.
