#!/usr/bin/env python3
"""Compile one source-MAP unscripted generic-door passability contract."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import tempfile
from collections import deque
from pathlib import Path
from typing import Any

from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError, sha256_path
from fo1_frm import decode_frm
from classic_door import decode_classic_door


SCHEMA = "opennv-fo1-destination-generic-door/v1"
TRANSPORT_SCHEMA = "opennv-fo1-campaign-map-transport/v1"
PRESENTATION_SCHEMA = "opennv-fo1-campaign-presentation/v1"
MAP_PRESENTATION_SCHEMA = "opennv-fo1-campaign-map-presentation/v1"
EXIT_SCHEMA = "opennv-fo1-exit-grid-transition/v1"
MAP_WIDTH = 200
MAP_HEIGHT = 200
FLOOR_WIDTH = MAP_WIDTH // 2
FLOOR_HEIGHT = MAP_HEIGHT // 2
FLOOR_COUNT = FLOOR_WIDTH * FLOOR_HEIGHT
NO_SCRIPT_INDEX = -1
NO_SCRIPT_ID = "ffffffff"
PID_RADIX = 16


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def neighbors(tile: int) -> list[int]:
    if not 0 <= tile < MAP_WIDTH * MAP_HEIGHT:
        return []
    column, row = tile % MAP_WIDTH, tile // MAP_WIDTH
    odd = bool(column & 1)
    offsets = ((1, 0 if odd else 1), (0, 1), (-1, 0 if odd else 1),
               (-1, -1 if odd else 0), (0, -1), (1, -1 if odd else 0))
    return [
        target_row * MAP_WIDTH + target_column
        for offset_column, offset_row in offsets
        if 0 <= (target_column := column + offset_column) < MAP_WIDTH
        and 0 <= (target_row := row + offset_row) < MAP_HEIGHT
    ]


def shortest_contact_path(start: int, target: int, floor_ids: list[int], default_tile: int,
                          blockers: set[int]) -> list[int] | None:
    if len(floor_ids) != FLOOR_COUNT:
        raise Fo1ProfileError("destination presentation floor grid is invalid")

    def walkable(tile: int) -> bool:
        column, row = tile % MAP_WIDTH, tile // MAP_WIDTH
        floor_index = (row // 2) * FLOOR_WIDTH + (FLOOR_WIDTH - 1 - column // 2)
        return floor_ids[floor_index] != default_tile and tile not in blockers

    if not walkable(start):
        raise Fo1ProfileError("exit-grid destination is not walkable in the presentation map")
    pending = deque([start])
    previous: dict[int, int | None] = {start: None}
    while pending:
        tile = pending.popleft()
        if tile in neighbors(target):
            path: list[int] = []
            current: int | None = tile
            while current is not None:
                path.append(current)
                current = previous[current]
            return list(reversed(path))
        for candidate in sorted(neighbors(tile)):
            if walkable(candidate) and candidate not in previous:
                previous[candidate] = tile
                pending.append(candidate)
    return None


def frm_summary(data: bytes) -> dict[str, Any]:
    decoded = decode_frm(data, [(0, 0, 0, 0)] * 256)
    return {
        "version": decoded["version"], "storedFps": decoded["storedFps"],
        "actionFrame": decoded["actionFrame"], "framesPerDirection": decoded["framesPerDirection"],
        "directions": [
            {"rotation": row["rotation"], "xOffset": row["xOffset"], "yOffset": row["yOffset"],
             "frameCount": len(row["frames"]),
             "frameDimensions": [{"width": frame["width"], "height": frame["height"],
                                  "x": frame["x"], "y": frame["y"]} for frame in row["frames"]]}
            for row in decoded["directions"]
        ],
    }


def build(transport_path: Path, presentation_path: Path, transition_path: Path,
          fallout2_master: Path, fallout2_critter: Path | None, output_path: Path) -> dict[str, Any]:
    if output_path.exists():
        raise Fo1ProfileError(f"refusing to overwrite destination generic-door descriptor: {output_path}")
    transport, presentation, transition = (
        read_json(transport_path), read_json(presentation_path), read_json(transition_path))
    if transport.get("schema") != TRANSPORT_SCHEMA or presentation.get("schema") != PRESENTATION_SCHEMA:
        raise Fo1ProfileError("unexpected source MAP transport or destination presentation schema")
    if transition.get("schema") != EXIT_SCHEMA or transition.get("status") != "compiled-owned-map-world-transition":
        raise Fo1ProfileError("unexpected exit-grid transition descriptor")
    source_map, destination = transport.get("source", {}).get("map", {}), transition.get("destination", {})
    if source_map.get("sha256") != destination.get("mapSha256") or source_map.get("file") != destination.get("name"):
        raise Fo1ProfileError("generic-door transport/exit-grid source join drifted")
    master_input = transport.get("source", {}).get("fallout2Master", {})
    if master_input.get("sha256") != sha256_path(fallout2_master):
        raise Fo1ProfileError("generic-door supplied master.dat does not match the transport input hash")
    maps = presentation.get("maps", [])
    map_id = Path(str(destination.get("name", ""))).stem.lower()
    if len(maps) != 1 or maps[0].get("id") != map_id or maps[0].get("file") != source_map.get("file"):
        raise Fo1ProfileError("generic-door presentation does not uniquely name the transition map")
    map_path = (presentation_path.parent / str(maps[0].get("path", ""))).resolve()
    map_document = read_json(map_path)
    if (map_document.get("schema") != MAP_PRESENTATION_SCHEMA or
            map_document.get("source", {}).get("mapSha256") != source_map.get("sha256") or
            sha256_path(map_path) != maps[0].get("sha256")):
        raise Fo1ProfileError("generic-door presentation map hash join drifted")
    elevation_id = destination.get("elevation")
    elevation = next((row for row in map_document.get("elevations", []) if row.get("elevation") == elevation_id), None)
    source_rows = next((row.get("objects", []) for row in transport.get("objectGraph", {}).get("objects", {}).get("elevations", []) if row.get("elevation") == elevation_id), None)
    if elevation is None or source_rows is None:
        raise Fo1ProfileError("generic-door destination elevation is absent from source/presentation")
    blockers = {row["tile"] for row in elevation.get("blockers", [])}
    default_tile = map_document.get("grid", {}).get("defaultTileId")
    candidates: list[tuple[list[int], dict[str, Any]]] = []
    for source in source_rows:
        prototype = source.get("prototype", {})
        if (source.get("tile") not in blockers or source.get("scriptIndex") != NO_SCRIPT_INDEX or
                source.get("sid") != NO_SCRIPT_ID or prototype.get("object_type") != 2 or
                prototype.get("subtype_name") != "door" or not source.get("artFilename") or
                not prototype.get("sha256")):
            continue
        route = shortest_contact_path(int(destination["tile"]), source["tile"], elevation["floorIds"], default_tile, blockers)
        if route is not None:
            candidates.append((route, source))
    if not candidates:
        raise Fo1ProfileError("destination MAP has no reachable unscripted MAP door blocker")
    route, door = min(candidates, key=lambda row: (len(row[0]), row[1]["tile"], row[1]["serial"]))
    resolver = Fo1ResourceResolver(None, fallout2_master, [] if fallout2_critter is None else [fallout2_critter])
    prototype_path = f"proto\\scenery\\{door['prototype']['filename']}"
    art_path = f"art\\scenery\\{door['artFilename']}"
    prototype_resource, art_resource = resolver.read(prototype_path), resolver.read(art_path)
    if hashlib.sha256(prototype_resource.data).hexdigest() != door["prototype"]["sha256"]:
        raise Fo1ProfileError("generic-door owned PRO bytes do not match the MAP prototype hash")
    art_sha256 = hashlib.sha256(art_resource.data).hexdigest()
    door_presentation = decode_classic_door(
        resolver, int(door["pid"], PID_RADIX), door["artFilename"]
    )
    document = {
        "schema": SCHEMA,
        "status": "compiled-owned-map-unscripted-generic-door-open-passability",
        "selection": {"policy": "nearest-reachable-unscripted-scenery-door-blocker-v1", "candidateCount": len(candidates)},
        "inputs": {
            "transport": {"path": str(transport_path.resolve()), "sha256": sha256_path(transport_path)},
            "presentation": {"path": str(presentation_path.resolve()), "sha256": sha256_path(presentation_path)},
            "presentationMap": {"path": str(map_path), "sha256": sha256_path(map_path)},
            "exitGridTransition": {"path": str(transition_path.resolve()), "sha256": sha256_path(transition_path)},
            "fallout2Master": {"path": str(fallout2_master.resolve()), "sha256": sha256_path(fallout2_master)},
        },
        "destination": {"mapId": map_id, "sourceFile": source_map["file"], "sourceMapSha256": source_map["sha256"], "elevation": elevation_id, "entryTile": destination["tile"]},
        "door": {
            "serial": door["serial"], "objectId": door["id"], "tile": door["tile"], "pid": door["pid"], "fid": door["fid"],
            "flags": door["flags"], "sourceOffset": door["sourceOffset"], "rotation": door["rotation"],
            "prototype": {"logicalPath": prototype_path, "source": prototype_resource.source,
                          "sha256": hashlib.sha256(prototype_resource.data).hexdigest(), "subtypeName": door["prototype"]["subtype_name"]},
            "art": {"logicalPath": art_path, "source": art_resource.source, "sha256": art_sha256,
                    "filename": door["artFilename"], "frm": frm_summary(art_resource.data)},
            "script": {"mapScriptIndex": NO_SCRIPT_INDEX, "sid": NO_SCRIPT_ID,
                       "semantics": "no-script-boundary-generic-door-open-passability-only"},
            "closed": {"walkable": False}, "open": {"walkable": True},
            "interactionActionPoints": "not-source-backed",
            "presentation": door_presentation,
        },
        "sourceWalkMaskRoute": {"pathTiles": route, "contactTile": route[-1], "contactIsAdjacent": route[-1] in neighbors(door["tile"])},
        "rendered": False, "interactive": False, "retailOrDerivedAssetsPackaged": False,
    }
    encoded = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=output_path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(encoded)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, output_path)
    return {"path": str(output_path.resolve()), "sha256": hashlib.sha256(encoded).hexdigest(), "doorSerial": door["serial"]}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--transport", type=Path, required=True)
    parser.add_argument("--presentation", type=Path, required=True)
    parser.add_argument("--exit-grid-transition", type=Path, required=True)
    parser.add_argument("--fallout2-master", type=Path, required=True)
    parser.add_argument("--fallout2-critter", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = build(args.transport.resolve(), args.presentation.resolve(), args.exit_grid_transition.resolve(),
                       args.fallout2_master.resolve(), args.fallout2_critter.resolve() if args.fallout2_critter else None,
                       args.output.resolve())
    except Exception as error:
        print(f"OPENNV_FO1_DESTINATION_GENERIC_DOOR_ERROR {error}")
        return 2
    print("OPENNV_FO1_DESTINATION_GENERIC_DOOR " + json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
