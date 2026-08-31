#!/usr/bin/env python3
"""Compile the bounded owned Fallout 2 Cameron-to-Arroyo trial route."""

from __future__ import annotations

import argparse
from collections import deque
import hashlib
import json
import re
import struct
import sys
from pathlib import Path
from typing import Any, Callable

from corpus_io import atomic_json
from classic_door import decode_classic_door
from fo1_map_objects import Fo1ResourceResolver, parse_map_objects, parse_script_section
from fo1_profile import Fo1ProfileError, parse_map_layout
from fo2_first_slice import _archive_paths, _load_json
from plugin_stack import file_sha256


SCHEMA = "opennv-fo2-arroyo-trial-route/v1"
SOURCE_SCHEMA = "opennv-fo2-owned-map-slice/v1"
TRANSITION_SCHEMA = "opennv-fo2-temple-transitions/v1"
MAP_WIDTH = 200
NO_BLOCK_FLAG = 0x10
WALL_OBJECT_TYPE = 3
EXIT_GRID_OBJECT_TYPE = 5
PID_RADIX = 16


def _parse_dialogue_catalog(payload: bytes) -> dict[int, str]:
    """Parse Fallout MSG rows without assuming their text fits on one line."""

    messages: dict[int, str] = {}
    active_id: int | None = None
    fragments: list[str] = []
    opening = re.compile(r"^\{(\d+)\}\{[^}]*\}\{(.*)$")
    for line_number, raw_line in enumerate(
        payload.decode("cp1252").splitlines(),
        start=1,
    ):
        line = raw_line.strip()
        if active_id is None:
            if not line or line.startswith("#"):
                continue
            match = opening.match(line)
            if match is None:
                raise Fo1ProfileError(
                    f"unsupported Fallout 2 MSG row at line {line_number}: {line!r}"
                )
            active_id = int(match.group(1))
            if active_id in messages:
                raise Fo1ProfileError(
                    f"duplicate Fallout 2 MSG id {active_id} at line {line_number}"
                )
            remainder = match.group(2)
            if remainder.endswith("}"):
                messages[active_id] = remainder[:-1].strip()
                active_id = None
            else:
                fragments = [remainder]
            continue

        if opening.match(line):
            raise Fo1ProfileError(
                f"unterminated Fallout 2 MSG id {active_id} before line {line_number}"
            )
        if line.endswith("}"):
            fragments.append(line[:-1])
            messages[active_id] = " ".join(
                fragment.strip() for fragment in fragments if fragment.strip()
            )
            active_id = None
            fragments = []
        else:
            fragments.append(line)

    if active_id is not None:
        raise Fo1ProfileError(f"unterminated Fallout 2 MSG id {active_id} at end of file")
    return messages


def _neighbors(tile: int) -> list[int]:
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
    return [
        target_y * MAP_WIDTH + target_x
        for dx, dy in offsets
        if 0 <= (target_x := x + dx) < MAP_WIDTH
        and 0 <= (target_y := y + dy) < MAP_WIDTH
    ]


def _path_sha256(path: list[dict[str, int | None]]) -> str:
    digest = hashlib.sha256()
    for row in path:
        digest.update(
            struct.pack(
                ">4i",
                int(row["elevation"]),
                int(row["tile"]),
                -1 if row["exitSerial"] is None else int(row["exitSerial"]),
                int(row["rotation"]),
            )
        )
    return digest.hexdigest()


def _walk_mask_sha256(walkable: set[int]) -> str:
    digest = hashlib.sha256()
    for tile in sorted(walkable):
        digest.update(struct.pack(">i", tile))
    return digest.hexdigest()


def _top_level(source: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        row
        for elevation in source["map"]["objects"]["elevations"]
        for row in elevation["objects"]
    ]


