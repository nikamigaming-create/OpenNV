"""Compile exact classic Fallout MAP exit-grid joins from decoded object graphs."""

from __future__ import annotations

from collections.abc import Iterable
import hashlib
from typing import Any

from fo1_map_objects import FO1_MAP_OBJECTS_FORMAT_CONTRACT_INTEGER_5
from fo1_profile import Fo1ProfileError
from fo2_temple_transitions import EXIT_GRID_OBJECT_TYPE, MAP_WIDTH


AREA_EXIT_MAP_INDEX = -1
WORLD_MAP_INDEX = -2
SCRIPT_RESOLVED_TILE = -1
SHA256_HEX_CHARACTERS = hashlib.sha256().digest_size * 2


def exit_grid_records(
    map_index: int,
    map_name: str,
    map_sha256: str,
    objects: dict[str, Any],
) -> list[dict[str, Any]]:
    """Return source-owned exit-grid records without interpreting scripts."""
    if not map_name or len(map_sha256) != SHA256_HEX_CHARACTERS:
        raise Fo1ProfileError("classic MAP exit-grid source identity is incomplete")
    if map_index < 0:
        return []
    records = []
    for elevation in objects["elevations"]:
        for obj in elevation["objects"]:
            values = obj.get("instanceValues", [])
            if (
                int(obj["prototype"]["object_type"]) != EXIT_GRID_OBJECT_TYPE
                or len(values) != 4
            ):
                continue
            target_map, target_tile, target_elevation, target_rotation = map(
                int, values
            )
            if target_map in {AREA_EXIT_MAP_INDEX, WORLD_MAP_INDEX}:
                continue
            if target_tile == SCRIPT_RESOLVED_TILE:
                continue
            source_tile = int(obj["tile"])
            source_elevation = int(obj["elevation"])
            if (
                not 0 <= source_tile < MAP_WIDTH * MAP_WIDTH
                or not 0 <= source_elevation <= 2
                or target_map < 0
                or not 0 <= target_tile < MAP_WIDTH * MAP_WIDTH
                or not 0 <= target_elevation <= 2
                or not 0
                <= target_rotation
                <= FO1_MAP_OBJECTS_FORMAT_CONTRACT_INTEGER_5
            ):
                raise Fo1ProfileError(
                    f"classic MAP exit-grid values are invalid: {map_name}:{obj['serial']}"
                )
            records.append(
                {
                    "serial": int(obj["serial"]),
                    "pid": str(obj["pid"]),
                    "prototypeSha256": str(obj["prototype"]["sha256"]),
                    "source": {
                        "mapIndex": map_index,
                        "mapName": map_name,
                        "mapSha256": map_sha256,
                        "tile": source_tile,
                        "elevation": source_elevation,
                    },
                    "destination": {
                        "mapIndex": target_map,
                        "tile": target_tile,
                        "elevation": target_elevation,
                        "rotation": target_rotation,
                    },
                }
            )
    return sorted(records, key=lambda row: row["serial"])


def reciprocal_map_joins(
    maps: Iterable[dict[str, Any]],
) -> list[dict[str, Any]]:
    """Join decoded exits only when both owned MAP directions are present."""
    map_rows = list(maps)
    by_index = {int(row["mapIndex"]): row for row in map_rows}
    if len(by_index) != len(map_rows):
        raise Fo1ProfileError("classic MAP catalog contains duplicate map indices")
    exits_by_pair: dict[tuple[int, int], list[dict[str, Any]]] = {}
    for row in map_rows:
        source_index = int(row["mapIndex"])
        for exit_grid in row["exitGrids"]:
            target_index = int(exit_grid["destination"]["mapIndex"])
            if target_index in by_index and target_index != source_index:
                exits_by_pair.setdefault((source_index, target_index), []).append(
                    exit_grid
                )
    joins = []
    for source_index, target_index in sorted(exits_by_pair):
        if source_index > target_index or (target_index, source_index) not in exits_by_pair:
            continue
        source = by_index[source_index]
        target = by_index[target_index]
        joins.append(
            {
                "sourceMap": {
                    "mapIndex": source_index,
                    "mapName": source["mapName"],
                    "mapSha256": source["mapSha256"],
                },
                "destinationMap": {
                    "mapIndex": target_index,
                    "mapName": target["mapName"],
                    "mapSha256": target["mapSha256"],
                },
                "forwardExitGrids": exits_by_pair[(source_index, target_index)],
                "reverseExitGrids": exits_by_pair[(target_index, source_index)],
                "reciprocal": True,
            }
        )
    return joins
