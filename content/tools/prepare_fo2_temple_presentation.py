#!/usr/bin/env python3
"""Build a disposable local PNG cache for the owned Fallout 2 Temple graph."""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any

from corpus_io import atomic_json
from fo1_frm import decode_frm_frame, palette_rgba_bytes
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError
from fo2_first_slice import (
    PROFILE_SCHEMA,
    RECIPE_SCHEMA,
    SCHEMA as SOURCE_SCHEMA,
    _archive_paths,
    _load_recipe,
    default_recipe_path,
)
from plugin_stack import file_sha256


CACHE_SCHEMA = "opennv-fo2-temple-presentation-cache/v1"
CACHE_MANIFEST_NAME = "fo2-temple-presentation-cache.json"
PALETTE_LOGICAL_PATH = "color.pal"
TILE_LIST_LOGICAL_PATH = "art\\tiles\\tiles.lst"
TILE_ID_MASK = 0x0FFF
ROOF_ID_SHIFT = 16
ARTIFACT_ID_HEX_LENGTH = 24
TILE_ENTRY_COUNT = 10000


def _load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise Fo1ProfileError(f"JSON document is not an object: {path}")
    return value


def _artifact_id(kind: str, logical_path: str, source_sha256: str, rotation: int, frame: int) -> str:
    identity = f"{kind}\0{logical_path}\0{source_sha256}\0{rotation}\0{frame}".encode("ascii")
    return hashlib.sha256(identity).hexdigest()[:ARTIFACT_ID_HEX_LENGTH]


def _save_admitted_frame(
    *,
    kind: str,
    logical_path: str,
    source: Any,
    colors: list[tuple[int, int, int, int]],
    rotation: int,
    frame_index: int,
    staging: Path,
) -> dict[str, Any]:
    decoded = decode_frm_frame(source.data, colors, rotation, frame_index)
    frame = decoded["frame"]
    artifact_id = _artifact_id(kind, logical_path, source.sha256, rotation, frame_index)
    relative = Path("assets") / kind / f"{artifact_id}.png"
    staging_path = staging / relative
    staging_path.parent.mkdir(parents=True, exist_ok=True)
    if staging_path.exists():
        raise Fo1ProfileError(f"Fallout 2 Temple artifact identity collision: {artifact_id}")
    frame["image"].save(staging_path, format="PNG", optimize=False)
    return {
        "id": artifact_id,
        "kind": kind,
        "logicalPath": logical_path,
        "source": source.source,
        "sourceBytes": len(source.data),
        "sourceSha256": source.sha256,
        "rotation": rotation,
        "frame": frame_index,
        "directionOffset": decoded["directionOffset"],
        "frameOffset": [frame["x"], frame["y"]],
        "width": frame["width"],
        "height": frame["height"],
        "png": relative.as_posix(),
        "pngBytes": staging_path.stat().st_size,
        "pngSha256": file_sha256(staging_path),
    }


