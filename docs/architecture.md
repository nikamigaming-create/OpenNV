# OpenNV architecture and code accountability

Status: **playable experimental Goodsprings sandbox; not a full campaign**.

OpenNV is a clean first-party runtime. The retail installation is a read-only
input, the generated cache is disposable, every cross-boundary artifact has a
schema and hashes, and no OpenMW runtime or source code participates.
The executable-value rules, single configuration path, and source gate are
defined in [data and configuration accountability](data-and-configuration-accountability.md).

```mermaid
flowchart LR
    Install[Legal Data folder] -->|1:1| Master[FalloutNV.esm]
    Install -->|1:1| MeshesBsa[Fallout - Meshes.bsa]
    Recipe[Hash-pinned cell recipe] --> Cell
    Master -->|1:N| Cell[CELL]
    Cell -->|1:N cell-child group| Ref[REFR]
    Ref -->|N:1 NAME| Base[World / item / container / door base]
    Ref -->|0:1 XTEL| DoorTarget[Destination door REFR]
    Base -->|N:1 MODL| Nif[NIF member]
    MeshesBsa --> Nif
    Nif -->|direct export| Gltf[glTF + provenance sidecar]
    Cell --> Scene[cell-scene.json]
    Ref --> Scene
    Gltf --> Scene
    Scene --> Loader[Godot CellSceneLoader]
    Loader --> Tree[Runtime node tree]
```

```mermaid
flowchart TD
    Runtime[OpenNV Node3D / coordinator]
    Runtime --> CellRoot[Cell root: XTEL-relative, uniformly scaled]
    CellRoot -->|1:N| Placement[REFR placement Node3D]
    Placement -->|exactly 1| Model[Verified glTF model]
    Model -->|1:N| Mesh[MeshInstance3D]
    Mesh -->|1:1 current slice| Collision[StaticBody3D trimesh]
    Placement -->|when base is DOOR| Door[DoorInstance state owner]
    Runtime --> Player[CharacterBody3D]
    Player --> Capsule[Capsule collision]
    Player --> Camera[Camera3D]
    Player -->|E ray| Door
    Player -->|E ray| Pickup[PickupInstance]
    Player -->|E ray| Container[ContainerInstance]
    Player -->|fire ray| Collision
    Pickup --> Session[GameplaySession]
    Container --> Session
    Door --> Session
    Session --> Hud[Objective / inventory / ammo HUD]
    Session --> Save[Atomic sandbox save]
    CellRoot -->|0:N actor manifests| ActorPlacement[ACHR placement]
    ActorPlacement -->|exactly 1| ActorModel[Verified skinned glTF actor]
    ActorModel --> ActorSkeleton[Skeleton3D + authored idle]
    CellRoot -->|1:1 reciprocal XTEL| ExteriorRoot[WastelandNV exterior root]
    ExteriorRoot --> Land[LAND height, color, texture layers]
    ExteriorRoot --> ExteriorActor[Easy Pete ACHR]
```

The actor data boundary is separate from model assembly and rendering:

```mermaid
flowchart LR
    Cell[CELL] -->|1:N| ActorRef[ACHR / ACRE]
    ActorRef -->|N:1 NAME| ActorBase[NPC_ / CREA]
    ActorBase -->|N:1| Race[RACE]
    ActorBase -->|0:1 each| Hair[HAIR]
    ActorBase -->|0:1 each| Eyes[EYES]
    ActorBase -->|0:N| HeadPart[HDPT]
    ActorBase -->|0:N inventory| Inventory[CNTO]
    Inventory -->|direct or recursive| Leveled[ARMO / LVLI]
    Leveled --> Armor[ARMO models + BMDT slots]
    ActorBase --> FaceGen[FGGS / FGGA / FGTS]
    Race --> Baseline[Female head/body tables and FaceGen baselines]
    FaceGen --> Morph[Pure geometry/texture composition primitives]
    MeshesBsa --> Assembly[Recipe-pinned actor assembly]
    ActorBase --> Assembly
    Race --> Assembly
    Hair --> Assembly
    Eyes --> Assembly
    HeadPart --> Assembly
    Armor --> Assembly
    Morph --> Assembly
    Assembly --> ActorGltf[Skinned glTF + animation + provenance sidecar]
```