def _walkable_by_elevation(
    source: dict[str, Any],
    ignored_serials: set[int] | None = None,
) -> dict[int, set[int]]:
    ignored = ignored_serials or set()
    objects = {
        int(row["elevation"]): row["objects"]
        for row in source["map"]["objects"]["elevations"]
    }
    result: dict[int, set[int]] = {}
    for elevation in source["map"]["layout"]["elevations"]:
        index = int(elevation["elevation"])
        floor_ids = [int(value) & 0x0FFF for value in elevation["rawEntries"]]
        blocked = {
            int(row["tile"])
            for row in objects[index]
            if int(row["tile"]) >= 0
            and int(row["serial"]) not in ignored
            and int(row["prototype"]["object_type"]) != WALL_OBJECT_TYPE
            and not int(row["flags"], 16) & NO_BLOCK_FLAG
        }
        result[index] = {
            tile
            for tile in range(MAP_WIDTH * MAP_WIDTH)
            if floor_ids[(tile // MAP_WIDTH // 2) * 100 + 99 - (tile % MAP_WIDTH // 2)] != 1
            and tile not in blocked
        }
    return result


def _intra_map_exits(source: dict[str, Any]) -> dict[tuple[int, int], list[dict[str, int]]]:
    map_index = int(source["map"]["header"]["mapIndex"])
    result: dict[tuple[int, int], list[dict[str, int]]] = {}
    for row in _top_level(source):
        values = row.get("instanceValues", [])
        if (
            int(row["prototype"]["object_type"]) != EXIT_GRID_OBJECT_TYPE
            or len(values) != 4
            or int(values[0]) != map_index
        ):
            continue
        result.setdefault((int(row["elevation"]), int(row["tile"])), []).append(
            {
                "serial": int(row["serial"]),
                "targetTile": int(values[1]),
                "targetElevation": int(values[2]),
                "targetRotation": int(values[3]),
            }
        )
    for rows in result.values():
        rows.sort(key=lambda item: item["serial"])
    return result


def _route(
    start: tuple[int, int],
    is_goal: Callable[[tuple[int, int]], bool],
    walkable: dict[int, set[int]],
    exits: dict[tuple[int, int], list[dict[str, int]]],
) -> list[dict[str, int | None]]:
    parents: dict[tuple[int, int], tuple[int, int] | None] = {start: None}
    arrivals: dict[tuple[int, int], tuple[int | None, int]] = {start: (None, 0)}
    queue = deque([start])
    goal: tuple[int, int] | None = None
    while queue:
        state = queue.popleft()
        if is_goal(state):
            goal = state
            break
        elevation, tile = state
        candidates = [
            ((elevation, neighbor), None, 0)
            for neighbor in _neighbors(tile)
            if neighbor in walkable[elevation]
        ]
        candidates.extend(
            (
                (exit_row["targetElevation"], exit_row["targetTile"]),
                exit_row["serial"],
                exit_row["targetRotation"],
            )
            for exit_row in exits.get(state, [])
            if exit_row["targetTile"] in walkable[exit_row["targetElevation"]]
        )
        for destination, exit_serial, rotation in candidates:
            if destination in parents:
                continue
            parents[destination] = state
            arrivals[destination] = (exit_serial, rotation)
            queue.append(destination)
    if goal is None:
        raise Fo1ProfileError("Fallout 2 trial route has no admitted source path")
    states: list[tuple[int, int]] = []
    cursor: tuple[int, int] | None = goal
    while cursor is not None:
        states.append(cursor)
        cursor = parents[cursor]
    states.reverse()
    path = [
        {
            "elevation": elevation,
            "tile": tile,
            "exitSerial": arrivals[(elevation, tile)][0],
            "rotation": arrivals[(elevation, tile)][1],
        }
        for elevation, tile in states
    ]
    path[0]["rotation"] = 0
    return path


def _require_object(
    source: dict[str, Any],
    rule: dict[str, Any],
    label: str,
) -> dict[str, Any]:
    matches = [row for row in _top_level(source) if int(row["serial"]) == int(rule["serial"])]
    if len(matches) != 1:
        raise Fo1ProfileError(f"Fallout 2 {label} source object is ambiguous")
    row = matches[0]
    if any(
        (
            int(row["tile"]) != int(rule["tile"]),
            int(row["elevation"]) != int(rule["elevation"]),
            row["pid"].casefold() != str(rule["pid"]).casefold(),
            row["prototype"]["sha256"].casefold()
            != str(rule["prototypeSha256"]).casefold(),
        )
    ):
        raise Fo1ProfileError(f"Fallout 2 {label} source identity drifted")
    return row


def compile_fo2_arroyo_trial_route(
    profile_path: Path,
    arroyo_source_path: Path,
    transition_path: Path,
    recipe_path: Path,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    arroyo_source_path = arroyo_source_path.resolve()
    transition_path = transition_path.resolve()
    recipe_path = recipe_path.resolve()
    profile = _load_json(profile_path)
    source = _load_json(arroyo_source_path)
    transitions = _load_json(transition_path)
    recipe = _load_json(recipe_path)
    if (
        source.get("schema") != SOURCE_SCHEMA
        or source.get("campaign") != "Fallout2"
        or source.get("slice") != "ArroyoCaves"
        or transitions.get("schema") != TRANSITION_SCHEMA
        or recipe.get("schema") != "opennv-fo2-arroyo-trial-route-recipe/v1"
    ):
        raise Fo1ProfileError("unexpected Fallout 2 trial-route inputs")
    if (
        source["sourceProfile"]["sourceProfileId"] != profile.get("sourceProfileId")
        or source["sourceProfile"]["sha256"] != file_sha256(profile_path)
        or transitions["sourceProfile"]["sourceProfileId"] != profile.get("sourceProfileId")
        or transitions["sourceProfile"]["sha256"] != file_sha256(profile_path)
    ):
        raise Fo1ProfileError("Fallout 2 trial-route profile binding drifted")

    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    trial = recipe["trialState"]
    cameron_rule = trial["cameron"]
    cameron = _require_object(source, cameron_rule, "Cameron")
    door = _require_object(source, {
        "serial": cameron_rule["release"]["doorSerial"],
        "tile": cameron_rule["release"]["doorTile"],
        "elevation": cameron_rule["release"]["doorElevation"],
        "pid": cameron_rule["release"]["doorPid"],
        "prototypeSha256": cameron_rule["release"]["doorPrototypeSha256"],
    }, "Cameron release door")
    if int(cameron["scriptIndex"]) != int(cameron_rule["program"]["scriptsListIndex"]):
        raise Fo1ProfileError("Fallout 2 Cameron script index drifted")
    if int(door["scriptIndex"]) != int(cameron_rule["release"]["doorScriptIndex"]):
        raise Fo1ProfileError("Fallout 2 Cameron door script index drifted")
    with resolver.access_scope() as accessed:
        door_presentation = decode_classic_door(
            resolver,
            int(door["pid"], PID_RADIX),
            str(door["artFilename"]),
        )
        global_resource = resolver.read(trial["globalCatalog"]["logicalPath"])
        global_text = global_resource.data.decode("cp1252")
        expected_global = trial["globalCatalog"]
        match = re.search(
            rf"^{re.escape(expected_global['name'])}\s*:=\s*(-?\d+)\s*;\s*//\s*\((\d+)\)",
            global_text,
            re.MULTILINE,
        )
        if (
            match is None
            or int(match.group(1)) != int(expected_global["initialValue"])
            or int(match.group(2)) != int(expected_global["index"])
        ):
            raise Fo1ProfileError("Fallout 2 trial global catalog identity drifted")
        program = resolver.read(cameron_rule["program"]["logicalPath"])
        messages_resource = resolver.read(cameron_rule["messageCatalog"]["logicalPath"])
        if (
            program.sha256 != cameron_rule["program"]["sha256"]
            or messages_resource.sha256 != cameron_rule["messageCatalog"]["sha256"]
        ):
            raise Fo1ProfileError("Fallout 2 Cameron owned script identity drifted")
        messages = _parse_dialogue_catalog(messages_resource.data)
        message_ids = set(cameron_rule["taggedSpeechBranch"]["selectedMessageIds"])
        message_ids.update((103, 104, 111, 122, 123, 166, 167, 169))
        if any(not messages.get(message_id, "").strip() for message_id in message_ids):
            raise Fo1ProfileError("Fallout 2 Cameron dialogue text is incomplete")

    walkable = _walkable_by_elevation(source)
    exits = _intra_map_exits(source)
    approach = _route(
        (int(source["incomingPlacement"]["elevation"]), int(source["incomingPlacement"]["tile"])),
        lambda state: state[0] == int(cameron["elevation"])
        and state[1] in _neighbors(int(cameron["tile"])),
        walkable,
        exits,
    )
    released_walkable = _walkable_by_elevation(
        source,
        {int(cameron["serial"]), int(door["serial"])},
    )
    live_exit = source["liveExitTransition"]
    return_path = _route(
        (int(approach[-1]["elevation"]), int(approach[-1]["tile"])),
        lambda state: state == (
            int(live_exit["source"]["elevation"]),
            int(live_exit["source"]["tile"]),
        ),
        released_walkable,
        exits,
    )

    temple_source_path = Path(transitions["sourceManifest"]["file"]).resolve()
    temple_source = _load_json(temple_source_path)
    if transitions["sourceManifest"]["sha256"] != file_sha256(temple_source_path):
        raise Fo1ProfileError("Fallout 2 Temple source binding drifted")
    gate_rule = trial["klintGate"]
    gate = _require_object(temple_source, {
        "serial": gate_rule["gateSerial"],
        "tile": gate_rule["sourceTile"],
        "elevation": gate_rule["elevation"],
        "pid": gate_rule["gatePid"],
        "prototypeSha256": gate_rule["prototypeSha256"],
    }, "Klint obelisk")
    acklint = next(
        row for row in transitions["liveMapScriptRecords"]
        if int(row["objectSerial"]) == int(gate_rule["actorSerial"])
    )
    if (
        acklint["program"]["logicalPath"].casefold()
        != gate_rule["actorProgramLogicalPath"].casefold()
        or acklint["program"]["sha256"] != gate_rule["actorProgramSha256"]
    ):
        raise Fo1ProfileError("Fallout 2 Klint map-enter script identity drifted")
    temple_walkable = _walkable_by_elevation(temple_source, {int(gate["serial"])})[0]
    temple_walkable.discard(int(gate_rule["destinationTile"]))
    map_four_exits = sorted(
        (row for row in transitions["exitGrids"] if int(row["destination"]["mapIndex"]) == 4),
        key=lambda row: int(row["serial"]),
    )
    temple_routes = []
    for exit_row in map_four_exits:
        try:
            path = _route(
                (int(live_exit["destination"]["elevation"]), int(live_exit["destination"]["tile"])),
                lambda state, row=exit_row: state == (int(row["elevation"]), int(row["tile"])),
                {0: temple_walkable},
                {},
            )
        except Fo1ProfileError:
            continue
        temple_routes.append((len(path), int(exit_row["serial"]), exit_row, path))
    if not temple_routes:
        raise Fo1ProfileError("Fallout 2 post-trial obelisk move exposes no ARVILLAG exit")
    _, _, village_exit, village_path = min(temple_routes)
    destination = next(
        row for row in transitions["destinationMaps"]
        if int(row["mapIndex"]) == int(village_exit["destination"]["mapIndex"])
    )
    with resolver.access_scope() as destination_accessed:
        destination_resource = resolver.read(destination["logicalPath"])
        if destination_resource.sha256 != destination["sha256"]:
            raise Fo1ProfileError("Fallout 2 ARVILLAG destination MAP identity drifted")
        destination_layout = parse_map_layout(destination_resource.data)
        _, destination_object_offset = parse_script_section(
            destination_resource.data,
            destination_layout.next_offset,
        )
        destination_objects, destination_end = parse_map_objects(
            destination_resource.data,
            destination_object_offset,
            destination_layout.header.version,
            resolver,
        )
        if destination_end != len(destination_resource.data):
            raise Fo1ProfileError("Fallout 2 ARVILLAG MAP leaves trailing source bytes")
    accessed.update(destination_accessed)
    destination_source = {
        "map": {
            "layout": {
                "elevations": [
                    {
                        "elevation": row.elevation,
                        "rawEntries": list(row.entries),
                    }
                    for row in destination_layout.elevations
                ]
            },
            "objects": destination_objects,
        }
    }
    destination_walkable = _walkable_by_elevation(destination_source)
    arrival_elevation = int(village_exit["destination"]["elevation"])
    arrival_tile = int(village_exit["destination"]["tile"])
    if arrival_tile not in destination_walkable.get(arrival_elevation, set()):
        raise Fo1ProfileError("Fallout 2 ARVILLAG arrival is not source-walkable")
    legal_neighbors = [
        (rotation, tile)
        for rotation, tile in enumerate(_neighbors(arrival_tile))
        if tile in destination_walkable[arrival_elevation]
    ]
    if not legal_neighbors:
        raise Fo1ProfileError("Fallout 2 ARVILLAG arrival has no legal first hex action")
    first_rotation, first_tile = legal_neighbors[0]

    def route_document(path: list[dict[str, int | None]]) -> dict[str, Any]:
        return {
            "steps": path,
            "stepCount": len(path) - 1,
            "sha256": _path_sha256(path),
            "elevationTransitions": [
                row for row in path if row["exitSerial"] is not None
            ],
        }

    return {
        "schema": SCHEMA,
        "status": "compiled-owned-bounded-trial-route",
        "campaign": "Fallout2",
        "sourceProfile": {
            "file": str(profile_path),
            "sourceProfileId": profile["sourceProfileId"],
            "sha256": file_sha256(profile_path),
        },
        "recipe": {
            "file": str(recipe_path),
            "id": recipe["id"],
            "sha256": file_sha256(recipe_path),
        },
        "arroyoSource": {
            "file": str(arroyo_source_path),
            "sha256": file_sha256(arroyo_source_path),
            "mapSha256": source["map"]["sha256"],
        },
        "templeTransitions": {
            "file": str(transition_path),
            "sha256": file_sha256(transition_path),
            "mapSha256": transitions["sourceMap"]["sha256"],
        },
        "globalState": {
            "name": expected_global["name"],
            "index": expected_global["index"],
            "initialValue": expected_global["initialValue"],
            "logicalPath": global_resource.logical_path,
            "source": global_resource.source,
            "sha256": global_resource.sha256,
        },
        "cameron": {
            "serial": cameron["serial"],
            "tile": cameron["tile"],
            "elevation": cameron["elevation"],
            "rotation": cameron["rotation"],
            "fid": cameron["fid"],
            "pid": cameron["pid"],
            "sid": cameron["sid"],
            "scriptIndex": cameron["scriptIndex"],
            "prototypeSha256": cameron["prototype"]["sha256"],
            "program": {
                "logicalPath": program.logical_path,
                "source": program.source,
                "sha256": program.sha256,
            },
            "messageCatalog": {
                "logicalPath": messages_resource.logical_path,
                "source": messages_resource.source,
                "sha256": messages_resource.sha256,
                "messageListId": cameron_rule["messageCatalog"]["messageListId"],
            },
            "taggedSpeechBranch": {
                **cameron_rule["taggedSpeechBranch"],
                "messages": {
                    str(message_id): messages[message_id].strip()
                    for message_id in sorted(message_ids)
                },
            },
            "release": {
                **cameron_rule["release"],
                "doorFid": door["fid"],
                "doorPrototypeSha256": door["prototype"]["sha256"],
                "doorPresentation": door_presentation,
            },
        },
        "movement": {
            **recipe["movement"],
            "walkMasks": [
                {
                    "elevation": elevation,
                    "walkableHexes": len(walkable[elevation]),
                    "sha256": _walk_mask_sha256(walkable[elevation]),
                }
                for elevation in sorted(walkable)
            ],
            "approachCameron": route_document(approach),
            "returnToTempleExit": route_document(return_path),
        },
        "klintGate": {
            **gate_rule,
            "fid": gate["fid"],
            "acklintScriptIndex": acklint["scriptIndex"],
            "postMoveWalkableHexes": len(temple_walkable),
            "postMoveWalkMaskSha256": _walk_mask_sha256(temple_walkable),
        },
        "villageTransition": {
            "path": route_document(village_path),
            "exitSerial": village_exit["serial"],
            "sourceTile": village_exit["tile"],
            "destination": village_exit["destination"],
            "destinationMap": destination,
            "destinationPresentationLoaded": False,
        },
        "villageArrival": {
            "mode": "nonvisual-owned-map-arrival-and-first-hex-action-v1",
            "mapLogicalPath": destination_resource.logical_path,
            "mapSource": destination_resource.source,
            "mapSha256": destination_resource.sha256,
            "mapBytes": len(destination_resource.data),
            "mapIndex": destination_layout.header.mapIndex,
            "elevation": arrival_elevation,
            "arrivalTile": arrival_tile,
            "arrivalRotation": int(village_exit["destination"]["rotation"]),
            "walkableHexes": len(destination_walkable[arrival_elevation]),
            "walkMaskSha256": _walk_mask_sha256(
                destination_walkable[arrival_elevation]
            ),
            "legalNeighborTiles": [tile for _, tile in legal_neighbors],
            "firstLegalAction": {
                "kind": "adjacent-source-walkable-hex-step",
                "fromTile": arrival_tile,
                "toTile": first_tile,
                "rotation": first_rotation,
            },
            "presentationLoaded": False,
        },
        "resources": [
            {
                "logicalPath": resolver.resources[path].logical_path,
                "source": resolver.resources[path].source,
                "bytes": len(resolver.resources[path].data),
                "sha256": resolver.resources[path].sha256,
            }
            for path in sorted(accessed)
        ],
        "unsupported": recipe["unsupported"],
        "retailOrDerivedAssetsPackaged": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--arroyo-source", type=Path, required=True)
    parser.add_argument("--temple-transitions", type=Path, required=True)
    parser.add_argument(
        "--recipe",
        type=Path,
        default=Path(__file__).resolve().parents[1]
        / "recipes"
        / "fo2-arroyo-trial-route-v1.json",
    )
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        document = compile_fo2_arroyo_trial_route(
            args.profile,
            args.arroyo_source,
            args.temple_transitions,
            args.recipe,
        )
        output = args.output.resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_FO2_TRIAL_ROUTE_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_TRIAL_ROUTE "
        + json.dumps(
            {
                "output": str(output),
                "approachSteps": document["movement"]["approachCameron"]["stepCount"],
                "returnSteps": document["movement"]["returnToTempleExit"]["stepCount"],
                "villageSteps": document["villageTransition"]["path"]["stepCount"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
