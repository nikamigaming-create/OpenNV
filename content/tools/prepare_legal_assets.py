#!/usr/bin/env python3
"""Validate an owned FNV Data folder and build the first direct OpenNV cache."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import os
import sys
from pathlib import Path

from actor_catalog import scan_actor_catalog
from cell_scene import load_recipe, load_spatial_recipe, prepare_cell_scene
from exterior_scene import prepare_exterior_scene
from compiler_provenance import FAMILIES, compiler_identities
from export_static_nif_gltf import export_static_nif
from opening_catalog import (
    FULL_PLAYER_FACEGEN_PROFILE,
    PLAYER_FACEGEN_PROFILES,
    ROUTE_PLAYER_FACEGEN_PROFILE,
    prepare_opening_manifest,
)
from owned_archive_stack import (
    AUDIO_ARCHIVE_RECIPE_SCHEMA,
    load_owned_archive_stack,
)
from prepare_actor import prepare_actor_set
from plugin_records import iter_plugin_records
from prepare_fo3_profile import (
    default_recipe_path as default_fo3_profile_recipe_path,
    prepare_profile as prepare_fo3_profile,
)
from runtime_configuration import configured_recipe_path, load_runtime_configuration


SCHEMA = "opennv-legal-asset-cache/v1"
INITIALLY_DISABLED_RECORD_FLAG = 0x00000800
FORM_ID_RADIX = 16


def discover_effective_exterior_actors(
    master: Path,
    recipe: dict[str, object],
    scene: dict[str, object],
) -> list[dict[str, object]]:
    discovery = recipe.get("actorDiscovery")
    if discovery is None:
        return []
    if discovery != {"mode": "effective-achr"}:
        raise ValueError("Exterior actor discovery must use effective-achr mode")
    cell = dict(scene["cell"])
    coordinates = dict(scene["coordinates"])
    coverage = dict(scene["coverage"])
    lod = dict(coverage["lod"])
    source_cells = {
        int(str(value), FORM_ID_RADIX) for value in cell["sourceCellFormIds"]
    }
    persistent_cell = int(str(recipe["persistentCellFormId"]), FORM_ID_RADIX)
    loaded_grids = {
        (int(value[0]), int(value[1])) for value in coordinates["loadedCellGrids"]
    }
    cell_size = float(lod["cellSizeGameUnits"])
    if not cell_size > 0.0:
        raise ValueError("Exterior actor discovery has an invalid source CELL size")
    actor_catalog = scan_actor_catalog(master)
    references = [
        reference
        for reference in actor_catalog.references
        if reference.record_type == "ACHR"
        and reference.cell_form_id in source_cells
        and reference.actor_form_id in actor_catalog.actors
        and (
            reference.cell_form_id != persistent_cell
            or (
                math.floor(reference.position[0] / cell_size),
                math.floor(reference.position[1] / cell_size),
            )
            in loaded_grids
        )
    ]
    parent_ids = {
        reference.enable_parent_form_id
        for reference in references
        if reference.enable_parent_form_id is not None
    }
    parent_flags = {
        record.form_id: record.flags
        for record in iter_plugin_records(master, frozenset({"REFR", "ACHR", "ACRE"}))
        if record.form_id in parent_ids
    }
    if set(parent_flags) != parent_ids:
        missing = sorted(parent_ids - set(parent_flags))
        raise ValueError(
            "Effective ACHR enable parents are absent: "
            + ",".join(f"{value:08x}" for value in missing)
        )
    documents = []
    for reference in sorted(references, key=lambda value: value.form_id):
        documents.append(
            {
                "schema": "opennv-actor-recipe/v1",
                "id": f"source-achr-{reference.form_id:08x}",
                "master": recipe["master"],
                "meshesArchive": recipe["meshesArchive"],
                "textureArchives": recipe["textureArchives"],
                "cellFormId": f"{reference.cell_form_id:08x}",
                "cellRecipe": str(recipe["id"]),
                "originGameUnits": list(coordinates["originGameUnits"]),
                "proofActorReferenceFormId": f"{reference.form_id:08x}",
                "enableParentInitiallyDisabled": (
                    None
                    if reference.enable_parent_form_id is None
                    else bool(
                        parent_flags[reference.enable_parent_form_id]
                        & INITIALLY_DISABLED_RECORD_FLAG
                    )
                ),
            }
        )
    return documents


def _install_matches_outside_player_facegen_profile(
    prior: object,
    current: object,
) -> bool:
    if not isinstance(prior, dict) or not isinstance(current, dict):
        return False
    prior_copy = copy.deepcopy(prior)
    current_copy = copy.deepcopy(current)
    for document in (prior_copy, current_copy):
        request = document.get("request")
        if isinstance(request, dict):
            request.pop("playerFaceGenProfile", None)
    return prior_copy == current_copy


def _player_facegen_profile(install: object) -> object:
    if not isinstance(install, dict) or not isinstance(install.get("request"), dict):
        return None
    return install["request"].get("playerFaceGenProfile")


def route_exterior_positions(
    opening_manifest: dict[str, object],
    persistent_cell_form_id: str,
) -> tuple[tuple[float, float, float], ...]:
    """Collect source route positions owned by one exterior persistent CELL."""
    positions: set[tuple[float, float, float]] = set()

    def visit(value: object) -> None:
        if isinstance(value, dict):
            position = value.get("positionGameUnits")
            if (
                str(value.get("cellFormId", "")).casefold()
                == persistent_cell_form_id.casefold()
                and isinstance(position, list)
                and len(position) == 3
                and all(isinstance(component, (int, float)) for component in position)
            ):
                positions.add(tuple(float(component) for component in position))
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(opening_manifest.get("newGameFlow"))
    return tuple(sorted(positions))


def file_sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def find_required_file(root: Path, expected_name: str) -> Path:
    matches = [path for path in root.iterdir() if path.is_file() and path.name.lower() == expected_name.lower()]
    if len(matches) != 1:
        raise FileNotFoundError(f"Expected one {expected_name!r} in {root}, found {len(matches)}")
    return matches[0]


def atomic_text(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(document, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    os.replace(temporary, path)


def _same_compiler(actual: object, expected: dict[str, str]) -> bool:
    return isinstance(actual, dict) and all(
        actual.get(key) == value for key, value in expected.items()
    )


def _hash_matches(path_value: object, digest_value: object) -> bool:
    if not isinstance(path_value, str) or not isinstance(digest_value, str):
        return False
    path = Path(path_value)
    return path.is_file() and file_sha256(path) == digest_value.lower()


def _document_compiler_matches(path_value: object, expected: dict[str, str]) -> bool:
    if not isinstance(path_value, str):
        return False
    path = Path(path_value)
    if not path.is_file():
        return False
    document = json.loads(path.read_text(encoding="utf-8"))
    return _same_compiler(document.get("compiler"), expected)


def _opening_inputs_match(path_value: object) -> bool:
    if not isinstance(path_value, str):
        return False
    document = json.loads(Path(path_value).read_text(encoding="utf-8"))
    return all(
        (
            video.get("source") is None
            or _hash_matches(video.get("source"), video.get("sha256"))
        )
        and (
            video.get("runtime") is None
            or _hash_matches(
                video["runtime"].get("output"),
                video["runtime"].get("outputSha256"),
            )
        )
        for video in document.get("videos", [])
    )


def _opening_preview_set_matches(
    opening_manifest_path: object,
    preview_set_path: object,
    preview_set_sha256: object,
) -> bool:
    if not isinstance(opening_manifest_path, str):
        return False
    document = json.loads(Path(opening_manifest_path).read_text(encoding="utf-8"))
    outputs = document.get("outputs")
    if not isinstance(outputs, dict):
        return False
    preview_set = outputs.get("playerFaceGenPreviewSet")
    return (
        isinstance(preview_set, dict)
        and preview_set.get("path") == preview_set_path
        and preview_set.get("sha256") == preview_set_sha256
        and _hash_matches(preview_set_path, preview_set_sha256)
    )


def reusable_families(
    prior: object,
    install: dict[str, object],
    identities: dict[str, object],
    *,
    require_cell: bool,
    require_actor: bool,
    cell_recipe_id: str | None = None,
    linked_recipe_ids: tuple[str, ...] = (),
    actor_recipe_ids: tuple[str, ...] = (),
) -> dict[str, bool]:
    """Fail-closed family reuse plan for an explicit prepare operation."""
    result = {family: False for family in FAMILIES}
    if not isinstance(prior, dict):
        return result
    if (
        prior.get("schema") != SCHEMA
        or prior.get("status") != "prepared-legal-assets"
        or not _install_matches_outside_player_facegen_profile(
            prior.get("install"), install
        )
        or not isinstance(prior.get("compilerFamilies"), dict)
        or not isinstance(prior.get("outputs"), dict)
    ):
        return result
    outputs = prior["outputs"]
    expected_families = identities["families"]
    for family in FAMILIES:
        expected = expected_families[family]
        if not _same_compiler(prior["compilerFamilies"].get(family), expected):
            continue
        try:
            if family == "static":
                result[family] = (
                    _hash_matches(outputs.get("model"), outputs.get("modelSha256"))
                    and _hash_matches(outputs.get("sidecar"), outputs.get("sidecarSha256"))
                    and _document_compiler_matches(outputs.get("sidecar"), expected)
                )
            elif family == "opening":
                result[family] = (
                    _player_facegen_profile(prior.get("install"))
                    == _player_facegen_profile(install)
                    and
                    _hash_matches(
                        outputs.get("openingManifest"),
                        outputs.get("openingManifestSha256"),
                    )
                    and _document_compiler_matches(outputs.get("openingManifest"), expected)
                    and _opening_inputs_match(outputs.get("openingManifest"))
                    and _opening_preview_set_matches(
                        outputs.get("openingManifest"),
                        outputs.get("openingPlayerFaceGenPreviewSet"),
                        outputs.get("openingPlayerFaceGenPreviewSetSha256"),
                    )
                )
            elif family == "cell":
                if not require_cell:
                    result[family] = outputs.get("cellScene") is None
                    continue
                linked = outputs.get("linkedCellScenes")
                primary = json.loads(
                    Path(str(outputs.get("cellScene"))).read_text(encoding="utf-8")
                )
                result[family] = (
                    isinstance(linked, list)
                    and primary.get("recipe") == cell_recipe_id
                    and tuple(str(row.get("recipe")) for row in linked)
                    == linked_recipe_ids
                    and _hash_matches(
                        outputs.get("cellScene"),
                        outputs.get("cellSceneSha256"),
                    )
                    and _document_compiler_matches(outputs.get("cellScene"), expected)
                    and all(
                        isinstance(row, dict)
                        and _hash_matches(row.get("scene"), row.get("sha256"))
                        and _document_compiler_matches(row.get("scene"), expected)
                        for row in linked
                    )
                )
            else:
                if not require_actor:
                    result[family] = outputs.get("actorScenes") is None
                    continue
                if not (
                    _hash_matches(
                        outputs.get("actorScenes"),
                        outputs.get("actorScenesSha256"),
                    )
                    and _document_compiler_matches(outputs.get("actorScenes"), expected)
                ):
                    continue
                actor_set = json.loads(Path(outputs["actorScenes"]).read_text(encoding="utf-8"))
                actors = actor_set.get("actors", [])
                result[family] = (
                    isinstance(actors, list)
                    and tuple(str(row.get("recipe")) for row in actors)
                    == actor_recipe_ids
                    and all(
                        isinstance(row, dict)
                        and _hash_matches(row.get("scene"), row.get("sha256"))
                        and _document_compiler_matches(row.get("scene"), expected)
                        for row in actors
                    )
                )
        except (KeyError, OSError, ValueError, json.JSONDecodeError):
            result[family] = False
    return result


def prepare(
    data_root: Path,
    cache_root: Path,
    logical_model: str | None = None,
    expected_meshes_sha256: str = "",
    cell_recipe: str | None = None,
    preferences_ini: Path | None = None,
    player_facegen_profile: str = FULL_PLAYER_FACEGEN_PROFILE,
) -> dict[str, object]:
    if player_facegen_profile not in PLAYER_FACEGEN_PROFILES:
        raise ValueError("Owned player FaceGen profile is unsupported")
    configuration = load_runtime_configuration()
    legal_assets = configuration.document["legalAssets"]
    if not isinstance(legal_assets, dict):
        raise ValueError("OpenNV legal-asset configuration is invalid")
    owned_data = legal_assets["ownedData"]
    if not isinstance(owned_data, dict):
        raise ValueError("OpenNV legal owned-data configuration is invalid")
    logical_model = logical_model or str(legal_assets["smokeModelLogicalPath"])
    cell_recipe = (
        str(legal_assets["defaultCellRecipe"])
        if cell_recipe is None
        else cell_recipe
    )
    master = find_required_file(data_root, str(owned_data["masterFile"]))
    default_ini = find_required_file(data_root.parent, str(owned_data["defaultIniFile"]))
    meshes = find_required_file(data_root, str(owned_data["meshesArchiveFile"]))
    ui_archive = find_required_file(data_root, str(owned_data["uiArchiveFile"]))
    master_hash = file_sha256(master)
    meshes_hash = file_sha256(meshes)
    if expected_meshes_sha256 and meshes_hash != expected_meshes_sha256.lower():
        raise ValueError(
            f"Meshes BSA hash mismatch: expected={expected_meshes_sha256.lower()} actual={meshes_hash}"
        )
    visual_archives = load_owned_archive_stack(
        data_root,
        configured_recipe_path("visualArchives"),
    )
    audio_archives = load_owned_archive_stack(
        data_root,
        configured_recipe_path("audioArchives"),
        AUDIO_ARCHIVE_RECIPE_SCHEMA,
    )
    texture_archives = (
        [
            find_required_file(data_root, str(file_name))
            for file_name in owned_data["textureArchiveFiles"]
        ]
        if cell_recipe
        else []
    )
    texture_archive_rows = [
        {
            "file": archive.name,
            "bytes": archive.stat().st_size,
            "sha256": file_sha256(archive),
        }
        for archive in texture_archives
    ]
    cell_recipe_document = load_recipe(cell_recipe) if cell_recipe else None
    linked_recipe_documents: list[dict[str, object]] = []
    configured_links = (
        None if cell_recipe_document is None else cell_recipe_document.get("linkedCellRecipes")
    )
    if (
        cell_recipe_document is not None
        and configured_links is None
        and cell_recipe_document.get("linkedExteriorRecipe")
    ):
        configured_links = [
            {
                "recipe": cell_recipe_document["linkedExteriorRecipe"],
                "fromDoorReferenceFormId": cell_recipe_document["entryDoorReferenceFormId"],
            }
        ]
    if configured_links is not None:
        if not isinstance(configured_links, list) or not configured_links:
            raise ValueError("Linked CELL recipes must be a non-empty ordered list")
        linked_recipe_documents = [
            load_spatial_recipe(str(row["recipe"])) for row in configured_links
        ]
    actor_recipe_ids = (
        []
        if cell_recipe_document is None
        else [str(value) for value in cell_recipe_document["actorRecipes"]]
    )
    for linked_recipe_document in linked_recipe_documents:
        actor_recipe_ids.extend(str(value) for value in linked_recipe_document["actorRecipes"])
    actor_discovery_enabled = any(
        document.get("actorDiscovery") is not None
        for document in [cell_recipe_document, *linked_recipe_documents]
        if document is not None
    )
    opening_recipe_path = configured_recipe_path("opening")
    install = {
        "dataRoot": str(data_root.resolve()),
        "request": {
            "logicalModel": logical_model,
            "cellRecipe": cell_recipe,
            "openingRecipe": opening_recipe_path.stem,
            "playerFaceGenProfile": player_facegen_profile,
        },
        "master": {"file": master.name, "bytes": master.stat().st_size, "sha256": master_hash},
        "defaultIni": {
            "file": default_ini.name,
            "bytes": default_ini.stat().st_size,
            "sha256": file_sha256(default_ini),
        },
        "preferencesIni": None
        if preferences_ini is None
        else {
            "file": preferences_ini.name,
            "path": str(preferences_ini.resolve()),
            "bytes": preferences_ini.stat().st_size,
            "sha256": file_sha256(preferences_ini),
        },
        "meshesArchive": {"file": meshes.name, "bytes": meshes.stat().st_size, "sha256": meshes_hash},
        "uiArchive": {
            "file": ui_archive.name,
            "bytes": ui_archive.stat().st_size,
            "sha256": file_sha256(ui_archive),
        },
        "textureArchives": texture_archive_rows,
        "archiveStack": visual_archives.manifest(),
        "audioArchiveStack": audio_archives.manifest(),
    }
    identities = compiler_identities(cell_recipe)
    prior_path = cache_root / "install-manifest.json"
    try:
        prior = json.loads(prior_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        prior = None
    reuse = reusable_families(
        prior,
        install,
        identities,
        require_cell=cell_recipe_document is not None,
        require_actor=bool(actor_recipe_ids) or actor_discovery_enabled,
        cell_recipe_id=(
            None if cell_recipe_document is None else str(cell_recipe_document["id"])
        ),
        linked_recipe_ids=tuple(str(row["id"]) for row in linked_recipe_documents),
        actor_recipe_ids=tuple(actor_recipe_ids),
    )
    if actor_discovery_enabled:
        reuse["actor"] = False
    if opening_recipe_path.stem != str(legal_assets["defaultOpeningRecipe"]):
        raise ValueError("Configured opening recipe registry and legal-assets default differ")
    if reuse["opening"]:
        opening_path = Path(str(prior["outputs"]["openingManifest"]))
        opening = {
            "output": str(opening_path.resolve()),
            "manifest": json.loads(opening_path.read_text(encoding="utf-8")),
        }
    else:
        opening = prepare_opening_manifest(
            data_root,
            master,
            ui_archive,
            visual_archives,
            audio_archives,
            cache_root,
            opening_recipe_path,
            configuration,
            str(owned_data["videoDirectoryName"]),
            master_hash,
            default_ini,
            preferences_ini,
            player_facegen_profile,
        )
    if reuse["static"]:
        asset = prior["asset"]
        outputs = prior["outputs"]
        gltf_path = Path(str(outputs["model"]))
        sidecar_path = Path(str(outputs["sidecar"]))
        sidecar = json.loads(sidecar_path.read_text(encoding="utf-8"))
    else:
        member = visual_archives.extract(logical_model)
        asset = {
            "logicalPath": member.logical_path,
            "bytes": len(member.data),
            "sha256": member.sha256,
            "compressedInArchive": member.compressed,
            "archiveOffset": member.archive_offset,
            "storedBytes": member.stored_bytes,
            "sourceArchive": member.source_archive,
            "sourceArchiveSha256": member.source_archive_sha256,
        }
        source_path = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
        source_path.parent.mkdir(parents=True, exist_ok=True)
        temporary_source = source_path.with_name(source_path.name + ".tmp")
        temporary_source.write_bytes(member.data)
        os.replace(temporary_source, source_path)
        output_root = cache_root / "generated" / "static"
        gltf_path = output_root / "retail-static.gltf"
        sidecar_path = output_root / "retail-static.opennv.json"
        sidecar = export_static_nif(
            source_path,
            member.logical_path,
            gltf_path,
            sidecar_path,
            configuration.content_compiler,
            strict=True,
        )
    cell_scene = None
    linked_cell_scenes: list[dict[str, object]] = []
    actor_scenes = None
    if cell_recipe_document is not None:
        if reuse["cell"]:
            cell_scene = {"output": str(prior["outputs"]["cellScene"])}
            linked_cell_scenes = list(prior["outputs"]["linkedCellScenes"])
        else:
            cell_scene = prepare_cell_scene(
                master,
                meshes,
                texture_archives,
                texture_archive_rows,
                cache_root,
                cell_recipe_document,
                master_hash,
                visual_archives,
                identities["families"]["cell"],
            )
            if configured_links is not None:
                available_scenes = [
                    json.loads(Path(str(cell_scene["output"])).read_text(encoding="utf-8"))
                ]
                seen_recipes = {str(cell_recipe_document["id"])}
                for configured_link, linked_recipe_document in zip(
                    configured_links,
                    linked_recipe_documents,
                    strict=True,
                ):
                    if not isinstance(configured_link, dict):
                        raise ValueError("Linked CELL recipe row must be an object")
                    recipe_id = str(configured_link.get("recipe", ""))
                    from_door = str(configured_link.get("fromDoorReferenceFormId", "")).lower()
                    if not recipe_id or not from_door or recipe_id in seen_recipes:
                        raise ValueError("Linked CELL recipe identity is missing or duplicated")
                    seen_recipes.add(recipe_id)
                    if linked_recipe_document["schema"] == "opennv-exterior-recipe/v1":
                        route_positions = (
                            route_exterior_positions(
                                opening["manifest"],
                                str(linked_recipe_document["persistentCellFormId"]),
                            )
                            if player_facegen_profile == ROUTE_PLAYER_FACEGEN_PROFILE
                            else ()
                        )
                        linked_scene = prepare_exterior_scene(
                            master,
                            meshes,
                            texture_archives,
                            texture_archive_rows,
                            cache_root,
                            linked_recipe_document,
                            master_hash,
                            owned_archives=visual_archives,
                            family_compiler=identities["families"]["cell"],
                            required_route_positions_game_units=route_positions,
                        )
                    else:
                        linked_scene = prepare_cell_scene(
                            master,
                            meshes,
                            texture_archives,
                            texture_archive_rows,
                            cache_root,
                            linked_recipe_document,
                            master_hash,
                            visual_archives,
                            identities["families"]["cell"],
                        )
                    linked_document = json.loads(
                        Path(str(linked_scene["output"])).read_text(encoding="utf-8")
                    )
                    to_door = str(linked_recipe_document["entryDoorReferenceFormId"]).lower()
                    source_doors = {
                        str(reference["formId"]).lower()
                        for reference in available_scenes[-1]["references"]
                        if isinstance(reference.get("interaction"), dict)
                        and reference["interaction"].get("type") == "door"
                    }
                    target_doors = {
                        str(reference["formId"]).lower()
                        for reference in linked_document["references"]
                        if isinstance(reference.get("interaction"), dict)
                        and reference["interaction"].get("type") == "door"
                    }
                    if from_door not in source_doors or to_door not in target_doors:
                        raise ValueError(
                            f"Linked CELL portal doors are absent: {from_door} -> {to_door}"
                        )
                    spawn = linked_document["spawn"]
                    if (
                        str(spawn.get("sourceDoorReferenceFormId", "")).lower() != from_door
                        or str(spawn.get("targetDoorReferenceFormId", "")).lower() != to_door
                    ):
                        raise ValueError(
                            f"Linked CELL XTEL differs: {from_door} -> {to_door}"
                        )
                    linked_cell_scenes.append(
                        {
                            "fromRecipe": available_scenes[-1]["recipe"],
                            "fromCellFormId": available_scenes[-1]["cell"]["formId"],
                            "recipe": linked_recipe_document["id"],
                            "cellFormId": linked_document["cell"]["formId"],
                            "recipeSha256": linked_document["recipeSha256"],
                            "scene": linked_scene["output"],
                            "sha256": file_sha256(Path(str(linked_scene["output"]))),
                            "fromDoorReferenceFormId": from_door,
                            "toDoorReferenceFormId": to_door,
                        }
                    )
                    available_scenes.append(linked_document)
            if linked_cell_scenes:
                cell_scene_path = Path(str(cell_scene["output"]))
                primary_document = json.loads(cell_scene_path.read_text(encoding="utf-8"))
                primary_document["linkedCells"] = linked_cell_scenes
                atomic_text(cell_scene_path, primary_document)
        actor_recipe_documents = [load_recipe(value) for value in actor_recipe_ids]
        scene_documents = [
            json.loads(Path(str(cell_scene["output"])).read_text(encoding="utf-8")),
            *(
                json.loads(Path(str(row["scene"])).read_text(encoding="utf-8"))
                for row in linked_cell_scenes
            ),
        ]
        recipes_by_id = {
            str(document["id"]): document
            for document in [cell_recipe_document, *linked_recipe_documents]
        }
        for scene_document in scene_documents:
            spatial_recipe = recipes_by_id[str(scene_document["recipe"])]
            actor_recipe_documents.extend(
                discover_effective_exterior_actors(
                    master,
                    spatial_recipe,
                    scene_document,
                )
            )
        references = [
            str(document["proofActorReferenceFormId"]).casefold()
            for document in actor_recipe_documents
        ]
        if len(references) != len(set(references)):
            raise ValueError("Named and discovered actor recipes overlap one ACHR")
        actor_recipe_ids = [str(document["id"]) for document in actor_recipe_documents]
        if actor_recipe_ids:
            if reuse["actor"]:
                actor_scenes = {"manifest": str(prior["outputs"]["actorScenes"])}
            else:
                actor_scenes = prepare_actor_set(
                    data_root,
                    cache_root,
                    actor_recipe_ids,
                    {
                        str(row["referenceFormId"]).casefold(): tuple(
                            str(path) for path in row["logicalPaths"]
                        )
                        for row in opening["manifest"]["newGameFlow"]["actorAnimations"]
                    },
                    {
                        str(opening["manifest"]["newGameFlow"]["guideActorAi"][
                            "referenceFormId"
                        ]).casefold(): tuple(
                            dict(row)
                            for row in opening["manifest"]["newGameFlow"][
                                "guideActorAi"
                            ]["animationObjects"]
                        )
                    },
                    {
                        str(opening["manifest"]["newGameFlow"]["guideActorAi"][
                            "referenceFormId"
                        ]).casefold(): {
                            str(opening["manifest"]["newGameFlow"]["guideActorAi"][
                                "furnitureOccupancy"
                            ]["exit"]["logicalPath"]): str(
                                opening["manifest"]["newGameFlow"]["guideActorAi"][
                                    "furnitureOccupancy"
                                ]["exit"]["sha256"]
                            )
                        }
                    },
                    identities["families"]["actor"],
                    recipe_documents=actor_recipe_documents,
                )
    manifest = {
        "schema": SCHEMA,
        "status": "prepared-legal-assets",
        "install": install,
        "compilerFamilies": identities["families"],
        "reuse": {
            family: "reused" if reuse[family] else "rebuilt"
            for family in FAMILIES
        },
        "asset": asset,
        "outputs": {
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "modelSha256": sidecar["outputs"]["gltf"]["sha256"],
            "sidecarSha256": file_sha256(sidecar_path),
            "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
            "cellScene": None if cell_scene is None else cell_scene["output"],
            "cellSceneSha256": (
                None if cell_scene is None else file_sha256(Path(str(cell_scene["output"])))
            ),
            "linkedCellScenes": linked_cell_scenes,
            "actorScenes": None if actor_scenes is None else actor_scenes["manifest"],
            "actorScenesSha256": (
                None
                if actor_scenes is None
                else file_sha256(Path(str(actor_scenes["manifest"])))
            ),
            "openingManifest": opening["output"],
            "openingManifestSha256": file_sha256(Path(str(opening["output"]))),
            "openingPlayerFaceGenPreviewSet": opening["manifest"]["outputs"][
                "playerFaceGenPreviewSet"
            ]["path"],
            "openingPlayerFaceGenPreviewSetSha256": opening["manifest"]["outputs"][
                "playerFaceGenPreviewSet"
            ]["sha256"],
        },
    }
    atomic_text(cache_root / "install-manifest.json", manifest)
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--compiler-identity", action="store_true")
    parser.add_argument("--data-root", type=Path)
    parser.add_argument("--cache-root", type=Path)
    parser.add_argument(
        "--campaign",
        choices=("NewVegas", "Fallout3"),
        default="NewVegas",
    )
    parser.add_argument(
        "--logical-model",
    )
    parser.add_argument("--expected-meshes-bsa-sha256", default="")
    parser.add_argument("--cell-recipe")
    parser.add_argument("--preferences-ini", type=Path)
    parser.add_argument(
        "--player-facegen-profile",
        choices=tuple(sorted(PLAYER_FACEGEN_PROFILES)),
        default=FULL_PLAYER_FACEGEN_PROFILE,
    )
    args = parser.parse_args()
    if args.compiler_identity:
        print(
            "OPENNV_CONTENT_COMPILER_IDENTITY "
            + json.dumps(compiler_identities(args.cell_recipe), sort_keys=True)
        )
        return 0
    if args.data_root is None or args.cache_root is None:
        parser.error("--data-root and --cache-root are required unless --compiler-identity is used")
    try:
        if args.campaign == "Fallout3":
            result = prepare_fo3_profile(
                args.data_root.resolve(),
                args.cache_root.resolve(),
                default_fo3_profile_recipe_path(),
            )
            manifest = result["manifest"]
            print(
                "OPENNV_FO3_PROFILE "
                + json.dumps(
                    {
                        "profile": result["output"],
                        "profileId": manifest["profileId"],
                        "runtimeBootReady": manifest["capabilities"]["runtimeBootReady"],
                        "blockers": manifest["blockers"],
                    },
                    sort_keys=True,
                )
            )
            return 0
        result = prepare(
            args.data_root.resolve(),
            args.cache_root.resolve(),
            args.logical_model,
            args.expected_meshes_bsa_sha256,
            args.cell_recipe,
            args.preferences_ini,
            args.player_facegen_profile,
        )
    except Exception as error:
        print(f"OPENNV_LEGAL_ASSET_ERROR {error}", file=sys.stderr)
        return 2
    actual_archive_hash = str(result["install"]["meshesArchive"]["sha256"])
    print("OPENNV_LEGAL_ASSET_CACHE " + json.dumps({
        "archive": actual_archive_hash,
        "asset": result["asset"]["sha256"],
        "model": result["outputs"]["modelSha256"],
        "cellScene": result["outputs"]["cellScene"],
        "openingManifest": result["outputs"]["openingManifest"],
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