`actor_catalog.py` owns these record relationships and preserves authored
placement/enable state; `facegen.py` owns only deterministic FaceGen math. This
keeps record parsing independent from `prepare_actor.py`, which resolves one
hash-pinned recipe, and `actor_gltf.py`, which owns only the skinned glTF,
material-flag, alpha, bind, and animation translation. None of those files
creates a Godot node or claims that a rendered actor matches retail.

## Runtime states

```mermaid
stateDiagram-v2
    [*] --> Unconfigured: no verified manifest
    Unconfigured --> Preparing: player selects legal Data
    Preparing --> CellLoaded: helper + hashes + cell gate pass
    CellLoaded --> CellLoaded: later launch reuses verified cache
    CellLoaded --> Preparing: packaged compiler hash changes
    Preparing --> Unconfigured: legal input invalid
    CellLoaded --> DoorOpen: interaction or proof opens door
    DoorOpen --> CellLoaded: interaction closes door
    CellLoaded --> Armed: collect authored .357
    Armed --> Fired: fire physical ray
    Fired --> Supplied: collect authored aid
    Supplied --> Complete: open entry door
    Complete --> Complete: cold reload restores state
```

## First-class flat/VR boundary

VR is a product mode, not a later camera patch. Before the next gameplay system
is promoted, desktop and OpenXR must share one authoritative intent/state/event
path:

```mermaid
flowchart LR
    Desktop[Keyboard / mouse adapter] --> Intent[Player intent]
    XR[OpenXR head / hand adapter] --> Intent
    Intent --> Game[Authoritative gameplay state]
    Game --> Snapshot[Stable state snapshot]
    Game --> Events[One-shot events]
    Snapshot --> Flat[Flat presentation]
    Events --> Flat
    Snapshot --> Stereo[Stereo presentation]
    Events --> Stereo
    Game <--> Save[One save contract]
```

`CellPlayer` now constructs either a flat camera/input rig or a real OpenXR
origin, tracked head, and two tracked controllers over the same
`GameplaySession`. `GameplaySession` renders either its flat HUD or a
controller-mounted world-space HUD without duplicating inventory, objective, or
save rules. This remains bounded vertical-slice code; actor, combat, VATS, and
quest rules may not accumulate there. No unused VR manager, empty interface, or
speculative abstraction counts as progress.

The software OpenXR gate now proves the action map loads, the rig contains its
required node hierarchy, world scale is one metre, physics runs at 90 Hz, the
HUD is world-space, and the flat save schema is shared. Promotion to
hardware-validated requires a real stereo run with head/two-controller poses,
room-scale collision, controller actions/haptics, identical route/save results,
and stereo-safe materials. Gameplay outcomes remain mode-independent; only
input translation and presentation differ.

The Saloon practice table follows this boundary. `PoolTableInstance` owns the
four recipe-pinned balls, authored reset state, pocket state, cue presentation,
and the single strike operation. `PoolBallInstance` is one hash-verified NIF
convex rigid body. Desktop look/power and tracked OpenXR cue-tip sweeps call the
same strike operation; `GameplaySession` serializes the resulting transforms,
velocities, and pocket state. The software gate requires an actual ball-to-ball
contact in both adapters. Physical headset pose, grip comfort, and haptics still
require a headset session.

## Source ownership

