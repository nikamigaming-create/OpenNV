#!/usr/bin/env python3
"""Build a disposable local PNG cache for the owned Fallout 2 Temple graph."""

from __future__ import annotations

import argparse
from collections import Counter
from dataclasses import asdict
import hashlib
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any, Callable

from corpus_io import atomic_json
from fo1_frm import decode_frm_frame, palette_rgba_bytes
from prepare_fo1_hex_scene import unproject_floor
from fo1_map_objects import Fo1ResourceResolver, parse_map_objects, parse_script_section
from fo1_profile import Fo1ProfileError, map_layout_manifest, parse_map_layout
from fo2_first_slice import (
    PROFILE_SCHEMA,
    RECIPE_SCHEMA,
    SCHEMA as SOURCE_SCHEMA,
    _archive_paths,
    _load_recipe,
    default_recipe_path,
)
from fo2_temple_transitions import SCHEMA as TRANSITION_SCHEMA, compile_fo2_temple_transitions
from plugin_stack import file_sha256


CACHE_SCHEMA = "opennv-fo2-temple-presentation-cache/v1"
CACHE_MANIFEST_NAME = "fo2-temple-presentation-cache.json"
TRANSITION_MANIFEST_NAME = "fo2-temple-transitions.json"
PALETTE_LOGICAL_PATH = "color.pal"
TILE_LIST_LOGICAL_PATH = "art\\tiles\\tiles.lst"
TILE_ID_MASK = 0x0FFF
ROOF_ID_SHIFT = 16
ARTIFACT_ID_HEX_LENGTH = 24
TILE_ENTRY_COUNT = 10000
FLOOR_PROJECTION_MODE = "classic-fallout-isometric-floor-unproject-v1"
FLOOR_UNPROJECTED_TEXTURE_SIZE = 128
FLOOR_ALPHA_FILL = "nearest-owned-opaque-pixel-v1"


