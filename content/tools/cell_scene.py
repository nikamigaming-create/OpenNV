"""Prepare one recipe-pinned, data-driven interior cell scene."""

from __future__ import annotations

import hashlib
import json
import math
import os
import sys
from pathlib import Path

from bsa_archive import BsaArchive
from cell_catalog import (
    ITEM_RECORD_TYPES,
    BaseObject,
    CellCatalog,
    PlacedReference,
    Transform,
    scan_cell_catalog,
)
from export_static_nif_gltf import export_static_nif
from texture_pipeline import TexturePipeline


CELL_SCENE_SCHEMA = "opennv-cell-scene/v6"
CELL_RECIPE_SCHEMA = "opennv-cell-recipe/v1"


def form_id(value: int) -> str:
    return f"{value:08x}"


def recipe_path(recipe_id: str) -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / f"{recipe_id}.json"


def load_recipe(recipe_id: str) -> dict[str, object]:
    path = recipe_path(recipe_id)
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != CELL_RECIPE_SCHEMA or document.get("id") != recipe_id:
        raise ValueError(f"Invalid OpenNV cell recipe: {path}")
    return document


def reference_selection_reason(base: BaseObject, recipe: dict[str, object]) -> str:
    selection = recipe["selection"]
    prefixes = tuple(str(value).lower() for value in selection["modelPrefixes"])
    record_types = {str(value) for value in selection["includeBaseRecordTypes"]}
    excluded_editor_ids = {str(value) for value in selection.get("excludeBaseEditorIds", [])}
    excluded_model_prefixes = tuple(
        str(value).lower() for value in selection.get("excludeModelPrefixes", [])
    )
    if not base.model_path:
        return "no-model"
    if base.editor_id in excluded_editor_ids:
        return "editor-only-base"
    if base.model_path.startswith(excluded_model_prefixes):
        return "special-effect-shader-required"
    if base.model_path.startswith(prefixes) or base.record_type in record_types:
        return "selected"
    return "outside-recipe"


def yaw_only(transform: Transform) -> bool:
    return math.isclose(transform.rotation_radians[0], 0.0, abs_tol=1.0e-5) and math.isclose(
        transform.rotation_radians[1], 0.0, abs_tol=1.0e-5
    )


def godot_position(position: tuple[float, float, float], origin: tuple[float, float, float]) -> list[float]:
    delta = tuple(value - anchor for value, anchor in zip(position, origin))
    return [delta[0], delta[2], -delta[1]]


def godot_yaw_radians(game_yaw_radians: float) -> float:
    return -game_yaw_radians


def normalized_rgb(color: tuple[int, int, int]) -> list[float]:
    return [component / 255.0 for component in color]


def environment_texture_paths(surface: dict[str, object]) -> tuple[str | None, str | None]:
    material = surface["material"]
    if "sf_environment_mapping" not in set(material.get("shaderFlags1Enabled", [])):
        return None, None
    if "sf_2_envmap_light_fade" in set(material.get("shaderFlags2Enabled", [])):
        return None, None
    textures = surface["textures"]
    environment = textures[4] if len(textures) > 4 and textures[4] else None
    mask = textures[5] if len(textures) > 5 and textures[5] else None
    return environment, mask


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
        scale = math.sqrt(1.0 + matrix[axis][axis] - matrix[next_axis][next_axis] - matrix[last_axis][last_axis]) * 2.0
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


def interaction_manifest(
    reference: PlacedReference,
    base: BaseObject,
    catalog: CellCatalog,
) -> dict[str, object] | None:
    if base.record_type in ITEM_RECORD_TYPES:
        interaction = {
            "type": "pickup",
            "itemFormId": form_id(base.form_id),
            "itemEditorId": base.editor_id,
            "itemRecordType": base.record_type,
            "count": 1,
        }
        weapon = catalog.weapons.get(base.form_id)
        if weapon is not None:
            interaction["weapon"] = {
                "damage": weapon.damage,
                "clipSize": weapon.clip_size,
                "ammoFormId": form_id(weapon.ammo_form_id) if weapon.ammo_form_id is not None else None,
            }
        return interaction
    if base.record_type == "CONT":
        container = catalog.containers.get(base.form_id)
        items = []
        if container is not None:
            for entry in container.items:
                item = catalog.base_objects.get(entry.item_form_id)
                items.append(
                    {
                        "itemFormId": form_id(entry.item_form_id),
                        "itemEditorId": item.editor_id if item is not None else "",
                        "itemRecordType": item.record_type if item is not None else "",
                        "count": entry.count,
                        "resolved": item is not None,
                    }
                )
        return {"type": "container", "items": items}
    if base.record_type == "DOOR":
        return {"type": "door"}
    return None


