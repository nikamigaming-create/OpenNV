#!/usr/bin/env python3
"""Compile direct reciprocal Fallout 2 MAP neighbors from owned archives."""

from __future__ import annotations

import argparse
from dataclasses import asdict
import json
import sys
from pathlib import Path
from typing import Any

from classic_map_joins import exit_grid_records, reciprocal_map_joins
from corpus_io import atomic_json
from fo1_frm import decode_frm
from fo1_map_objects import Fo1ResourceResolver, parse_map_objects, parse_script_section
from fo1_profile import Fo1ProfileError, map_layout_manifest, parse_map_layout
from fo2_arroyo_trial_route import _walk_mask_sha256, _walkable_by_elevation
from fo2_first_slice import (
    FORM_ID_RADIX,
    FRM_PALETTE_SIZE,
    _archive_paths,
    _flatten_objects,
    _frm_structure,
    _load_json,
)
from fo2_temple_transitions import NO_BLOCK_FLAG, _maps_sections
from plugin_stack import file_sha256


SCHEMA = "opennv-fo2-adjacent-map-catalog/v1"
SOURCE_SCHEMA = "opennv-fo2-owned-map-slice/v1"
CLASSIC_DEFAULT_TILE_ID = 1


def _compile_map(
    resolver: Fo1ResourceResolver,
    logical_path: str,
    expected_index: int,
) -> dict[str, Any]:
    resource = resolver.read(logical_path)
    layout = parse_map_layout(resource.data)
    if layout.header.mapIndex != expected_index:
        raise Fo1ProfileError(
            f"Fallout 2 adjacent MAP index drifted: {logical_path}"
        )
    scripts, objects_offset = parse_script_section(resource.data, layout.next_offset)
    objects, end_offset = parse_map_objects(
        resource.data, objects_offset, layout.header.version, resolver
    )
    if end_offset != len(resource.data):
        raise Fo1ProfileError(
            f"Fallout 2 adjacent MAP object graph has trailing bytes: {logical_path}"
        )
    flat = _flatten_objects(objects)
    frm_placements: dict[str, list[dict[str, Any]]] = {}
    for obj in flat:
        frm_path = resolver.placed_idle_frm_path(int(obj["fid"], FORM_ID_RADIX))
        frm_placements.setdefault(frm_path, []).append(
            {
                "serial": obj["serial"],
                "fid": obj["fid"],
                "frame": obj["frame"],
                "rotation": obj["rotation"],
                "elevation": obj["elevation"],
                "tile": obj["tile"],
            }
        )
    palette = [(0, 0, 0, 0)] * FRM_PALETTE_SIZE
    frms = []
    for frm_path in sorted(frm_placements):
        frm = resolver.read(frm_path)
        frms.append(
            {
                "logicalPath": frm.logical_path,
                "source": frm.source,
                "bytes": len(frm.data),
                "sha256": frm.sha256,
                "structure": _frm_structure(decode_frm(frm.data, palette)),
                "placements": frm_placements[frm_path],
            }
        )
    walkable = _walkable_by_elevation(
        {"map": {"layout": map_layout_manifest(layout), "objects": objects}}
    )
    return {
        "mapIndex": expected_index,
        "mapName": layout.header.name,
        "logicalPath": resource.logical_path,
        "source": resource.source,
        "bytes": len(resource.data),
        "mapSha256": resource.sha256,
        "sha256": resource.sha256,
        "header": asdict(layout.header),
        "layout": map_layout_manifest(layout),
        "defaultTileId": CLASSIC_DEFAULT_TILE_ID,
        "scriptLists": scripts,
        "objectsOffset": objects_offset,
        "endOffset": end_offset,
        "objects": objects,
        "frms": frms,
        "allObjectCount": len(flat),
        "blockerSerials": sorted(
            int(row["serial"])
            for row in flat
            if not (int(row["flags"], FORM_ID_RADIX) & NO_BLOCK_FLAG)
        ),
        "walkTopology": [
            {
                "elevation": elevation,
                "walkableHexes": len(tiles),
                "walkMaskSha256": _walk_mask_sha256(tiles),
                "tiles": sorted(tiles),
            }
            for elevation, tiles in sorted(walkable.items())
        ],
        "exitGrids": exit_grid_records(
            expected_index, layout.header.name, resource.sha256, objects
        ),
    }


