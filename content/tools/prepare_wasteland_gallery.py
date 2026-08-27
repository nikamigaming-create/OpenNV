#!/usr/bin/env python3
"""Compile one declarative owned-data gallery from a legally owned install."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from cell_scene import load_spatial_recipe, prepare_cell_scene
from export_static_nif_gltf import compiler_provenance
from exterior_scene import prepare_exterior_scene
from gltf_io import compiler_sources_sha256
from owned_archive_stack import OwnedArchiveStack
from prepare_actor import (
    ActorPreparationContext,
    create_actor_preparation_context,
    load_recipe as load_actor_recipe,
    prepare_actor,
)
from prepare_creature_review import default_archive_recipe_path
from prepare_gallery_creature import (
    GalleryCreaturePreparationContext,
    create_gallery_creature_preparation_context,
    prepare_gallery_creature,
)
from runtime_configuration import load_runtime_configuration


GALLERY_SCHEMA = "opennv-owned-gallery/v4"
OUTPUT_SCHEMA = "opennv-owned-gallery-compiled/v5"
SHOT_SCHEMA = "opennv-gallery-shot/v5"
LOCATION_CONTRACT_SCHEMA = "opennv-gallery-location-contract/v2"
FORM_ID_PATTERN = re.compile(r"^[0-9a-fA-F]{8}$")
EXTERIOR_DOOR_CONTRACT_FIELDS = (
    "entryDoorReferenceFormId",
    "reciprocalDoorReferenceFormId",
)
SCENE_COMPILERS = {
    "interior": prepare_cell_scene,
    "exterior": prepare_exterior_scene,
}


def _validate_npc_subject(subject: dict[str, object]) -> None:
    if "allowedUnsupportedGeometryTypes" in subject:
        raise ValueError(
            "NPC gallery subjects cannot declare creature geometry omissions"
        )


def _validate_creature_subject(subject: dict[str, object]) -> None:
    if not isinstance(subject.get("allowedUnsupportedGeometryTypes"), list):
        raise ValueError(
            "Creature gallery subjects require an explicit unsupported-geometry ledger"
        )


SUBJECT_VALIDATORS: dict[str, Callable[[dict[str, object]], None]] = {
    "npc": _validate_npc_subject,
    "creature": _validate_creature_subject,
}


def _sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _document_sha256(document: object) -> str:
    payload = json.dumps(
        document,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _verified_descriptor_file(
    descriptor: dict[str, object],
    label: str,
) -> tuple[Path, dict[str, object]]:
    path = Path(str(descriptor.get("path", ""))).resolve()
    if not path.is_file():
        raise FileNotFoundError(f"{label} is missing: {path}")
    actual = {
        "path": str(path),
        "bytes": path.stat().st_size,
        "sha256": _sha256(path),
    }
    if (
        actual["bytes"] != int(descriptor.get("bytes", -1))
        or actual["sha256"].lower()
        != str(descriptor.get("sha256", "")).lower()
    ):
        raise ValueError(f"{label} differs from its retail evidence descriptor: {path}")
    return path, actual


def _retail_grass_observation(
    retail_evidence_descriptor: dict[str, object],
) -> tuple[Path, dict[str, object]]:
    evidence_path, _ = _verified_descriptor_file(
        retail_evidence_descriptor,
        "Gallery retail evidence",
    )
    evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
    retail = evidence.get("retail")
    if not isinstance(retail, dict) or not isinstance(retail.get("oracleJsonl"), dict):
        raise ValueError(f"Gallery retail evidence has no oracle JSONL: {evidence_path}")
    return _verified_descriptor_file(
        retail["oracleJsonl"],
        "Gallery retail grass observation",
    )


def _location_scene_key(
    location: dict[str, object],
    subject: dict[str, object] | None,
) -> str:
    location_id = str(location["id"])
    if str(location["locationClass"]) == "interior":
        return location_id
    if subject is None:
        raise ValueError(f"Exterior gallery location requires one subject: {location_id}")
    return f"{location_id}--{subject['id']}"


def _subject_location_recipe(
    recipe: dict[str, object],
    location: dict[str, object],
    subject: dict[str, object] | None,
) -> dict[str, object]:
    result = copy.deepcopy(recipe)
    if str(location["locationClass"]) == "exterior":
        if subject is None:
            raise ValueError("Exterior gallery recipe requires one subject")
        result["id"] = f"{recipe['id']}--{subject['id']}"
    return result


def _gallery_compiler_sha256() -> str:
    return compiler_sources_sha256(
        sorted(Path(__file__).resolve().parent.glob("*.py"))
    )


def _atomic_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    os.replace(temporary, path)


def _load_gallery(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if (
        not isinstance(document, dict)
        or document.get("schema") != GALLERY_SCHEMA
        or document.get("status") != "owned-authored-placement-non-parity"
    ):
        raise ValueError(f"Unexpected OpenNV owned-data gallery: {path}")
    expected_subject_count = document.get("expectedSubjectCount")
    if not isinstance(expected_subject_count, int) or expected_subject_count <= 0:
        raise ValueError("Gallery expectedSubjectCount must be positive")
    subjects = document.get("subjects")
    if not isinstance(subjects, list) or len(subjects) != expected_subject_count:
        raise ValueError(
            "Gallery subject count differs from its declared contract"
        )
    ordinals = [int(subject["ordinal"]) for subject in subjects]
    if ordinals != list(range(1, expected_subject_count + 1)):
        raise ValueError("Gallery ordinals must be contiguous and ordered")
    ids = [str(subject["id"]) for subject in subjects]
    if len(set(ids)) != expected_subject_count:
        raise ValueError("Gallery subject IDs must be unique")
    profiles = document.get("sceneProfiles")
    subject_profiles = document.get("subjectProfiles")
    locations = document.get("locations")
    if not isinstance(profiles, dict) or not profiles:
        raise ValueError("Gallery sceneProfiles must be nonempty")
    if not isinstance(locations, list) or not locations:
        raise ValueError("Gallery locations must be nonempty")
    if not isinstance(subject_profiles, dict) or not subject_profiles:
        raise ValueError("Gallery subjectProfiles must be nonempty")
    for profile_id, profile in profiles.items():
        if (
            not isinstance(profile, dict)
            or not str(profile_id).strip()
            or str(profile.get("compiler", "")) not in SCENE_COMPILERS
            or not str(profile.get("templateRecipe", "")).strip()
            or not str(profile.get("locationClass", "")).strip()
        ):
            raise ValueError(f"Gallery scene profile is invalid: {profile_id}")
    for profile_id, profile in subject_profiles.items():
        if (
            not isinstance(profile, dict)
            or not str(profile_id).strip()
            or str(profile.get("compiler", "")) not in SUBJECT_COMPILERS
            or not str(profile.get("recordType", "")).strip()
        ):
            raise ValueError(f"Gallery subject profile is invalid: {profile_id}")
    for location in locations:
        scene = location.get("scene") if isinstance(location, dict) else None
        if (
            not isinstance(scene, dict)
            or str(scene.get("profile", "")) not in profiles
            or not str(scene.get("recipeId", "")).strip()
            or not str(scene.get("expectedCellFormId", "")).strip()
            or not isinstance(scene.get("expectedInterior"), bool)
            or not isinstance(scene.get("overrides"), dict)
            or not isinstance(scene.get("removeFields"), list)
            or not str(location.get("actorCellFormId", "")).strip()
        ):
            raise ValueError(f"Gallery location scene contract is invalid: {location}")
        profile = profiles[str(scene["profile"])]
        if str(location.get("locationClass", "")) != str(profile["locationClass"]):
            raise ValueError("Gallery location class differs from its scene profile")
        if str(location["locationClass"]) == "exterior":
            overrides = scene["overrides"]
            removed_fields = set(scene["removeFields"])
            if any(
                field in removed_fields
                or FORM_ID_PATTERN.fullmatch(str(overrides.get(field, ""))) is None
                for field in EXTERIOR_DOOR_CONTRACT_FIELDS
            ):
                raise ValueError(
                    "Exterior gallery locations require an explicit authored XTEL door pair"
                )
    for subject in subjects:
        profile = subject_profiles.get(str(subject.get("profile", "")))
        if (
            not isinstance(profile, dict)
            or str(profile.get("compiler", "")) not in SUBJECT_VALIDATORS
            or str(profile.get("recordType", "")).strip() == ""
            or str(subject.get("profile", "")).strip() == ""
            or str(subject.get("referenceFormId", "")).strip() == ""
            or str(subject.get("baseFormId", "")).strip() == ""
            or not isinstance(subject.get("enableState"), dict)
            or not str(subject["enableState"].get("mode", "")).strip()
            or not str(subject.get("outputFile", "")).strip()
        ):
            raise ValueError(f"Gallery subject output/state contract is invalid: {subject}")
        SUBJECT_VALIDATORS[str(profile["compiler"])](subject)
    return document


def _gallery_form_id(value: object, label: str) -> str:
    text = str(value).strip()
    if FORM_ID_PATTERN.fullmatch(text) is None:
        raise ValueError(f"Gallery {label} is not one eight-digit FormID: {value}")
    return text.lower()


def _gallery_scene_identity(location: dict[str, object]) -> dict[str, object]:
    location_class = str(location["locationClass"])
    scene = location["scene"]
    interior = bool(scene["expectedInterior"])
    worldspace_value = scene.get("expectedWorldspaceFormId")
    worldspace = (
        None
        if worldspace_value is None
        else _gallery_form_id(worldspace_value, "rendered scene WRLD FormID")
    )
    if (
        interior != (location_class == "interior")
        or (interior and worldspace is not None)
        or (not interior and worldspace is None)
    ):
        raise ValueError(
            f"Gallery location has an inconsistent rendered CELL/WRLD identity: "
            f"{location['id']}"
        )
    return {
        "cellFormId": _gallery_form_id(
            scene["expectedCellFormId"], "rendered scene CELL FormID"
        ),
        "worldspaceFormId": worldspace,
        "interior": interior,
    }


def _gallery_shot_identity(
    subject: dict[str, object],
    subject_profile: dict[str, object],
    location: dict[str, object],
) -> dict[str, object]:
    enable_state = subject["enableState"]
    if str(enable_state.get("mode", "")) not in (
        "authored",
        "proof-enable-initially-disabled",
    ):
        raise ValueError(f"Gallery subject has an invalid enable state: {subject['id']}")
    output_file = str(subject["outputFile"])
    if Path(output_file).name != output_file:
        raise ValueError(f"Gallery subject output is not one file name: {subject['id']}")
    return {
        "id": str(subject["id"]),
        "ordinal": int(subject["ordinal"]),
        "label": str(subject["label"]),
        "locationId": str(subject["locationId"]),
        "location": str(location["location"]),
        "locationClass": str(location["locationClass"]),
        "referenceFormId": _gallery_form_id(
            subject["referenceFormId"], "authored reference FormID"
        ),
        "baseFormId": _gallery_form_id(subject["baseFormId"], "base FormID"),
        "actor": {
            "cellFormId": _gallery_form_id(
                location["actorCellFormId"], "actor-owned CELL FormID"
            ),
        },
        "scene": _gallery_scene_identity(location),
        "recordType": str(subject_profile["recordType"]),
        "enableState": copy.deepcopy(enable_state),
        "outputFile": output_file,
    }


def _data_file(data_root: Path, name: str, expected_hash: str) -> Path:
    path = data_root / name
    if not path.is_file():
        raise FileNotFoundError(path)
    actual = _sha256(path)
    if actual.lower() != expected_hash.lower():
        raise ValueError(
            f"Owned gallery source hash mismatch: {name} "
            f"expected={expected_hash} actual={actual}"
        )
    return path


def _merge_overlay(target: dict[str, object], overlay: dict[str, object]) -> None:
    for key, value in overlay.items():
        current = target.get(key)
        if isinstance(current, dict) and isinstance(value, dict):
            _merge_overlay(current, value)
        else:
            target[key] = copy.deepcopy(value)


def _location_recipe(
    location: dict[str, object],
    scene_profiles: dict[str, dict[str, object]],
) -> tuple[dict[str, object], dict[str, object]]:
    scene_contract = location["scene"]
    profile = scene_profiles[str(scene_contract["profile"])]
    recipe = copy.deepcopy(profile["template"])
    _merge_overlay(recipe, scene_contract["overrides"])
    for field in scene_contract["removeFields"]:
        if not isinstance(field, str) or not field or "." in field:
            raise ValueError(f"Gallery removeFields entry is invalid: {field!r}")
        recipe.pop(field, None)
    if str(recipe.get("id", "")) != str(scene_contract["recipeId"]):
        raise ValueError("Gallery scene recipe ID differs from its contract")
    return profile, recipe


def _seal_location_scene(
    scene: dict[str, object],
    scene_path: Path,
    location: dict[str, object],
    profile: dict[str, object],
    recipe: dict[str, object],
    configuration_sha256: str,
    gallery_compiler_sha256: str,
    manifest_key: str,
    subject_id: str | None,
    retail_grass_observation: dict[str, object] | None,
) -> dict[str, object]:
    scene_contract = location["scene"]
    source = scene.get("source")
    archive_stack = source.get("ownedArchiveStack") if isinstance(source, dict) else None
    if not isinstance(archive_stack, dict):
        raise ValueError(
            f"Gallery location has no owned archive-stack provenance: {location['id']}"
        )
    contract = {
        "schema": LOCATION_CONTRACT_SCHEMA,
        "manifestKey": manifest_key,
        "locationId": str(location["id"]),
        "subjectId": subject_id,
        "sceneProfile": str(scene_contract["profile"]),
        "sceneCompiler": str(profile["compiler"]),
        "sceneContractSha256": _document_sha256(scene_contract),
        "mergedRecipeSha256": _document_sha256(recipe),
        "runtimeConfigurationSha256": configuration_sha256,
        "galleryCompilerSha256": gallery_compiler_sha256,
        "ownedArchiveStackSha256": _document_sha256(archive_stack),
        "retailGrassObservation": copy.deepcopy(retail_grass_observation),
    }
    scene["galleryLocationContract"] = contract
    _atomic_json(scene_path, scene)
    return contract


def _compile_location(
    location: dict[str, object],
    profile: dict[str, object],
    recipe: dict[str, object],
    master: Path,
    meshes: Path,
    textures: list[Path],
    texture_rows: list[dict[str, object]],
    cache_root: Path,
    master_hash: str,
    configuration_sha256: str,
    gallery_compiler_sha256: str,
    owned_archives: OwnedArchiveStack,
    manifest_key: str,
    subject_id: str | None = None,
    retail_grass_observation_path: Path | None = None,
    retail_grass_observation: dict[str, object] | None = None,
) -> dict[str, object]:
    compiler = SCENE_COMPILERS[str(profile["compiler"])]
    if str(profile["compiler"]) == "exterior":
        if retail_grass_observation_path is None or retail_grass_observation is None:
            raise ValueError(
                f"Exterior gallery scene has no shot-bound retail grass evidence: {manifest_key}"
            )
        scene = compiler(
            master,
            meshes,
            textures,
            texture_rows,
            cache_root,
            recipe,
            master_hash,
            retail_grass_observation=retail_grass_observation_path,
            retail_grass_render_state_observation=retail_grass_observation_path,
            owned_archives=owned_archives,
        )
    else:
        if retail_grass_observation_path is not None or retail_grass_observation is not None:
            raise ValueError(
                f"Interior gallery scene unexpectedly received grass evidence: {manifest_key}"
            )
        scene = compiler(
            master,
            meshes,
            textures,
            texture_rows,
            cache_root,
            recipe,
            master_hash,
            owned_archives=owned_archives,
        )
    scene_path = Path(str(scene.pop("output")))
    contract = _seal_location_scene(
        scene,
        scene_path,
        location,
        profile,
        recipe,
        configuration_sha256,
        gallery_compiler_sha256,
        manifest_key,
        subject_id,
        retail_grass_observation,
    )
    return {
        "recipe": str(recipe["id"]),
        "recipeSha256": _document_sha256(recipe),
        "scene": str(scene_path.resolve()),
        "sceneSha256": _sha256(scene_path),
        "originGameUnits": list(scene["coordinates"]["originGameUnits"]),
        "locationContract": contract,
    }


def _reuse_compiled_location(
    location: dict[str, object],
    profile: dict[str, object],
    recipe: dict[str, object],
    reuse_root: Path,
    master_hash: str,
    configuration_sha256: str,
    gallery_compiler_sha256: str,
    expected_asset_compiler: dict[str, str],
    expected_archive_stack_sha256: str,
    manifest_key: str | None = None,
    subject_id: str | None = None,
    retail_grass_observation: dict[str, object] | None = None,
) -> dict[str, object]:
    scene_contract = location["scene"]
    recipe_id = str(scene_contract["recipeId"])
    scene_path = (
        reuse_root / "generated" / "cells" / recipe_id / "cell-scene.json"
    )
    if not scene_path.is_file():
        raise FileNotFoundError(
            f"Reusable gallery location has no sealed scene: {scene_path}"
        )
    scene = json.loads(scene_path.read_text(encoding="utf-8"))
    cell = scene.get("cell")
    coordinates = scene.get("coordinates")
    source = scene.get("source")
    configuration = scene.get("configuration")
    compiler = scene.get("compiler")
    location_contract = scene.get("galleryLocationContract")
    expected_cell = str(scene_contract["expectedCellFormId"]).lower()
    expected_interior = bool(scene_contract["expectedInterior"])
    expected_location_contract = {
        "schema": LOCATION_CONTRACT_SCHEMA,
        "manifestKey": manifest_key or str(location["id"]),
        "locationId": str(location["id"]),
        "subjectId": subject_id,
        "sceneProfile": str(scene_contract["profile"]),
        "sceneCompiler": str(profile["compiler"]),
        "sceneContractSha256": _document_sha256(scene_contract),
        "mergedRecipeSha256": _document_sha256(recipe),
        "runtimeConfigurationSha256": configuration_sha256,
        "galleryCompilerSha256": gallery_compiler_sha256,
        "ownedArchiveStackSha256": expected_archive_stack_sha256,
        "retailGrassObservation": copy.deepcopy(retail_grass_observation),
    }
    if (
        not isinstance(scene, dict)
        or scene.get("recipe") != recipe_id
        or not isinstance(cell, dict)
        or str(cell.get("formId", "")).lower() != expected_cell
        or bool(cell.get("interior")) != expected_interior
        or not isinstance(source, dict)
        or str(source.get("masterSha256", "")).lower() != master_hash.lower()
        or not isinstance(source.get("ownedArchiveStack"), dict)
        or _document_sha256(source["ownedArchiveStack"])
        != expected_archive_stack_sha256
        or not isinstance(configuration, dict)
        or str(configuration.get("sha256", "")).lower()
        != configuration_sha256.lower()
        or compiler != expected_asset_compiler
        or location_contract != expected_location_contract
        or not isinstance(coordinates, dict)
    ):
        raise ValueError(f"Reusable gallery location identity mismatch: {scene_path}")
    expected_worldspace = scene_contract.get("expectedWorldspaceFormId")
    if expected_worldspace is not None and str(
        cell.get("worldspaceFormId", "")
    ).lower() != str(expected_worldspace).lower():
        raise ValueError(f"Reusable gallery worldspace mismatch: {scene_path}")
    origin = coordinates.get("originGameUnits")
    if (
        not isinstance(origin, list)
        or len(origin) != 3
        or any(
            not isinstance(value, (int, float)) or not math.isfinite(float(value))
            for value in origin
        )
    ):
        raise ValueError(f"Reusable gallery location has an invalid origin: {scene_path}")
    return {
        "recipe": recipe_id,
        "recipeSha256": _document_sha256(recipe),
        "scene": str(scene_path.resolve()),
        "sceneSha256": _sha256(scene_path),
        "originGameUnits": [float(value) for value in origin],
        "locationContract": expected_location_contract,
    }


def _actor_recipe(
    template: dict[str, object],
    subject: dict[str, object],
    authored_cell_form_id: str,
    origin_game_units: list[float],
    cell_recipe_id: str,
) -> dict[str, object]:
    return {
        "schema": "opennv-actor-recipe/v1",
        "id": f"gallery-{subject['id']}-actor-v1",
        "master": copy.deepcopy(template["master"]),
        "meshesArchive": copy.deepcopy(template["meshesArchive"]),
        "textureArchives": copy.deepcopy(template["textureArchives"]),
        "cellFormId": authored_cell_form_id,
        "cellRecipe": cell_recipe_id,
        "proofActorReferenceFormId": str(subject["referenceFormId"]),
        "expectedBaseFormId": str(subject["baseFormId"]),
        "originGameUnits": origin_game_units,
    }


def _creature_recipe(
    master_row: dict[str, object],
    subject: dict[str, object],
    authored_cell_form_id: str,
    origin_game_units: list[float],
) -> dict[str, object]:
    return {
        "schema": "opennv-gallery-creature-recipe/v1",
        "id": f"gallery-{subject['id']}-actor-v1",
        "master": copy.deepcopy(master_row),
        "cellFormId": authored_cell_form_id,
        "proofActorReferenceFormId": str(subject["referenceFormId"]),
        "expectedBaseFormId": str(subject["baseFormId"]),
        "originGameUnits": origin_game_units,
        "allowedUnsupportedGeometryTypes": list(subject["allowedUnsupportedGeometryTypes"]),
    }


@dataclass(frozen=True)
class SubjectCompilationRequest:
    data_root: Path
    cache_root: Path
    recipe_path: Path
    archive_recipe_path: Path
    actor_template: dict[str, object]
    master_row: dict[str, object]
    subject: dict[str, object]
    authored_cell_form_id: str
    origin_game_units: list[float]
    cell_recipe_id: str
    actor_preparation_context: ActorPreparationContext
    creature_preparation_context: GalleryCreaturePreparationContext


def _compile_npc_subject(request: SubjectCompilationRequest) -> dict[str, object]:
    recipe = _actor_recipe(
        request.actor_template,
        request.subject,
        request.authored_cell_form_id,
        request.origin_game_units,
        request.cell_recipe_id,
    )
    _atomic_json(request.recipe_path, recipe)
    return prepare_actor(
        request.data_root,
        request.cache_root,
        str(recipe["id"]),
        recipe,
        request.actor_preparation_context,
    )


def _compile_creature_subject(request: SubjectCompilationRequest) -> dict[str, object]:
    recipe = _creature_recipe(
        request.master_row,
        request.subject,
        request.authored_cell_form_id,
        request.origin_game_units,
    )
    _atomic_json(request.recipe_path, recipe)
    return prepare_gallery_creature(
        request.data_root,
        request.cache_root,
        request.recipe_path,
        request.archive_recipe_path,
        request.creature_preparation_context,
    )


SUBJECT_COMPILERS: dict[
    str,
    Callable[[SubjectCompilationRequest], dict[str, object]],
] = {
    "npc": _compile_npc_subject,
    "creature": _compile_creature_subject,
}


def _shot_contract(
    subject: dict[str, object],
    subject_profile: dict[str, object],
    location: dict[str, object],
    retail_evidence: dict[str, object],
) -> dict[str, object]:
    return {
        "schema": SHOT_SCHEMA,
        "status": "owned-authored-placement",
        **_gallery_shot_identity(subject, subject_profile, location),
        "retailEvidence": copy.deepcopy(retail_evidence),
    }


def prepare_gallery(
    data_root: Path,
    cache_root: Path,
    gallery_path: Path,
    archive_recipe_path: Path,
    retail_evidence_manifest_path: Path,
    reuse_location_root: Path | None = None,
) -> dict[str, object]:
    if cache_root.exists():
        raise FileExistsError(f"Refusing to overwrite gallery cache: {cache_root}")
    gallery = _load_gallery(gallery_path)
    from gallery_retail_evidence import load_evidence_manifest

    retail_evidence_manifest, retail_evidence_by_id = load_evidence_manifest(
        gallery_path,
        retail_evidence_manifest_path,
    )
    configuration = load_runtime_configuration()
    gallery_compiler_sha256 = _gallery_compiler_sha256()
    expected_asset_compiler = compiler_provenance()
    scene_profiles = {
        str(profile_id): {
            **profile,
            "template": load_spatial_recipe(str(profile["templateRecipe"])),
        }
        for profile_id, profile in gallery["sceneProfiles"].items()
    }
    actor_template = load_actor_recipe(str(gallery["actorTemplateRecipe"]))

    master_row = actor_template["master"]
    mesh_row = actor_template["meshesArchive"]
    texture_template_rows = actor_template["textureArchives"]
    master = _data_file(data_root, str(master_row["file"]), str(master_row["sha256"]))
    meshes = _data_file(data_root, str(mesh_row["file"]), str(mesh_row["sha256"]))
    textures = [
        _data_file(data_root, str(row["file"]), str(row["sha256"]))
        for row in texture_template_rows
    ]
    texture_rows = [
        {"file": path.name, "bytes": path.stat().st_size, "sha256": _sha256(path)}
        for path in textures
    ]
    master_hash = _sha256(master)
    verified_actor_sources = (
        (master.resolve(), master_hash.lower()),
        (meshes.resolve(), str(mesh_row["sha256"]).lower()),
        *(
            (path.resolve(), str(row["sha256"]).lower())
            for path, row in zip(textures, texture_template_rows)
        ),
    )
    actor_preparation_context = create_actor_preparation_context(
        data_root,
        actor_template,
        verified_actor_sources,
    )
    creature_preparation_context = create_gallery_creature_preparation_context(
        data_root,
        master_row,
        archive_recipe_path,
        configuration,
        actor_preparation_context.catalog,
        master_hash,
    )
    cache_root.mkdir(parents=True)
    contracts_root = cache_root / "gallery-contracts"
    location_contracts_root = contracts_root / "locations"

    location_documents = {
        str(location["id"]): location for location in gallery["locations"]
    }
    compiled_locations: dict[str, dict[str, object]] = {}
    expected_archive_stack_sha256 = _document_sha256(
        creature_preparation_context.archives.manifest()
    )
    jobs = []
    for subject in gallery["subjects"]:
        location_id = str(subject["locationId"])
        location = location_documents.get(location_id)
        if location is None:
            raise ValueError(f"Gallery subject has no location: {subject['id']}")
        subject_id = str(subject["id"])
        retail_evidence_descriptor = retail_evidence_by_id[subject_id]
        manifest_key = _location_scene_key(location, subject)
        compiled_location = compiled_locations.get(manifest_key)
        if compiled_location is None:
            profile, base_recipe = _location_recipe(location, scene_profiles)
            recipe = _subject_location_recipe(base_recipe, location, subject)
            grass_path: Path | None = None
            grass_descriptor: dict[str, object] | None = None
            contract_subject_id: str | None = None
            if str(location["locationClass"]) == "exterior":
                grass_path, grass_descriptor = _retail_grass_observation(
                    retail_evidence_descriptor
                )
                contract_subject_id = subject_id
            location_recipe_path = (
                location_contracts_root / f"{manifest_key}-scene-recipe.json"
            )
            _atomic_json(location_recipe_path, recipe)
            if reuse_location_root is not None:
                compiled_location = _reuse_compiled_location(
                    location,
                    profile,
                    recipe,
                    reuse_location_root,
                    master_hash,
                    configuration.sha256,
                    gallery_compiler_sha256,
                    expected_asset_compiler,
                    expected_archive_stack_sha256,
                    manifest_key,
                    contract_subject_id,
                    grass_descriptor,
                )
            else:
                compiled_location = _compile_location(
                    location,
                    profile,
                    recipe,
                    master,
                    meshes,
                    textures,
                    texture_rows,
                    cache_root,
                    master_hash,
                    configuration.sha256,
                    gallery_compiler_sha256,
                    creature_preparation_context.archives,
                    manifest_key,
                    contract_subject_id,
                    grass_path,
                    grass_descriptor,
                )
            compiled_location["recipeContract"] = str(
                location_recipe_path.resolve()
            )
            compiled_location["recipeContractSha256"] = _sha256(
                location_recipe_path
            )
            compiled_locations[manifest_key] = compiled_location
        authored_cell_form_id = str(location["actorCellFormId"])
        origin = list(compiled_location["originGameUnits"])
        subject_profile = gallery["subjectProfiles"][str(subject["profile"])]
        record_type = str(subject_profile["recordType"])
        recipe_path = contracts_root / f"{subject['id']}-actor-recipe.json"
        subject_compiler = SUBJECT_COMPILERS[str(subject_profile["compiler"])]
        actor_scene = subject_compiler(
            SubjectCompilationRequest(
                data_root,
                cache_root,
                recipe_path,
                archive_recipe_path,
                actor_template,
                master_row,
                subject,
                authored_cell_form_id,
                origin,
                str(compiled_location["recipe"]),
                actor_preparation_context,
                creature_preparation_context,
            )
        )
        actor_scene_path = Path(str(actor_scene["manifest"]))
        shot = _shot_contract(
            subject,
            subject_profile,
            location,
            retail_evidence_by_id[str(subject["id"])],
        )
        shot_path = contracts_root / f"{int(subject['ordinal']):02d}-{subject['id']}.json"
        _atomic_json(shot_path, shot)
        jobs.append(
            {
                "ordinal": int(subject["ordinal"]),
                "id": str(subject["id"]),
                "label": str(subject["label"]),
                "location": str(location["location"]),
                "locationClass": str(location["locationClass"]),
                "recordType": record_type,
                "subjectProfile": str(subject["profile"]),
                "locationSceneKey": manifest_key,
                "cellScene": compiled_location["scene"],
                "cellSceneSha256": compiled_location["sceneSha256"],
                "actorScene": str(actor_scene_path),
                "actorSceneSha256": _sha256(actor_scene_path),
                "shotContract": str(shot_path.resolve()),
                "shotContractSha256": _sha256(shot_path),
                "retailEvidence": copy.deepcopy(
                    retail_evidence_descriptor
                ),
                "outputFile": shot["outputFile"],
            }
        )

    manifest = {
        "schema": OUTPUT_SCHEMA,
        "status": "compiled-owned-authored-gallery-retail-bound",
        "gallery": {"path": str(gallery_path), "sha256": _sha256(gallery_path)},
        "configuration": configuration.manifest(),
        "compiler": {
            "gallerySha256": gallery_compiler_sha256,
            "assetCompiler": expected_asset_compiler,
        },
        "ownedData": {
            "master": {"path": str(master), "sha256": master_hash},
            "meshes": {"path": str(meshes), "sha256": _sha256(meshes)},
            "textures": [
                {"path": str(path), "sha256": _sha256(path)} for path in textures
            ],
            "archiveStack": creature_preparation_context.archives.manifest(),
        },
        "shotCount": len(jobs),
        "interiorShots": sum(job["locationClass"] == "interior" for job in jobs),
        "exteriorShots": sum(job["locationClass"] == "exterior" for job in jobs),
        "retailEvidenceManifest": {
            "path": str(retail_evidence_manifest_path),
            "bytes": retail_evidence_manifest_path.stat().st_size,
            "sha256": _sha256(retail_evidence_manifest_path),
            "schema": retail_evidence_manifest["schema"],
            "status": retail_evidence_manifest["status"],
        },
        "retailCaptureUsed": False,
        "retailEvidenceUsed": True,
        "parityClaimed": False,
        "complexity": {
            "locationLookup": "single-pass-hash-index",
            "subjectLookup": "single-pass-hash-index",
            "retailEvidenceLookup": "single-pass-hash-index",
            "textureLookup": "preindexed-owned-archive-stack",
            "actorCatalog": "single-scan-shared-context",
            "visualArchives": "single-open-shared-context",
            "processingOrder": "locations-plus-subjects",
        },
        "locationCompilation": (
            {
                "mode": "hash-verified-sealed-scene-reuse",
                "root": str(reuse_location_root),
                "configurationSha256": configuration.sha256,
                "galleryCompilerSha256": gallery_compiler_sha256,
            }
            if reuse_location_root is not None
            else {
                "mode": "compiled-fresh",
                "configurationSha256": configuration.sha256,
                "galleryCompilerSha256": gallery_compiler_sha256,
            }
        ),
        "locations": compiled_locations,
        "jobs": jobs,
    }
    manifest_path = cache_root / "gallery-manifest.json"
    _atomic_json(manifest_path, manifest)
    manifest["manifest"] = str(manifest_path.resolve())
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--gallery", type=Path, required=True)
    parser.add_argument("--retail-evidence-manifest", type=Path, required=True)
    parser.add_argument("--reuse-location-root", type=Path)
    parser.add_argument(
        "--archive-recipe",
        type=Path,
        default=default_archive_recipe_path(),
    )
    args = parser.parse_args()
    try:
        result = prepare_gallery(
            args.data_root.resolve(),
            args.cache_root.resolve(),
            args.gallery.resolve(),
            args.archive_recipe.resolve(),
            args.retail_evidence_manifest.resolve(),
            (
                args.reuse_location_root.resolve()
                if args.reuse_location_root is not None
                else None
            ),
        )
    except Exception as error:
        print(f"OPENNV_OWNED_GALLERY_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_OWNED_GALLERY "
        + json.dumps(
            {
                "manifest": result["manifest"],
                "shotCount": result["shotCount"],
                "interiorShots": result["interiorShots"],
                "exteriorShots": result["exteriorShots"],
                "status": result["status"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
