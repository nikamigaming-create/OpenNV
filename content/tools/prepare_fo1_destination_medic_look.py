#!/usr/bin/env python3
"""Compile the first reachable VAULT13 Medic look-at interaction from owned MAP/script/message data."""

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

from classic_ssl_effects import (
    decode_single_message_look,
    decode_single_reply_option_dialogue,
)
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError, sha256_path
from prepare_fo1_destination_generic_door import (
    EXIT_SCHEMA, MAP_HEIGHT, MAP_WIDTH, PRESENTATION_SCHEMA, TRANSPORT_SCHEMA, neighbors,
)


SCHEMA = "opennv-fo1-destination-medic-look/v1"
GENERIC_DOOR_SCHEMA = "opennv-fo1-destination-generic-door/v1"
MAP_PRESENTATION_SCHEMA = "opennv-fo1-campaign-map-presentation/v1"
FLOOR_WIDTH = MAP_WIDTH // 2
MEDIC_SYMBOL = "SCRIPT_MEDIC"
SCRIPT_DEFINE = re.compile(r"^\s*#define\s+(SCRIPT_[A-Z0-9_]+)\s+\(\s*(\d+)\s*\)", re.MULTILINE)
MESSAGE_ROW = re.compile(r"^\s*\{\s*(\d+)\s*\}\s*\{[^}]*\}\s*\{(?P<text>.*)\}\s*$")


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def read_script_id(header: Path) -> int:
    values = {symbol: int(value) for symbol, value in SCRIPT_DEFINE.findall(header.read_text(encoding="cp1252"))}
    if MEDIC_SYMBOL not in values:
        raise Fo1ProfileError("source scripts header does not define SCRIPT_MEDIC")
    return values[MEDIC_SYMBOL]


def read_look_message(
    script_path: Path,
    message_path: Path,
) -> tuple[int, str, dict[str, Any], dict[int, str]]:
    script = script_path.read_text(encoding="cp1252")
    if not re.search(rf"#define\s+NAME\s+{MEDIC_SYMBOL}\b", script):
        raise Fo1ProfileError("Medic script does not bind NAME to SCRIPT_MEDIC")
    effect_program = decode_single_message_look(script)
    message_id = effect_program["events"]["look_at_p_proc"][0]["then"][1]["messageId"]
    messages: dict[int, str] = {}
    for line in message_path.read_text(encoding="cp1252").splitlines():
        row = MESSAGE_ROW.match(line)
        if row is not None:
            messages[int(row.group(1))] = row.group("text")
    if message_id not in messages or not messages[message_id]:
        raise Fo1ProfileError("Medic source message file does not contain the look-at message")
    dialogue = decode_single_reply_option_dialogue(script, "MedicSeriouslyWounded")
    effect_program["events"].update(dialogue["events"])
    return message_id, messages[message_id], effect_program, messages


