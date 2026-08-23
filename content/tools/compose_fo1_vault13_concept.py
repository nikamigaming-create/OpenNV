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
        offset = [float(value) for value in recipe["placement"]["presentationOffsetGodotUnits"]]
        if len(offset) != 3:
            raise ValueError("Vault door presentation offset must contain three values")
        reference["positionGodotUnits"] = [
            float(value) + offset[index] for index, value in enumerate(original_position)
        ]
        original_game_position = list(reference["positionGameUnits"])
        reference["positionGameUnits"] = [
            float(original_game_position[0]) + offset[0],
            float(original_game_position[1]) - offset[2],
            float(original_game_position[2]) + offset[1],
        ]
        reference["presentationMapping"] = {
            "sourceDoorSerial": proof_recipe["sourceDoorSerial"],
            "targetBaseFormId": proof_recipe["targetBaseFormId"],
            "targetEditorId": proof_recipe["targetEditorId"],
            "originalPositionGodotUnits": original_position,
            "presentationOffsetGodotUnits": offset,
            "claim": recipe["placement"]["claim"],
        }

    mapped_door = next(reference for reference in references if reference["formId"] == reference_id)
    accent = recipe["lighting"]["doorAccent"]
    accent_offset = [float(value) for value in accent["positionOffsetGodotUnits"]]
    if len(accent_offset) != 3:
        raise ValueError("Vault door accent-light offset must contain three values")
    accent_position = [
        float(value) + accent_offset[index]
        for index, value in enumerate(mapped_door["positionGodotUnits"])
    ]
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
    composition = {
        "schema": RECIPE_SCHEMA,
        "id": recipe["id"],
        "status": "renderable-concept",
        "donorSceneSha256": sha256_path(donor_scene_path),
        "doorProofSha256": sha256_path(door_manifest_path),
        "sourceDoor": proof["sourceObjectContract"]["door"],
        "placement": recipe["placement"],
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
