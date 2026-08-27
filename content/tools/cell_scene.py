"""Prepare one recipe-pinned, data-driven interior cell scene."""

from __future__ import annotations

import json
import math
import os
import sys
from pathlib import Path

from cell_catalog import (
    INITIALLY_DISABLED_RECORD_FLAG,
    BaseObject,
    CellCatalog,
    PlacedReference,
    scan_cell_catalog,
)
from material_contract import environment_texture_paths
from scene_asset_pipeline import (
    form_id,
    interaction_manifest,
    prepare_scene_assets,
    reference_selection_reason,
    vr_smoke_loadout_manifest,
)
from runtime_configuration import load_runtime_configuration
from first_person_rig import prepare_first_person_rig
from owned_archive_stack import OwnedArchiveStack


CELL_SCENE_SCHEMA = "opennv-cell-scene/v10"
CELL_RECIPE_SCHEMA = "opennv-cell-recipe/v1"
EXTERIOR_RECIPE_SCHEMA = "opennv-exterior-recipe/v1"
FORM_ID_RADIX = 16
BYTE_CHANNEL_MAXIMUM = 255.0
QUATERNION_COMPONENT_SCALE = 0.25
POOL_CUE_TIP_ENDPOINTS = {"maximum-z", "minimum-z"}
POOL_COLLISION_SOURCES = {"presentation-render-triangles"}


def recipe_path(recipe_id: str) -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / f"{recipe_id}.json"


def load_recipe(recipe_id: str) -> dict[str, object]:
    document = load_spatial_recipe(recipe_id)
    if document.get("schema") != CELL_RECIPE_SCHEMA:
        raise ValueError(f"OpenNV recipe is not an interior cell recipe: {recipe_path(recipe_id)}")
    return document


def load_spatial_recipe(recipe_id: str) -> dict[str, object]:
    path = recipe_path(recipe_id)
    document = json.loads(path.read_text(encoding="utf-8"))
    if (
        document.get("schema") not in {CELL_RECIPE_SCHEMA, EXTERIOR_RECIPE_SCHEMA}
        or document.get("id") != recipe_id
        or not isinstance(document.get("exportStrict"), bool)
        or not isinstance(document.get("textureAliases"), dict)
    ):
        raise ValueError(f"Invalid OpenNV cell recipe: {path}")
    return document


def godot_position(
    position: tuple[float, float, float],
    origin: tuple[float, float, float],
) -> list[float]:
    delta = tuple(value - anchor for value, anchor in zip(position, origin))
    return [delta[0], delta[2], -delta[1]]


def godot_yaw_radians(game_yaw_radians: float) -> float:
    return -game_yaw_radians


def normalized_rgb(color: tuple[int, int, int]) -> list[float]:
    return [component / BYTE_CHANNEL_MAXIMUM for component in color]


def _matrix_multiply(left: list[list[float]], right: list[list[float]]) -> list[list[float]]:
    return [
        [sum(left[row][axis] * right[axis][column] for axis in range(3)) for column in range(3)]
        for row in range(3)
    ]


def godot_rotation_quaternion(rotation_radians: tuple[float, float, float]) -> list[float]:
    x, y, z = rotation_radians
    z = godot_yaw_radians(z)
    cosine_x, sine_x = math.cos(x), math.sin(x)
    cosine_y, sine_y = math.cos(y), math.sin(y)
    cosine_z, sine_z = math.cos(z), math.sin(z)
    rotation_x = [[1.0, 0.0, 0.0], [0.0, cosine_x, -sine_x], [0.0, sine_x, cosine_x]]
    rotation_y = [[cosine_y, 0.0, sine_y], [0.0, 1.0, 0.0], [-sine_y, 0.0, cosine_y]]
    rotation_z = [[cosine_z, -sine_z, 0.0], [sine_z, cosine_z, 0.0], [0.0, 0.0, 1.0]]
    game_rotation = _matrix_multiply(rotation_z, _matrix_multiply(rotation_y, rotation_x))
    conversion = [[1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, -1.0, 0.0]]
    conversion_inverse = [[conversion[column][row] for column in range(3)] for row in range(3)]
    matrix = _matrix_multiply(conversion, _matrix_multiply(game_rotation, conversion_inverse))

    trace = matrix[0][0] + matrix[1][1] + matrix[2][2]
    if trace > 0.0:
        scale = math.sqrt(trace + 1.0) * 2.0
        quaternion = [
            (matrix[2][1] - matrix[1][2]) / scale,
            (matrix[0][2] - matrix[2][0]) / scale,
            (matrix[1][0] - matrix[0][1]) / scale,
            QUATERNION_COMPONENT_SCALE * scale,
        ]
    else:
        axis = max(range(3), key=lambda index: matrix[index][index])
        next_axis = (axis + 1) % 3
        last_axis = (axis + 2) % 3
        scale = math.sqrt(
            1.0 + matrix[axis][axis] - matrix[next_axis][next_axis] - matrix[last_axis][last_axis]
        ) * 2.0
        components = [0.0, 0.0, 0.0, 0.0]
        components[axis] = QUATERNION_COMPONENT_SCALE * scale
        components[3] = (matrix[last_axis][next_axis] - matrix[next_axis][last_axis]) / scale
        components[next_axis] = (matrix[next_axis][axis] + matrix[axis][next_axis]) / scale
        components[last_axis] = (matrix[last_axis][axis] + matrix[axis][last_axis]) / scale
        quaternion = components
    length = math.sqrt(sum(component * component for component in quaternion))
    return [component / length for component in quaternion]