def prepare_fo2_temple_presentation(
    profile_path: Path,
    source_manifest_path: Path,
    output_root: Path,
    recipe_path: Path | None = None,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    source_manifest_path = source_manifest_path.resolve()
    output_root = output_root.resolve()
    if output_root.exists():
        raise Fo1ProfileError(f"refusing to overwrite Fallout 2 Temple cache: {output_root}")
    profile = _load_json(profile_path)
    recipe_path = (recipe_path or default_recipe_path()).resolve()
    recipe = _load_recipe(recipe_path)
    source_manifest = _load_json(source_manifest_path)
    if (
        profile.get("schema") != PROFILE_SCHEMA
        or recipe.get("schema") != RECIPE_SCHEMA
        or source_manifest.get("schema") != SOURCE_SCHEMA
        or source_manifest.get("status") != "transported-source-manifest"
        or source_manifest.get("campaign") != "Fallout2"
        or source_manifest.get("slice") != "TempleOfTrials"
        or source_manifest.get("retailOrDerivedAssetsPackaged") is not False
        or source_manifest.get("generatedCaches") != []
        or source_manifest.get("promotion", {}).get("transported") is not True
        or source_manifest.get("runtimeCompatibility", {}).get("ready") is not False
    ):
        raise Fo1ProfileError("Fallout 2 Temple source manifest is not the admitted source-only graph")
    source_profile = source_manifest.get("sourceProfile", {})
    if (
        source_profile.get("sourceProfileId") != profile.get("sourceProfileId")
        or source_profile.get("saveCompatibilityId") != profile.get("saveCompatibilityId")
        or source_profile.get("sha256") != file_sha256(profile_path)
    ):
        raise Fo1ProfileError("Fallout 2 Temple source/profile binding drift")
    if source_manifest.get("overlayOrderHighToLow") != recipe["overlayOrderHighToLow"]:
        raise Fo1ProfileError("Fallout 2 Temple source overlay order drift")

    install_root = Path(str(profile.get("install", {}).get("root", ""))).resolve()
    if output_root.is_relative_to(install_root):
        raise Fo1ProfileError("Fallout 2 Temple cache must be outside the owned install")
    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{output_root.name}-", dir=output_root.parent))
    try:
        with resolver.access_scope() as accessed:
            palette_resource = resolver.read(PALETTE_LOGICAL_PATH)
            colors = palette_rgba_bytes(palette_resource.data)
            tile_names = resolver.list_lines(TILE_LIST_LOGICAL_PATH)

            tile_usage: dict[int, dict[str, Any]] = {}
            layout = source_manifest.get("map", {}).get("layout", {})
            elevations = layout.get("elevations")
            if not isinstance(elevations, list) or not elevations:
                raise Fo1ProfileError("Fallout 2 Temple source manifest has no elevation tiles")
            for elevation in elevations:
                entries = elevation.get("rawEntries")
                if not isinstance(entries, list) or len(entries) != TILE_ENTRY_COUNT:
                    raise Fo1ProfileError("Fallout 2 Temple elevation must contain 10,000 tile entries")
                floor_counts = Counter(int(entry) & TILE_ID_MASK for entry in entries)
                roof_counts = Counter((int(entry) >> ROOF_ID_SHIFT) & TILE_ID_MASK for entry in entries)
                for role, counts in (("floor", floor_counts), ("roof", roof_counts)):
                    for tile_id, count in counts.items():
                        row = tile_usage.setdefault(tile_id, {"id": tile_id, "uses": []})
                        row["uses"].append(
                            {
                                "elevation": elevation["elevation"],
                                "role": role,
                                "count": count,
                            }
                        )

            for tile_id in sorted(tile_usage):
                if not 0 <= tile_id < len(tile_names):
                    raise Fo1ProfileError(f"Fallout 2 Temple tile ID exceeds tiles.lst: {tile_id}")
                filename = tile_names[tile_id].split(" ", 1)[0].strip()
                if not filename:
                    raise Fo1ProfileError(f"Fallout 2 Temple tile ID has no FRM filename: {tile_id}")
                logical_path = f"art\\tiles\\{filename}".casefold()
                resource = resolver.read(logical_path)
                artifact = _save_admitted_frame(
                    kind="tiles",
                    logical_path=logical_path,
                    source=resource,
                    colors=colors,
                    rotation=0,
                    frame_index=0,
                    staging=staging,
                )
                tile_usage[tile_id]["filename"] = filename
                tile_usage[tile_id]["artifact"] = artifact

            object_artifacts: dict[str, dict[str, Any]] = {}
            object_bindings = []
            for frm in source_manifest.get("frms", []):
                logical_path = str(frm.get("logicalPath", ""))
                resource = resolver.read(logical_path)
                if (
                    frm.get("source") != resource.source
                    or frm.get("bytes") != len(resource.data)
                    or frm.get("sha256") != resource.sha256
                ):
                    raise Fo1ProfileError(f"Fallout 2 Temple object FRM identity drift: {logical_path}")
                admitted: dict[tuple[int, int], list[dict[str, Any]]] = {}
                for placement in frm.get("placements", []):
                    key = (int(placement["rotation"]), int(placement["frame"]))
                    admitted.setdefault(key, []).append(placement)
                if not admitted:
                    raise Fo1ProfileError(f"Fallout 2 Temple object FRM has no admitted frame: {logical_path}")
                for (rotation, frame_index), placements in sorted(admitted.items()):
                    artifact = _save_admitted_frame(
                        kind="objects",
                        logical_path=logical_path,
                        source=resource,
                        colors=colors,
                        rotation=rotation,
                        frame_index=frame_index,
                        staging=staging,
                    )
                    existing = object_artifacts.setdefault(artifact["id"], artifact)
                    if existing != artifact:
                        raise Fo1ProfileError("Fallout 2 Temple object artifact identity collision")
                    object_bindings.append(
                        {
                            "artifactId": artifact["id"],
                            "logicalPath": logical_path,
                            "rotation": rotation,
                            "frame": frame_index,
                            "placements": placements,
                        }
                    )

        artifacts = [tile_usage[tile_id]["artifact"] for tile_id in sorted(tile_usage)] + [
            object_artifacts[key] for key in sorted(object_artifacts)
        ]
        document = {
            "schema": CACHE_SCHEMA,
            "status": "decoded-disposable-local-cache",
            "campaign": "Fallout2",
            "slice": "TempleOfTrials",
            "sourceProfile": {
                "file": str(profile_path),
                "sourceProfileId": profile["sourceProfileId"],
                "sha256": file_sha256(profile_path),
            },
            "sourceManifest": {
                "file": str(source_manifest_path),
                "schema": source_manifest["schema"],
                "mapSha256": source_manifest["map"]["sha256"],
                "sha256": file_sha256(source_manifest_path),
            },
            "overlayOrderHighToLow": recipe["overlayOrderHighToLow"],
            "palette": {
                "logicalPath": palette_resource.logical_path,
                "source": palette_resource.source,
                "bytes": len(palette_resource.data),
                "sha256": palette_resource.sha256,
                "decodedColors": len(colors),
            },
            "admission": {
                "tiles": "direction 0, frame 0 for each exact floor/roof tile ID in Map 126",
                "objects": "only each rotation/frame pair referenced by the transported MAP object graph",
            },
            "tileBindings": [
                {key: value for key, value in tile_usage[tile_id].items() if key != "artifact"}
                | {"artifactId": tile_usage[tile_id]["artifact"]["id"]}
                for tile_id in sorted(tile_usage)
            ],
            "objectBindings": object_bindings,
            "artifacts": artifacts,
            "resources": [
                {
                    "logicalPath": resolver.resources[path].logical_path,
                    "source": resolver.resources[path].source,
                    "bytes": len(resolver.resources[path].data),
                    "sha256": resolver.resources[path].sha256,
                }
                for path in sorted(accessed)
            ],
            "counts": {
                "tileIds": len(tile_usage),
                "tileArtifacts": len(tile_usage),
                "objectFrmIdentities": len(source_manifest["frms"]),
                "objectArtifacts": len(object_artifacts),
                "pngArtifacts": len(artifacts),
            },
            "promotion": {
                "transported": True,
                "decodedPresentationAssets": True,
                "rendered": False,
                "interactive": False,
                "parityReviewed": False,
                "headsetAccepted": False,
            },
            "runtimeCompatibility": {
                "ready": False,
                "firstSliceBlocker": (
                    "The exact Temple palette/FRM pixels are decoded into a disposable local cache, "
                    "but no Godot consumer, character flow, gameplay, or save state exists."
                ),
            },
            "cachePolicy": {
                "disposition": "disposable-local-only",
                "containsDerivedOwnedPixels": True,
                "distributionAllowed": False,
            },
            "retailOrDerivedAssetsPackaged": False,
        }
        atomic_json(staging / CACHE_MANIFEST_NAME, document)
        os.replace(staging, output_root)
        return document
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Build a disposable local Fallout 2 Temple PNG presentation cache."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--source-manifest", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=None)
    args = parser.parse_args()
    try:
        document = prepare_fo2_temple_presentation(
            args.profile,
            args.source_manifest,
            args.output_root,
            args.recipe,
        )
    except Exception as error:
        print(f"OPENNV_FO2_TEMPLE_PRESENTATION_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_TEMPLE_PRESENTATION "
        + json.dumps(
            {
                "cache": str(args.output_root.resolve()),
                **document["counts"],
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