def shortest_contact_path(start: int, target: int, floor_ids: list[int], default_tile: int,
                          blockers: set[int]) -> list[int] | None:
    def walkable(tile: int) -> bool:
        column, row = tile % MAP_WIDTH, tile // MAP_WIDTH
        floor_index = (row // 2) * FLOOR_WIDTH + (FLOOR_WIDTH - 1 - column // 2)
        return floor_ids[floor_index] != default_tile and tile not in blockers

    if not walkable(start):
        raise Fo1ProfileError("opened generic-door tile is not walkable in the presentation map")
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


def build(transport_path: Path, presentation_path: Path, transition_path: Path, generic_door_path: Path,
          scripts_header: Path, medic_script: Path, medic_message: Path, ettu_root: Path,
          fallout2_master: Path, fallout2_critter: Path, output_path: Path) -> dict[str, Any]:
    if output_path.exists():
        raise Fo1ProfileError(f"refusing to overwrite destination Medic look descriptor: {output_path}")
    transport, presentation, transition, generic_door = (
        read_json(transport_path), read_json(presentation_path), read_json(transition_path), read_json(generic_door_path))
    if transport.get("schema") != TRANSPORT_SCHEMA or presentation.get("schema") != PRESENTATION_SCHEMA:
        raise Fo1ProfileError("unexpected source MAP transport or destination presentation schema")
    if transition.get("schema") != EXIT_SCHEMA or generic_door.get("schema") != GENERIC_DOOR_SCHEMA:
        raise Fo1ProfileError("unexpected exit-grid or generic-door descriptor")
    if generic_door.get("status") != "compiled-owned-map-unscripted-generic-door-open-passability":
        raise Fo1ProfileError("generic-door descriptor does not admit opened MAP passability")
    source_map, destination = transport["source"]["map"], transition["destination"]
    if source_map["sha256"] != destination["mapSha256"] or source_map["file"] != destination["name"]:
        raise Fo1ProfileError("Medic look transport/exit-grid source join drifted")
    generic_destination = generic_door.get("destination", {})
    if (generic_destination.get("sourceMapSha256") != source_map["sha256"] or
            generic_destination.get("elevation") != destination["elevation"]):
        raise Fo1ProfileError("Medic look generic-door MAP join drifted")
    maps = presentation.get("maps", [])
    map_id = Path(destination["name"]).stem.lower()
    if len(maps) != 1 or maps[0].get("id") != map_id:
        raise Fo1ProfileError("Medic look presentation does not uniquely name the transition map")
    map_path = (presentation_path.parent / maps[0]["path"]).resolve()
    map_document = read_json(map_path)
    if (map_document.get("schema") != MAP_PRESENTATION_SCHEMA or
            map_document.get("source", {}).get("mapSha256") != source_map["sha256"] or
            sha256_path(map_path) != maps[0]["sha256"]):
        raise Fo1ProfileError("Medic look presentation map hash join drifted")
    elevation = next((row for row in map_document["elevations"] if row["elevation"] == destination["elevation"]), None)
    source_rows = next((row["objects"] for row in transport["objectGraph"]["objects"]["elevations"] if row["elevation"] == destination["elevation"]), None)
    if elevation is None or source_rows is None:
        raise Fo1ProfileError("Medic look destination elevation is absent from source/presentation")
    script_id = read_script_id(scripts_header)
    message_id, message_text, effect_program, messages = read_look_message(
        medic_script, medic_message
    )
    dialogue_actions = effect_program["events"]["MedicSeriouslyWounded"][0]["then"]
    reply_action, option_action = dialogue_actions
    if not messages.get(reply_action["messageId"]) or not messages.get(option_action["messageId"]):
        raise Fo1ProfileError("Medic dialogue source messages are unavailable")
    door = generic_door["door"]
    if not door.get("open", {}).get("walkable"):
        raise Fo1ProfileError("generic-door descriptor does not mark its opened tile walkable")
    blockers = {row["tile"] for row in elevation["blockers"]}
    if door["tile"] not in blockers:
        raise Fo1ProfileError("generic-door tile is not a source presentation blocker")
    blockers.remove(door["tile"])
    candidates: list[tuple[list[int], dict[str, Any]]] = []
    for actor in source_rows:
        if actor.get("scriptIndex") != script_id or actor.get("tile", -1) < 0 or actor.get("prototype", {}).get("object_type") != 1:
            continue
        route = shortest_contact_path(door["tile"], actor["tile"], elevation["floorIds"], map_document["grid"]["defaultTileId"], blockers)
        if route is not None:
            candidates.append((route, actor))
    if not candidates:
        raise Fo1ProfileError("opened generic-door route has no reachable SCRIPT_MEDIC actor")
    route, actor = min(candidates, key=lambda row: (len(row[0]), row[1]["tile"], row[1]["serial"]))
    resolver = Fo1ResourceResolver(ettu_root, fallout2_master, [fallout2_critter])
    prototype_path = f"proto\\critters\\{actor['prototype']['filename']}"
    prototype = resolver.read(prototype_path)
    if prototype.sha256 != actor["prototype"]["sha256"]:
        raise Fo1ProfileError("Medic owned PRO bytes do not match the MAP prototype hash")
    art_path = resolver.placed_idle_frm_path(int(actor["fid"], 16))
    art = resolver.read(art_path)
    document = {
        "schema": SCHEMA,
        "status": "compiled-owned-map-scripted-medic-look-at",
        "selection": {"policy": "nearest-reachable-script-medic-actor-after-opened-generic-door-v1", "candidateCount": len(candidates)},
        "inputs": {
            "transport": {"path": str(transport_path.resolve()), "sha256": sha256_path(transport_path)},
            "presentation": {"path": str(presentation_path.resolve()), "sha256": sha256_path(presentation_path)},
            "presentationMap": {"path": str(map_path), "sha256": sha256_path(map_path)},
            "exitGridTransition": {"path": str(transition_path.resolve()), "sha256": sha256_path(transition_path)},
            "genericDoor": {"path": str(generic_door_path.resolve()), "sha256": sha256_path(generic_door_path)},
            "scriptsHeader": {"path": str(scripts_header.resolve()), "sha256": sha256_path(scripts_header)},
            "medicScript": {"path": str(medic_script.resolve()), "sha256": sha256_path(medic_script)},
            "medicMessage": {"path": str(medic_message.resolve()), "sha256": sha256_path(medic_message)},
        },
        "destination": {"mapId": map_id, "sourceFile": source_map["file"], "sourceMapSha256": source_map["sha256"], "elevation": destination["elevation"], "entryTile": destination["tile"]},
        "actor": {
            "serial": actor["serial"], "objectId": actor["id"], "tile": actor["tile"], "pid": actor["pid"], "fid": actor["fid"], "sid": actor["sid"], "scriptIndex": actor["scriptIndex"], "sourceOffset": actor["sourceOffset"],
            "prototype": {"logicalPath": prototype_path, "source": prototype.source, "sha256": prototype.sha256},
            "art": {"logicalPath": art_path, "source": art.source, "sha256": art.sha256, "mapArtFilename": actor["artFilename"]},
        },
        "semantics": {"procedure": "look_at_p_proc", "messageId": message_id, "messageText": message_text,
                      "result": "display-message-only", "dialogue": "unimplemented-fail-closed",
                      "combat": "not-proven-by-look-at-only", "actionPoints": "not-source-backed"},
        "effectProgram": effect_program,
        "dialogueResult": {
            "procedure": "MedicSeriouslyWounded",
            "reply": {
                "messageId": reply_action["messageId"],
                "messageText": messages[reply_action["messageId"]],
            },
            "option": {
                "messageId": option_action["messageId"],
                "messageText": messages[option_action["messageId"]],
                "target": option_action["target"],
                "reaction": option_action["reaction"],
            },
            "optionSelection": "unimplemented-fail-closed",
        },
        "sourceWalkMaskRoute": {"pathTiles": route, "contactTile": route[-1], "contactIsAdjacent": route[-1] in neighbors(actor["tile"])},
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
    return {"path": str(output_path.resolve()), "sha256": hashlib.sha256(encoded).hexdigest(), "actorSerial": actor["serial"]}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--transport", type=Path, required=True)
    parser.add_argument("--presentation", type=Path, required=True)
    parser.add_argument("--exit-grid-transition", type=Path, required=True)
    parser.add_argument("--generic-door", type=Path, required=True)
    parser.add_argument("--scripts-header", type=Path, required=True)
    parser.add_argument("--medic-script", type=Path, required=True)
    parser.add_argument("--medic-message", type=Path, required=True)
    parser.add_argument("--ettu-root", type=Path, required=True)
    parser.add_argument("--fallout2-master", type=Path, required=True)
    parser.add_argument("--fallout2-critter", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = build(*[getattr(args, name).resolve() for name in (
            "transport", "presentation", "exit_grid_transition", "generic_door", "scripts_header", "medic_script",
            "medic_message", "ettu_root", "fallout2_master", "fallout2_critter", "output")])
    except Exception as error:
        print(f"OPENNV_FO1_DESTINATION_MEDIC_LOOK_ERROR {error}")
        return 2
    print("OPENNV_FO1_DESTINATION_MEDIC_LOOK " + json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
