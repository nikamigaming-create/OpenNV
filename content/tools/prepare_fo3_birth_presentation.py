#!/usr/bin/env python3
"""Prepare a bounded Vault 101 birth-room presentation from an owned FO3 profile."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
from pathlib import Path

from bsa_archive import BsaArchive, canonical_member_path
from cell_scene import godot_position, godot_rotation_quaternion, godot_yaw_radians
from export_static_nif_gltf import (
    NoStaticPresentationGeometryError,
    export_static_nif,
)
from material_contract import material_bindings, texture_binding_requests
from prepare_actor import prepare_actor
from runtime_configuration import load_runtime_configuration
from texture_pipeline import TexturePipeline


RECIPE_SCHEMA = "opennv-fo3-birth-presentation-recipe/v1"
OUTPUT_SCHEMA = "opennv-fo3-vault101-birth-presentation/v6"
PROFILE_SCHEMA = "opennv-owned-game-profile/v1"
OUTPUT_NAME = "fo3-vault101-birth-presentation.json"
SHA256_HEX_CHARACTERS = 64


def _sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _sha256_file(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _read_json(path: Path) -> tuple[dict[str, object], bytes]:
    payload = path.read_bytes()
    document = json.loads(payload)
    if not isinstance(document, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return document, payload


def _required_object(source: dict[str, object], name: str) -> dict[str, object]:
    value = source.get(name)
    if not isinstance(value, dict):
        raise ValueError(f"Required object is absent: {name}")
    return value


def _required_list(source: dict[str, object], name: str) -> list[object]:
    value = source.get(name)
    if not isinstance(value, list):
        raise ValueError(f"Required array is absent: {name}")
    return value


def _required_string(source: dict[str, object], name: str) -> str:
    value = source.get(name)
    if not isinstance(value, str) or not value:
        raise ValueError(f"Required string is absent: {name}")
    return value


def _required_sha256(source: dict[str, object], name: str) -> str:
    value = _required_string(source, name).lower()
    if len(value) != SHA256_HEX_CHARACTERS or any(
        character not in "0123456789abcdef" for character in value
    ):
        raise ValueError(f"SHA-256 field is invalid: {name}")
    return value


def _atomic_bytes(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def _atomic_json(path: Path, document: dict[str, object]) -> None:
    _atomic_bytes(
        path,
        (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8"),
    )


def _cache_relative_derivative(cache_root: Path, path: Path) -> str:
    root = cache_root.resolve()
    candidate = path.resolve()
    try:
        relative = candidate.relative_to(root)
    except ValueError as error:
        raise ValueError(
            f"Fallout 3 derivative escapes its local cache: {candidate}"
        ) from error
    if relative == Path("."):
        raise ValueError("Fallout 3 derivative path is the cache root")
    return relative.as_posix()


def _default_recipe_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / "fo3-vault101-birth-presentation-v1.json"


def _default_actor_recipe_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / "fo3-vault101-doctor-li-actor-v1.json"


def _default_dad_actor_recipe_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / "fo3-vault101-dad-actor-v1.json"


def _archive(install: dict[str, object], role: str) -> dict[str, object]:
    matches = [
        row
        for row in _required_list(install, "archives")
        if isinstance(row, dict) and row.get("role") == role
    ]
    if len(matches) != 1:
        raise ValueError(f"Owned Fallout 3 archive role is ambiguous: {role}")
    return matches[0]


def _verify_source_file(row: dict[str, object]) -> Path:
    path = Path(_required_string(row, "source")).resolve()
    expected_bytes = int(row.get("bytes", 0))
    if not path.is_file() or path.stat().st_size != expected_bytes:
        raise ValueError(f"Owned source is absent or changed: {path}")
    actual_sha256 = _sha256_file(path)
    if actual_sha256 != _required_sha256(row, "sha256"):
        raise ValueError(f"Owned source hash differs: {path}")
    return path


def _distance(first: list[object], second: list[object]) -> float:
    if len(first) != 3 or len(second) != 3:
        raise ValueError("Fallout 3 reference position must contain three values")
    return math.sqrt(
        sum((float(first[index]) - float(second[index])) ** 2 for index in range(3))
    )


def _gltf_position_bounds(path: Path) -> tuple[list[float], list[float]]:
    document, _payload = _read_json(path)
    meshes = _required_list(document, "meshes")
    accessors = _required_list(document, "accessors")
    minima = [math.inf, math.inf, math.inf]
    maxima = [-math.inf, -math.inf, -math.inf]
    positions = 0
    for mesh in meshes:
        if not isinstance(mesh, dict):
            raise ValueError(f"glTF mesh row is malformed: {path}")
        for primitive in _required_list(mesh, "primitives"):
            if not isinstance(primitive, dict):
                raise ValueError(f"glTF primitive row is malformed: {path}")
            attributes = _required_object(primitive, "attributes")
            accessor_index = attributes.get("POSITION")
            if not isinstance(accessor_index, int) or not (
                0 <= accessor_index < len(accessors)
            ):
                raise ValueError(f"glTF POSITION accessor is invalid: {path}")
            accessor = accessors[accessor_index]
            if not isinstance(accessor, dict):
                raise ValueError(f"glTF POSITION accessor row is malformed: {path}")
            minimum = _required_list(accessor, "min")
            maximum = _required_list(accessor, "max")
            if len(minimum) != 3 or len(maximum) != 3:
                raise ValueError(f"glTF POSITION bounds are not three-dimensional: {path}")
            for axis in range(3):
                lower = float(minimum[axis])
                upper = float(maximum[axis])
                if not math.isfinite(lower) or not math.isfinite(upper) or upper < lower:
                    raise ValueError(f"glTF POSITION bounds are invalid: {path}")
                minima[axis] = min(minima[axis], lower)
                maxima[axis] = max(maxima[axis], upper)
            positions += 1
    if positions == 0:
        raise ValueError(f"glTF contains no bounded POSITION accessor: {path}")
    return minima, maxima


def prepare(
    profile_path: Path,
    cache_root: Path,
    recipe_path: Path,
    actor_recipe_path: Path,
    dad_actor_recipe_path: Path,
    cg01_dad_actor_recipe_path: Path | None,
) -> Path:
    profile, _profile_payload = _read_json(profile_path.resolve())
    if (
        profile.get("schema") != PROFILE_SCHEMA
        or profile.get("campaign") != "Fallout3"
        or profile.get("status") != "registered-owned-profile"
    ):
        raise ValueError("Fallout 3 owned profile identity is unsupported")
    install = _required_object(profile, "install")
    meshes_row = _archive(install, "meshes")
    meshes_path = _verify_source_file(meshes_row)
    textures_row = _archive(install, "textures")
    textures_path = _verify_source_file(textures_row)

    opening = _required_object(profile, "opening")
    birth_source = _required_object(opening, "birthSlice")
    birth_path = Path(_required_string(birth_source, "output")).resolve()
    birth, birth_payload = _read_json(birth_path)
    birth_sha256 = _sha256_bytes(birth_payload)
    if birth_sha256 != _required_sha256(birth_source, "sha256"):
        raise ValueError("Fallout 3 birth-slice manifest hash differs from its profile")

    recipe, recipe_payload = _read_json(recipe_path.resolve())
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise ValueError("Fallout 3 birth-presentation recipe schema is unsupported")
    if cg01_dad_actor_recipe_path is None:
        actor_recipes = _required_object(recipe, "actorRecipes")
        cg01_dad_actor_recipe_path = (
            recipe_path.resolve().parent / _required_string(actor_recipes, "cg01Dad")
        )
    recipe_source = _required_object(recipe, "source")
    birth_recipe = _required_object(birth, "recipe")
    cell = _required_object(birth, "cell")
    start_graph = _required_object(birth, "startGraph")
    entry = _required_object(start_graph, "playerSpawn")
    if (
        birth.get("schema") != recipe_source.get("birthSliceSchema")
        or birth_recipe.get("id") != recipe_source.get("birthSliceRecipeId")
        or birth_recipe.get("sha256") != recipe_source.get("birthSliceRecipeSha256")
        or cell.get("formId") != recipe_source.get("cellFormId")
        or entry.get("formId") != recipe_source.get("entryReferenceFormId")
    ):
        raise ValueError("Fallout 3 birth-presentation recipe does not bind the owned slice")

    source = _required_object(birth, "source")
    source_meshes = _required_object(source, "meshesArchive")
    source_textures = _required_object(source, "texturesArchive")
    if (
        source_meshes.get("file") != meshes_row.get("file")
        or int(source_meshes.get("bytes", 0)) != int(meshes_row.get("bytes", 0))
        or source_meshes.get("sha256") != meshes_row.get("sha256")
    ):
        raise ValueError("Fallout 3 birth slice and owned mesh archive differ")
    if (
        source_textures.get("file") != textures_row.get("file")
        or int(source_textures.get("bytes", 0)) != int(textures_row.get("bytes", 0))
        or source_textures.get("sha256") != textures_row.get("sha256")
    ):
        raise ValueError("Fallout 3 birth slice and owned texture archive differ")

    graph = _required_object(birth, "cellGraph")
    bases = {
        _required_string(row, "formId"): row
        for row in _required_list(graph, "bases")
        if isinstance(row, dict)
    }
    resources = {
        canonical_member_path(_required_string(row, "logicalPath")): row
        for row in _required_list(graph, "modelResources")
        if isinstance(row, dict)
    }
    selection = _required_object(recipe, "selection")
    allowed_reference_types = set(_required_list(selection, "includeReferenceRecordTypes"))
    allowed_base_types = set(_required_list(selection, "includeBaseRecordTypes"))
    allowed_prefixes = tuple(
        canonical_member_path(str(value)) + "\\"
        for value in _required_list(selection, "includeModelPrefixes")
    )
    maximum_distance = float(selection.get("maximumDistanceFromEntryGameUnits", 0.0))
    if maximum_distance <= 0.0:
        raise ValueError("Fallout 3 birth-presentation radius must be positive")
    require_enabled = selection.get("requireInitiallyEnabled") is True
    require_single = selection.get("requireSingleMainModel") is True
    include_cg01 = selection.get("includeCg01DadStartMarker") is True
    if not require_enabled or not require_single or not include_cg01:
        raise ValueError("Fallout 3 birth presentation must fail closed on enable/model identity")

    entry_transform = _required_object(entry, "transform")
    entry_position = _required_list(entry_transform, "positionGameUnits")
    doctor_source = _required_object(birth, "doctorActor")
    doctor_reference = _required_object(doctor_source, "reference")
    doctor_base = _required_object(doctor_source, "base")
    doctor_appearance = _required_object(doctor_source, "appearance")
    father_rows = [
        row
        for row in _required_list(start_graph, "actors")
        if isinstance(row, dict) and row.get("role") == "father"
    ]
    if len(father_rows) != 1:
        raise ValueError("Fallout 3 CG00 Dad start-graph identity is ambiguous")
    father_source = father_rows[0]
    father_reference = _required_object(father_source, "reference")
    father_start_marker = _required_object(father_source, "startMarker")
    father_base = bases.get(_required_string(father_reference, "baseFormId"))
    if father_base is None:
        raise ValueError("Fallout 3 CG00 Dad base is absent from the owned CELL graph")
    actor_recipe, actor_recipe_payload = _read_json(actor_recipe_path.resolve())
    actor_recipe_master = _required_object(actor_recipe, "master")
    actor_recipe_meshes = _required_object(actor_recipe, "meshesArchive")
    actor_recipe_textures = _required_list(actor_recipe, "textureArchives")
    if (
        actor_recipe.get("schema") != "opennv-actor-recipe/v1"
        or actor_recipe.get("cellFormId") != cell.get("formId")
        or actor_recipe.get("proofActorReferenceFormId") != doctor_reference.get("formId")
        or actor_recipe.get("expectedBaseFormId") != doctor_base.get("formId")
        or actor_recipe.get("originGameUnits") != entry_position
        or actor_recipe_master.get("file") != source.get("master", {}).get("file")
        or actor_recipe_master.get("sha256") != source.get("master", {}).get("sha256")
        or actor_recipe_meshes.get("file") != source_meshes.get("file")
        or actor_recipe_meshes.get("sha256") != source_meshes.get("sha256")
        or len(actor_recipe_textures) != 1
        or not isinstance(actor_recipe_textures[0], dict)
        or actor_recipe_textures[0].get("file") != source_textures.get("file")
        or actor_recipe_textures[0].get("sha256") != source_textures.get("sha256")
    ):
        raise ValueError("Fallout 3 Doctor Li recipe does not bind the owned birth slice")
    dad_actor_recipe, dad_actor_recipe_payload = _read_json(
        dad_actor_recipe_path.resolve()
    )
    dad_actor_recipe_master = _required_object(dad_actor_recipe, "master")
    dad_actor_recipe_meshes = _required_object(dad_actor_recipe, "meshesArchive")
    dad_actor_recipe_textures = _required_list(dad_actor_recipe, "textureArchives")
    if (
        dad_actor_recipe.get("schema") != "opennv-actor-recipe/v1"
        or dad_actor_recipe.get("cellFormId") != cell.get("formId")
        or dad_actor_recipe.get("proofActorReferenceFormId")
        != father_reference.get("formId")
        or dad_actor_recipe.get("expectedBaseFormId") != father_base.get("formId")
        or dad_actor_recipe.get("originGameUnits") != entry_position
        or dad_actor_recipe.get("bodyModPolicy")
        != "owned-race-base-diffuse-when-precomputed-absent"
        or dad_actor_recipe_master.get("file") != source.get("master", {}).get("file")
        or dad_actor_recipe_master.get("sha256")
        != source.get("master", {}).get("sha256")
        or dad_actor_recipe_meshes.get("file") != source_meshes.get("file")
        or dad_actor_recipe_meshes.get("sha256") != source_meshes.get("sha256")
        or len(dad_actor_recipe_textures) != 1
        or not isinstance(dad_actor_recipe_textures[0], dict)
        or dad_actor_recipe_textures[0].get("file") != source_textures.get("file")
        or dad_actor_recipe_textures[0].get("sha256")
        != source_textures.get("sha256")
    ):
        raise ValueError("Fallout 3 CG00 Dad recipe does not bind the owned birth slice")

    character_selection = _required_object(opening, "characterSelection")
    cg01_transition = _required_object(character_selection, "cg01Stage0Transition")
    stage0_result = _required_object(cg01_transition, "stage0Result")
    stage0_commands = _required_list(stage0_result, "commands")
    cg01_move_commands = [
        row
        for row in stage0_commands
        if isinstance(row, dict)
        and row.get("kind") == "moveToReference"
        and isinstance(row.get("subject"), dict)
        and isinstance(row["subject"].get("base"), dict)
        and row["subject"]["base"].get("editorId") == "CG01Dad"
    ]
    if len(cg01_move_commands) != 1:
        raise ValueError("Fallout 3 CG01 Dad stage-0 MoveTo is absent or ambiguous")
    cg01_move = cg01_move_commands[0]
    cg01_dad_source = _required_object(cg01_move, "subject")
    cg01_dad_base = _required_object(cg01_dad_source, "base")
    cg01_dad_marker = _required_object(cg01_move, "target")
    if (
        cg01_transition.get("cellFormId") != cell.get("formId")
        or cg01_dad_source.get("cellFormId") != cell.get("formId")
        or cg01_dad_marker.get("cellFormId") != cell.get("formId")
    ):
        raise ValueError("Fallout 3 CG01 Dad stage-0 source join differs")
    cg01_dad_marker_position = _required_list(
        _required_object(cg01_dad_marker, "sourceTransform"),
        "positionGameUnits",
    )

    cg01_dad_actor_recipe, cg01_dad_actor_recipe_payload = _read_json(
        cg01_dad_actor_recipe_path.resolve()
    )
    cg01_dad_actor_recipe_master = _required_object(
        cg01_dad_actor_recipe, "master"
    )
    cg01_dad_actor_recipe_meshes = _required_object(
        cg01_dad_actor_recipe, "meshesArchive"
    )
    cg01_dad_actor_recipe_textures = _required_list(
        cg01_dad_actor_recipe, "textureArchives"
    )
    if (
        cg01_dad_actor_recipe.get("schema") != "opennv-actor-recipe/v1"
        or cg01_dad_actor_recipe.get("cellFormId") != cell.get("formId")
        or cg01_dad_actor_recipe.get("proofActorReferenceFormId")
        != cg01_dad_source.get("formId")
        or cg01_dad_actor_recipe.get("expectedBaseFormId")
        != cg01_dad_base.get("formId")
        or cg01_dad_actor_recipe.get("originGameUnits") != entry_position
        or cg01_dad_actor_recipe.get("bodyModPolicy")
        != "owned-race-base-diffuse-when-precomputed-absent"
        or cg01_dad_actor_recipe_master.get("file")
        != source.get("master", {}).get("file")
        or cg01_dad_actor_recipe_master.get("sha256")
        != source.get("master", {}).get("sha256")
        or cg01_dad_actor_recipe_meshes.get("file") != source_meshes.get("file")
        or cg01_dad_actor_recipe_meshes.get("sha256")
        != source_meshes.get("sha256")
        or len(cg01_dad_actor_recipe_textures) != 1
        or not isinstance(cg01_dad_actor_recipe_textures[0], dict)
        or cg01_dad_actor_recipe_textures[0].get("file")
        != source_textures.get("file")
        or cg01_dad_actor_recipe_textures[0].get("sha256")
        != source_textures.get("sha256")
    ):
        raise ValueError("Fallout 3 CG01 Dad recipe does not bind the owned stage-0 join")
    selected: list[tuple[dict[str, object], dict[str, object], str]] = []
    excluded: dict[str, int] = {}
    for value in _required_list(graph, "references"):
        if not isinstance(value, dict):
            raise ValueError("Fallout 3 CELL reference row is malformed")
        reason = "selected"
        base = bases.get(_required_string(value, "baseFormId"))
        if base is None:
            reason = "unresolved-base"
        elif value.get("recordType") not in allowed_reference_types:
            reason = "reference-type"
        elif base.get("recordType") not in allowed_base_types:
            reason = "base-type"
        elif bool(value.get("initiallyDisabled")):
            reason = "initially-disabled"
        else:
            models = _required_list(base, "models")
            if len(models) != 1 or not isinstance(models[0], dict) or models[0].get("field") != "MODL":
                reason = "single-main-model"
            else:
                model_path = canonical_member_path(_required_string(models[0], "path"))
                if not model_path.startswith(allowed_prefixes):
                    reason = "model-prefix"
                else:
                    transform = _required_object(value, "transform")
                    position = _required_list(transform, "positionGameUnits")
                    if min(
                        _distance(position, entry_position),
                        _distance(position, cg01_dad_marker_position),
                    ) > maximum_distance:
                        reason = "outside-owned-slice-radius"
        if reason != "selected":
            excluded[reason] = excluded.get(reason, 0) + 1
            continue
        selected.append((value, base, model_path))
    if not selected:
        raise ValueError("Fallout 3 birth-presentation recipe selected no references")
    selected_models = {row[2] for row in selected}
    if (
        len(selected) != int(selection.get("expectedSelectedReferences", -1))
        or len(selected_models)
        != int(selection.get("expectedSelectedUniqueModels", -1))
    ):
        raise ValueError(
            "Fallout 3 birth-presentation selection coverage differs: "
            f"references={len(selected)} models={len(selected_models)}"
        )

    archive = BsaArchive(meshes_path)
    configuration = load_runtime_configuration()
    presentation = _required_object(recipe, "presentation")
    export_strict = presentation.get("exportStrict")
    if not isinstance(export_strict, bool):
        raise ValueError("Fallout 3 birth-presentation strict-export policy is absent")
    output_root = cache_root.resolve() / "generated" / str(recipe["id"])
    asset_root = output_root / "assets"
    source_root = cache_root.resolve() / "source" / "fallout3"
    assets: dict[str, dict[str, object]] = {}
    asset_sidecars: dict[str, dict[str, object]] = {}
    non_presentation: list[dict[str, object]] = []
    for model_path in sorted(selected_models):
        logical_path = canonical_member_path("meshes\\" + model_path)
        resource = resources.get(logical_path)
        if resource is None:
            raise ValueError(f"Selected model is absent from transported resources: {logical_path}")
        member = archive.extract(logical_path)
        if (
            len(member.data) != int(resource.get("bytes", 0))
            or member.sha256 != _required_sha256(resource, "sha256")
            or resource.get("sourceArchive") != meshes_path.name
        ):
            raise ValueError(f"Owned model differs from transported resource: {logical_path}")
        asset_id = hashlib.sha256(logical_path.encode("utf-8")).hexdigest()[
            : configuration.content_compiler.asset_id_hex_characters
        ]
        source_path = source_root / Path(logical_path.replace("\\", "/"))
        _atomic_bytes(source_path, member.data)
        gltf_path = asset_root / f"{asset_id}.gltf"
        sidecar_path = asset_root / f"{asset_id}.opennv.json"
        try:
            sidecar = export_static_nif(
                source_path,
                logical_path,
                gltf_path,
                sidecar_path,
                configuration.content_compiler,
                strict=export_strict,
            )
        except NoStaticPresentationGeometryError as error:
            _atomic_json(sidecar_path, error.evidence)
            non_presentation.append(
                {
                    "logicalPath": logical_path,
                    "sourceSha256": member.sha256,
                    "classification": error.evidence["classification"],
                    "sidecar": str(sidecar_path.resolve()),
                }
            )
            continue
        assets[model_path] = {
            "id": asset_id,
            "logicalPath": logical_path,
            "sourceBytes": len(member.data),
            "sourceSha256": member.sha256,
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "surfaces": int(sidecar["coverage"]["surfaces"]),
            "collisionExportedButNotConsumed": bool(
                sidecar["coverage"]["collisionExported"]
            ),
        }
        bounds_minimum, bounds_maximum = _gltf_position_bounds(gltf_path)
        assets[model_path]["boundsGodotGameUnits"] = {
            "min": bounds_minimum,
            "max": bounds_maximum,
        }
        asset_sidecars[model_path] = sidecar
    retained = [row for row in selected if row[2] in assets]
    if not retained:
        raise ValueError("Fallout 3 birth presentation retained no renderable references")

    binding_uses: dict[str, list[dict[str, str]]] = {}
    for model_path, sidecar in asset_sidecars.items():
        for surface_index, surface in enumerate(sidecar["surfaces"]):
            for request in texture_binding_requests(surface):
                binding_uses.setdefault(request["path"], []).append(
                    {
                        "modelPath": model_path,
                        "surfaceIndex": str(surface_index),
                        "surfaceName": str(surface["name"]),
                        "role": request["role"],
                        "missingOwnedMember": request["missingOwnedMember"],
                    }
                )
    texture_pipeline = TexturePipeline(
        [textures_path],
        cache_root.resolve(),
        {},
        configuration.content_compiler,
    )
    texture_artifacts = {}
    unresolved_texture_bindings: list[dict[str, object]] = []
    for requested in sorted(binding_uses):
        source_count = texture_pipeline.member_source_count(requested)
        if source_count == 1:
            texture_artifacts[requested] = texture_pipeline.prepare(requested)
            continue
        policies = {
            use["missingOwnedMember"]
            for use in binding_uses[requested]
        }
        if source_count == 0 and policies == {"unbound-no-substitution"}:
            unresolved_texture_bindings.append(
                {
                    "requestedPath": requested,
                    "ownedMemberSources": 0,
                    "disposition": "unbound-no-substitution",
                    "uses": binding_uses[requested],
                }
            )
            continue
        raise FileNotFoundError(
            "Fallout 3 birth-room texture binding did not resolve uniquely: "
            f"path={requested} sources={source_count} policies={sorted(policies)}"
        )
    texture_ids = {
        requested: artifact.asset_id
        for requested, artifact in texture_artifacts.items()
    }
    for model_path, asset in assets.items():
        asset["materials"] = material_bindings(
            asset_sidecars[model_path],
            texture_ids,
            configuration.content_compiler,
        )

    origin = tuple(float(value) for value in entry_position)
    references: list[dict[str, object]] = []
    for reference, base, model_path in retained:
        transform = _required_object(reference, "transform")
        position = tuple(float(value) for value in _required_list(transform, "positionGameUnits"))
        rotation = tuple(float(value) for value in _required_list(transform, "rotationRadians"))
        references.append(
            {
                "formId": _required_string(reference, "formId"),
                "baseFormId": _required_string(reference, "baseFormId"),
                "baseRecordType": _required_string(base, "recordType"),
                "baseEditorId": _required_string(base, "editorId"),
                "assetId": assets[model_path]["id"],
                "positionGameUnits": list(position),
                "positionGodotGameUnits": godot_position(position, origin),
                "rotationRadians": list(rotation),
                "rotationGodotQuaternion": godot_rotation_quaternion(rotation),
                "yawGodotRadians": godot_yaw_radians(rotation[2]),
                "scale": float(transform.get("scale", 1.0)),
                "initiallyDisabled": False,
            }
        )

    entry_rotation = tuple(
        float(value) for value in _required_list(entry_transform, "rotationRadians")
    )
    support_reference_form_id = _required_string(
        presentation, "proofCameraSupportReferenceFormId"
    )
    support_matches = [
        row
        for row in retained
        if row[0].get("formId") == support_reference_form_id
    ]
    if len(support_matches) != 1:
        raise ValueError(
            "Fallout 3 proof-camera support reference is absent or ambiguous"
        )
    support_reference, support_base, support_model_path = support_matches[0]
    if (
        support_base.get("editorId")
        != _required_string(presentation, "proofCameraSupportBaseEditorId")
        or support_model_path
        != _required_string(presentation, "proofCameraSupportModelPath").lower()
    ):
        raise ValueError("Fallout 3 proof-camera support identity differs")
    support_transform = _required_object(support_reference, "transform")
    support_position = tuple(
        float(value)
        for value in _required_list(support_transform, "positionGameUnits")
    )
    support_rotation = tuple(
        float(value)
        for value in _required_list(support_transform, "rotationRadians")
    )
    if abs(support_rotation[0]) > 1.0e-6 or abs(support_rotation[1]) > 1.0e-6:
        raise ValueError(
            "Fallout 3 proof-camera support surface is not horizontal"
        )
    support_scale = float(support_transform.get("scale", 1.0))
    if not math.isfinite(support_scale) or support_scale <= 0.0:
        raise ValueError("Fallout 3 proof-camera support scale is invalid")
    support_bounds = _required_object(
        assets[support_model_path], "boundsGodotGameUnits"
    )
    support_bounds_maximum = _required_list(support_bounds, "max")
    support_local_position = godot_position(support_position, origin)
    support_surface_y = (
        float(support_local_position[1])
        + float(support_bounds_maximum[1]) * support_scale
    )
    camera_clearance = float(
        presentation.get("proofCameraSurfaceClearanceGameUnits", 0.0)
    )
    camera_near = float(presentation.get("proofCameraNearGameUnits", 0.0))
    if (
        not math.isfinite(support_surface_y)
        or support_surface_y <= 0.0
        or not math.isfinite(camera_clearance)
        or camera_clearance <= camera_near
        or not math.isfinite(camera_near)
        or camera_near <= 0.0
    ):
        raise ValueError("Fallout 3 proof-camera surface clearance is invalid")
    camera_local_position = [0.0, support_surface_y + camera_clearance, 0.0]
    camera_game_position = [
        origin[0],
        origin[1],
        origin[2] + camera_local_position[1],
    ]
    actor_manifest = prepare_actor(
        Path(_required_string(source, "dataRoot")).resolve(),
        cache_root.resolve(),
        _required_string(actor_recipe, "id"),
        recipe_document=actor_recipe,
    )
    actor_reference = _required_object(actor_manifest, "reference")
    actor_identity = _required_object(actor_manifest, "actor")
    actor_coverage = _required_object(actor_manifest, "coverage")
    doctor_transform = _required_object(doctor_reference, "transform")
    doctor_race = _required_object(doctor_appearance, "race")
    doctor_hair = _required_object(doctor_appearance, "hair")
    doctor_eyes = _required_object(doctor_appearance, "eyes")
    doctor_head_parts = _required_list(doctor_appearance, "headParts")
    doctor_outfits = _required_list(doctor_appearance, "outfits")
    if (
        actor_manifest.get("schema") != "opennv-actor-scene/v5"
        or actor_manifest.get("status") != "skinned-animated"
        or actor_manifest.get("cellFormId") != cell.get("formId")
        or actor_reference.get("formId") != doctor_reference.get("formId")
        or actor_reference.get("baseFormId") != doctor_base.get("formId")
        or actor_reference.get("initiallyDisabled") is not False
        or actor_reference.get("positionGameUnits")
        != doctor_transform.get("positionGameUnits")
        or actor_reference.get("rotationRadians")
        != doctor_transform.get("rotationRadians")
        or actor_reference.get("scale") != doctor_transform.get("scale")
        or actor_identity.get("editorId") != doctor_base.get("editorId")
        or actor_identity.get("name") != doctor_base.get("name")
        or actor_identity.get("female") is not doctor_appearance.get("female")
        or actor_identity.get("raceFormId") != doctor_race.get("formId")
        or actor_identity.get("hairFormId") != doctor_hair.get("formId")
        or actor_identity.get("eyesFormId") != doctor_eyes.get("formId")
        or actor_identity.get("headPartFormIds")
        != [row.get("formId") for row in doctor_head_parts if isinstance(row, dict)]
        or actor_identity.get("outfitFormIds")
        != [row.get("formId") for row in doctor_outfits if isinstance(row, dict)]
        or actor_coverage.get("animated") is not True
        or int(actor_coverage.get("components", 0)) <= 0
        or int(actor_coverage.get("skins", 0)) <= 0
        or int(actor_coverage.get("surfaces", 0)) <= 0
        or int(actor_coverage.get("textures", 0)) <= 0
        or int(actor_coverage.get("faceGenMorphTargets", 0)) <= 0
        or int(actor_coverage.get("omittedSurfaces", -1)) != 0
    ):
        raise ValueError("Compiled Doctor Li actor differs from the transported owned actor")

    actor_scene_path = Path(_required_string(actor_manifest, "manifest")).resolve()
    actor_sidecar_path = actor_scene_path.parent / _required_string(
        _required_object(actor_manifest, "outputs"), "sidecar"
    )
    actor_sidecar, _actor_sidecar_payload = _read_json(actor_sidecar_path)
    transported_models = {
        canonical_member_path(_required_string(row, "logicalPath")): row
        for row in _required_list(_required_object(doctor_source, "resources"), "models")
        if isinstance(row, dict)
    }
    bound_actor_models = []
    for row in _required_list(actor_sidecar, "nifDecodes"):
        if not isinstance(row, dict):
            raise ValueError("Compiled Doctor Li NIF provenance row is malformed")
        logical_path = canonical_member_path("meshes\\" + _required_string(row, "logicalPath"))
        transported = transported_models.get(logical_path)
        if transported is None or row.get("sha256") != transported.get("sha256"):
            raise ValueError(
                f"Compiled Doctor Li model escapes transported ownership: {logical_path}"
            )
        bound_actor_models.append(logical_path)
    if len(bound_actor_models) != int(actor_coverage["components"]) + 1:
        raise ValueError("Compiled Doctor Li component provenance coverage differs")

    dad_manifest = prepare_actor(
        Path(_required_string(source, "dataRoot")).resolve(),
        cache_root.resolve(),
        _required_string(dad_actor_recipe, "id"),
        recipe_document=dad_actor_recipe,
    )
    dad_reference = _required_object(dad_manifest, "reference")
    dad_identity = _required_object(dad_manifest, "actor")
    dad_coverage = _required_object(dad_manifest, "coverage")
    father_transform = _required_object(father_reference, "transform")
    father_marker_transform = _required_object(father_start_marker, "transform")
    if (
        dad_manifest.get("schema") != "opennv-actor-scene/v5"
        or dad_manifest.get("status") != "skinned-animated"
        or dad_manifest.get("cellFormId") != cell.get("formId")
        or dad_manifest.get("bodyModLogicalPath") is not None
        or dad_manifest.get("bodyModPolicy")
        != "owned-race-base-diffuse-when-precomputed-absent"
        or dad_manifest.get("bodySurfaceTextureSource")
        != "owned-race-base-diffuse-no-body-mod"
        or dad_reference.get("formId") != father_reference.get("formId")
        or dad_reference.get("baseFormId") != father_base.get("formId")
        or dad_reference.get("initiallyDisabled") is not False
        or dad_reference.get("positionGameUnits")
        != father_transform.get("positionGameUnits")
        or dad_reference.get("rotationRadians")
        != father_transform.get("rotationRadians")
        or dad_reference.get("scale") != father_transform.get("scale")
        or dad_identity.get("editorId") != father_base.get("editorId")
        or dad_identity.get("name") != "Dad"
        or dad_identity.get("female") is not False
        or int(dad_coverage.get("components", 0)) <= 0
        or int(dad_coverage.get("skins", 0)) <= 0
        or int(dad_coverage.get("surfaces", 0)) <= 0
        or int(dad_coverage.get("textures", 0)) <= 0
        or int(dad_coverage.get("faceGenMorphTargets", 0)) <= 0
        or int(dad_coverage.get("omittedSurfaces", -1)) != 0
    ):
        raise ValueError("Compiled CG00 Dad differs from the direct owned NPC")
    dad_scene_path = Path(_required_string(dad_manifest, "manifest")).resolve()

    cg01_dad_manifest = prepare_actor(
        Path(_required_string(source, "dataRoot")).resolve(),
        cache_root.resolve(),
        _required_string(cg01_dad_actor_recipe, "id"),
        recipe_document=cg01_dad_actor_recipe,
    )
    cg01_dad_reference = _required_object(cg01_dad_manifest, "reference")
    cg01_dad_identity = _required_object(cg01_dad_manifest, "actor")
    cg01_dad_coverage = _required_object(cg01_dad_manifest, "coverage")
    cg01_dad_transform = _required_object(cg01_dad_source, "sourceTransform")
    cg01_dad_marker_transform = _required_object(
        cg01_dad_marker, "sourceTransform"
    )
    if (
        cg01_dad_manifest.get("schema") != "opennv-actor-scene/v5"
        or cg01_dad_manifest.get("status") != "skinned-animated"
        or cg01_dad_manifest.get("cellFormId") != cell.get("formId")
        or cg01_dad_manifest.get("bodyModLogicalPath") is not None
        or cg01_dad_manifest.get("bodyModPolicy")
        != "owned-race-base-diffuse-when-precomputed-absent"
        or cg01_dad_manifest.get("bodySurfaceTextureSource")
        != "owned-race-base-diffuse-no-body-mod"
        or cg01_dad_reference.get("formId") != cg01_dad_source.get("formId")
        or cg01_dad_reference.get("baseFormId") != cg01_dad_base.get("formId")
        or cg01_dad_reference.get("initiallyDisabled") is not True
        or cg01_dad_reference.get("positionGameUnits")
        != cg01_dad_transform.get("positionGameUnits")
        or cg01_dad_reference.get("rotationRadians")
        != cg01_dad_transform.get("rotationRadians")
        or cg01_dad_reference.get("scale") != cg01_dad_transform.get("scale")
        or cg01_dad_identity.get("editorId") != cg01_dad_base.get("editorId")
        or cg01_dad_identity.get("name") != "Dad"
        or cg01_dad_identity.get("female") is not False
        or int(cg01_dad_coverage.get("components", 0)) <= 0
        or int(cg01_dad_coverage.get("skins", 0)) <= 0
        or int(cg01_dad_coverage.get("surfaces", 0)) <= 0
        or int(cg01_dad_coverage.get("textures", 0)) <= 0
        or int(cg01_dad_coverage.get("faceGenMorphTargets", 0)) <= 0
        or int(cg01_dad_coverage.get("omittedSurfaces", -1)) != 0
    ):
        raise ValueError("Compiled CG01 Dad differs from the source stage-0 actor")
    cg01_dad_scene_path = Path(
        _required_string(cg01_dad_manifest, "manifest")
    ).resolve()

    document: dict[str, object] = {
        "schema": OUTPUT_SCHEMA,
        "status": "prepared-owned-materials-doctor-cg00-dad-and-cg01-dad-not-yet-rendered",
        "recipe": {
            "id": _required_string(recipe, "id"),
            "path": str(recipe_path.resolve()),
            "sha256": _sha256_bytes(recipe_payload),
        },
        "source": {
            "profile": str(profile_path.resolve()),
            "birthSlice": str(birth_path),
            "birthSliceSha256": birth_sha256,
            "birthSliceRecipeId": _required_string(birth_recipe, "id"),
            "birthSliceRecipeSha256": _required_sha256(birth_recipe, "sha256"),
            "meshesArchive": {
                "file": meshes_path.name,
                "bytes": meshes_path.stat().st_size,
                "sha256": _sha256_file(meshes_path),
            },
            "texturesArchive": {
                "file": textures_path.name,
                "bytes": textures_path.stat().st_size,
                "sha256": _sha256_file(textures_path),
            },
        },
        "configuration": configuration.manifest(),
        "cell": {
            "formId": _required_string(cell, "formId"),
            "editorId": _required_string(cell, "editorId"),
            "name": _required_string(cell, "name"),
            "interior": cell.get("interior") is True,
        },
        "coordinates": {
            "source": "Gamebryo X-right/Y-forward/Z-up, radians",
            "target": "Godot X-right/Y-up/-Z-forward",
            "unitsToMeters": configuration.world_units_to_meters,
            "originGameUnits": list(origin),
        },
        "entry": {
            "source": "owned-player-start-marker-transform",
            "referenceFormId": _required_string(entry, "formId"),
            "positionGameUnits": list(origin),
            "positionGodotGameUnits": [0.0, 0.0, 0.0],
            "rotationRadians": list(entry_rotation),
            "rotationGodotQuaternion": godot_rotation_quaternion(entry_rotation),
            "yawGodotRadians": godot_yaw_radians(entry_rotation[2]),
        },
        "proofCamera": {
            "authority": "owned-CG00-support-mesh-top-derived-proof-only-not-retail-camera",
            "entryReferenceFormId": _required_string(entry, "formId"),
            "supportReferenceFormId": support_reference_form_id,
            "supportBaseEditorId": _required_string(support_base, "editorId"),
            "supportAssetId": assets[support_model_path]["id"],
            "supportSurfaceGodotGameUnits": support_surface_y,
            "surfaceClearanceGameUnits": camera_clearance,
            "nearGameUnits": camera_near,
            "positionGameUnits": camera_game_position,
            "positionGodotGameUnits": camera_local_position,
            "rotationGodotQuaternion": godot_rotation_quaternion(entry_rotation),
        },
        "doctorActor": {
            "source": "transported-owned-ACHR-NPC-template-and-appearance-closure",
            "scene": _cache_relative_derivative(cache_root, actor_scene_path),
            "sha256": _sha256_file(actor_scene_path),
            "recipe": {
                "id": _required_string(actor_recipe, "id"),
                "path": str(actor_recipe_path.resolve()),
                "sha256": _sha256_bytes(actor_recipe_payload),
            },
            "sourceRecordBindings": {
                "referenceFormId": _required_string(doctor_reference, "formId"),
                "baseFormId": _required_string(doctor_base, "formId"),
                "baseRecordDataSha256": _required_sha256(
                    doctor_base, "recordDataSha256"
                ),
                "raceRecordDataSha256": _required_sha256(
                    doctor_race, "recordDataSha256"
                ),
                "hairRecordDataSha256": _required_sha256(
                    doctor_hair, "recordDataSha256"
                ),
                "eyesRecordDataSha256": _required_sha256(
                    doctor_eyes, "recordDataSha256"
                ),
            },
            "boundTransportedModels": sorted(bound_actor_models),
            "reference": actor_reference,
            "actor": actor_identity,
            "coverage": actor_coverage,
            "poseAuthority": (
                "owned mtidle compiler input only; CG00 package and scripted idle selection "
                "are not implemented"
            ),
        },
        "dadActor": {
            "source": "direct-owned-CG00Dad-ACHR-NPC-race-and-FaceGen",
            "scene": _cache_relative_derivative(cache_root, dad_scene_path),
            "sha256": _sha256_file(dad_scene_path),
            "recipe": {
                "id": _required_string(dad_actor_recipe, "id"),
                "path": str(dad_actor_recipe_path.resolve()),
                "sha256": _sha256_bytes(dad_actor_recipe_payload),
            },
            "sourceRecordBindings": {
                "referenceFormId": _required_string(father_reference, "formId"),
                "baseFormId": _required_string(father_base, "formId"),
                "baseRecordDataSha256": _required_sha256(
                    father_base, "recordDataSha256"
                ),
            },
            "reference": dad_reference,
            "actor": dad_identity,
            "coverage": dad_coverage,
            "bodySurfaceTextureSource": _required_string(
                dad_manifest, "bodySurfaceTextureSource"
            ),
            "bodyModPolicy": _required_string(dad_manifest, "bodyModPolicy"),
            "startMarker": {
                "referenceFormId": _required_string(father_start_marker, "formId"),
                "positionGameUnits": _required_list(
                    father_marker_transform, "positionGameUnits"
                ),
                "positionGodotGameUnits": godot_position(
                    tuple(
                        float(value)
                        for value in _required_list(
                            father_marker_transform, "positionGameUnits"
                        )
                    ),
                    origin,
                ),
                "rotationRadians": _required_list(
                    father_marker_transform, "rotationRadians"
                ),
                "rotationGodotQuaternion": godot_rotation_quaternion(
                    tuple(
                        float(value)
                        for value in _required_list(
                            father_marker_transform, "rotationRadians"
                        )
                    )
                ),
            },
            "poseAuthority": (
                "owned mtidle compiler input and exact stage-0 MoveTo marker only; "
                "CG00 package idle selection is not implemented"
            ),
        },
        "cg01DadActor": {
            "source": "source-stage-0-CG01Dad-ACHR-NPC-raw-authored-appearance",
            "scene": _cache_relative_derivative(cache_root, cg01_dad_scene_path),
            "sha256": _sha256_file(cg01_dad_scene_path),
            "recipe": {
                "id": _required_string(cg01_dad_actor_recipe, "id"),
                "path": str(cg01_dad_actor_recipe_path.resolve()),
                "sha256": _sha256_bytes(cg01_dad_actor_recipe_payload),
            },
            "sourceRecordBindings": {
                "referenceFormId": _required_string(cg01_dad_source, "formId"),
                "referenceRecordSha256": _required_sha256(
                    cg01_dad_source, "recordSha256"
                ),
                "baseFormId": _required_string(cg01_dad_base, "formId"),
                "baseRecordDataSha256": _required_sha256(
                    cg01_dad_base, "recordSha256"
                ),
            },
            "reference": cg01_dad_reference,
            "actor": cg01_dad_identity,
            "coverage": cg01_dad_coverage,
            "bodySurfaceTextureSource": _required_string(
                cg01_dad_manifest, "bodySurfaceTextureSource"
            ),
            "bodyModPolicy": _required_string(cg01_dad_manifest, "bodyModPolicy"),
            "startMarker": {
                "referenceFormId": _required_string(cg01_dad_marker, "formId"),
                "referenceRecordSha256": _required_sha256(
                    cg01_dad_marker, "recordSha256"
                ),
                "positionGameUnits": _required_list(
                    cg01_dad_marker_transform, "positionGameUnits"
                ),
                "positionGodotGameUnits": godot_position(
                    tuple(
                        float(value)
                        for value in _required_list(
                            cg01_dad_marker_transform, "positionGameUnits"
                        )
                    ),
                    origin,
                ),
                "rotationRadians": _required_list(
                    cg01_dad_marker_transform, "rotationRadians"
                ),
                "rotationGodotQuaternion": godot_rotation_quaternion(
                    tuple(
                        float(value)
                        for value in _required_list(
                            cg01_dad_marker_transform, "rotationRadians"
                        )
                    )
                ),
            },
            "poseAuthority": (
                "owned mtidle compiler input and exact CG01 stage-0 MoveTo marker; "
                "stage-5 enable is runtime-applied; stage-65 matched race and FaceGen "
                "geometry are not applied to this raw authored actor"
            ),
        },
        "presentation": {
            "verticalFovDegrees": float(presentation["verticalFovDegrees"]),
            "proofAmbientColor": presentation["proofAmbientColor"],
            "proofAmbientEnergy": float(presentation["proofAmbientEnergy"]),
            "proofFogNearGameUnits": float(presentation["proofFogNearGameUnits"]),
            "proofFogFarGameUnits": float(presentation["proofFogFarGameUnits"]),
            "proofFogPower": float(presentation["proofFogPower"]),
            "proofBackgroundColor": presentation["proofBackgroundColor"],
            "lightingAuthority": "recipe-proof-only-not-retail-CELL-lighting",
            "materialAuthority": "owned-NIF-surface-identity-and-owned-DDS-bindings",
        },
        "assets": sorted(assets.values(), key=lambda value: str(value["id"])),
        "textures": sorted(
            (artifact.manifest() for artifact in texture_artifacts.values()),
            key=lambda value: str(value["id"]),
        ),
        "unresolvedTextureBindings": unresolved_texture_bindings,
        "references": sorted(references, key=lambda value: str(value["formId"])),
        "coverage": {
            "sourceCellReferences": len(_required_list(graph, "references")),
            "selectedReferences": len(selected),
            "renderableReferences": len(references),
            "selectedUniqueModels": len(selected_models),
            "renderableAssets": len(assets),
            "nonPresentationAssets": len(non_presentation),
            "authoredTextureBindingRequests": sum(len(rows) for rows in binding_uses.values()),
            "resolvedUniqueTextures": len(texture_artifacts),
            "unresolvedUniqueTextures": len(unresolved_texture_bindings),
            "excludedReferencesByReason": dict(sorted(excluded.items())),
        },
        "nonPresentationAssets": non_presentation,
        "promotion": {
            "transported": True,
            "texturesPrepared": True,
            "doctorActorPrepared": True,
            "dadActorPrepared": True,
            "cg01DadActorPrepared": True,
            "runtimeManifestValidated": False,
            "runtimeSceneConstructed": False,
            "rendered": False,
            "interactive": False,
            "actorsRendered": False,
            "questCommandsExecuted": False,
            "parityReviewed": False,
            "headsetAccepted": False,
        },
        "unsupported": _required_list(recipe, "unsupported"),
    }
    output = output_root / OUTPUT_NAME
    _atomic_json(output, document)
    return output


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=_default_recipe_path())
    parser.add_argument(
        "--actor-recipe", type=Path, default=_default_actor_recipe_path()
    )
    parser.add_argument(
        "--dad-actor-recipe", type=Path, default=_default_dad_actor_recipe_path()
    )
    parser.add_argument(
        "--cg01-dad-actor-recipe",
        type=Path,
        default=None,
    )
    arguments = parser.parse_args()
    output = prepare(
        arguments.profile,
        arguments.cache_root,
        arguments.recipe,
        arguments.actor_recipe,
        arguments.dad_actor_recipe,
        arguments.cg01_dad_actor_recipe,
    )
    print(
        json.dumps(
            {
                "schema": OUTPUT_SCHEMA,
                "output": str(output.resolve()),
                "sha256": _sha256_file(output),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
