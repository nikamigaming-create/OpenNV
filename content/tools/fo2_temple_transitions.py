#!/usr/bin/env python3
"""Compile asset-free Fallout 2 Temple exit and script identities from owned data."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any

from corpus_io import atomic_json
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError, parse_map_layout
from fo2_first_slice import _archive_paths, _load_json, _load_recipe
from plugin_stack import file_sha256


SCHEMA = "opennv-fo2-temple-transitions/v1"
SOURCE_SCHEMA = "opennv-fo2-first-slice/v2"
MAP_WIDTH = 200
NO_BLOCK_FLAG = 0x10
EXIT_GRID_OBJECT_TYPE = 5
DOOR_OBJECT_TYPE = 2
DOOR_SUBTYPE = 0


def _script_entries(data: bytes) -> list[str]:
    try:
        lines = data.decode("cp1252").replace("\r\n", "\n").replace("\r", "\n").split("\n")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("Fallout 2 scripts.lst is not cp1252") from error
    entries = []
    for line in lines:
        program = line.split(";", 1)[0].strip()
        if program:
            entries.append(program)
    if not entries:
        raise Fo1ProfileError("Fallout 2 scripts.lst has no program entries")
    return entries


def _maps_sections(data: bytes) -> dict[int, dict[str, str]]:
    try:
        lines = data.decode("cp1252").replace("\r\n", "\n").replace("\r", "\n").split("\n")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("Fallout 2 maps.txt is not cp1252") from error
    sections: dict[int, dict[str, str]] = {}
    active: dict[str, str] | None = None
    for raw in lines:
        line = raw.strip()
        if not line or line.startswith(("#", ";")):
            continue
        if line.startswith("[") and line.endswith("]"):
            label = line[1:-1].strip().split()
            active = None
            if len(label) == 2 and label[0].casefold() == "map" and label[1].isdigit():
                index = int(label[1])
                if index in sections:
                    raise Fo1ProfileError(f"duplicate Fallout 2 maps.txt section: {index}")
                active = sections.setdefault(index, {})
            continue
        if active is not None and "=" in line:
            key, value = line.split("=", 1)
            key = key.strip().casefold()
            if key in active:
                raise Fo1ProfileError(f"duplicate Fallout 2 maps.txt key: {key}")
            active[key] = value.split(";", 1)[0].strip()
    return sections


def _program_identity(
    resolver: Fo1ResourceResolver,
    entries: list[str],
    list_index: int,
    index_semantics: str,
) -> dict[str, Any]:
    if not 0 <= list_index < len(entries):
        raise Fo1ProfileError(f"Fallout 2 script index is outside scripts.lst: {list_index}")
    name = entries[list_index]
    resource = resolver.read(f"scripts\\{name}")
    return {
        "scriptsListIndex": list_index,
        "indexSemantics": index_semantics,
        "program": name,
        "logicalPath": resource.logical_path,
        "source": resource.source,
        "bytes": len(resource.data),
        "sha256": resource.sha256,
    }


def _live_map_script_records(script_lists: list[dict[str, Any]]) -> list[dict[str, Any]]:
    records = []
    for script_list in script_lists:
        total = 0
        for extent in script_list["extents"]:
            length = int(extent["length"])
            slots = extent["slots"]
            if not 0 <= length <= len(slots):
                raise Fo1ProfileError("Fallout 2 MAP script extent length drifted")
            for slot in slots[:length]:
                records.append(
                    {
                        "type": int(script_list["type"]),
                        "extent": int(extent["index"]),
                        "slot": int(slot["slot"]),
                        "sid": str(slot["sid"]),
                        "bytes": int(slot["bytes"]),
                    }
                )
            total += length
        if total != int(script_list["liveCount"]):
            raise Fo1ProfileError("Fallout 2 MAP script live-count drifted")
    return records


def _script_records_sha256(records: list[dict[str, Any]]) -> str:
    lines = []
    for record in records:
        lines.append(
            "|".join(
                str(value)
                for value in (
                    record["type"],
                    record["extent"],
                    record["slot"],
                    record["sid"],
                    record["bytes"],
                    record["objectSerial"],
                    record["objectTile"],
                    record["scriptIndex"],
                    record["program"]["sha256"],
                )
            )
        )
    return hashlib.sha256("\n".join(lines).encode("ascii")).hexdigest()


def compile_fo2_temple_transitions(
    profile_path: Path,
    source_manifest_path: Path,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    source_manifest_path = source_manifest_path.resolve()
    profile = _load_json(profile_path)
    source = _load_json(source_manifest_path)
    if (
        source.get("schema") != SOURCE_SCHEMA
        or source.get("status") != "transported-source-manifest"
        or source.get("campaign") != "Fallout2"
        or source.get("slice") != "TempleOfTrials"
        or source.get("runtimeCompatibility", {}).get("ready") is not False
        or source.get("retailOrDerivedAssetsPackaged") is not False
    ):
        raise Fo1ProfileError("unexpected Fallout 2 Temple source manifest")
    source_profile = source.get("sourceProfile", {})
    if (
        Path(str(source_profile.get("file", ""))).resolve() != profile_path
        or source_profile.get("sha256") != file_sha256(profile_path)
        or source_profile.get("sourceProfileId") != profile.get("sourceProfileId")
    ):
        raise Fo1ProfileError("Fallout 2 Temple source/profile binding drifted")
    recipe_path = Path(str(source.get("recipe", {}).get("file", ""))).resolve()
    recipe = _load_recipe(recipe_path)
    if (
        source["recipe"].get("id") != recipe.get("id")
        or source["recipe"].get("sha256") != file_sha256(recipe_path)
    ):
        raise Fo1ProfileError("Fallout 2 Temple recipe binding drifted")
    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])

    map_document = source["map"]
    if map_document["header"]["mapIndex"] != 126 or map_document["header"]["name"] != "ARTEMPLE.MAP":
        raise Fo1ProfileError("Fallout 2 Temple MAP identity drifted")
    objects = map_document["objects"]["elevations"][0]["objects"]
    doors = [
        obj
        for obj in objects
        if obj["prototype"]["object_type"] == DOOR_OBJECT_TYPE
        and obj["prototype"].get("subtype") == DOOR_SUBTYPE
    ]
    exit_objects = [
        obj
        for obj in objects
        if obj["prototype"]["object_type"] == EXIT_GRID_OBJECT_TYPE
        and len(obj.get("instanceValues", [])) == 4
    ]
    if not exit_objects:
        raise Fo1ProfileError("Fallout 2 Temple has no source exit-grid records")

    with resolver.access_scope() as accessed:
        scripts_list = resolver.read("scripts\\scripts.lst")
        entries = _script_entries(scripts_list.data)
        stored_map_script_index = int(map_document["header"]["scriptIndex"])
        header_program = _program_identity(
            resolver,
            entries,
            stored_map_script_index - 1,
            "MAP-header-one-based-to-scripts-list",
        )
        if Path(header_program["program"]).stem.casefold() != source["mapRegistry"]["values"]["map_name"].casefold():
            raise Fo1ProfileError("Fallout 2 Temple header script does not resolve to ARTemple.int")

        live_records = _live_map_script_records(map_document["scriptLists"])
        scripted_objects = [obj for obj in objects if int(obj["scriptIndex"]) >= 0]
        by_sid = {obj["sid"]: obj for obj in scripted_objects}
        if len(by_sid) != len(scripted_objects) or set(by_sid) != {row["sid"] for row in live_records}:
            raise Fo1ProfileError("Fallout 2 Temple live MAP scripts do not join to source objects")
        object_programs: dict[int, dict[str, Any]] = {}
        for obj in scripted_objects:
            index = int(obj["scriptIndex"])
            object_programs.setdefault(
                index,
                _program_identity(resolver, entries, index, "MAP-object-direct-scripts-list-index"),
            )
        script_records = []
        for record in live_records:
            obj = by_sid[record["sid"]]
            script_records.append(
                {
                    **record,
                    "objectSerial": int(obj["serial"]),
                    "objectTile": int(obj["tile"]),
                    "scriptIndex": int(obj["scriptIndex"]),
                    "program": object_programs[int(obj["scriptIndex"])],
                }
            )

        maps_resource = resolver.read("data\\maps.txt")
        map_sections = _maps_sections(maps_resource.data)
        destination_maps: dict[int, dict[str, Any]] = {}
        exits = []
        for obj in sorted(exit_objects, key=lambda row: int(row["serial"])):
            target_map, target_tile, target_elevation, target_rotation = map(
                int, obj["instanceValues"]
            )
            if (
                target_map < 0
                or not 0 <= target_tile < MAP_WIDTH * MAP_WIDTH
                or not 0 <= target_elevation <= 2
                or not 0 <= target_rotation <= 5
            ):
                raise Fo1ProfileError(f"Fallout 2 Temple exit-grid target is invalid: {obj['serial']}")
            section = map_sections.get(target_map)
            if not section or not section.get("map_name") or not section.get("lookup_name"):
                raise Fo1ProfileError(f"Fallout 2 destination map registry is incomplete: {target_map}")
            if target_map not in destination_maps:
                resource = resolver.read(f"maps\\{section['map_name']}.map")
                layout = parse_map_layout(resource.data)
                if layout.header.mapIndex != target_map or target_elevation not in {
                    elevation.elevation for elevation in layout.elevations
                }:
                    raise Fo1ProfileError(f"Fallout 2 destination MAP header drifted: {target_map}")
                destination_maps[target_map] = {
                    "mapIndex": target_map,
                    "lookupName": section["lookup_name"],
                    "mapName": section["map_name"],
                    "logicalPath": resource.logical_path,
                    "source": resource.source,
                    "bytes": len(resource.data),
                    "sha256": resource.sha256,
                    "header": {
                        "version": layout.header.version,
                        "name": layout.header.name,
                        "mapIndex": layout.header.mapIndex,
                    },
                    "presentElevations": [row.elevation for row in layout.elevations],
                }
            exits.append(
                {
                    "serial": int(obj["serial"]),
                    "objectId": int(obj["id"]),
                    "tile": int(obj["tile"]),
                    "elevation": int(obj["elevation"]),
                    "flags": obj["flags"],
                    "fid": obj["fid"],
                    "pid": obj["pid"],
                    "artFilename": obj["artFilename"],
                    "sourceBlocking": not (int(obj["flags"], 16) & NO_BLOCK_FLAG),
                    "destination": {
                        "mapIndex": target_map,
                        "tile": target_tile,
                        "elevation": target_elevation,
                        "rotation": target_rotation,
                    },
                }
            )

    door_records = [
        {
            "serial": int(obj["serial"]),
            "tile": int(obj["tile"]),
            "sid": obj["sid"],
            "scriptIndex": int(obj["scriptIndex"]),
            "instanceValues": obj["instanceValues"],
        }
        for obj in doors
    ]
    return {
        "schema": SCHEMA,
        "status": "compiled-owned-transition-records",
        "campaign": "Fallout2",
        "slice": "TempleOfTrials",
        "sourceManifest": {
            "file": str(source_manifest_path),
            "sha256": file_sha256(source_manifest_path),
        },
        "sourceProfile": {
            "file": str(profile_path),
            "sourceProfileId": profile["sourceProfileId"],
            "sha256": file_sha256(profile_path),
        },
        "sourceMap": {
            "mapIndex": 126,
            "logicalPath": map_document["logicalPath"],
            "sha256": map_document["sha256"],
        },
        "scriptsList": {
            "logicalPath": scripts_list.logical_path,
            "source": scripts_list.source,
            "bytes": len(scripts_list.data),
            "sha256": scripts_list.sha256,
            "entries": len(entries),
        },
        "headerMapProgram": {
            "storedScriptIndex": stored_map_script_index,
            **header_program,
            "executionImplemented": False,
        },
        "liveMapScriptRecords": script_records,
        "liveMapScriptRecordsSha256": _script_records_sha256(script_records),
        "doors": {
            "sourceObjects": door_records,
            "count": len(door_records),
            "runtimeImplemented": False,
        },
        "exitGrids": exits,
        "destinationMaps": [destination_maps[index] for index in sorted(destination_maps)],
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
            "exitGridTransition": "source-instance-values-only",
            "headerMapProgramExecution": False,
            "objectProgramExecution": False,
            "doorTransition": False,
            "runtimeReady": False,
        },
        "unsupported": [
            "INT bytecode execution and inferred script behavior",
            "doors because Map 126 contains no source door-prototype objects",
            "destination MAP rendering, actors, character state, gameplay, and save state",
            "multihex footprint expansion, retail parity, FPS, and OpenXR",
        ],
        "retailOrDerivedAssetsPackaged": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Compile asset-free Fallout 2 Temple exit/script transition records."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--source-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        document = compile_fo2_temple_transitions(args.profile, args.source_manifest)
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_FO2_TEMPLE_TRANSITIONS_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_TEMPLE_TRANSITIONS "
        + json.dumps(
            {
                "manifest": str(output),
                "exitGrids": len(document["exitGrids"]),
                "doorObjects": document["doors"]["count"],
                "destinationMaps": len(document["destinationMaps"]),
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