def compile_fo2_adjacent_maps(
    profile_path: Path,
    source_slice_path: Path,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    source_slice_path = source_slice_path.resolve()
    profile = _load_json(profile_path)
    source_slice = _load_json(source_slice_path)
    if (
        source_slice.get("schema") != SOURCE_SCHEMA
        or source_slice.get("campaign") != "Fallout2"
        or source_slice.get("retailOrDerivedAssetsPackaged") is not False
    ):
        raise Fo1ProfileError("unexpected Fallout 2 adjacent source MAP slice")
    source_profile = source_slice.get("sourceProfile", {})
    if (
        Path(str(source_profile.get("file", ""))).resolve() != profile_path
        or source_profile.get("sha256") != file_sha256(profile_path)
        or source_profile.get("sourceProfileId") != profile.get("sourceProfileId")
    ):
        raise Fo1ProfileError("Fallout 2 adjacent MAP profile binding drifted")
    recipe_path = Path(str(source_slice.get("recipe", {}).get("file", ""))).resolve()
    recipe = _load_json(recipe_path)
    if (
        source_slice["recipe"].get("sha256") != file_sha256(recipe_path)
        or recipe.get("overlayOrderHighToLow")
        != ["patch000.dat", "critter.dat", "master.dat"]
    ):
        raise Fo1ProfileError("Fallout 2 adjacent MAP overlay binding drifted")

    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    with resolver.access_scope() as accessed:
        registry = _maps_sections(resolver.read("data\\maps.txt").data)
        source_map = source_slice["map"]
        source_index = int(source_map["header"]["mapIndex"])
        source = {
            "mapIndex": source_index,
            "mapName": source_map["header"]["name"],
            "logicalPath": source_map["logicalPath"],
            "mapSha256": source_map["sha256"],
            "exitGrids": exit_grid_records(
                source_index,
                source_map["header"]["name"],
                source_map["sha256"],
                source_map["objects"],
            ),
        }
        target_indices = sorted(
            {
                int(row["destination"]["mapIndex"])
                for row in source["exitGrids"]
                if int(row["destination"]["mapIndex"]) != source_index
            }
        )
        adjacent = []
        for target_index in target_indices:
            section = registry.get(target_index)
            if not section or not section.get("map_name"):
                raise Fo1ProfileError(
                    f"Fallout 2 adjacent MAP registry is incomplete: {target_index}"
                )
            adjacent.append(
                _compile_map(
                    resolver,
                    f"maps\\{section['map_name']}.map",
                    target_index,
                )
            )
        joins = reciprocal_map_joins([source, *adjacent])
        reciprocal_targets = {
            int(row["destinationMap"]["mapIndex"])
            if int(row["sourceMap"]["mapIndex"]) == source_index
            else int(row["sourceMap"]["mapIndex"])
            for row in joins
            if source_index
            in {
                int(row["sourceMap"]["mapIndex"]),
                int(row["destinationMap"]["mapIndex"]),
            }
        }
        if not reciprocal_targets:
            raise Fo1ProfileError("Fallout 2 source MAP has no reciprocal owned neighbor")

    return {
        "schema": SCHEMA,
        "status": "compiled-owned-reciprocal-adjacent-maps",
        "campaign": "Fallout2",
        "slice": "AdjacentMaps",
        "sourceProfile": {
            "file": str(profile_path),
            "sourceProfileId": profile["sourceProfileId"],
            "saveCompatibilityId": profile["saveCompatibilityId"],
            "sha256": file_sha256(profile_path),
        },
        "sourceSlice": {
            "file": str(source_slice_path),
            "sha256": file_sha256(source_slice_path),
            "mapIndex": source_index,
        },
        "recipe": {
            "file": str(recipe_path),
            "schema": recipe["schema"],
            "id": recipe["id"],
            "sha256": file_sha256(recipe_path),
        },
        "overlayOrderHighToLow": recipe["overlayOrderHighToLow"],
        "maps": adjacent,
        "mapJoins": joins,
        "resources": [
            {
                "logicalPath": resolver.resources[path].logical_path,
                "source": resolver.resources[path].source,
                "bytes": len(resolver.resources[path].data),
                "sha256": resolver.resources[path].sha256,
            }
            for path in sorted(accessed)
        ],
        "runtimePolicy": {
            "joinOwner": "ClassicMapJoinOwner",
            "missingReciprocalJoin": "fail-closed",
            "scriptExecution": "decoded-subsets-only",
        },
        "promotion": {"transported": True},
        "runtimeCompatibility": {"ready": False},
        "generatedCaches": [],
        "retailOrDerivedAssetsPackaged": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--source-slice", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        if output.exists():
            raise Fo1ProfileError(f"refusing to overwrite adjacent MAP catalog: {output}")
        document = compile_fo2_adjacent_maps(args.profile, args.source_slice)
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_FO2_ADJACENT_MAP_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_ADJACENT_MAP "
        + json.dumps(
            {
                "manifest": str(output),
                "maps": len(document["maps"]),
                "joins": len(document["mapJoins"]),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