| File | Sole responsibility | Must not own |
| --- | --- | --- |
| `plugin_records.py` | Bounded TES4-family headers, groups, compression, subrecords | Cell or rendering semantics |
| `cell_catalog.py` | CELL, base, REFR, DATA, XTEL relationships | BSA/NIF/Godot behavior |
| `actor_catalog.py` | ACHR/ACRE, NPC_/CREA, RACE, HAIR, EYES, HDPT, ARMO, FaceGen and placement relationships | Mesh assembly or rendering |
| `facegen.py` | Pure EGM/EGT morph and retail skin/body texture composition primitives | Record selection or runtime nodes |
| `actor_gltf.py` | One actor skeleton/skin/mesh/idle assembly to glTF plus provenance | Record selection, placement, or runtime behavior |
| `actor_material.py` | Bethesda actor shader, tint, vertex-color, specular, and alpha flag translation | Geometry, records, or runtime lighting |
| `prepare_actor.py` | Hash-pinned retail actor recipe resolution and atomic disposable cache output | Godot loading or parity verdicts |
| `bsa_archive.py` | Indexed BSA v104 member lookup and extraction | Record or scene semantics |
| `export_static_nif_gltf.py` | NIF static geometry, winding/stencil culling metadata, glTF, and provenance | World placement or gameplay |
| `havok_collision_gltf.py` | Bounded authored packed triangles plus convex/list dynamic body, shape, mass, friction, bounce, damping and filter export | Runtime body policy or unsupported shape guessing |
| `gltf_io.py` | Deterministic buffer/accessor packing and atomic glTF artifact writes | NIF, LAND, actor, or gameplay semantics |
| `cell_scene.py` | Recipe selection, XTEL origin, full Gamebryo-to-Godot transform/scale conversion, asset/reference/material manifest | Godot nodes or input |
| `scene_asset_pipeline.py` | Shared NIF extraction, material bindings, interactions and data-resolved loadout artifacts | CELL selection or Godot nodes |
| `exterior_scene.py` | Bounded grid/persistent reference selection, reciprocal XTEL and exterior manifest | LAND decoding or runtime nodes |
| `landscape_catalog.py` | LAND ownership, height/normal/color, LTEX/TXST and quadrant-layer contracts | Godot nodes or weather |
| `landscape_gltf.py` | One LAND grid plus deterministic owned-texture layer bake and provenance | CELL selection or runtime physics |
| `texture_pipeline.py` | Embedded-name texture-BSA lookup and DDS-to-PNG cache | Runtime material policy |
| `prepare_legal_assets.py` | Legal-input validation and atomic cache transaction | Rendering |
| `goodsprings-saloon-structure-v1.json` | Exact proof target, hash, selection, entry, scale | Parsing logic |
| `goodsprings-trudy-actor-v1.json` | Exact retail master/archives, ACHR, and CELL identity | Appearance guesses or rendering logic |
| `runtime/config/open-nv-runtime-v1.json` | Single versioned non-retail policy boundary with provenance and parity status | Fallout-authored placement, identity, or item stats |
| `runtime_configuration.py` / `RuntimeConfiguration.cs` | Strict typed load, validation, and SHA-256 identity for that boundary | Feature behavior |
| `validate_runtime_report.py` | Join runtime proof reports to owned-data manifests and configuration | Rendering or gameplay implementation |
| `audit_source_constants.py` | Production Python/C#/JavaScript/PowerShell literal accountability gate | Runtime defaults or content policy |
| `test_cell_catalog.py` | Synthetic group/relationship/transform regressions | Retail bytes |
| `test_actor_catalog.py` | Synthetic actor identity/appearance/placement graph regressions | Mesh or renderer assertions |
| `test_facegen.py` | Synthetic geometry, texture-mode, skin, and body composition regressions | Retail actor selection |
| `test_actor_gltf.py` | Bethesda material, alpha, vertex-color, and non-accumulating idle translation regressions | Retail visual approval |
| `test_static_nif_gltf.py` | Synthetic BSA/NIF geometry regressions | Runtime orchestration |
| `OpenNV.Content.spec` | One-file helper inputs and packaged recipe/data files | Content semantics |
| `LegalAssetPreparer.cs` | Packaged-helper process and cache/compiler validation | Record parsing |
| `VerifiedGltfLoader.cs` | Sidecar/model/buffer hash verification and glTF load | Cell placement |
| `CellContentLoader.cs` | One verified CELL presentation/entity root with authored collision instances | Binary parsing or player ownership |
| `CellSceneLoader.cs` | Shared session/view composition, linked CELL alignment, reciprocal portal and proof queries | Binary parsing |
| `RuntimeMaterialLoader.cs` | Hash-verified 2D/cubemap load and name-keyed retail material passes | DDS/BSA parsing |
| `ActorModelSlice.cs` | Hash-verified skinned glTF import, idle start, and non-accumulating bounds contract | Record parsing or placement |
| `CellActorLoader.cs` | Actor-manifest identity, CELL ownership, enable-state gate, and ACHR placement | Actor export or AI state simulation |
| `RetailActorStateContract.cs` | Fail-closed retail shot-state parsing for ACHR transform, camera, idle phase, arm bones, and face/hair hashes | Process addresses, asset parsing, or rendering |
| `EnvironmentCapture.cs` | Native cell/actor frames, application of validated retail shot state, normalized telemetry, hashes, and visual-quality gates | Gameplay or desktop control |
| `actor_parity.py` | Retail/Godot identity, camera, pixel metrics, and labelled differential sheets | Rendering or automatic human approval |
| `DoorInstance.cs` | One door's closed/open transform state | Input or global registry |
| `PickupInstance.cs` | One authored pickup's identity and weapon profile | Inventory ownership |
| `ContainerInstance.cs` | One authored container's resolved content contract | Session persistence |
| `PoolBallInstance.cs` | One authored dynamic convex body and its persisted motion/pocket state | Table rules or input |
| `PoolTableInstance.cs` | One table assembly, cue presentation, shared strike/reset/pocket behavior, and ball ownership | Input polling or asset parsing |
| `GameplaySession.cs` | Objective, HUD, inventory, ammo, world delta, pool snapshots, save/reload | Asset parsing |
| `CellPlayer.cs` | Shared collision body plus flat/OpenXR view, movement, activation, firing, and pool-input adapters | Asset preparation or gameplay outcomes |
| `RuntimeCoordinator.cs` | Startup routing, reports, and gate orchestration | UI construction or file-format parsing |
| `LegalAssetSetupView.cs` | First-run folder selection and status UI | Preparation or rendering |
| `StaticModelSlice.cs` | Legacy one-model proof view | Cell relationships |
| `main.tscn` | One composition root bound to the coordinator | Dynamic entity data |
| `runtime-manifest.json` | Launcher-visible capabilities and executable contract | Promotion claims beyond gates |
| `Test-GodotRuntime.ps1` | Source, synthetic, retail-opt-in, format, and analyzer gates | Packaging state |
| `Build-ContentTool.ps1` | Helper packaging, CLI smoke, and license collection | Runtime behavior |
| `Build-GodotRuntime.ps1` | Clean export, first/reuse proof, notice and asset-free ZIP gates | Gameplay logic |
| `desktop/src/*` | Cross-platform campaign/launch shell | Asset parsing or rendering |
| `site/*` | Static public status and product identity | Runtime capability decisions |