def _flatten_map_objects(objects: dict[str, Any]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []

    def add(value: dict[str, Any]) -> None:
        rows.append(value)
        for inventory in value["inventory"]:
            add(inventory["object"])

    for elevation in objects["elevations"]:
        for value in elevation["objects"]:
            add(value)
    return rows


def _derive_map_presentation_graph(
    map_data: bytes,
    resolver: Fo1ResourceResolver,
) -> tuple[dict[str, Any], dict[str, list[dict[str, Any]]]]:
    layout = parse_map_layout(map_data)
    scripts, objects_offset = parse_script_section(map_data, layout.next_offset)
    objects, end_offset = parse_map_objects(
        map_data,
        objects_offset,
        layout.header.version,
        resolver,
    )
    if end_offset != len(map_data):
        raise Fo1ProfileError(
            f"Fallout 2 MAP object graph leaves {len(map_data) - end_offset} trailing bytes"
        )
    flat_objects = _flatten_map_objects(objects)
    placements: dict[str, list[dict[str, Any]]] = {}
    for value in flat_objects:
        logical_path = resolver.placed_idle_frm_path(int(value["fid"], 16))
        placements.setdefault(logical_path.casefold(), []).append(
            {
                "serial": value["serial"],
                "fid": value["fid"],
                "frame": value["frame"],
                "rotation": value["rotation"],
                "elevation": value["elevation"],
                "tile": value["tile"],
            }
        )
    return (
        {
            "header": asdict(layout.header),
            "layout": map_layout_manifest(layout),
            "scriptLists": scripts,
            "objectsOffset": objects_offset,
            "endOffset": end_offset,
            "objects": objects,
            "allObjectCount": len(flat_objects),
        },
        placements,
    )


def _load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise Fo1ProfileError(f"JSON document is not an object: {path}")
    return value


def _artifact_id(
    kind: str,
    logical_path: str,
    source_sha256: str,
    rotation: int,
    frame: int,
    presentation: str | None = None,
) -> str:
    identity = f"{kind}\0{logical_path}\0{source_sha256}\0{rotation}\0{frame}"
    if presentation is not None:
        identity += f"\0{presentation}"
    encoded = identity.encode("ascii")
    return hashlib.sha256(encoded).hexdigest()[:ARTIFACT_ID_HEX_LENGTH]


def _save_admitted_frame(
    *,
    kind: str,
    logical_path: str,
    source: Any,
    colors: list[tuple[int, int, int, int]],
    rotation: int,
    frame_index: int,
    staging: Path,
    unproject_floor_frame: bool = False,
) -> dict[str, Any]:
    decoded = decode_frm_frame(source.data, colors, rotation, frame_index)
    frame = decoded["frame"]
    source_image = frame["image"]
    presentation = FLOOR_PROJECTION_MODE if unproject_floor_frame else None
    artifact_id = _artifact_id(
        kind,
        logical_path,
        source.sha256,
        rotation,
        frame_index,
        presentation,
    )
    relative = Path("assets") / kind / f"{artifact_id}.png"
    staging_path = staging / relative
    staging_path.parent.mkdir(parents=True, exist_ok=True)
    if staging_path.exists():
        raise Fo1ProfileError(f"Fallout 2 artifact identity collision: {artifact_id}")
    image = (
        unproject_floor(source_image, FLOOR_UNPROJECTED_TEXTURE_SIZE)
        if unproject_floor_frame
        else source_image
    )
    image.save(staging_path, format="PNG", optimize=False)
    artifact = {
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
        "width": image.width,
        "height": image.height,
        "png": relative.as_posix(),
        "pngBytes": staging_path.stat().st_size,
        "pngSha256": file_sha256(staging_path),
    }
    if unproject_floor_frame:
        artifact["floorProjection"] = {
            "mode": FLOOR_PROJECTION_MODE,
            "sourceWidth": frame["width"],
            "sourceHeight": frame["height"],
            "outputSizePixels": FLOOR_UNPROJECTED_TEXTURE_SIZE,
            "alphaFill": FLOOR_ALPHA_FILL,
        }
    return artifact


def prepare_fo2_map_presentation(
    profile_path: Path,
    source_manifest_path: Path,
    output_root: Path,
    recipe_path: Path | None = None,
    *,
    source_schema: str = SOURCE_SCHEMA,
    source_status: str = "transported-source-manifest",
    source_slice: str = "TempleOfTrials",
    cache_schema: str = CACHE_SCHEMA,
    cache_manifest_name: str = CACHE_MANIFEST_NAME,
    map_index: int = 126,
    map_name: str = "ARTEMPLE.MAP",
    map_logical_path: str = "maps\\artemple.map",
    map_label: str = "Temple",
    source_map_index: int | None = None,
    recipe_schema: str = RECIPE_SCHEMA,
    cache_enricher: Callable[[Path, dict[str, Any], dict[str, Any]], None] | None = None,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    source_manifest_path = source_manifest_path.resolve()
    output_root = output_root.resolve()
    if output_root.exists():
        raise Fo1ProfileError(f"refusing to overwrite Fallout 2 {map_label} cache: {output_root}")
    profile = _load_json(profile_path)
    recipe_path = (recipe_path or default_recipe_path()).resolve()
    recipe = (
        _load_recipe(recipe_path)
        if recipe_schema == RECIPE_SCHEMA
        else _load_json(recipe_path)
    )
    source_manifest = _load_json(source_manifest_path)
    if (
        profile.get("schema") != PROFILE_SCHEMA
        or recipe.get("schema") != recipe_schema
        or recipe.get("campaign") != "Fallout2"
        or source_manifest.get("schema") != source_schema
        or source_manifest.get("status") != source_status
        or source_manifest.get("campaign") != "Fallout2"
        or source_manifest.get("slice") != source_slice
        or source_manifest.get("retailOrDerivedAssetsPackaged") is not False
        or source_manifest.get("generatedCaches") != []
        or source_manifest.get("promotion", {}).get("transported") is not True
        or source_manifest.get("runtimeCompatibility", {}).get("ready") is not False
    ):
        raise Fo1ProfileError(
            f"Fallout 2 {map_label} source manifest is not the admitted source-only graph"
        )
    source_profile = source_manifest.get("sourceProfile", {})
    if (
        source_profile.get("sourceProfileId") != profile.get("sourceProfileId")
        or source_profile.get("saveCompatibilityId") != profile.get("saveCompatibilityId")
        or source_profile.get("sha256") != file_sha256(profile_path)
    ):
        raise Fo1ProfileError(f"Fallout 2 {map_label} source/profile binding drift")
    if source_manifest.get("overlayOrderHighToLow") != recipe["overlayOrderHighToLow"]:
        raise Fo1ProfileError(f"Fallout 2 {map_label} source overlay order drift")
    if source_map_index is None:
        source_map = source_manifest.get("map", {})
    else:
        matching_maps = [
            row
            for row in source_manifest.get("maps", [])
            if int(row.get("mapIndex", -1)) == source_map_index
        ]
        if len(matching_maps) != 1:
            raise Fo1ProfileError(
                f"Fallout 2 {map_label} adjacent source MAP is absent or duplicated"
            )
        source_map = matching_maps[0]
    source_frms = source_map.get("frms", source_manifest.get("frms", []))
    source_header = source_map.get("header", {})
    if (
        str(source_map.get("logicalPath", "")).casefold() != map_logical_path.casefold()
        or int(source_header.get("mapIndex", -1)) != map_index
        or str(source_header.get("name", "")).casefold() != map_name.casefold()
    ):
        raise Fo1ProfileError(
            f"Fallout 2 {map_label} source MAP identity does not match Map {map_index}"
        )

    install_root = Path(str(profile.get("install", {}).get("root", ""))).resolve()
    if output_root.is_relative_to(install_root):
        raise Fo1ProfileError(f"Fallout 2 {map_label} cache must be outside the owned install")
    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{output_root.name}-", dir=output_root.parent))
    try:
        with resolver.access_scope() as accessed:
            map_resource = resolver.read(map_logical_path)
            if (
                source_map.get("source") != map_resource.source
                or int(source_map.get("bytes", -1)) != len(map_resource.data)
                or source_map.get("sha256") != map_resource.sha256
            ):
                raise Fo1ProfileError(
                    f"Fallout 2 {map_label} source MAP identity drift: {map_logical_path}"
                )
            derived_map_graph, derived_placements = _derive_map_presentation_graph(
                map_resource.data,
                resolver,
            )
            source_map_graph = {
                key: source_map.get(key)
                for key in derived_map_graph
            }
            if source_map_graph != derived_map_graph:
                raise Fo1ProfileError(
                    f"Fallout 2 {map_label} source MAP graph differs from owned bytes"
                )
            manifest_placements: dict[str, list[dict[str, Any]]] = {}
            for frm in source_frms:
                logical_path = str(frm.get("logicalPath", "")).casefold()
                if not logical_path or logical_path in manifest_placements:
                    raise Fo1ProfileError(
                        f"Fallout 2 {map_label} source FRM graph is invalid"
                    )
                manifest_placements[logical_path] = frm.get("placements")
            if manifest_placements != derived_placements:
                raise Fo1ProfileError(
                    f"Fallout 2 {map_label} source FRM placements differ from owned MAP bytes"
                )
            palette_resource = resolver.read(PALETTE_LOGICAL_PATH)
            colors = palette_rgba_bytes(palette_resource.data)
            tile_names = resolver.list_lines(TILE_LIST_LOGICAL_PATH)

            tile_usage: dict[int, dict[str, Any]] = {}
            layout = source_map.get("layout", {})
            elevations = layout.get("elevations")
            if not isinstance(elevations, list) or not elevations:
                raise Fo1ProfileError(
                    f"Fallout 2 {map_label} source manifest has no elevation tiles"
                )
            for elevation in elevations:
                entries = elevation.get("rawEntries")
                if not isinstance(entries, list) or len(entries) != TILE_ENTRY_COUNT:
                    raise Fo1ProfileError(
                        f"Fallout 2 {map_label} elevation must contain 10,000 tile entries"
                    )
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
                    raise Fo1ProfileError(
                        f"Fallout 2 {map_label} tile ID exceeds tiles.lst: {tile_id}"
                    )
                filename = tile_names[tile_id].split(" ", 1)[0].strip()
                if not filename:
                    raise Fo1ProfileError(
                        f"Fallout 2 {map_label} tile ID has no FRM filename: {tile_id}"
                    )
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
                    unproject_floor_frame=True,
                )
                tile_usage[tile_id]["filename"] = filename
                tile_usage[tile_id]["artifact"] = artifact

            object_artifacts: dict[str, dict[str, Any]] = {}
            object_bindings = []
            for frm in source_frms:
                logical_path = str(frm.get("logicalPath", ""))
                resource = resolver.read(logical_path)
                if (
                    frm.get("source") != resource.source
                    or frm.get("bytes") != len(resource.data)
                    or frm.get("sha256") != resource.sha256
                ):
                    raise Fo1ProfileError(
                        f"Fallout 2 {map_label} object FRM identity drift: {logical_path}"
                    )
                admitted: dict[tuple[int, int], list[dict[str, Any]]] = {}
                for placement in frm.get("placements", []):
                    key = (int(placement["rotation"]), int(placement["frame"]))
                    admitted.setdefault(key, []).append(placement)
                if not admitted:
                    raise Fo1ProfileError(
                        f"Fallout 2 {map_label} object FRM has no admitted frame: {logical_path}"
                    )
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
                        raise Fo1ProfileError(
                            f"Fallout 2 {map_label} object artifact identity collision"
                        )
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
            "schema": cache_schema,
            "status": "decoded-disposable-local-cache",
            "campaign": "Fallout2",
            "slice": source_slice,
            "sourceProfile": {
                "file": str(profile_path),
                "sourceProfileId": profile["sourceProfileId"],
                "sha256": file_sha256(profile_path),
            },
            "sourceManifest": {
                "file": str(source_manifest_path),
                "schema": source_manifest["schema"],
                "mapSha256": source_map["mapSha256"]
                if source_map_index is not None
                else source_map["sha256"],
                "mapIndex": map_index,
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
                "tiles": (
                    "direction 0, frame 0 for each exact floor/roof tile ID "
                    f"in Map {map_index}, deterministically unprojected from its owned "
                    "isometric diamond into an opaque square texture when source pixels exist"
                ),
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
                "objectFrmIdentities": len(source_frms),
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
                    f"The exact {map_label} palette/FRM pixels are decoded into a disposable local "
                    "cache, but no Godot destination consumer, character flow, gameplay, or save "
                    "state exists."
                ),
            },
            "cachePolicy": {
                "disposition": "disposable-local-only",
                "containsDerivedOwnedPixels": True,
                "distributionAllowed": False,
            },
            "retailOrDerivedAssetsPackaged": False,
        }
        if cache_enricher is not None:
            cache_enricher(staging, document, source_manifest)
        atomic_json(staging / cache_manifest_name, document)
        os.replace(staging, output_root)
        return document
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def prepare_fo2_temple_presentation(
    profile_path: Path,
    source_manifest_path: Path,
    output_root: Path,
    recipe_path: Path | None = None,
) -> dict[str, Any]:
    return prepare_fo2_map_presentation(
        profile_path,
        source_manifest_path,
        output_root,
        recipe_path,
        cache_enricher=lambda staging, document, source: _emit_temple_transition_output(
            staging,
            document,
            source,
            profile_path=profile_path,
            source_manifest_path=source_manifest_path,
        ),
    )


