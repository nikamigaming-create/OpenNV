#!/usr/bin/env python3
"""Validate an owned FNV Data folder and build the first direct OpenNV cache."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

from bsa_archive import extract_member
from cell_scene import load_recipe, prepare_cell_scene
from export_static_nif_gltf import export_static_nif
from prepare_actor import prepare_actor_set


SCHEMA = "opennv-legal-asset-cache/v1"


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


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
    logical_model: str,
    expected_meshes_sha256: str = "",
    cell_recipe: str = "",
) -> dict[str, object]:
    master = find_required_file(data_root, "FalloutNV.esm")
    meshes = find_required_file(data_root, "Fallout - Meshes.bsa")
    master_hash = file_sha256(master)
    meshes_hash = file_sha256(meshes)
    if expected_meshes_sha256 and meshes_hash != expected_meshes_sha256.lower():
        raise ValueError(
            f"Meshes BSA hash mismatch: expected={expected_meshes_sha256.lower()} actual={meshes_hash}"
        )
    member = extract_member(meshes, logical_model)

    source_path = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
    source_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_source = source_path.with_name(source_path.name + ".tmp")
    temporary_source.write_bytes(member.data)
    os.replace(temporary_source, source_path)

    output_root = cache_root / "generated" / "static"
    gltf_path = output_root / "retail-static.gltf"
    sidecar_path = output_root / "retail-static.opennv.json"
    sidecar = export_static_nif(source_path, member.logical_path, gltf_path, sidecar_path, strict=True)
    cell_scene = None
    actor_scenes = None
    texture_archives: list[Path] = []
    texture_archive_rows: list[dict[str, object]] = []
    if cell_recipe:
        texture_archives = [
            find_required_file(data_root, "Fallout - Textures.bsa"),
            find_required_file(data_root, "Fallout - Textures2.bsa"),
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
        )
        actor_recipe_ids = [str(value) for value in cell_recipe_document["actorRecipes"]]
        if actor_recipe_ids:
            actor_scenes = prepare_actor_set(data_root, cache_root, actor_recipe_ids)
    manifest = {
        "schema": SCHEMA,
        "status": "prepared-legal-assets",
        "install": {
            "dataRoot": str(data_root.resolve()),
            "master": {"file": master.name, "bytes": master.stat().st_size, "sha256": master_hash},
            "meshesArchive": {"file": meshes.name, "bytes": meshes.stat().st_size, "sha256": meshes_hash},
            "textureArchives": texture_archive_rows,
        },
        "asset": {
            "logicalPath": member.logical_path,
            "bytes": len(member.data),
            "sha256": member.sha256,
            "compressedInArchive": member.compressed,
            "archiveOffset": member.archive_offset,
            "storedBytes": member.stored_bytes,
        },
        "outputs": {
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "modelSha256": sidecar["outputs"]["gltf"]["sha256"],
            "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
            "cellScene": None if cell_scene is None else cell_scene["output"],
            "cellSceneSha256": (
                None if cell_scene is None else file_sha256(Path(str(cell_scene["output"])))
            ),
            "actorScenes": None if actor_scenes is None else actor_scenes["manifest"],
            "actorScenesSha256": (
                None
                if actor_scenes is None
                else file_sha256(Path(str(actor_scenes["manifest"])))
            ),
        },
    }
    atomic_text(cache_root / "install-manifest.json", manifest)
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument(
        "--logical-model",
        default="meshes\\landscape\\nv_rocks\\nvn_rockcanyon12.nif",
    )
    parser.add_argument("--expected-meshes-bsa-sha256", default="")
    parser.add_argument("--cell-recipe", default="")
    args = parser.parse_args()
    try:
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
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