Tests mirror those boundaries: synthetic plugin graph tests cover parsing and
relationships; synthetic NIF tests cover deterministic geometry; Godot reports
cover node counts, floor placement, collision, door traversal, objective
completion, save, and cold reload. Build scripts
package only a clean commit, refuse overwrites, scan for commercial extensions,
and exercise first-run plus cache-reuse routes when legal data is supplied.

## Current truth and deliberate gaps

Implemented: direct owned ESM/BSA/NIF/DDS/LAND path, XTEL-derived spawn, 504
enabled interior/exterior references, 228 visible/held/terrain assets, 379 textures, 476
materials, 97 saloon pickups, five containers, 27 authored lights, full converted item rotations,
supported authored packed-triangle collision,
movement, HUD, inventory, authored `.357` damage/clip data, firing, objectives,
doors, atomic save, cold reload, and launcher-enabled sandbox play.
The saloon door `0010618e` and exterior door `0010636f` are a reciprocal pair.
Their visible planes align below `0.000001` metre, both states persist together,
and the gate passes closed collision, open fire-ray clearance, and two-way player
capsule traversal. The linked exterior includes LAND `000db010`; Sunny Smiles
and the settler load inside while Easy Pete loads outside from their ACHRs.
The OpenXR software path adds Oculus Touch plus OpenXR generic-controller maps, `XROrigin3D`, tracked head
and hands, metre scale, 90 Hz physics, locomotion, snap-turn, controller
activation/fire/save, haptics, runtime-provided controller models, world-space
HUD, standing-eye-height calibration, a data-resolved 10mm smoke loadout,
reload, and explicit launcher routing.

