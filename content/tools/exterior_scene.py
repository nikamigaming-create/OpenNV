"""Prepare one bounded exterior CELL plus persistent references and LAND."""

from __future__ import annotations

from pathlib import Path

from cell_catalog import BaseObject, PlacedReference, scan_cell_catalog
from cell_scene import (
    CELL_SCENE_SCHEMA,
    EXTERIOR_RECIPE_SCHEMA,
    _atomic_json,
    arrival_transform,
    godot_position,
    godot_rotation_quaternion,
    godot_yaw_radians,
    normalized_rgb,
)
from cell_catalog import INITIALLY_DISABLED_RECORD_FLAG
from landscape_catalog import scan_landscape_catalog
from landscape_gltf import export_landscape_gltf
from scene_asset_pipeline import (
    form_id,
    interaction_manifest,
    prepare_scene_assets,
    reference_selection_reason,
)
from texture_pipeline import TexturePipeline
from runtime_configuration import load_runtime_configuration


FORM_ID_RADIX = 16
EXTERIOR_CELL_SIZE_GAME_UNITS = 4096.0
LAND_VERTEX_AXIS_COUNT = 33
LAND_QUAD_AXIS_COUNT = LAND_VERTEX_AXIS_COUNT - 1


def prepare_exterior_scene(
    master_path: Path,
    meshes_path: Path,
    texture_archive_paths: list[Path],
    texture_archive_rows: list[dict[str, object]],
    cache_root: Path,
    recipe: dict[str, object],
    master_sha256: str,
) -> dict[str, object]:
    configuration = load_runtime_configuration()
    units_to_meters = configuration.world_units_to_meters
    if recipe.get("schema") != EXTERIOR_RECIPE_SCHEMA:
        raise ValueError(f"Unexpected exterior recipe schema: {recipe.get('schema')}")
    expected_master = str(recipe["master"]["sha256"])
    if master_sha256 != expected_master:
        raise ValueError(
            f"Exterior recipe master hash mismatch: expected={expected_master} actual={master_sha256}"
        )

    catalog = scan_cell_catalog(master_path)
    cell_form_id = int(str(recipe["cellFormId"]), FORM_ID_RADIX)
    persistent_cell_form_id = int(str(recipe["persistentCellFormId"]), FORM_ID_RADIX)
    worldspace_form_id = int(str(recipe["worldspaceFormId"]), FORM_ID_RADIX)
    cell = catalog.cells.get(cell_form_id)
    persistent_cell = catalog.cells.get(persistent_cell_form_id)
    if (
        cell is None
        or cell.interior
        or cell.coordinates is None
        or cell.worldspace_form_id != worldspace_form_id
        or persistent_cell is None
        or persistent_cell.interior
        or persistent_cell.worldspace_form_id != worldspace_form_id
    ):
        raise ValueError("Exterior recipe CELL/worldspace relationship is invalid")
    entry_door = int(str(recipe["entryDoorReferenceFormId"]), FORM_ID_RADIX)
    reciprocal_door = int(str(recipe["reciprocalDoorReferenceFormId"]), FORM_ID_RADIX)
    source_door, arrival = arrival_transform(catalog, entry_door)
    if source_door != reciprocal_door:
        raise ValueError(
            f"Exterior entry XTEL mismatch: expected={reciprocal_door:08x} actual={source_door:08x}"
        )
    origin = arrival.position
    cell_minimum = tuple(
        coordinate * EXTERIOR_CELL_SIZE_GAME_UNITS for coordinate in cell.coordinates
    )
    cell_maximum = tuple(
        minimum + EXTERIOR_CELL_SIZE_GAME_UNITS for minimum in cell_minimum
    )
    candidates = list(catalog.references_for(cell_form_id))
    candidates.extend(
        reference
        for reference in catalog.references_for(persistent_cell_form_id)
        if cell_minimum[0] <= reference.transform.position[0] < cell_maximum[0]
        and cell_minimum[1] <= reference.transform.position[1] < cell_maximum[1]
    )
    if len({reference.form_id for reference in candidates}) != len(candidates):
        raise ValueError("Exterior recipe selected duplicate references across CELL ownership")

    selected: list[tuple[PlacedReference, BaseObject]] = []
    excluded_references: list[dict[str, str]] = []
    for reference in candidates:
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
        selected.append((reference, base))
    if entry_door not in {reference.form_id for reference, _base in selected}:
        raise ValueError(f"Exterior scene did not select its entry door {entry_door:08x}")

    assets, asset_sidecars, texture_artifacts, compiler = prepare_scene_assets(
        meshes_path,
        texture_archive_paths,
        cache_root,
        recipe,
        selected,
        configuration.content_compiler,
    )
    landscapes = scan_landscape_catalog(master_path, {cell_form_id})
    landscape = landscapes.landscape_for_cell(cell_form_id)
    if landscape.worldspace_form_id != worldspace_form_id:
        raise ValueError("Exterior LAND belongs to another worldspace")
    terrain_pipeline = TexturePipeline(
        texture_archive_paths,
        cache_root,
        {str(source): str(target) for source, target in recipe.get("textureAliases", {}).items()},
        configuration.content_compiler,
    )
    terrain_asset, terrain_texture = export_landscape_gltf(
        landscape,
        landscapes,
        cell.coordinates,
        origin,
        terrain_pipeline,
        cache_root / "generated" / "cells" / str(recipe["id"]) / "assets",
        configuration.content_compiler,
    )

    references = []
    for reference, base in selected:
        asset = assets[base.model_path]
        references.append(
            {
                "formId": form_id(reference.form_id),
                "cellFormId": form_id(reference.cell_form_id),
                "baseFormId": form_id(reference.base_form_id),
                "baseRecordType": base.record_type,
                "baseEditorId": base.editor_id,
                "assetId": asset["id"],
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
                "interaction": interaction_manifest(reference, base, catalog),
            }
        )
    references.append(
        {
            "formId": form_id(landscape.form_id),
            "cellFormId": form_id(cell_form_id),
            "baseFormId": form_id(landscape.form_id),
            "baseRecordType": "LAND",
            "baseEditorId": f"LAND_{landscape.form_id:08x}",
            "assetId": terrain_asset["id"],
            "positionGameUnits": list(origin),
            "positionGodotUnits": [0.0, 0.0, 0.0],
            "yawRadians": 0.0,
            "yawGodotRadians": 0.0,
            "rotationGodotQuaternion": [0.0, 0.0, 0.0, 1.0],
            "scale": 1.0,
            "initiallyDisabled": False,
            "teleportDestinationFormId": None,
            "interaction": None,
        }
    )

    lights = []
    for reference in candidates:
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

    environment = configuration.document["exteriorEnvironment"]
    output_path = cache_root / "generated" / "cells" / str(recipe["id"]) / "cell-scene.json"
    all_assets = [*assets.values(), terrain_asset]
    all_textures = [
        *(texture_artifacts[path].manifest() for path in sorted(texture_artifacts)),
        terrain_texture,
    ]
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
        "configuration": configuration.manifest(),
        "cell": {
            "formId": form_id(cell.form_id),
            "editorId": str(recipe["editorId"]),
            "interior": False,
            "worldspaceFormId": form_id(worldspace_form_id),
            "sourceCellFormIds": [form_id(cell_form_id), form_id(persistent_cell_form_id)],
        },
        "coordinates": {
            "source": "Gamebryo X-right/Y-forward/Z-up, radians",
            "target": "Godot X-right/Y-up/-Z-forward",
            "unitsToMeters": units_to_meters,
            "originGameUnits": list(origin),
            "grid": list(cell.coordinates),
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
            "doorReferenceFormId": form_id(entry_door),
            "visibilityModel": "linked-authored-space",
        },
        "portal": {
            "sourceCellFormId": form_id(cell_form_id),
            "persistentCellFormId": form_id(persistent_cell_form_id),
            "entryDoorReferenceFormId": form_id(entry_door),
            "reciprocalDoorReferenceFormId": form_id(reciprocal_door),
        },
        "lighting": {
            "mode": environment["mode"],
            "ambientColor": environment["ambientColor"],
            "directionalColor": environment["directionalColor"],
            "fogColor": environment["fogColor"],
            "fogNearGameUnits": environment["fogNearGameUnits"],
            "fogFarGameUnits": environment["fogFarGameUnits"],
            "directionalRotationDegrees": environment["directionalRotationDegrees"],
            "directionalFade": environment["directionalFade"],
            "fogClipDistanceGameUnits": environment["fogFarGameUnits"],
            "fogPower": environment["fogPower"],
            "lights": lights,
        },
        "assets": sorted(all_assets, key=lambda value: value["id"]),
        "textures": sorted(all_textures, key=lambda value: value["id"]),
        "references": references,
        "coverage": {
            "selectedReferences": len(selected),
            "sourceReferences": len(candidates),
            "exportedAssets": len(all_assets),
            "doors": sum(1 for _reference, base in selected if base.record_type == "DOOR"),
            "excludedReferences": excluded_references,
            "excludedEditorMarkerSurfaces": sum(
                len(sidecar["coverage"]["excludedEditorMarkerSurfaces"])
                for sidecar in asset_sidecars.values()
            ),
            "collision": "authored-bhk-packed-plus-LAND-height-grid",
            "textures": "decoded-png-plus-LAND-layer-bake",
            "decodedTextures": len(all_textures),
            "materialBindings": sum(len(asset["materials"]) for asset in all_assets),
            "authoredLights": len(lights),
            "landscape": {
                "formId": form_id(landscape.form_id),
                "compressionChecksumValid": landscape.compression_checksum_valid,
                "vertices": LAND_VERTEX_AXIS_COUNT * LAND_VERTEX_AXIS_COUNT,
                "triangles": LAND_QUAD_AXIS_COUNT * LAND_QUAD_AXIS_COUNT * 2,
                "baseLayers": len(landscape.base_layers),
                "alphaLayers": len(landscape.alpha_layers),
            },
        },
    }
    _atomic_json(output_path, document)
    document["output"] = str(output_path.resolve())
    return document
