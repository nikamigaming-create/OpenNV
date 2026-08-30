#!/usr/bin/env python3
"""Compile the first reachable source MAP inventory interaction for a FO1 destination."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import tempfile
from collections import deque
from pathlib import Path
from typing import Any

from fo1_profile import Fo1ProfileError, sha256_path


SCHEMA = "opennv-fo1-destination-inventory-interaction/v1"
TRANSPORT_SCHEMA = "opennv-fo1-campaign-map-transport/v1"
PRESENTATION_SCHEMA = "opennv-fo1-campaign-presentation/v1"
MAP_PRESENTATION_SCHEMA = "opennv-fo1-campaign-map-presentation/v1"
EXIT_SCHEMA = "opennv-fo1-exit-grid-transition/v1"
MAP_WIDTH = 200
MAP_HEIGHT = 200
FLOOR_WIDTH = MAP_WIDTH // 2
FLOOR_HEIGHT = MAP_HEIGHT // 2
FLOOR_COUNT = FLOOR_WIDTH * FLOOR_HEIGHT
PID_DEFINE = re.compile(r"^\s*#define\s+(PID_[A-Z0-9_]+)\s+\((\d+)\)", re.MULTILINE)


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


def shortest_contact_path(start: int, host_tile: int, floor_ids: list[int], default_tile: int,
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
        if any(neighbor == tile for neighbor in neighbors(host_tile)):
            path: list[int] = []
            current: int | None = tile
            while current is not None:
                path.append(current)
                current = previous[current]
            return list(reversed(path))
        for target in sorted(neighbors(tile)):
            if walkable(target) and target not in previous:
                previous[target] = tile
                pending.append(target)
    return None


def item_symbols(header: Path) -> dict[str, str]:
    rows = {f"{int(value):08x}": name for name, value in PID_DEFINE.findall(header.read_text(encoding="cp1252"))}
    if not rows:
        raise Fo1ProfileError("Fallout item PID header has no PID definitions")
    return rows


def build(transport_path: Path, presentation_path: Path, transition_path: Path,
          item_header_path: Path, output_path: Path) -> dict[str, Any]:
    if output_path.exists():
        raise Fo1ProfileError(f"refusing to overwrite destination interaction descriptor: {output_path}")
    transport, presentation, transition = (
        read_json(transport_path), read_json(presentation_path), read_json(transition_path))
    if transport.get("schema") != TRANSPORT_SCHEMA or presentation.get("schema") != PRESENTATION_SCHEMA:
        raise Fo1ProfileError("unexpected source MAP transport or destination presentation schema")
    if transition.get("schema") != EXIT_SCHEMA or transition.get("status") != "compiled-owned-map-world-transition":
        raise Fo1ProfileError("unexpected exit-grid transition descriptor")
    source_map = transport.get("source", {}).get("map", {})
    destination = transition.get("destination", {})
    if (source_map.get("sha256") != destination.get("mapSha256") or
            source_map.get("file") != destination.get("name")):
        raise Fo1ProfileError("destination interaction transport/exit-grid source join drifted")
    maps = presentation.get("maps", [])
    map_id = Path(str(destination.get("name", ""))).stem.lower()
    if len(maps) != 1 or maps[0].get("id") != map_id or maps[0].get("file") != source_map.get("file"):
        raise Fo1ProfileError("destination interaction presentation does not uniquely name the transition map")
    map_path = (presentation_path.parent / str(maps[0].get("path", ""))).resolve()
    map_document = read_json(map_path)
    if (map_document.get("schema") != MAP_PRESENTATION_SCHEMA or
            map_document.get("source", {}).get("mapSha256") != source_map.get("sha256") or
            sha256_path(map_path) != maps[0].get("sha256")):
        raise Fo1ProfileError("destination interaction presentation map hash join drifted")
    elevation_id = destination.get("elevation")
    elevation = next((row for row in map_document.get("elevations", []) if row.get("elevation") == elevation_id), None)
    if elevation is None:
        raise Fo1ProfileError("destination interaction elevation is absent from presentation")
    symbols = item_symbols(item_header_path)
    source_rows = next((row.get("objects", []) for row in transport.get("objectGraph", {}).get("objects", {}).get("elevations", []) if row.get("elevation") == elevation_id), None)
    if source_rows is None:
        raise Fo1ProfileError("destination interaction elevation is absent from MAP transport")
    default_tile = map_document.get("grid", {}).get("defaultTileId")
    blockers = {row["tile"] for row in elevation.get("blockers", [])}
    candidates: list[tuple[list[int], dict[str, Any]]] = []
    for source in source_rows:
        inventory = source.get("inventory", [])
        prototype = source.get("prototype", {})
        if (not inventory or source.get("tile", -1) < 0 or source.get("scriptIndex") != -1 or
                prototype.get("object_type") != 0 or prototype.get("subtype_name") != "container" or
                source.get("inventoryCapacity", 0) <= 0):
            continue
        path = shortest_contact_path(int(destination["tile"]), source["tile"], elevation["floorIds"], default_tile, blockers)
        if path is not None:
            candidates.append((path, source))
    if not candidates:
        raise Fo1ProfileError("destination MAP has no reachable unscripted container with source inventory")
    path, host = min(candidates, key=lambda row: (len(row[0]), row[1]["tile"], row[1]["serial"]))
    items = []
    for row in host["inventory"]:
        item = row["object"]
        prototype = item["prototype"]
        symbol = symbols.get(item["pid"])
        if (symbol is None or row["quantity"] <= 0 or prototype.get("object_type") != 0 or
                not prototype.get("sha256")):
            raise Fo1ProfileError("destination container inventory is not an admitted positive source item stack")
        items.append({
            "index": row["index"], "serial": item["serial"], "objectId": item["id"],
            "pid": item["pid"], "fid": item["fid"], "sourceOffset": item["sourceOffset"],
            "symbol": symbol, "displayName": symbol, "quantity": row["quantity"],
            "prototypeFilename": prototype.get("filename"), "prototypeSource": prototype.get("source"),
            "prototypeSha256": prototype["sha256"], "profile": {"subtypeName": prototype.get("subtype_name")},
        })
    document = {
        "schema": SCHEMA,
        "status": "compiled-owned-map-nearest-reachable-container-interaction",
        "selection": {"policy": "nearest-reachable-unscripted-container-with-positive-source-inventory-v1", "candidateCount": len(candidates)},
        "inputs": {
            "transport": {"path": str(transport_path.resolve()), "sha256": sha256_path(transport_path)},
            "presentation": {"path": str(presentation_path.resolve()), "sha256": sha256_path(presentation_path)},
            "presentationMap": {"path": str(map_path), "sha256": sha256_path(map_path)},
            "exitGridTransition": {"path": str(transition_path.resolve()), "sha256": sha256_path(transition_path)},
            "itemPidHeader": {"path": str(item_header_path.resolve()), "sha256": sha256_path(item_header_path)},
        },
        "destination": {"mapId": map_id, "sourceFile": source_map["file"], "sourceMapSha256": source_map["sha256"], "elevation": elevation_id, "entryTile": destination["tile"]},
        "host": {
            "schema": "opennv-fo1-map-inventory-host/v1", "serial": host["serial"], "objectId": host["id"],
            "tile": host["tile"], "pid": host["pid"], "fid": host["fid"], "flags": host["flags"],
            "sourceOffset": host["sourceOffset"], "inventoryPointer": host["inventoryPointer"],
            "inventoryCapacity": host["inventoryCapacity"], "prototypeFilename": host["prototype"].get("filename"),
            "prototypeSource": host["prototype"].get("source"), "prototypeSha256": host["prototype"]["sha256"], "items": items,
        },
        "sourceWalkMaskRoute": {"pathTiles": path, "contactTile": path[-1], "contactIsAdjacent": path[-1] in neighbors(host["tile"])},
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
    return {"path": str(output_path.resolve()), "sha256": hashlib.sha256(encoded).hexdigest(), "hostSerial": host["serial"]}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--transport", type=Path, required=True)
    parser.add_argument("--presentation", type=Path, required=True)
    parser.add_argument("--exit-grid-transition", type=Path, required=True)
    parser.add_argument("--item-pid-header", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = build(args.transport.resolve(), args.presentation.resolve(), args.exit_grid_transition.resolve(), args.item_pid_header.resolve(), args.output.resolve())
    except Exception as error:
        print(f"OPENNV_FO1_DESTINATION_INTERACTION_ERROR {error}")
        return 2
    print("OPENNV_FO1_DESTINATION_INTERACTION " + json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