Implemented: direct humanoid record relationships, deterministic FaceGen
geometry/texture primitives, and a hash-verified one-to-many actor cache. The
ordinary legal installer prepares the world actor set and loads the authored
enabled Goodsprings settler, Sunny Smiles, and exterior Easy Pete at their ACHR
placements. Initially disabled Trudy remains out
of normal gameplay until quest/enable state is implemented; an explicit proof
override is still required for her comparison lane. Cache generation and import
are not fidelity claims. The capture lane can consume a compact retail state contract and
apply each shot's live ACHR placement, camera/aim, vertical FOV, and `mtidle`
phase. Projection remains explicitly provisional; current framing evidence
favors 59.840 degrees vertical by interpreting retail 75 degrees at the 4:3
reference aspect, until a render-pass world projection is recovered. Production still needs
quest/enable-parent/package state before an initially disabled actor can appear
naturally.

The latest private differential applies Trudy plus the authored seated
Goodsprings settler in both shots. Identity, placement, yaw, camera, animation
phase, and all 56 deform-bone world transforms pass for both actors; worst bone
translation is below `0.000002` metres. Rendering remains a hard failure at
portrait/full-body MAE `0.0790`/`0.0811` and changed-pixel fractions
`75.9%`/`86.4%`. The remaining visual gap is not a camera, placement, or pose
excuse.

The cell transform boundary negates Gamebryo yaw when producing Godot Y rotation:
retail forward is `(sin(yaw), cos(yaw))`, while Godot positive Y rotation has the
opposite sign. Cell scene v7 and actor scene v3 make that correction explicit,
carry decoded alpha/vertex/emission/cubemap state, and hash the complete actor
artifact chain. Runtime culling now follows each NIF's
`NiStencilProperty`; the former recipe-wide double-sided override is removed.

Not implemented: environment-map light fade and external-emittance color, Havok
shape families beyond packed triangles and convex/list dynamics, broad filter policy, SpeedTree vegetation, neighboring exterior
streaming, retail weather/time, visible first-person
weapon animation, damageable actors/creatures, VATS, or full
campaigns. There are no placeholder managers for
these. Each enters only with a data contract, synthetic test, retail proof, and
promotion gate.

The current screenshots are **not** a retail-fidelity claim. The decoded alpha,
self-illum, six-face cubemap, name-keyed surface, and CELL depth-fog paths are
implemented, but external emittance, environment light fade, the full retail
lighting/HDR path, complete Havok behavior, and actor pixels remain open
differential gates. The clean-room shader observations are recorded in
`docs/evidence/fnv-retail-material-shader-contract.md`.

Next promotion order:

1. package the linked saloon/exterior route and rerun physical OpenXR acceptance;
2. close fixed-camera interior/exterior material, lighting, weather, and actor-pixel gates;
3. add neighboring exterior CELL streaming and SpeedTree vegetation;
4. promote authored packages/dialogue plus jukebox interaction/audio;
5. extend Havok body/filter/dynamic behavior beyond the promoted pool slice;
6. add damageable targets, authored flat/VR weapon presentation,
   ballistics/projectiles,
   creatures and raiders; and
7. promote VATS only after the same combat route passes deterministic recording,
   flat/VR presentation, and cold-reload gates.

The asset distribution follows the four-surface model described in
[Shipping an asset-free Godot XR port](https://github.com/Brobert-in-aus/guides/blob/main/vr/shipping-an-asset-free-godot-xr-port.md): public source, asset-free build,
user-owned game data, and private identity material remain separately auditable.
OpenNV does not use the guide's native-ABI shortcut because no lawful,
cross-platform New Vegas simulation library is available to embed. See the
retained retail evidence in
[fnv-esm-cell-contract.md](evidence/fnv-esm-cell-contract.md) and the explicit
[OpenXR runtime contract](evidence/openxr-runtime-contract.md). The promoted
interior/exterior boundary is recorded in the
[Goodsprings linked-world contract](evidence/fnv-goodsprings-linked-world-contract.md).
