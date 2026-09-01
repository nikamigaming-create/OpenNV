#!/usr/bin/env python3
"""Compile the asset-free owned Fallout 2 Arroyo village MAP/FRM graph."""

from __future__ import annotations

import argparse
from dataclasses import asdict
import json
import re
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
from fo2_arroyo_trial_route import (
    _parse_dialogue_catalog,
    _walk_mask_sha256,
    _walkable_by_elevation,
)
from fo2_first_slice import (
    FORM_ID_RADIX,
    FRM_PALETTE_SIZE,
    _archive_paths,
    _flatten_objects,
    _frm_structure,
    _load_json,
)
from plugin_stack import file_sha256


SCHEMA = "opennv-fo2-owned-map-slice/v1"
ROUTE_SCHEMA = "opennv-fo2-arroyo-trial-route/v1"
MAP_INDEX = 4
MAP_NAME = "ARVILLAG.MAP"
MAP_LOGICAL_PATH = "maps\\arvillag.map"


def compile_fo2_arvillag_slice(
    profile_path: Path,
    route_path: Path,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    route_path = route_path.resolve()
    profile = _load_json(profile_path)
    route = _load_json(route_path)
    if (
        route.get("schema") != ROUTE_SCHEMA
        or route.get("status") != "compiled-owned-bounded-trial-route"
        or route.get("campaign") != "Fallout2"
        or route.get("retailOrDerivedAssetsPackaged") is not False
    ):
        raise Fo1ProfileError("unexpected Fallout 2 ARVILLAG route contract")
    source_profile = route.get("sourceProfile", {})
    if (
        Path(str(source_profile.get("file", ""))).resolve() != profile_path
        or source_profile.get("sha256") != file_sha256(profile_path)
        or source_profile.get("sourceProfileId") != profile.get("sourceProfileId")
    ):
        raise Fo1ProfileError("Fallout 2 ARVILLAG profile binding drifted")
    recipe_path = Path(str(route.get("recipe", {}).get("file", ""))).resolve()
    recipe = _load_json(recipe_path)
    if (
        route["recipe"].get("sha256") != file_sha256(recipe_path)
        or recipe.get("schema") != "opennv-fo2-arroyo-trial-route-recipe/v1"
        or recipe.get("overlayOrderHighToLow")
        != ["patch000.dat", "critter.dat", "master.dat"]
    ):
        raise Fo1ProfileError("Fallout 2 ARVILLAG overlay recipe drifted")
    arrival = route.get("villageArrival", {})
    if (
        int(arrival.get("mapIndex", -1)) != MAP_INDEX
        or str(arrival.get("mapLogicalPath", "")).casefold()
        != MAP_LOGICAL_PATH.casefold()
        or int(arrival.get("elevation", -1)) != 0
        or arrival.get("presentationLoaded") is not False
    ):
        raise Fo1ProfileError("Fallout 2 ARVILLAG arrival contract drifted")

    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    with resolver.access_scope() as accessed:
        map_resource = resolver.read(MAP_LOGICAL_PATH)
        if (
            map_resource.sha256 != arrival.get("mapSha256")
            or len(map_resource.data) != int(arrival.get("mapBytes", -1))
        ):
            raise Fo1ProfileError("Fallout 2 ARVILLAG MAP identity drifted")
        layout = parse_map_layout(map_resource.data)
        if (
            layout.header.mapIndex != MAP_INDEX
            or layout.header.name != MAP_NAME
            or int(arrival["elevation"])
            not in {row.elevation for row in layout.elevations}
        ):
            raise Fo1ProfileError("Fallout 2 ARVILLAG MAP header drifted")
        scripts, objects_offset = parse_script_section(
            map_resource.data,
            layout.next_offset,
        )
        objects, end_offset = parse_map_objects(
            map_resource.data,
            objects_offset,
            layout.header.version,
            resolver,
        )
        if end_offset != len(map_resource.data):
            raise Fo1ProfileError("Fallout 2 ARVILLAG object graph has trailing bytes")
        initialization_scripts = compile_map_int_initialization(
            asdict(layout.header), scripts, resolver
        )

        flat_objects = _flatten_objects(objects)
        programs = [
            initialization_scripts["mapHeader"]["program"],
            *(
                row["program"]
                for row in initialization_scripts["liveScriptSlots"]
            ),
        ]
        configured_roles = recipe.get("villageIntRoles", {})
        if set(configured_roles) != {"elder", "firstSpeakingNpc"}:
            raise Fo1ProfileError("Fallout 2 ARVILLAG INT roles are incomplete")
        village_int_roles: dict[str, dict[str, Any]] = {}
        configured_metarules = recipe.get("villageIntMetarules", {})
        global_resource = resolver.read(
            recipe["trialState"]["globalCatalog"]["logicalPath"]
        )
        global_rows = {
            int(match.group("index")): {
                "name": match.group("name"),
                "initialValue": int(match.group("value")),
            }
            for match in re.finditer(
                r"^(?P<name>[A-Za-z0-9_]+)\s*:=\s*(?P<value>-?[0-9]+)\s*;"
                r"\s*//\s*\((?P<index>[0-9]+)\)",
                global_resource.data.decode("cp1252"),
                re.MULTILINE,
            )
        }
        if not global_rows:
            raise Fo1ProfileError("Fallout 2 ARVILLAG global catalog is empty")
        for role, configured_path in configured_roles.items():
            matches = [
                program
                for program in programs
                if program is not None
                and str(program["logicalPath"]).casefold()
                == str(configured_path).casefold()
            ]
            unique_programs = {
                (program["logicalPath"], program["sha256"]): program
                for program in matches
            }
            if len(unique_programs) != 1:
                raise Fo1ProfileError(
                    f"Fallout 2 ARVILLAG {role} INT identity is ambiguous"
                )
            program = next(iter(unique_programs.values()))
            actor_matches = [
                row
                for row in flat_objects
                if int(row["scriptIndex"]) == int(program["scriptsListIndex"])
            ]
            if len(actor_matches) != 1:
                raise Fo1ProfileError(
                    f"Fallout 2 ARVILLAG {role} actor identity is ambiguous"
                )
            actor = actor_matches[0]
            message_list_ids: set[int] = set()
            for procedure in program["inventory"]["procedures"]:
                if procedure["name"] not in (
                    "look_at_p_proc",
                    "description_p_proc",
                ):
                    continue
                instructions = procedure["instructions"]
                for index, instruction in enumerate(instructions):
                    if instruction["opcode"] != "8105" or index < 2:
                        continue
                    message_list = instructions[index - 2]
                    message_id = instructions[index - 1]
                    if (
                        message_list["opcode"] != "c001"
                        or message_id["opcode"] != "c001"
                    ):
                        continue
                    message_list_ids.add(int(message_list["operand"]))
            if len(message_list_ids) != 1:
                raise Fo1ProfileError(
                    f"Fallout 2 ARVILLAG {role} message-list identity is ambiguous"
                )
            message_path = (
                "text\\english\\dialog\\"
                + Path(str(program["program"])).stem.casefold()
                + ".msg"
            )
            message_resource = resolver.read(message_path)
            messages = _parse_dialogue_catalog(message_resource.data)
            map_enter = next(
                procedure
                for procedure in program["inventory"]["procedures"]
                if procedure["name"] == "map_enter_p_proc"
            )
            referenced_globals = {
                int(map_enter["instructions"][index - 1]["operand"])
                for index, instruction in enumerate(map_enter["instructions"])
                if instruction["opcode"] == "80c5"
                and index > 0
                and map_enter["instructions"][index - 1]["opcode"] == "c001"
            }
            if any(index not in global_rows for index in referenced_globals):
                raise Fo1ProfileError(
                    f"Fallout 2 ARVILLAG {role} global source join is incomplete"
                )
            referenced_metarules = {
                (
                    int(map_enter["instructions"][index - 2]["operand"]),
                    int(map_enter["instructions"][index - 1]["operand"]),
                )
                for index, instruction in enumerate(map_enter["instructions"])
                if instruction["opcode"] == "810b"
                and index > 1
                and map_enter["instructions"][index - 2]["opcode"] == "c001"
                and map_enter["instructions"][index - 1]["opcode"] == "c001"
            }
            role_metarules = {
                semantic: row
                for semantic, row in configured_metarules.items()
                if (int(row["rule"]), int(row["argument"]))
                in referenced_metarules
            }
            if {
                (int(row["rule"]), int(row["argument"]))
                for row in role_metarules.values()
            } != referenced_metarules:
                raise Fo1ProfileError(
                    f"Fallout 2 ARVILLAG {role} metarule source join is incomplete"
                )
            village_int_roles[role] = {
                "actor": {
                    key: actor[key]
                    for key in (
                        "serial",
                        "tile",
                        "elevation",
                        "rotation",
                        "fid",
                        "pid",
                        "sid",
                        "scriptIndex",
                    )
                },
                "program": {
                    key: program[key]
                    for key in (
                        "scriptsListIndex",
                        "program",
                        "logicalPath",
                        "source",
                        "bytes",
                        "sha256",
                    )
                },
                "messageCatalog": {
                    "messageListId": next(iter(message_list_ids)),
                    "logicalPath": message_resource.logical_path,
                    "source": message_resource.source,
                    "bytes": len(message_resource.data),
                    "sha256": message_resource.sha256,
                    "messages": messages,
                },
                "initialGlobalVariables": {
                    str(index): global_rows[index]
                    for index in sorted(referenced_globals)
                },
                "mapEnterMetarules": role_metarules,
            }
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
                        "logicalPath": (
                            f"proto\\{directory}\\{prototype['filename']}"
                        ).casefold(),
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

    source_for_walk = {
        "map": {
            "layout": map_layout_manifest(layout),
            "objects": objects,
        }
    }
    walkable = _walkable_by_elevation(source_for_walk)
    elevation = int(arrival["elevation"])
    admitted = walkable[elevation]
    legal_neighbors = [int(value) for value in arrival["legalNeighborTiles"]]
    first = arrival["firstLegalAction"]
    if (
        int(arrival["arrivalTile"]) not in admitted
        or len(admitted) != int(arrival["walkableHexes"])
        or _walk_mask_sha256(admitted) != arrival["walkMaskSha256"]
        or any(tile not in admitted for tile in legal_neighbors)
        or int(first["fromTile"]) != int(arrival["arrivalTile"])
        or int(first["toTile"]) not in legal_neighbors
    ):
        raise Fo1ProfileError("Fallout 2 ARVILLAG walk contract drifted")

    roof_patches = sum(
        1
        for row in layout.elevations
        if row.elevation == elevation
        for entry in row.entries
        if (entry >> 16) & 0x0FFF != 1
    )
    return {
        "schema": SCHEMA,
        "status": "transported-owned-map-source-and-presentation-graph",
        "campaign": "Fallout2",
        "slice": "ArroyoVillage",
        "sourceProfile": {
            "file": str(profile_path),
            "sourceProfileId": profile["sourceProfileId"],
            "saveCompatibilityId": profile["saveCompatibilityId"],
            "sha256": file_sha256(profile_path),
        },
        "trialRoute": {
            "file": str(route_path),
            "schema": ROUTE_SCHEMA,
            "sha256": file_sha256(route_path),
        },
        "recipe": {
            "file": str(recipe_path),
            "id": recipe["id"],
            "sha256": file_sha256(recipe_path),
        },
        "overlayOrderHighToLow": recipe["overlayOrderHighToLow"],
        "incomingPlacement": {
            "authority": "exact Map 126 exit-grid destination values",
            "mapIndex": MAP_INDEX,
            "tile": int(arrival["arrivalTile"]),
            "elevation": elevation,
            "rotation": int(arrival["arrivalRotation"]),
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
        "villageIntRoles": village_int_roles,
        "arrivalWalkContract": {
            "semantics": (
                "non-default-floor-minus-central-non-wall-blockers-with-owned-exit-grids-v1"
            ),
            "walkMaskSha256": _walk_mask_sha256(admitted),
            "walkableHexes": len(admitted),
            "legalNeighborTiles": legal_neighbors,
            "firstLegalAction": first,
            "multihexExpansionImplemented": False,
        },
        "roofCutawayBoundary": {
            "sourceRoofPatches": roof_patches,
            "rendered": False,
            "reason": "owned MAP/FRM data supplies no accepted 3D roof-height contract",
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
                "The exact ARVILLAG source graph is transported, but its decoded local "
                "presentation and Godot consumer are separate disposable outputs."
            ),
        },
        "unsupported": [
            "general INT execution, quests, combat, and actor AI",
            "source-authored roof height, wall-edge collision, and multihex expansion",
            "retail visual parity, FPS, and OpenXR acceptance",
        ],
        "retailOrDerivedAssetsPackaged": False,
        "generatedCaches": [],
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Compile the asset-free owned Fallout 2 ARVILLAG graph."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--trial-route", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        if output.exists():
            raise Fo1ProfileError(f"refusing to overwrite ARVILLAG source: {output}")
        document = compile_fo2_arvillag_slice(args.profile, args.trial_route)
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_FO2_ARVILLAG_SOURCE_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_ARVILLAG_SOURCE "
        + json.dumps(
            {
                "manifest": str(output),
                "objects": document["map"]["objects"]["totalTopLevelObjects"],
                "frms": len(document["frms"]),
                "walkable": document["arrivalWalkContract"]["walkableHexes"],
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