def vr_smoke_loadout_manifest(
    recipe: dict[str, object],
    catalog: CellCatalog,
) -> dict[str, object]:
    configured = recipe["vrSmokeLoadout"]
    weapon_form_id = int(str(configured["weaponFormId"]), 16)
    reserve_magazines = int(configured["reserveMagazines"])
    if reserve_magazines < 1:
        raise ValueError("VR smoke loadout must retain at least one reserve magazine")
    weapon = catalog.weapons.get(weapon_form_id)
    weapon_base = catalog.base_objects.get(weapon_form_id)
    if (
        weapon is None
        or weapon_base is None
        or weapon_base.record_type != "WEAP"
        or weapon_base.model_path is None
    ):
        raise ValueError(f"VR smoke weapon is not a resolved WEAP: {weapon_form_id:08x}")
    ammo_form_id = int(str(configured.get("ammoFormId", "0")), 16)
    if ammo_form_id == 0:
        if weapon.ammo_form_id is None:
            raise ValueError(f"VR smoke weapon has no ammo form: {weapon_form_id:08x}")
        ammo_form_id = weapon.ammo_form_id
    ammo = catalog.base_objects.get(ammo_form_id)
    if ammo is None or ammo.record_type != "AMMO":
        raise ValueError(f"VR smoke ammo is not a resolved AMMO: {ammo_form_id:08x}")
    return {
        "weaponFormId": form_id(weapon_form_id),
        "weaponEditorId": weapon_base.editor_id,
        "modelPath": weapon_base.model_path,
        "ammoFormId": form_id(ammo_form_id),
        "ammoEditorId": ammo.editor_id,
        "damage": weapon.damage,
        "clipSize": weapon.clip_size,
        "reserveRounds": weapon.clip_size * reserve_magazines,
        "source": "recipe-identity-plus-retail-records",
    }


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

    archive = BsaArchive(meshes_path)
    assets: dict[str, dict[str, object]] = {}
    asset_sidecars: dict[str, dict[str, object]] = {}
    compiler: dict[str, str] | None = None
    models = sorted(
        {base.model_path for _, base in selected if base.model_path}
        | {str(vr_loadout["modelPath"])}
    )
    for model_path in models:
        logical_path = "meshes\\" + model_path
        asset_id = hashlib.sha256(logical_path.encode()).hexdigest()[:20]
        member = archive.extract(logical_path)
        source_path = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
        _atomic_bytes(source_path, member.data)
        output_root = cache_root / "generated" / "cells" / str(recipe["id"]) / "assets"
        gltf_path = output_root / f"{asset_id}.gltf"
        sidecar_path = output_root / f"{asset_id}.opennv.json"
        try:
            sidecar = export_static_nif(
                source_path,
                member.logical_path,
                gltf_path,
                sidecar_path,
                strict=False,
            )
        except Exception as error:
            raise ValueError(f"Cell asset export failed: {member.logical_path}: {error}") from error
        if compiler is None:
            compiler = sidecar["compiler"]
        elif compiler != sidecar["compiler"]:
            raise ValueError("Cell assets were produced by different compilers")
        assets[model_path] = {
            "id": asset_id,
            "logicalPath": member.logical_path,
            "sourceSha256": member.sha256,
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "surfaces": sidecar["coverage"]["surfaces"],
        }
        asset_sidecars[model_path] = sidecar

    requested_textures = sorted(
        {
            texture
            for sidecar in asset_sidecars.values()
            for surface in sidecar["surfaces"]
            for texture in surface["textures"]
            if texture
        }
    )
    texture_pipeline = TexturePipeline(
        texture_archive_paths,
        cache_root,
        {str(source): str(target) for source, target in recipe.get("textureAliases", {}).items()},
    )
    texture_artifacts = {
        requested: texture_pipeline.prepare(requested) for requested in requested_textures
    }
    for model_path, asset in assets.items():
        bindings = []
        for surface_index, surface in enumerate(asset_sidecars[model_path]["surfaces"]):
            textures = surface["textures"]
            diffuse = textures[0] if len(textures) > 0 and textures[0] else None
            normal = textures[1] if len(textures) > 1 and textures[1] else None
            emissive = textures[2] if len(textures) > 2 and textures[2] else None
            material = surface["material"]
            environment, environment_mask = environment_texture_paths(surface)
            glossiness = float(material.get("glossiness", 10.0))
            specular = [float(value) for value in material.get("specular", [0.0, 0.0, 0.0])]
            roughness = (
                1.0
                if max(specular) <= 1.0e-6
                else max(0.08, min(1.0, math.sqrt(2.0 / (glossiness + 2.0))))
            )
            unshaded = "BSShaderNoLightingProperty" in surface["propertyTypes"]
            emissive_color = [float(value) for value in material.get("emissive", [0.0, 0.0, 0.0])]
            emissive_controlled = bool(material.get("emissiveControlled", False))
            emissive_active = not unshaded and (emissive is not None or emissive_controlled)
            emission_texture = emissive if emissive_active else None
            if not emissive_active:
                emissive_color = [0.0, 0.0, 0.0]
            alpha = float(material.get("alpha", 1.0))
            bindings.append(
                {
                    "surfaceIndex": surface_index,
                    "name": surface["name"],
                    "diffuseTextureId": texture_artifacts[diffuse].asset_id if diffuse else None,
                    "normalTextureId": texture_artifacts[normal].asset_id if normal else None,
                    "emissiveTextureId": (
                        texture_artifacts[emission_texture].asset_id
                        if emission_texture
                        else None
                    ),
                    "environmentTextureId": (
                        texture_artifacts[environment].asset_id if environment else None
                    ),
                    "environmentMaskTextureId": (
                        texture_artifacts[environment_mask].asset_id
                        if environment_mask
                        else None
                    ),
                    "environmentMapScale": float(material.get("environmentMapScale", 1.0)),
                    "emissiveColor": emissive_color,
                    "emissiveReplace": emissive_controlled and emissive is None,
                    "baseColorFactor": [
                        *[float(value) for value in material.get("baseColor", [1.0, 1.0, 1.0])],
                        alpha,
                    ],
                    "roughness": roughness,
                    "alphaContract": material["alphaContract"],
                    "vertexColorMode": material["vertexColorMode"],
                    "doubleSided": int(material.get("stencilDrawMode", 1)) == 3,
                    "unshaded": unshaded,
                }
            )
        asset["materials"] = bindings

    vr_weapon_model = str(vr_loadout["modelPath"])
    vr_loadout["modelAssetId"] = assets[vr_weapon_model]["id"]
    muzzle_markers = [
        marker
        for marker in asset_sidecars[vr_weapon_model]["attachmentMarkers"]
        if marker["name"] == "ProjectileNode"
    ]
    if len(muzzle_markers) != 1:
        raise ValueError(
            f"VR smoke weapon must expose one ProjectileNode: {vr_weapon_model}"
        )
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
        "vr": {
            "startingLoadout": vr_loadout,
        },
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
        "textures": [
            texture_artifacts[path].manifest() for path in sorted(texture_artifacts)
        ],
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
            "collision": "runtime-trimesh",
            "textures": "decoded-png-material-bindings",
            "decodedTextures": len(texture_artifacts),
            "materialBindings": sum(len(asset["materials"]) for asset in assets.values()),
            "authoredLights": len(lights),
            "pickups": sum(1 for reference in references if reference["interaction"] and reference["interaction"]["type"] == "pickup"),
            "containers": sum(1 for reference in references if reference["interaction"] and reference["interaction"]["type"] == "container"),
        },
    }
    _atomic_json(output_path, document)
    document["output"] = str(output_path.resolve())
    return document
