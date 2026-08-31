"""Compile source-ordered classic MAP initialization INT identities."""

from __future__ import annotations

from typing import Any

from classic_int_effects import inventory_int_program
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError


SCHEMA = "opennv-classic-map-int-initialization/v1"


def _script_entries(data: bytes) -> list[str]:
    try:
        lines = data.decode("cp1252").replace("\r\n", "\n").replace("\r", "\n").split("\n")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("classic scripts.lst is not cp1252") from error
    entries = []
    for line in lines:
        program = line.split(";", 1)[0].strip()
        if program:
            entries.append(program)
    if not entries:
        raise Fo1ProfileError("classic scripts.lst has no program entries")
    return entries


def compile_map_int_initialization(
    header: dict[str, Any],
    script_lists: list[dict[str, Any]],
    resolver: Fo1ResourceResolver,
) -> dict[str, Any]:
    scripts_list = resolver.read("scripts\\scripts.lst")
    entries = _script_entries(scripts_list.data)
    stored_header_index = int(header["scriptIndex"])
    header_index = stored_header_index - 1
    if not 0 <= header_index < len(entries):
        raise Fo1ProfileError(
            f"classic MAP header script index is outside scripts.lst: {stored_header_index}"
        )

    program_cache: dict[int, dict[str, Any]] = {}

    def program(index: int) -> dict[str, Any]:
        if not 0 <= index < len(entries):
            raise Fo1ProfileError(f"classic MAP script index is outside scripts.lst: {index}")
        cached = program_cache.get(index)
        if cached is not None:
            return cached
        resource = resolver.read(f"scripts\\{entries[index]}")
        cached = {
            "scriptsListIndex": index,
            "program": entries[index],
            "logicalPath": resource.logical_path,
            "source": resource.source,
            "bytes": len(resource.data),
            "sha256": resource.sha256,
            "inventory": inventory_int_program(resource.data),
        }
        program_cache[index] = cached
        return cached

    ordered_slots = []
    for script_list in script_lists:
        live = 0
        for extent in script_list["extents"]:
            length = int(extent["length"])
            slots = extent["slots"]
            if not 0 <= length <= len(slots):
                raise Fo1ProfileError("classic MAP script extent length drifted")
            for slot in slots[:length]:
                script_index = int(slot["scriptIndex"])
                ordered_slots.append(
                    {
                        "order": len(ordered_slots),
                        "type": int(script_list["type"]),
                        "extent": int(extent["index"]),
                        "slot": int(slot["slot"]),
                        "sourceOffset": int(slot["sourceOffset"]),
                        "sid": str(slot["sid"]),
                        "objectId": (
                            None if slot["objectId"] is None else int(slot["objectId"])
                        ),
                        "scriptIndex": script_index,
                        "program": program(script_index),
                    }
                )
            live += length
        if live != int(script_list["liveCount"]):
            raise Fo1ProfileError("classic MAP script live count drifted")

    header_program = program(header_index)
    random_sites = [
        {
            "owner": "map-header",
            "sid": None,
            "program": header_program["program"],
            **site,
        }
        for site in header_program["inventory"]["randomSites"]
    ]
    for slot in ordered_slots:
        random_sites.extend(
            {
                "owner": "live-map-script-slot",
                "sid": slot["sid"],
                "program": slot["program"]["program"],
                **site,
            }
            for site in slot["program"]["inventory"]["randomSites"]
        )
    return {
        "schema": SCHEMA,
        "scriptsList": {
            "logicalPath": scripts_list.logical_path,
            "source": scripts_list.source,
            "bytes": len(scripts_list.data),
            "sha256": scripts_list.sha256,
        },
        "mapHeader": {
            "storedScriptIndex": stored_header_index,
            "indexSemantics": "MAP-header-one-based-to-scripts-list",
            "program": header_program,
        },
        "liveScriptSlots": ordered_slots,
        "randomSites": random_sites,
        "engineInterleavingTransported": False,
    }
