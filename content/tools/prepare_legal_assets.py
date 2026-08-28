#!/usr/bin/env python3
"""Validate an owned FNV Data folder and build the first direct OpenNV cache."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

from cell_scene import load_recipe, load_spatial_recipe, prepare_cell_scene
from exterior_scene import prepare_exterior_scene
from export_static_nif_gltf import compiler_provenance, export_static_nif
from opening_catalog import prepare_opening_manifest
from owned_archive_stack import (
    AUDIO_ARCHIVE_RECIPE_SCHEMA,
    load_owned_archive_stack,
)
from prepare_actor import prepare_actor_set
from prepare_fo3_profile import (
    default_recipe_path as default_fo3_profile_recipe_path,
    prepare_profile as prepare_fo3_profile,
)
from runtime_configuration import configured_recipe_path, load_runtime_configuration


SCHEMA = "opennv-legal-asset-cache/v1"


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


def prepare(
    data_root: Path,
    cache_root: Path,
    logical_model: str | None = None,
    expected_meshes_sha256: str = "",
    cell_recipe: str | None = None,
) -> dict[str, object]:
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
    opening_recipe_path = configured_recipe_path("opening")
    if opening_recipe_path.stem != str(legal_assets["defaultOpeningRecipe"]):
        raise ValueError("Configured opening recipe registry and legal-assets default differ")
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
    )
    member = visual_archives.extract(logical_model)

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
    texture_archives: list[Path] = []
    texture_archive_rows: list[dict[str, object]] = []
    if cell_recipe:
        texture_archives = [
            find_required_file(data_root, str(file_name))
            for file_name in owned_data["textureArchiveFiles"]
        ]
        texture_archive_rows = [
            {
                "file": archive.name,
                "bytes": archive.stat().st_size,
                "sha256": file_sha256(archive),
            }
            for archive in texture_archives
        ]
        cell_recipe_document = load_recipe(cell_recipe)
        cell_scene = prepare_cell_scene(
            master,
            meshes,
            texture_archives,
            texture_archive_rows,
            cache_root,
            cell_recipe_document,
            master_hash,
            visual_archives,
        )
        linked_recipe_documents: list[dict[str, object]] = []
        configured_links = cell_recipe_document.get("linkedCellRecipes")
        if configured_links is None and cell_recipe_document.get("linkedExteriorRecipe"):
            configured_links = [
                {
                    "recipe": cell_recipe_document["linkedExteriorRecipe"],
                    "fromDoorReferenceFormId": cell_recipe_document["entryDoorReferenceFormId"],
                }
            ]
        if configured_links is not None:
            if not isinstance(configured_links, list) or not configured_links:
                raise ValueError("Linked CELL recipes must be a non-empty ordered list")
            available_scenes = [
                json.loads(Path(str(cell_scene["output"])).read_text(encoding="utf-8"))
            ]
            seen_recipes = {str(cell_recipe_document["id"])}
            for configured_link in configured_links:
                if not isinstance(configured_link, dict):
                    raise ValueError("Linked CELL recipe row must be an object")
                recipe_id = str(configured_link.get("recipe", ""))
                from_door = str(configured_link.get("fromDoorReferenceFormId", "")).lower()
                if not recipe_id or not from_door or recipe_id in seen_recipes:
                    raise ValueError("Linked CELL recipe identity is missing or duplicated")
                seen_recipes.add(recipe_id)
                linked_recipe_document = load_spatial_recipe(recipe_id)
                if linked_recipe_document["schema"] == "opennv-exterior-recipe/v1":
                    linked_scene = prepare_exterior_scene(
                        master,
                        meshes,
                        texture_archives,
                        texture_archive_rows,
                        cache_root,
                        linked_recipe_document,
                        master_hash,
                        owned_archives=visual_archives,
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
                linked_recipe_documents.append(linked_recipe_document)
                available_scenes.append(linked_document)
        if linked_cell_scenes:
            cell_scene_path = Path(str(cell_scene["output"]))
            primary_document = json.loads(cell_scene_path.read_text(encoding="utf-8"))
            primary_document["linkedCells"] = linked_cell_scenes
            atomic_text(cell_scene_path, primary_document)
        actor_recipe_ids = [str(value) for value in cell_recipe_document["actorRecipes"]]
        for linked_recipe_document in linked_recipe_documents:
            actor_recipe_ids.extend(str(value) for value in linked_recipe_document["actorRecipes"])
        if actor_recipe_ids:
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
            )
    manifest = {
        "schema": SCHEMA,
        "status": "prepared-legal-assets",
        "install": {
            "dataRoot": str(data_root.resolve()),
            "master": {"file": master.name, "bytes": master.stat().st_size, "sha256": master_hash},
            "defaultIni": {
                "file": default_ini.name,
                "bytes": default_ini.stat().st_size,
                "sha256": file_sha256(default_ini),
            },
            "meshesArchive": {"file": meshes.name, "bytes": meshes.stat().st_size, "sha256": meshes_hash},
            "uiArchive": {
                "file": ui_archive.name,
                "bytes": ui_archive.stat().st_size,
                "sha256": file_sha256(ui_archive),
            },
            "textureArchives": texture_archive_rows,
            "archiveStack": visual_archives.manifest(),
        },
        "asset": {
            "logicalPath": member.logical_path,
            "bytes": len(member.data),
            "sha256": member.sha256,
            "compressedInArchive": member.compressed,
            "archiveOffset": member.archive_offset,
            "storedBytes": member.stored_bytes,
            "sourceArchive": member.source_archive,
            "sourceArchiveSha256": member.source_archive_sha256,
        },
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
    args = parser.parse_args()
    if args.compiler_identity:
        print(
            "OPENNV_CONTENT_COMPILER_IDENTITY "
            + json.dumps(compiler_provenance(), sort_keys=True)
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