def _atomic_bytes(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def _atomic_json(path: Path, document: object) -> None:
    _atomic_bytes(path, (json.dumps(document, indent=2, sort_keys=True) + "\n").encode())


def _find_cell(catalog: CellCatalog, editor_id: str) -> int:
    matches = [cell.form_id for cell in catalog.cells.values() if cell.editor_id == editor_id]
    if len(matches) != 1:
        raise ValueError(f"Expected one CELL with editor ID {editor_id!r}, found {len(matches)}")
    return matches[0]


def arrival_transform(catalog: CellCatalog, target_door_form_id: int) -> tuple[int, Transform]:
    incoming = [
        reference
        for reference in catalog.references
        if reference.teleport_destination_form_id == target_door_form_id
        and reference.teleport_destination_transform is not None
    ]
    if len(incoming) != 1:
        raise ValueError(
            f"Expected one incoming XTEL for door {target_door_form_id:08x}, found {len(incoming)}"
        )
    return incoming[0].form_id, incoming[0].teleport_destination_transform


def pool_gameplay_manifest(
    recipe: dict[str, object],
    catalog: CellCatalog,
    selected: list[tuple[PlacedReference, BaseObject]],
    assets: dict[str, dict[str, object]],
    asset_sidecars: dict[str, dict[str, object]],
) -> tuple[dict[str, object] | None, dict[int, str]]:
    configured = recipe.get("poolGameplay")
    if configured is None:
        return None, {}
    if not isinstance(configured, dict):
        raise ValueError("Cell pool gameplay recipe must be an object")

    table_id = int(str(configured["tableReferenceFormId"]), FORM_ID_RADIX)
    cue_id = int(str(configured["cueReferenceFormId"]), FORM_ID_RADIX)
    rack_id = int(str(configured["rackReferenceFormId"]), FORM_ID_RADIX)
    cue_ball_id = int(str(configured["cueBallReferenceFormId"]), FORM_ID_RADIX)
    object_ball_ids = [
        int(str(value), FORM_ID_RADIX)
        for value in configured["objectBallReferenceFormIds"]
    ]
    role_by_reference = {
        table_id: "table",
        cue_id: "cue",
        rack_id: "rack",
        cue_ball_id: "cue-ball",
        **{reference_id: "object-ball" for reference_id in object_ball_ids},
    }
    expected_count = 4 + len(object_ball_ids)
    if len(role_by_reference) != expected_count or not object_ball_ids:
        raise ValueError("Cell pool gameplay reference identities must be unique and complete")

    selected_by_reference = {reference.form_id: (reference, base) for reference, base in selected}
    missing = sorted(set(role_by_reference) - set(selected_by_reference))
    if missing:
        raise ValueError(
            "Cell pool gameplay references were not selected: "
            + ", ".join(form_id(value) for value in missing)
        )
    cue_tip_endpoint = str(configured["cueTipEndpoint"])
    if cue_tip_endpoint not in POOL_CUE_TIP_ENDPOINTS:
        raise ValueError(f"Unsupported pool cue tip endpoint: {cue_tip_endpoint}")

    playable_table_model = str(configured["playableTableModelPath"]).lower()
    if playable_table_model not in assets:
        raise ValueError(f"Playable pool table model was not exported: {playable_table_model}")
    playable_table_sidecar = asset_sidecars[playable_table_model]
    if not playable_table_sidecar["coverage"]["collisionExported"]:
        raise ValueError("Playable pool table requires authored packed collision")
    playable_collision_source = str(configured["playableCollisionSource"])
    if playable_collision_source not in POOL_COLLISION_SOURCES:
        raise ValueError(f"Unsupported playable pool collision source: {playable_collision_source}")

    def component(reference_id: int, role: str) -> dict[str, object]:
        reference, base = selected_by_reference[reference_id]
        if base.model_path is None:
            raise ValueError(f"Pool component has no model: {reference_id:08x}")
        sidecar = asset_sidecars[base.model_path]
        physics_bodies = sidecar["coverage"]["dynamicPhysicsBodies"]
        if role != "table" and len(physics_bodies) != 1:
            raise ValueError(
                f"Pool component requires one authored dynamic body: {reference_id:08x}"
            )
        return {
            "role": role,
            "referenceFormId": form_id(reference.form_id),
            "baseFormId": form_id(reference.base_form_id),
            "baseRecordType": base.record_type,
            "baseEditorId": base.editor_id,
            "authoredModelPath": base.model_path,
            "authoredAssetId": assets[base.model_path]["id"],
            "authoredDynamicBodyCount": len(physics_bodies),
        }

    table = component(table_id, "table")
    table["presentationModelPath"] = playable_table_model
    table["presentationAssetId"] = assets[playable_table_model]["id"]
    table["gameplayCollisionSource"] = playable_collision_source
    cue = component(cue_id, "cue")
    cue["tipEndpoint"] = cue_tip_endpoint
    balls = [component(cue_ball_id, "cue-ball")]
    balls.extend(component(reference_id, "object-ball") for reference_id in object_ball_ids)
    return (
        {
            "mode": str(configured["mode"]),
            "source": "recipe-identities-plus-retail-reference-transforms-and-nif-physics",
            "table": table,
            "cue": cue,
            "rack": component(rack_id, "rack"),
            "balls": balls,
        },
        role_by_reference,
    )


def prepare_cell_scene(
    master_path: Path,
    meshes_path: Path,
    texture_archive_paths: list[Path],
    texture_archive_rows: list[dict[str, object]],
    cache_root: Path,
    recipe: dict[str, object],
    master_sha256: str,
    owned_archives: OwnedArchiveStack | None = None,
) -> dict[str, object]:
    configuration = load_runtime_configuration()
    units_to_meters = configuration.world_units_to_meters
    expected_master = str(recipe["master"]["sha256"])
    if master_sha256 != expected_master:
        raise ValueError(f"Cell recipe master hash mismatch: expected={expected_master} actual={master_sha256}")

    catalog = scan_cell_catalog(master_path)
    vr_loadout = vr_smoke_loadout_manifest(recipe, catalog)
    cell_form_id = _find_cell(catalog, str(recipe["cellEditorId"]))
    cell = catalog.cells[cell_form_id]
    entry_door = int(str(recipe["entryDoorReferenceFormId"]), FORM_ID_RADIX)
    source_door, arrival = arrival_transform(catalog, entry_door)
    origin = arrival.position
    if cell.lighting is None:
        raise ValueError(f"Cell recipe requires XCLL lighting: {cell.editor_id}")

    selected: list[tuple[PlacedReference, BaseObject]] = []
    excluded_references: list[dict[str, str]] = []
    for reference in catalog.references_for(cell_form_id):
        base = catalog.base_objects.get(reference.base_form_id)
        if base is None:
            continue
        selection_reason = reference_selection_reason(
            base,
            recipe,
            configuration.content_compiler,
        )
        if selection_reason != "selected":
            if base.model_path:
                excluded_references.append(
                    {
                        "formId": form_id(reference.form_id),
                        "baseEditorId": base.editor_id,
                        "modelPath": base.model_path,
                        "reason": selection_reason,
                    }
                )
            continue
        selected.append((reference, base))
    if not selected:
        raise ValueError(f"Cell recipe selected no references: {recipe['id']}")

    extra_model_paths = {str(vr_loadout["modelPath"])}
    configured_pool = recipe.get("poolGameplay")
    if isinstance(configured_pool, dict):
        extra_model_paths.add(str(configured_pool["playableTableModelPath"]).lower())
    (
        assets,
        asset_sidecars,
        texture_artifacts,
        compiler,
        non_presentation_assets,
        unresolved_texture_bindings,
    ) = prepare_scene_assets(
        meshes_path,
        texture_archive_paths,
        cache_root,
        recipe,
        selected,
        configuration.content_compiler,
        extra_model_paths,
        owned_archives=owned_archives,
    )
    retained_selected = []
    for reference, base in selected:
        non_presentation = non_presentation_assets.get(str(base.model_path))
        if non_presentation is None:
            retained_selected.append((reference, base))
            continue
        excluded_references.append(
            {
                "formId": form_id(reference.form_id),
                "baseEditorId": base.editor_id,
                "modelPath": str(base.model_path),
                "reason": "owned-nif-no-presentation-geometry",
                "classificationSidecar": str(non_presentation["sidecar"]),
            }
        )
    selected = retained_selected
    if not selected:
        raise ValueError(f"Cell recipe retained no presentation references: {recipe['id']}")
    first_person_rig = prepare_first_person_rig(
        meshes_path,
        texture_archive_paths,
        cache_root,
        recipe,
        configuration.content_compiler,
        owned_archives,
    )
    pool_gameplay, pool_roles = pool_gameplay_manifest(
        recipe,
        catalog,
        selected,
        assets,
        asset_sidecars,
    )
    vr_weapon_model = str(vr_loadout["modelPath"])
    vr_loadout["modelAssetId"] = assets[vr_weapon_model]["id"]
    muzzle_markers = [
        marker
        for marker in asset_sidecars[vr_weapon_model]["attachmentMarkers"]
        if marker["name"] == "ProjectileNode"
    ]
    if len(muzzle_markers) != 1:
        raise ValueError(f"VR smoke weapon must expose one ProjectileNode: {vr_weapon_model}")
    vr_loadout["muzzlePositionGodotUnits"] = muzzle_markers[0]["positionGodotUnits"]

    references = []
    for reference, base in selected:
        asset = assets[base.model_path]
        pool_role = pool_roles.get(reference.form_id)
        interaction = interaction_manifest(reference, base, catalog)
        if pool_role is not None:
            interaction = {
                "type": "pool-table" if pool_role == "table" else "pool-component",
                "role": pool_role,
            }
        references.append(
            {
                "formId": form_id(reference.form_id),
                "baseFormId": form_id(reference.base_form_id),
                "baseRecordType": base.record_type,
                "baseEditorId": base.editor_id,
                "assetId": asset["id"],
                "cellFormId": form_id(cell_form_id),
                "positionGameUnits": list(reference.transform.position),
                "positionGodotUnits": godot_position(reference.transform.position, origin),
                "yawRadians": reference.transform.rotation_radians[2],
                "yawGodotRadians": godot_yaw_radians(reference.transform.rotation_radians[2]),
                "rotationGodotQuaternion": godot_rotation_quaternion(reference.transform.rotation_radians),
                "scale": reference.scale,
                "initiallyDisabled": bool(reference.flags & INITIALLY_DISABLED_RECORD_FLAG),
                "teleportDestinationFormId": (
                    form_id(reference.teleport_destination_form_id)
                    if reference.teleport_destination_form_id is not None
                    else None
                ),
                "interaction": interaction,
            }
        )
    lights = []
    for reference in catalog.references_for(cell_form_id):
        light = catalog.lights.get(reference.base_form_id)
        if light is None:
            continue
        lights.append(
            {
                "formId": form_id(reference.form_id),
                "baseFormId": form_id(reference.base_form_id),
                "baseEditorId": light.editor_id,
                "positionGameUnits": list(reference.transform.position),
                "positionGodotUnits": godot_position(reference.transform.position, origin),
                "radiusGameUnits": light.radius,
                "radiusMeters": light.radius * units_to_meters,
                "color": normalized_rgb(light.color_rgb),
                "intensity": light.intensity,
                "falloff": light.falloff,
                "fieldOfView": light.field_of_view,
                "lightFlags": light.flags,
                "initiallyDisabled": bool(reference.flags & INITIALLY_DISABLED_RECORD_FLAG),
            }
        )

    output_path = cache_root / "generated" / "cells" / str(recipe["id"]) / "cell-scene.json"
    document = {
        "schema": CELL_SCENE_SCHEMA,
        "status": "geometry-structure",
        "recipe": str(recipe["id"]),
        "source": {
            "master": master_path.name,
            "masterSha256": master_sha256,
            "textureArchives": texture_archive_rows,
            "ownedArchiveStack": (
                owned_archives.manifest() if owned_archives is not None else None
            ),
        },
        "compiler": compiler,
        "configuration": configuration.manifest(),
        "cell": {
            "formId": form_id(cell.form_id),
            "editorId": cell.editor_id,
            "interior": cell.interior,
        },
        "coordinates": {
            "source": "Gamebryo X-right/Y-forward/Z-up, radians",
            "target": "Godot X-right/Y-up/-Z-forward",
            "unitsToMeters": units_to_meters,
            "originGameUnits": list(origin),
        },
        "spawn": {
            "sourceDoorReferenceFormId": form_id(source_door),
            "targetDoorReferenceFormId": form_id(entry_door),
            "positionGameUnits": list(arrival.position),
            "positionGodotUnits": [0.0, 0.0, 0.0],
            "yawRadians": arrival.rotation_radians[2],
            "yawGodotRadians": godot_yaw_radians(arrival.rotation_radians[2]),
        },
        "proof": {
            "doorReferenceFormId": str(recipe["portalProofDoorReferenceFormId"]),
            "visibilityModel": "whole-cell-no-portal-culling",
        },
        "firstPerson": {
            "startingLoadout": vr_loadout,
            "rig": first_person_rig,
        },
        "poolGameplay": pool_gameplay,
        "lighting": {
            "ambientColor": normalized_rgb(cell.lighting.ambient_rgb),
            "directionalColor": normalized_rgb(cell.lighting.directional_rgb),
            "fogColor": normalized_rgb(cell.lighting.fog_rgb),
            "fogNearGameUnits": cell.lighting.fog_near,
            "fogFarGameUnits": cell.lighting.fog_far,
            "directionalRotationDegrees": list(cell.lighting.directional_rotation),
            "directionalFade": cell.lighting.directional_fade,
            "fogClipDistanceGameUnits": cell.lighting.fog_clip_distance,
            "fogPower": cell.lighting.fog_power,
            "lights": lights,
        },
        "assets": sorted(assets.values(), key=lambda value: value["id"]),
        "textures": [texture_artifacts[path].manifest() for path in sorted(texture_artifacts)],
        "unresolvedTextureBindings": unresolved_texture_bindings,
        "references": references,
        "coverage": {
            "selectedReferences": len(selected),
            "sourceReferences": len(catalog.references_for(cell_form_id)),
            "exportedAssets": len(assets),
            "doors": sum(1 for _, base in selected if base.record_type == "DOOR"),
            "excludedReferences": excluded_references,
            "nonPresentationAssets": [
                non_presentation_assets[path]
                for path in sorted(non_presentation_assets)
            ],
            "excludedEditorMarkerSurfaces": sum(
                len(sidecar["coverage"]["excludedEditorMarkerSurfaces"])
                for sidecar in asset_sidecars.values()
            ),
            "excludedNonPresentationSurfaces": sum(
                len(sidecar["coverage"]["excludedNonPresentationSurfaces"])
                for sidecar in asset_sidecars.values()
            ),
            "sourcePoseBakedSkinSurfaces": sum(
                sidecar["coverage"]["sourcePoseBakedSkinSurfaces"]
                for sidecar in asset_sidecars.values()
            ),
            "collision": "authored-bhk-packed-plus-explicit-interaction-policy",
            "textures": "decoded-png-material-bindings",
            "decodedTextures": len(texture_artifacts),
            "missingOptionalMaterialTextures": unresolved_texture_bindings,
            "materialBindings": sum(len(asset["materials"]) for asset in assets.values()),
            "authoredLights": len(lights),
            "pickups": sum(
                1
                for reference in references
                if reference["interaction"] and reference["interaction"]["type"] == "pickup"
            ),
            "containers": sum(
                1
                for reference in references
                if reference["interaction"] and reference["interaction"]["type"] == "container"
            ),
        },
    }
    _atomic_json(output_path, document)
    document["output"] = str(output_path.resolve())
    return document
