#!/usr/bin/env python3
"""Compile one hash-pinned exterior CELL from a player's owned FNV data."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

from cell_scene import EXTERIOR_RECIPE_SCHEMA, load_spatial_recipe
from exterior_scene import prepare_exterior_scene
from owned_archive_stack import load_owned_archive_stack
from runtime_configuration import configured_recipe_path


MANIFEST_SCHEMA = "opennv-exterior-cell-cache/v1"
EXIT_DATA_ERROR = 2


def _find_file(root: Path, name: str) -> Path:
    matches = [
        path
        for path in root.iterdir()
        if path.is_file() and path.name.lower() == name.lower()
    ]
    if len(matches) != 1:
        raise FileNotFoundError(f"Expected one {name!r} in {root}, found {len(matches)}")
    return matches[0]


def _sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _verified_recipe_file(data_root: Path, source: object) -> tuple[Path, str]:
    if not isinstance(source, dict) or set(source) != {"file", "sha256"}:
        raise ValueError("Exterior CELL source identity must contain only file and sha256")
    path = _find_file(data_root, str(source["file"]))
    actual = _sha256(path)
    expected = str(source["sha256"]).lower()
    if actual != expected:
        raise ValueError(
            f"Owned source hash mismatch for {path.name}: expected={expected} actual={actual}"
        )
    return path, actual


def _verified_streaming(game_root: Path, streaming: object) -> dict[str, object]:
    if not isinstance(streaming, dict):
        raise ValueError("Exterior CELL recipe requires a streaming contract")
    mode = str(streaming.get("mode"))
    if mode == "bounded-proof":
        if set(streaming) != {"mode", "loadedGridDiameter"}:
            raise ValueError("Bounded exterior streaming has unexpected fields")
        return {
            "mode": mode,
            "loadedGridDiameter": int(streaming["loadedGridDiameter"]),
        }
    if mode != "retail-ini" or set(streaming) != {
        "mode",
        "loadedGridDiameter",
        "source",
        "section",
        "key",
    }:
        raise ValueError("Retail exterior streaming contract is incomplete")
    path, sha256 = _verified_recipe_file(game_root, streaming["source"])
    section = str(streaming["section"])
    key = str(streaming["key"])
    current_section = ""
    observed_values: list[str] = []
    for raw_line in path.read_text(encoding="utf-8-sig").splitlines():
        line = raw_line.strip()
        if line.startswith("[") and line.endswith("]"):
            current_section = line[1:-1].strip()
            continue
        if current_section != section or "=" not in line:
            continue
        candidate_key, value = line.split("=", 1)
        if candidate_key.strip() == key:
            observed_values.append(value.strip())
    if len(observed_values) != 1:
        raise ValueError(f"Retail streaming setting is missing: [{section}] {key}")
    observed = int(observed_values[0])
    expected = int(streaming["loadedGridDiameter"])
    if observed != expected:
        raise ValueError(
            f"Retail streaming setting mismatch: expected={expected} observed={observed}"
        )
    return {
        "mode": mode,
        "loadedGridDiameter": expected,
        "file": path.name,
        "path": str(path),
        "sha256": sha256,
        "section": section,
        "key": key,
        "observedValue": observed,
    }


def _write_json(path: Path, document: object) -> None:
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    os.replace(temporary, path)


def prepare(
    data_root: Path,
    cache_root: Path,
    recipe_id: str,
    retail_grass_observation: Path | None = None,
    retail_grass_render_state_observation: Path | None = None,
) -> dict[str, object]:
    if cache_root.exists():
        raise FileExistsError(f"Refusing to overwrite exterior CELL cache: {cache_root}")
    recipe = load_spatial_recipe(recipe_id)
    if recipe.get("schema") != EXTERIOR_RECIPE_SCHEMA:
        raise ValueError(f"Recipe is not an exterior CELL target: {recipe_id}")
    streaming = _verified_streaming(data_root.parent, recipe.get("streaming"))
    master, master_sha256 = _verified_recipe_file(data_root, recipe.get("master"))
    meshes, meshes_sha256 = _verified_recipe_file(
        data_root,
        recipe.get("meshesArchive"),
    )
    texture_sources = recipe.get("textureArchives")
    if not isinstance(texture_sources, list) or not texture_sources:
        raise ValueError("Exterior CELL recipe requires a nonempty texture archive stack")
    textures_and_hashes = [
        _verified_recipe_file(data_root, source)
        for source in texture_sources
    ]
    textures = [path for path, _sha256_value in textures_and_hashes]
    texture_rows = [
        {
            "file": path.name,
            "bytes": path.stat().st_size,
            "sha256": sha256,
        }
        for path, sha256 in textures_and_hashes
    ]
    visual_archives = load_owned_archive_stack(
        data_root,
        configured_recipe_path("visualArchives"),
    )
    scene = prepare_exterior_scene(
        master,
        meshes,
        textures,
        texture_rows,
        cache_root,
        recipe,
        master_sha256,
        retail_grass_observation,
        retail_grass_render_state_observation,
        visual_archives,
    )
    scene_path = Path(str(scene["output"]))
    manifest = {
        "schema": MANIFEST_SCHEMA,
        "status": "compiled-owned-exterior-cell",
        "recipe": recipe_id,
        "source": {
            "dataRoot": str(data_root),
            "master": {
                "path": str(master),
                "sha256": master_sha256,
            },
            "meshesArchive": {
                "path": str(meshes),
                "sha256": meshes_sha256,
            },
            "textureArchives": texture_rows,
            "archiveStack": visual_archives.manifest(),
            "streaming": streaming,
            "retailGrassObservation": (
                {
                    "path": str(retail_grass_observation),
                    "sha256": _sha256(retail_grass_observation),
                }
                if retail_grass_observation is not None
                else None
            ),
            "retailGrassRenderStateObservation": (
                {
                    "path": str(retail_grass_render_state_observation),
                    "sha256": _sha256(retail_grass_render_state_observation),
                }
                if retail_grass_render_state_observation is not None
                else None
            ),
        },
        "cell": scene["cell"],
        "coverage": scene["coverage"],
        "output": {
            "scene": str(scene_path),
            "sceneSha256": _sha256(scene_path),
        },
    }
    _write_json(cache_root / "manifest.json", manifest)
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--recipe", required=True)
    parser.add_argument(
        "--retail-grass-observation",
        type=Path,
        help=(
            "Optional private canonical actor-observation JSONL containing the "
            "matched GRASS23x002 placement stream"
        ),
    )
    parser.add_argument(
        "--retail-grass-render-state-observation",
        type=Path,
        help=(
            "Optional private canonical actor-observation JSONL containing the "
            "matched GRASS23x002 D3D9 render state"
        ),
    )
    args = parser.parse_args()
    try:
        manifest = prepare(
            args.data_root.resolve(),
            args.cache_root.resolve(),
            args.recipe,
            (
                args.retail_grass_observation.resolve()
                if args.retail_grass_observation is not None
                else None
            ),
            (
                args.retail_grass_render_state_observation.resolve()
                if args.retail_grass_render_state_observation is not None
                else None
            ),
        )
    except Exception as error:
        print(f"OPENNV_EXTERIOR_CELL_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_EXTERIOR_CELL "
        + json.dumps(
            {
                "manifest": str((args.cache_root.resolve() / "manifest.json")),
                "scene": manifest["output"]["scene"],
                "status": manifest["status"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
