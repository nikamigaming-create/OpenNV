#!/usr/bin/env python3
"""Compile the owned Fallout 2 Arroyo Caves destination source graph."""

from __future__ import annotations

import argparse
from collections import deque
from dataclasses import asdict
import hashlib
import json
import struct
import sys
from pathlib import Path
from typing import Any

from corpus_io import atomic_json
from classic_int_initialization import compile_map_int_initialization
from fo1_frm import decode_frm
from fo1_map_objects import (
    OBJECT_TYPE_NAMES,
    TYPE_DIRECTORIES,
    Fo1ResourceResolver,
    parse_map_objects,
    parse_script_section,
)
from fo1_profile import Fo1ProfileError, map_layout_manifest, parse_map_layout
from fo2_first_slice import (
    FORM_ID_RADIX,
    FRM_PALETTE_SIZE,
    _archive_paths,
    _flatten_objects,
    _frm_structure,
    _load_json,
    _load_recipe,
    _maps_section,
)
from plugin_stack import file_sha256


SCHEMA = "opennv-fo2-owned-map-slice/v1"
TRANSITION_SCHEMA = "opennv-fo2-temple-transitions/v1"
MAP_INDEX = 3
MAP_NAME = "arcaves"
MAP_LOGICAL_PATH = "maps\\arcaves.map"
MAP_WIDTH = 200
NO_BLOCK_FLAG = 0x10


def _walk_mask_sha256(walkable: set[int]) -> str:
    digest = hashlib.sha256()
    for tile in sorted(walkable):
        digest.update(struct.pack(">i", tile))
    return digest.hexdigest()


def _path_sha256(path: list[int]) -> str:
    digest = hashlib.sha256()
    for tile in path:
        digest.update(struct.pack(">i", tile))
    return digest.hexdigest()


def _entry_component(start: int, walkable: set[int]) -> set[int]:
    if start not in walkable:
        raise Fo1ProfileError("Fallout 2 Arroyo Caves transition arrival is not source-walkable")
    visited = {start}
    queue = deque([start])
    while queue:
        tile = queue.popleft()
        x, y = tile % MAP_WIDTH, tile // MAP_WIDTH
        odd = x & 1
        offsets = (
            (-1, -1 if odd else 0),
            (-1, 0 if odd else 1),
            (0, 1),
            (1, 0 if odd else 1),
            (1, -1 if odd else 0),
            (0, -1),
        )
        for dx, dy in offsets:
            target_x, target_y = x + dx, y + dy
            neighbor = (
                target_y * MAP_WIDTH + target_x
                if 0 <= target_x < MAP_WIDTH and 0 <= target_y < MAP_WIDTH
                else -1
            )
            if neighbor in walkable and neighbor not in visited:
                visited.add(neighbor)
                queue.append(neighbor)
    return visited


def _shortest_path(start: int, target: int, walkable: set[int]) -> list[int]:
    if start not in walkable or target not in walkable:
        raise Fo1ProfileError("Fallout 2 Arroyo Caves path endpoint is not source-walkable")
    parents = {start: -1}
    queue = deque([start])
    while queue and target not in parents:
        tile = queue.popleft()
        x, y = tile % MAP_WIDTH, tile // MAP_WIDTH
        odd = x & 1
        offsets = (
            (-1, -1 if odd else 0),
            (-1, 0 if odd else 1),
            (0, 1),
            (1, 0 if odd else 1),
            (1, -1 if odd else 0),
            (0, -1),
        )
        for dx, dy in offsets:
            target_x, target_y = x + dx, y + dy
            neighbor = (
                target_y * MAP_WIDTH + target_x
                if 0 <= target_x < MAP_WIDTH and 0 <= target_y < MAP_WIDTH
                else -1
            )
            if neighbor in walkable and neighbor not in parents:
                parents[neighbor] = tile
                queue.append(neighbor)
    if target not in parents:
        raise Fo1ProfileError(
            "Fallout 2 Arroyo Caves exit is not reachable from the incoming placement"
        )
    path = []
    tile = target
    while tile >= 0:
        path.append(tile)
        tile = parents[tile]
    path.reverse()
    return path