def _emit_temple_transition_output(
    staging: Path,
    document: dict[str, Any],
    source_manifest: dict[str, Any],
    *,
    profile_path: Path,
    source_manifest_path: Path,
) -> None:
    """Publish the transition compiler output bound to this exact cache source."""
    profile_path = profile_path.resolve()
    source_manifest_path = source_manifest_path.resolve()
    transition = compile_fo2_temple_transitions(profile_path, source_manifest_path)
    source_descriptor = transition.get("sourceManifest", {})
    profile_descriptor = transition.get("sourceProfile", {})
    if (
        transition.get("schema") != TRANSITION_SCHEMA
        or transition.get("status") != "compiled-owned-transition-records"
        or source_descriptor.get("file") != str(source_manifest_path)
        or source_descriptor.get("sha256") != document["sourceManifest"]["sha256"]
        or profile_descriptor.get("file") != str(profile_path)
        or profile_descriptor.get("sourceProfileId") != document["sourceProfile"]["sourceProfileId"]
        or profile_descriptor.get("sha256") != document["sourceProfile"]["sha256"]
        or source_manifest.get("sourceProfile", {}).get("sourceProfileId")
        != document["sourceProfile"]["sourceProfileId"]
    ):
        raise Fo1ProfileError(
            "Fallout 2 Temple transition output does not bind the cache source/profile."
        )
    transition_path = staging / TRANSITION_MANIFEST_NAME
    atomic_json(transition_path, transition)
    document["outputs"] = {
        "templeTransitions": {
            "file": TRANSITION_MANIFEST_NAME,
            "sha256": file_sha256(transition_path),
            "sourceManifestSha256": document["sourceManifest"]["sha256"],
            "sourceProfileSha256": document["sourceProfile"]["sha256"],
            "sourceProfileId": document["sourceProfile"]["sourceProfileId"],
        }
    }


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
