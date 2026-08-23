"""Prepare one recipe-pinned, data-driven interior cell scene."""

from __future__ import annotations

import json
import math
import os
import sys
from pathlib import Path

from cell_catalog import BaseObject, CellCatalog, PlacedReference, Transform, scan_cell_catalog
from scene_asset_pipeline import (
    environment_texture_paths,
    form_id,
    interaction_manifest,
    prepare_scene_assets,
    reference_selection_reason,
    vr_smoke_loadout_manifest,
)


CELL_SCENE_SCHEMA = "opennv-cell-scene/v7"
CELL_RECIPE_SCHEMA = "opennv-cell-recipe/v1"
EXTERIOR_RECIPE_SCHEMA = "opennv-exterior-recipe/v1"


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
    ):
        raise ValueError(f"Invalid OpenNV cell recipe: {path}")
    return document


def yaw_only(transform: Transform) -> bool:
    return math.isclose(transform.rotation_radians[0], 0.0, abs_tol=1.0e-5) and math.isclose(
        transform.rotation_radians[1], 0.0, abs_tol=1.0e-5
    )


def godot_position(
    position: tuple[float, float, float],
    origin: tuple[float, float, float],
) -> list[float]:
    delta = tuple(value - anchor for value, anchor in zip(position, origin))
    return [delta[0], delta[2], -delta[1]]


def godot_yaw_radians(game_yaw_radians: float) -> float:
    return -game_yaw_radians


def normalized_rgb(color: tuple[int, int, int]) -> list[float]:
    return [component / 255.0 for component in color]


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
            0.25 * scale,
        ]
    else:
        axis = max(range(3), key=lambda index: matrix[index][index])
        next_axis = (axis + 1) % 3
        last_axis = (axis + 2) % 3
        scale = math.sqrt(
            1.0 + matrix[axis][axis] - matrix[next_axis][next_axis] - matrix[last_axis][last_axis]
        ) * 2.0
        components = [0.0, 0.0, 0.0, 0.0]
        components[axis] = 0.25 * scale
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


def prepare_cell_scene(
    master_path: Path,
    meshes_path: Path,
    texture_archive_paths: list[Path],
    texture_archive_rows: list[dict[str, object]],
    cache_root: Path,
    recipe: dict[str, object],
    master_sha256: str,
) -> dict[str, object]:
    expected_master = str(recipe["master"]["sha256"])
    if master_sha256 != expected_master:
        raise ValueError(f"Cell recipe master hash mismatch: expected={expected_master} actual={master_sha256}")

    catalog = scan_cell_catalog(master_path)
    vr_loadout = vr_smoke_loadout_manifest(recipe, catalog)
    cell_form_id = _find_cell(catalog, str(recipe["cellEditorId"]))
    cell = catalog.cells[cell_form_id]
    entry_door = int(str(recipe["entryDoorReferenceFormId"]), 16)
    source_door, arrival = arrival_transform(catalog, entry_door)
    origin = arrival.position
    if cell.lighting is None:
        raise ValueError(f"Cell recipe requires XCLL lighting: {cell.editor_id}")

    selected: list[tuple[PlacedReference, BaseObject]] = []
    excluded_references: list[dict[str, str]] = []
    skipped_non_yaw: list[str] = []
    allow_non_yaw_types = {
        str(value) for value in recipe["selection"].get("allowNonYawRecordTypes", [])
    }
    for reference in catalog.references_for(cell_form_id):
        base = catalog.base_objects.get(reference.base_form_id)
        if base is None:
            continue
        selection_reason = reference_selection_reason(base, recipe)
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
        if (
            bool(recipe["selection"]["yawOnly"])
            and base.record_type not in allow_non_yaw_types
            and not yaw_only(reference.transform)
        ):
            skipped_non_yaw.append(form_id(reference.form_id))
            continue
        selected.append((reference, base))
    if not selected:
        raise ValueError(f"Cell recipe selected no references: {recipe['id']}")

    assets, asset_sidecars, texture_artifacts, compiler = prepare_scene_assets(
        meshes_path,
        texture_archive_paths,
        cache_root,
        recipe,
        selected,
        {str(vr_loadout["modelPath"])},
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
        references.append(
            {
                "formId": form_id(reference.form_id),
                "baseFormId": form_id(reference.base_form_id),
                "baseRecordType": base.record_type,
                "baseEditorId": base.editor_id,
                "assetId": asset["id"],
                "positionGameUnits": list(reference.transform.position),
                "positionGodotUnits": godot_position(reference.transform.position, origin),
                "yawRadians": reference.transform.rotation_radians[2],
                "yawGodotRadians": godot_yaw_radians(reference.transform.rotation_radians[2]),
                "rotationGodotQuaternion": godot_rotation_quaternion(reference.transform.rotation_radians),
                "initiallyDisabled": bool(reference.flags & 0x00000800),
                "teleportDestinationFormId": (
                    form_id(reference.teleport_destination_form_id)
                    if reference.teleport_destination_form_id is not None
                    else None
                ),
                "interaction": interaction_manifest(reference, base, catalog),
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
                "radiusMeters": light.radius * float(recipe["unitsToMeters"]),
                "color": normalized_rgb(light.color_rgb),
                "intensity": light.intensity,
                "falloff": light.falloff,
                "fieldOfView": light.field_of_view,
                "lightFlags": light.flags,
                "initiallyDisabled": bool(reference.flags & 0x00000800),
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
        },
        "compiler": compiler,
        "cell": {
            "formId": form_id(cell.form_id),
            "editorId": cell.editor_id,
            "interior": cell.interior,
        },
        "coordinates": {
            "source": "Gamebryo X-right/Y-forward/Z-up, radians",
            "target": "Godot X-right/Y-up/-Z-forward",
            "unitsToMeters": recipe["unitsToMeters"],
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
        "vr": {"startingLoadout": vr_loadout},
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
            "calibration": recipe["lightingCalibration"],
            "lights": lights,
        },
        "assets": sorted(assets.values(), key=lambda value: value["id"]),
        "textures": [texture_artifacts[path].manifest() for path in sorted(texture_artifacts)],
        "references": references,
        "coverage": {
            "selectedReferences": len(selected),
            "exportedAssets": len(assets),
            "doors": sum(1 for _, base in selected if base.record_type == "DOOR"),
            "skippedNonYawReferences": skipped_non_yaw,
            "excludedReferences": excluded_references,
            "excludedEditorMarkerSurfaces": sum(
                len(sidecar["coverage"]["excludedEditorMarkerSurfaces"])
                for sidecar in asset_sidecars.values()
            ),
            "collision": "authored-bhk-packed-with-interaction-fallback",
            "textures": "decoded-png-material-bindings",
            "decodedTextures": len(texture_artifacts),
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