def compile_fo2_arroyo_caves_slice(
    profile_path: Path,
    temple_transition_path: Path,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    temple_transition_path = temple_transition_path.resolve()
    profile = _load_json(profile_path)
    transitions = _load_json(temple_transition_path)
    if (
        transitions.get("schema") != TRANSITION_SCHEMA
        or transitions.get("status") != "compiled-owned-transition-records"
        or transitions.get("runtimePolicy", {}).get("runtimeReady") is not False
        or transitions.get("retailOrDerivedAssetsPackaged") is not False
    ):
        raise Fo1ProfileError("unexpected Fallout 2 Temple transition contract")
    source_profile = transitions.get("sourceProfile", {})
    if (
        Path(str(source_profile.get("file", ""))).resolve() != profile_path
        or source_profile.get("sha256") != file_sha256(profile_path)
        or source_profile.get("sourceProfileId") != profile.get("sourceProfileId")
    ):
        raise Fo1ProfileError("Fallout 2 Arroyo Caves profile binding drifted")
    temple_source_path = Path(str(transitions["sourceManifest"]["file"])).resolve()
    temple_source = _load_json(temple_source_path)
    if transitions["sourceManifest"]["sha256"] != file_sha256(temple_source_path):
        raise Fo1ProfileError("Fallout 2 Temple source hash drifted")
    recipe_path = Path(str(temple_source["recipe"]["file"])).resolve()
    recipe = _load_recipe(recipe_path)
    if temple_source["recipe"]["sha256"] != file_sha256(recipe_path):
        raise Fo1ProfileError("Fallout 2 overlay recipe hash drifted")

    destination = next(
        (row for row in transitions["destinationMaps"] if int(row["mapIndex"]) == MAP_INDEX),
        None,
    )
    if destination is None or destination["mapName"].casefold() != MAP_NAME:
        raise Fo1ProfileError("Fallout 2 Temple transition has no Arroyo Caves destination")
    incoming = [
        row for row in transitions["exitGrids"] if int(row["destination"]["mapIndex"]) == MAP_INDEX
    ]
    incoming_targets = {
        (
            int(row["destination"]["tile"]),
            int(row["destination"]["elevation"]),
            int(row["destination"]["rotation"]),
        )
        for row in incoming
    }
    if not incoming or len(incoming_targets) != 1:
        raise Fo1ProfileError("Fallout 2 Arroyo Caves incoming placement is ambiguous")
    arrival_tile, arrival_elevation, arrival_rotation = next(iter(incoming_targets))

    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    with resolver.access_scope() as accessed:
        registry_resource = resolver.read("data\\maps.txt")
        registry_values = _maps_section(registry_resource.data, "Map 003")
        if (
            registry_values.get("lookup_name") != destination["lookupName"]
            or registry_values.get("map_name") != MAP_NAME
        ):
            raise Fo1ProfileError("Fallout 2 Arroyo Caves maps.txt identity drifted")
        map_resource = resolver.read(MAP_LOGICAL_PATH)
        if (
            map_resource.sha256 != destination["sha256"]
            or len(map_resource.data) != int(destination["bytes"])
        ):
            raise Fo1ProfileError("Fallout 2 Arroyo Caves MAP identity drifted")
        layout = parse_map_layout(map_resource.data)
        if (
            layout.header.mapIndex != MAP_INDEX
            or layout.header.name != "ARCAVES.MAP"
            or arrival_elevation not in {row.elevation for row in layout.elevations}
        ):
            raise Fo1ProfileError("Fallout 2 Arroyo Caves MAP header drifted")
        scripts, objects_offset = parse_script_section(map_resource.data, layout.next_offset)
        objects, end_offset = parse_map_objects(
            map_resource.data,
            objects_offset,
            layout.header.version,
            resolver,
        )
        if end_offset != len(map_resource.data):
            raise Fo1ProfileError("Fallout 2 Arroyo Caves object graph has trailing bytes")

        initialization_scripts = compile_map_int_initialization(
            asdict(layout.header), scripts, resolver
        )

        flat_objects = _flatten_objects(objects)
        prototypes: dict[str, dict[str, Any]] = {}
        frm_placements: dict[str, list[dict[str, Any]]] = {}
        for obj in flat_objects:
            prototype = obj["prototype"]
            pid = obj["pid"]
            if prototype["filename"] is not None:
                directory = TYPE_DIRECTORIES[prototype["object_type"]]
                prototypes.setdefault(
                    pid,
                    {
                        **prototype,
                        "logicalPath": f"proto\\{directory}\\{prototype['filename']}".casefold(),
                        "objectTypeName": OBJECT_TYPE_NAMES[prototype["object_type"]],
                        "placedObjectSerials": [],
                    },
                )["placedObjectSerials"].append(obj["serial"])
            fid = int(obj["fid"], FORM_ID_RADIX)
            frm_path = resolver.placed_idle_frm_path(fid)
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
        for logical_path in sorted(frm_placements):
            resource = resolver.read(logical_path)
            frms.append(
                {
                    "logicalPath": logical_path,
                    "source": resource.source,
                    "bytes": len(resource.data),
                    "sha256": resource.sha256,
                    "structure": _frm_structure(decode_frm(resource.data, palette)),
                    "placements": frm_placements[logical_path],
                }
            )

    top_level = [obj for elevation in objects["elevations"] for obj in elevation["objects"]]
    reciprocal = [
        obj
        for obj in top_level
        if obj["prototype"]["object_type"] == 5
        and len(obj["instanceValues"]) == 4
        and int(obj["instanceValues"][0]) == 126
    ]
    reciprocal_targets = {
        tuple(map(int, obj["instanceValues"])) for obj in reciprocal
    }
    if not reciprocal or len(reciprocal_targets) != 1:
        raise Fo1ProfileError("Fallout 2 Arroyo Caves reciprocal Temple transition is ambiguous")
    reciprocal_target = next(iter(reciprocal_targets))

    arrival_layout = next(
        row for row in layout.elevations if row.elevation == arrival_elevation
    )
    floor_ids = [entry & 0x0FFF for entry in arrival_layout.entries]
    elevation_objects = next(
        row["objects"] for row in objects["elevations"] if row["elevation"] == arrival_elevation
    )
    blocked = {
        int(obj["tile"])
        for obj in elevation_objects
        if int(obj["tile"]) >= 0 and not int(obj["flags"], 16) & NO_BLOCK_FLAG
    }
    walkable = {
        tile
        for tile in range(MAP_WIDTH * MAP_WIDTH)
        if floor_ids[(tile // MAP_WIDTH // 2) * 100 + 99 - (tile % MAP_WIDTH // 2)] != 1
        and tile not in blocked
    }
    component = _entry_component(arrival_tile, walkable)
    reciprocal_records = [
        {
            "serial": int(obj["serial"]),
            "tile": int(obj["tile"]),
            "elevation": int(obj["elevation"]),
            "flags": obj["flags"],
            "fid": obj["fid"],
            "pid": obj["pid"],
            "artFilename": obj["artFilename"],
            "destination": {
                "mapIndex": int(obj["instanceValues"][0]),
                "tile": int(obj["instanceValues"][1]),
                "elevation": int(obj["instanceValues"][2]),
                "rotation": int(obj["instanceValues"][3]),
            },
            "reachableFromIncomingPlacement": int(obj["tile"]) in component,
        }
        for obj in reciprocal
    ]
    reachable_exits = []
    for record in reciprocal_records:
        if not record["reachableFromIncomingPlacement"]:
            continue
        path = _shortest_path(arrival_tile, int(record["tile"]), component)
        reachable_exits.append((len(path) - 1, int(record["serial"]), record, path))
    if not reachable_exits:
        raise Fo1ProfileError(
            "Fallout 2 Arroyo Caves has no source-walkable reciprocal Temple exit"
        )
    _, _, selected_exit, selected_path = min(reachable_exits)
    temple_map = temple_source["map"]
    if (
        temple_map["header"]["mapIndex"] != reciprocal_target[0]
        or temple_map["logicalPath"] != "maps\\artemple.map"
    ):
        raise Fo1ProfileError("Fallout 2 reciprocal Temple MAP identity drifted")
    return {
        "schema": SCHEMA,
        "status": "transported-owned-map-source-and-presentation-graph",
        "campaign": "Fallout2",
        "slice": "ArroyoCaves",
        "sourceProfile": {
            "file": str(profile_path),
            "sourceProfileId": profile["sourceProfileId"],
            "saveCompatibilityId": profile["saveCompatibilityId"],
            "sha256": file_sha256(profile_path),
        },
        "sourceTransition": {
            "file": str(temple_transition_path),
            "schema": TRANSITION_SCHEMA,
            "sha256": file_sha256(temple_transition_path),
            "incomingExitSerials": sorted(int(row["serial"]) for row in incoming),
        },
        "recipe": {
            "file": str(recipe_path),
            "id": recipe["id"],
            "sha256": file_sha256(recipe_path),
        },
        "overlayOrderHighToLow": recipe["overlayOrderHighToLow"],
        "mapRegistry": {
            "logicalPath": registry_resource.logical_path,
            "source": registry_resource.source,
            "bytes": len(registry_resource.data),
            "sha256": registry_resource.sha256,
            "section": "Map 003",
            "values": registry_values,
        },
        "incomingPlacement": {
            "authority": "exact Map 126 exit-grid instance values",
            "mapIndex": MAP_INDEX,
            "tile": arrival_tile,
            "tileX": arrival_tile % MAP_WIDTH,
            "tileY": arrival_tile // MAP_WIDTH,
            "elevation": arrival_elevation,
            "rotation": arrival_rotation,
            "headerDefaultIsDifferent": (
                arrival_tile != layout.header.enteringTile
                or arrival_elevation != layout.header.enteringElevation
                or arrival_rotation != layout.header.enteringRotation
            ),
        },
        "map": {
            "logicalPath": map_resource.logical_path,
            "source": map_resource.source,
            "bytes": len(map_resource.data),
            "sha256": map_resource.sha256,
            "header": asdict(layout.header),
            "layout": map_layout_manifest(layout),
            "scriptLists": scripts,
            "objectsOffset": objects_offset,
            "endOffset": end_offset,
            "objects": objects,
            "allObjectCount": len(flat_objects),
        },
        "initializationScripts": initialization_scripts,
        "arrivalWalkContract": {
            "semantics": "non-default-floor-art-minus-central-source-blocking-object-hexes-v1",
            "walkMaskSha256": _walk_mask_sha256(walkable),
            "walkableHexes": len(walkable),
            "entryComponentHexes": len(component),
            "multihexExpansionImplemented": False,
        },
        "reciprocalTempleExitGrids": reciprocal_records,
        "liveExitTransition": {
            "selection": "shortest-source-walk-path-then-serial-v1",
            "source": {
                "mapIndex": MAP_INDEX,
                "mapSha256": map_resource.sha256,
                "exitSerial": selected_exit["serial"],
                "tile": selected_exit["tile"],
                "elevation": selected_exit["elevation"],
                "fid": selected_exit["fid"],
                "pid": selected_exit["pid"],
            },
            "path": selected_path,
            "pathSteps": len(selected_path) - 1,
            "pathSha256": _path_sha256(selected_path),
            "destination": {
                "mapIndex": reciprocal_target[0],
                "logicalPath": temple_map["logicalPath"],
                "mapSha256": temple_map["sha256"],
                "tile": reciprocal_target[1],
                "elevation": reciprocal_target[2],
                "rotation": reciprocal_target[3],
            },
        },
        "reciprocalTransition": {
            "sourceRecords": len(reciprocal_records),
            "reachableFromIncomingPlacement": sum(
                row["reachableFromIncomingPlacement"] for row in reciprocal_records
            ),
            "destination": {
                "mapIndex": reciprocal_target[0],
                "tile": reciprocal_target[1],
                "elevation": reciprocal_target[2],
                "rotation": reciprocal_target[3],
            },
            "runtimeImplemented": False,
        },
        "prototypes": [prototypes[pid] for pid in sorted(prototypes)],
        "frms": frms,
        "resources": [
            {
                "logicalPath": resolver.resources[path].logical_path,
                "source": resolver.resources[path].source,
                "bytes": len(resolver.resources[path].data),
                "sha256": resolver.resources[path].sha256,
            }
            for path in sorted(accessed)
        ],
        "promotion": {
            "transported": True,
            "decodedPresentationAssets": False,
            "rendered": False,
            "interactive": False,
            "runtimeReady": False,
        },
        "runtimeCompatibility": {
            "ready": False,
            "firstSliceBlocker": (
                "The exact Arroyo Caves source graph and reciprocal Temple exit are transported, "
                "but no decoded presentation cache or Godot destination consumer exists."
            ),
        },
        "unsupported": [
            "INT execution, actors, Chosen One state, combat, gameplay, and save state",
            "multihex footprint expansion, retail parity, FPS, and OpenXR",
            "runtime destination scene construction and reciprocal transition execution",
        ],
        "retailOrDerivedAssetsPackaged": False,
        "generatedCaches": [],
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Compile the asset-free owned Fallout 2 Arroyo Caves destination graph."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--temple-transitions", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        document = compile_fo2_arroyo_caves_slice(args.profile, args.temple_transitions)
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_FO2_ARROYO_CAVES_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_ARROYO_CAVES "
        + json.dumps(
            {
                "manifest": str(output),
                "objects": document["map"]["objects"]["totalTopLevelObjects"],
                "frms": len(document["frms"]),
                "reciprocalExitGrids": len(document["reciprocalTempleExitGrids"]),
                "reachableReciprocalExitGrids": document["reciprocalTransition"][
                    "reachableFromIncomingPlacement"
                ],
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
