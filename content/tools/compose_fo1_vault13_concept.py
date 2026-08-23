#!/usr/bin/env python3
"""Compose a bounded Vault 13 entrance concept from verified owned-data caches."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import shutil
import tempfile
from pathlib import Path


RECIPE_SCHEMA = "opennv-fo1-concept-composition/v1"
MANIFEST_SCHEMA = "opennv-fo1-concept-cache/v1"


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def distance(first: list[float], second: list[float]) -> float:
    return math.sqrt(sum((float(left) - float(right)) ** 2 for left, right in zip(first, second)))


def gltf_bounds(path: Path) -> dict[str, list[float]]:
    document = read_json(path)
    minimums = []
    maximums = []
    for mesh in document.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            position_accessor = primitive.get("attributes", {}).get("POSITION")
            if position_accessor is None:
                continue
            accessor = document["accessors"][position_accessor]
            if "min" not in accessor or "max" not in accessor:
                raise ValueError(f"glTF POSITION accessor lacks bounds: {path}")
            minimums.append([float(value) for value in accessor["min"]])
            maximums.append([float(value) for value in accessor["max"]])
    if not minimums:
        raise ValueError(f"glTF has no bounded POSITION accessor: {path}")
    minimum = [min(row[index] for row in minimums) for index in range(3)]
    maximum = [max(row[index] for row in maximums) for index in range(3)]
    return {
        "minimum": minimum,
        "maximum": maximum,
        "size": [maximum[index] - minimum[index] for index in range(3)],
        "center": [(minimum[index] + maximum[index]) / 2.0 for index in range(3)],
    }


def quaternion_rotate(vector: list[float], quaternion: list[float]) -> list[float]:
    if len(vector) != 3 or len(quaternion) != 4:
        raise ValueError("Godot vector/quaternion dimensions are invalid")
    x, y, z, w = (float(value) for value in quaternion)
    length = math.sqrt(x * x + y * y + z * z + w * w)
    if length <= 1.0e-12:
        raise ValueError("Godot placement quaternion has zero length")
    x, y, z, w = x / length, y / length, z / length, w / length
    vx, vy, vz = (float(value) for value in vector)
    tx = 2.0 * (y * vz - z * vy)
    ty = 2.0 * (z * vx - x * vz)
    tz = 2.0 * (x * vy - y * vx)
    return [
        vx + w * tx + (y * tz - z * ty),
        vy + w * ty + (z * tx - x * tz),
        vz + w * tz + (x * ty - y * tx),
    ]


def add_vector(first: list[float], second: list[float]) -> list[float]:
    return [float(first[index]) + float(second[index]) for index in range(3)]


def compose(
    recipe_path: Path,
    donor_scene_path: Path,
    door_manifest_path: Path,
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise ValueError(f"refusing to overwrite concept cache: {output_root}")
    recipe = read_json(recipe_path)
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise ValueError(f"unexpected concept recipe: {recipe_path}")
    donor = read_json(donor_scene_path)
    donor_recipe = recipe["donor"]
    for key, actual in (
        ("sceneSchema", donor.get("schema")),
        ("recipe", donor.get("recipe")),
        ("cellFormId", donor["cell"]["formId"]),
        ("cellEditorId", donor["cell"]["editorId"]),
    ):
        if actual != donor_recipe[key]:
            raise ValueError(f"donor {key} drift: expected {donor_recipe[key]!r}, got {actual!r}")

    proof = read_json(door_manifest_path)
    proof_recipe = recipe["doorProof"]
    if proof.get("schema") != proof_recipe["schema"]:
        raise ValueError("unexpected Vault door proof schema")
    if proof["recipe"]["id"] != proof_recipe["recipeId"]:
        raise ValueError("Vault door proof recipe drift")
    if proof["sourceObjectContract"]["door"]["serial"] != proof_recipe["sourceDoorSerial"]:
        raise ValueError("Vault door source serial drift")
    if proof["target"]["baseFormId"] != proof_recipe["targetBaseFormId"]:
        raise ValueError("Vault door target FormID drift")
    if proof["target"]["editorId"] != proof_recipe["targetEditorId"]:
        raise ValueError("Vault door target EDID drift")

    model_path = Path(proof["outputs"]["model"])
    sidecar_path = Path(proof["outputs"]["sidecar"])
    materials_path = Path(proof["outputs"]["materialManifest"])
    if sha256_path(model_path) != proof["outputs"]["modelSha256"]:
        raise ValueError("Vault door glTF hash drift")
    if sha256_path(materials_path) != proof["outputs"]["materialManifestSha256"]:
        raise ValueError("Vault door material-manifest hash drift")
    sidecar = read_json(sidecar_path)
    materials = read_json(materials_path)
    if sidecar["compiler"] != donor["compiler"]:
        raise ValueError("Vault door and cave donor compiler provenance differ")
    if sidecar["source"]["sha256"] != proof["target"]["sourceNifSha256"]:
        raise ValueError("Vault door source NIF identity drift")
    if materials.get("schema") != "opennv-static-material-manifest/v1":
        raise ValueError("unexpected Vault door material manifest")

    reference_id = donor_recipe["replaceReferenceFormId"]
    source_reference = next(
        (row for row in donor["references"] if row["formId"] == reference_id),
        None,
    )
    if source_reference is None:
        raise ValueError(f"donor entrance reference is absent: {reference_id}")
    if source_reference["baseFormId"] != donor_recipe["replaceReferenceBaseFormId"]:
        raise ValueError("donor entrance base FormID drift")
    if source_reference["baseEditorId"] != donor_recipe["replaceReferenceBaseEditorId"]:
        raise ValueError("donor entrance base EDID drift")

    mount_reference = next(
        (
            row
            for row in donor["references"]
            if row["formId"] == donor_recipe["mountReferenceFormId"]
        ),
        None,
    )
    if mount_reference is None:
        raise ValueError("donor cave-entrance mount reference is absent")
    if mount_reference["baseFormId"] != donor_recipe["mountReferenceBaseFormId"]:
        raise ValueError("donor cave-entrance mount base FormID drift")
    if mount_reference["baseEditorId"] != donor_recipe["mountReferenceBaseEditorId"]:
        raise ValueError("donor cave-entrance mount EDID drift")
    donor_assets = {asset["id"]: asset for asset in donor["assets"]}
    mount_asset = donor_assets[mount_reference["assetId"]]
    if mount_asset["logicalPath"] != donor_recipe["mountLogicalPath"]:
        raise ValueError("donor cave-entrance mount model drift")

    door_asset_id = hashlib.sha256(
        (recipe["id"] + proof["target"]["sourceNifSha256"]).encode("utf-8")
    ).hexdigest()[:20]
    door_asset = {
        "id": door_asset_id,
        "logicalPath": proof["target"]["logicalPath"],
        "model": str(model_path.resolve()),
        "sidecar": str(sidecar_path.resolve()),
        "sourceSha256": proof["target"]["sourceNifSha256"],
        "surfaces": sidecar["coverage"]["surfaces"],
        "materials": materials["asset"]["materials"],
    }

    mount_bounds = gltf_bounds(Path(mount_asset["model"]))
    door_bounds = gltf_bounds(model_path)
    mount_quaternion = [float(value) for value in mount_reference["rotationGodotQuaternion"]]
    local_y = mount_bounds["minimum"][1] + door_bounds["size"][1] / 2.0
    local_x = mount_bounds["center"][0]
    mouth_candidates = [
        {
            "name": "minimum-z",
            "localCenter": [local_x, local_y, mount_bounds["minimum"][2]],
            "outwardYawOffsetRadians": math.pi,
        },
        {
            "name": "maximum-z",
            "localCenter": [local_x, local_y, mount_bounds["maximum"][2]],
            "outwardYawOffsetRadians": 0.0,
        },
    ]
    for candidate in mouth_candidates:
        candidate["worldCenter"] = add_vector(
            mount_reference["positionGodotUnits"],
            quaternion_rotate(candidate["localCenter"], mount_quaternion),
        )
        candidate["distanceToDonorDoor"] = distance(
            [candidate["worldCenter"][0], candidate["worldCenter"][2]],
            [source_reference["positionGodotUnits"][0], source_reference["positionGodotUnits"][2]],
        )
    selected_mouth = min(mouth_candidates, key=lambda row: row["distanceToDonorDoor"])
    target_center = selected_mouth["worldCenter"]
    rotated_door_center = quaternion_rotate(door_bounds["center"], mount_quaternion)
    door_position = [
        float(target_center[index]) - rotated_door_center[index] for index in range(3)
    ]

    radius = float(recipe["selection"]["radiusGodotUnits"])
    entrance_position = source_reference["positionGodotUnits"]
    references = [
        dict(reference)
        for reference in donor["references"]
        if distance(reference["positionGodotUnits"], entrance_position) <= radius
    ]
    if not references or not any(reference["formId"] == reference_id for reference in references):
        raise ValueError("bounded donor selection omitted the entrance door")
    for reference in references:
        if reference["formId"] != reference_id:
            continue
        reference["assetId"] = door_asset_id
        original_position = list(reference["positionGodotUnits"])
        reference["positionGodotUnits"] = door_position
        reference["yawGodotRadians"] = float(mount_reference["yawGodotRadians"])
        reference["yawRadians"] = -float(mount_reference["yawGodotRadians"])
        reference["rotationGodotQuaternion"] = mount_quaternion
        origin = [float(value) for value in donor["coordinates"]["originGameUnits"]]
        reference["positionGameUnits"] = [
            origin[0] + door_position[0],
            origin[1] - door_position[2],
            origin[2] + door_position[1],
        ]
        reference["presentationMapping"] = {
            "sourceDoorSerial": proof_recipe["sourceDoorSerial"],
            "targetBaseFormId": proof_recipe["targetBaseFormId"],
            "targetEditorId": proof_recipe["targetEditorId"],
            "originalPositionGodotUnits": original_position,
            "mountReferenceFormId": mount_reference["formId"],
            "mountAssetId": mount_reference["assetId"],
            "mountBounds": mount_bounds,
            "doorBounds": door_bounds,
            "selectedMouth": selected_mouth,
            "resolvedDoorCenterGodotUnits": target_center,
            "claim": recipe["placement"]["claim"],
        }

    mapped_door = next(reference for reference in references if reference["formId"] == reference_id)
    accent = recipe["lighting"]["doorAccent"]
    accent_offset = [float(value) for value in accent["positionOffsetMountLocalUnits"]]
    if len(accent_offset) != 3:
        raise ValueError("Vault door accent-light offset must contain three values")
    if selected_mouth["name"] == "minimum-z":
        accent_offset[2] *= -1.0
    accent_position = add_vector(
        target_center,
        quaternion_rotate(accent_offset, mount_quaternion),
    )
    unit_scale = float(donor["coordinates"]["unitsToMeters"])
    accent_light = {
        "formId": "fo1-v13-door-accent",
        "baseFormId": "authored-concept-light",
        "baseEditorId": "FO1Vault13DoorAccent",
        "positionGameUnits": None,
        "positionGodotUnits": accent_position,
        "radiusGameUnits": float(accent["radiusMeters"]) / unit_scale,
        "radiusMeters": float(accent["radiusMeters"]),
        "color": [float(value) for value in accent["color"]],
        "intensity": float(accent["intensity"]),
        "falloff": 1.0,
        "fieldOfView": 0.0,
        "lightFlags": 0,
        "initiallyDisabled": False,
        "provenance": "authored concept lighting; not retail or Fallout 1 parity",
    }
    lighting = {
        **donor["lighting"],
        "lights": [*donor["lighting"]["lights"], accent_light],
    }

    textures_by_id = {texture["id"]: texture for texture in donor["textures"]}
    for texture in materials["textures"]:
        existing = textures_by_id.get(texture["id"])
        if existing is not None and existing != texture:
            raise ValueError(f"texture identity collision: {texture['id']}")
        textures_by_id[texture["id"]] = texture

    assets = [asset for asset in donor["assets"] if asset["id"] != door_asset_id]
    assets.append(door_asset)
    camera_recipe = recipe["camera"]
    camera_yaw = (
        float(mount_reference["yawGodotRadians"])
        + float(selected_mouth["outwardYawOffsetRadians"])
        + math.radians(float(camera_recipe["outwardYawOffsetDegrees"]))
    )
    camera = {
        "focusGodotUnits": target_center,
        "homeSizeMeters": float(camera_recipe["homeSizeMeters"]),
        "yawGodotRadians": camera_yaw,
        "pitchDegrees": float(camera_recipe["pitchDegrees"]),
        "source": "resolved donor entrance mount plus explicit presentation angles",
    }
    composition = {
        "schema": RECIPE_SCHEMA,
        "id": recipe["id"],
        "status": "renderable-concept",
        "donorSceneSha256": sha256_path(donor_scene_path),
        "doorProofSha256": sha256_path(door_manifest_path),
        "sourceDoor": proof["sourceObjectContract"]["door"],
        "placement": recipe["placement"],
        "resolvedMount": mapped_door["presentationMapping"],
        "camera": camera,
        "hudObjective": recipe["hud"]["objective"],
        "selectionRadiusGodotUnits": radius,
        "unsupported": recipe["unsupported"],
    }
    scene = {
        **donor,
        "concept": composition,
        "lighting": lighting,
        "assets": sorted(assets, key=lambda row: row["id"]),
        "textures": [textures_by_id[key] for key in sorted(textures_by_id)],
        "references": references,
        "coverage": {
            **donor["coverage"],
            "selectedReferences": len(references),
            "exportedAssets": len(assets),
            "decodedTextures": len(textures_by_id),
            "materialBindings": sum(len(asset["materials"]) for asset in assets),
            "authoredLights": len(lighting["lights"]),
            "composition": "bounded-donor-cave-plus-vault13-door-static-pose",
        },
    }

    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=output_root.name + ".", dir=output_root.parent))
    try:
        scene_path = staging / "cell-scene.json"
        write_json(scene_path, scene)
        manifest = {
            "schema": MANIFEST_SCHEMA,
            "status": "renderable-concept",
            "recipe": {"id": recipe["id"], "sha256": sha256_path(recipe_path)},
            "inputs": {
                "donorScene": str(donor_scene_path.resolve()),
                "donorSceneSha256": sha256_path(donor_scene_path),
                "doorProof": str(door_manifest_path.resolve()),
                "doorProofSha256": sha256_path(door_manifest_path),
            },
            "output": {
                "cellScene": str((output_root / "cell-scene.json").resolve()),
                "cellSceneSha256": sha256_path(scene_path),
                "references": len(references),
                "assets": len(assets),
                "textures": len(textures_by_id),
            },
            "sourceDoor": proof["sourceObjectContract"]["door"],
            "unsupported": recipe["unsupported"],
        }
        write_json(staging / "concept-manifest.json", manifest)
        os.replace(staging, output_root)
        return manifest
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--donor-scene", type=Path, required=True)
    parser.add_argument("--door-proof", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    manifest = compose(
        args.recipe.resolve(),
        args.donor_scene.resolve(),
        args.door_proof.resolve(),
        args.output_root.resolve(),
    )
    print(json.dumps(manifest, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
