#!/usr/bin/env python3
"""Transport every locally owned Fallout 1 MAP into neutral hashed contracts.

This corpus lane stops at source layout, scripts, objects, prototypes, and
resource identity. It does not create Godot nodes, substitute 3D assets, or
claim that quests and gameplay are executable.
"""

from __future__ import annotations

import argparse
from dataclasses import asdict
import hashlib
import json
import os
from pathlib import Path
import shutil
import tempfile
from typing import Any

from classic_map_joins import exit_grid_records, reciprocal_map_joins
from classic_int_initialization import compile_map_int_initialization
from fo1_campaign_inventory import parse_maps_txt
from fo1_map_objects import (
    CONTRACT_SCHEMA as OBJECT_CONTRACT_SCHEMA,
    Fo1ResourceResolver,
    build_contract,
)
from fo1_profile import (
    Fo1ProfileError,
    map_layout_manifest,
    parse_map_layout,
    sha256_path,
)


CAMPAIGN_SCHEMA = "opennv-fo1-campaign-transport/v1"
MAP_SCHEMA = "opennv-fo1-campaign-map-transport/v1"
CACHE_SCHEMA = "opennv-fo1-campaign-transport-cache/v1"


def canonical_map_id(path: Path) -> str:
    value = path.stem.casefold()
    if not value or any(character not in "abcdefghijklmnopqrstuvwxyz0123456789_-" for character in value):
        raise Fo1ProfileError(f"Fallout MAP filename is not a canonical ID: {path.name}")
    return value


def json_payload(document: object) -> bytes:
    return (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")


def write_payload(path: Path, document: object) -> str:
    payload = json_payload(document)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return hashlib.sha256(payload).hexdigest()


def map_summary(
    map_id: str,
    relative_path: str,
    digest: str,
    document: dict[str, Any],
) -> dict[str, Any]:
    object_graph = document["objectGraph"]
    script_lists = object_graph["scriptLists"]
    initialization = document["initializationScripts"]
    all_objects = sum(
        1
        for elevation in object_graph["objects"]["elevations"]
        for obj in elevation["objects"]
        for _ in flatten_object(obj)
    )
    layout = document["layout"]
    return {
        "id": map_id,
        "file": document["source"]["map"]["file"],
        "path": relative_path,
        "sha256": digest,
        "sourceMapSha256": document["source"]["map"]["sha256"],
        "mapIndex": document["header"]["mapIndex"],
        "version": document["header"]["version"],
        "presentElevations": layout["presentElevations"],
        "topLevelObjects": object_graph["objects"]["totalTopLevelObjects"],
        "allObjects": all_objects,
        "doors": len(object_graph["doors"]),
        "liveScripts": sum(row["liveCount"] for row in script_lists),
        "headerIntPrograms": int(initialization["mapHeader"]["program"] is not None),
        "randomSites": len(initialization["randomSites"]),
        "exitGrids": len(document["exitGrids"]),
        "resources": len(document["resources"]),
        "entry": document["entry"],
        "mapsTxt": document["mapsTxt"],
        "promotion": document["promotion"],
    }


def flatten_object(source: dict[str, Any]):
    yield source
    for inventory in source["inventory"]:
        yield from flatten_object(inventory["object"])


def build_campaign_transport(
    maps_dir: Path,
    maps_txt: Path,
    ettu_root: Path,
    fallout2_master: Path,
    output_root: Path,
    map_file: str | None = None,
) -> dict[str, Any]:
    maps_dir = maps_dir.resolve()
    maps_txt = maps_txt.resolve()
    ettu_root = ettu_root.resolve()
    fallout2_master = fallout2_master.resolve()
    output_root = output_root.resolve()
    if output_root.exists():
        raise Fo1ProfileError(
            f"refusing to overwrite Fallout campaign transport cache: {output_root}"
        )
    if not maps_dir.is_dir() or not maps_txt.is_file():
        raise FileNotFoundError(maps_dir if not maps_dir.is_dir() else maps_txt)
    if not fallout2_master.is_file():
        raise FileNotFoundError(fallout2_master)
    if map_file is None:
        map_paths = sorted(maps_dir.glob("*.MAP"), key=lambda path: path.name.casefold())
    else:
        requested = Path(map_file)
        if requested.is_absolute() or requested.name != map_file or requested.suffix.upper() != ".MAP":
            raise Fo1ProfileError(f"Fallout campaign map selector is not one exact MAP filename: {map_file}")
        selected = (maps_dir / requested).resolve()
        if not selected.is_relative_to(maps_dir) or not selected.is_file():
            raise Fo1ProfileError(f"Fallout campaign selected MAP is absent: {map_file}")
        map_paths = [selected]
    if not map_paths:
        raise Fo1ProfileError(f"Fallout campaign contains no MAP files: {maps_dir}")
    map_ids = [canonical_map_id(path) for path in map_paths]
    if len(set(map_ids)) != len(map_ids):
        raise Fo1ProfileError("Fallout campaign has duplicate case-insensitive MAP IDs")

    configured = parse_maps_txt(maps_txt.read_text(encoding="cp1252"))
    resolver = Fo1ResourceResolver(ettu_root, fallout2_master)
    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(
        tempfile.mkdtemp(
            prefix=f".{output_root.name}-",
            dir=output_root.parent,
        )
    )
    try:
        map_rows: list[dict[str, Any]] = []
        join_maps: list[dict[str, Any]] = []
        seen_source_hashes: set[str] = set()
        for map_id, map_path in zip(map_ids, map_paths, strict=True):
            source_bytes = map_path.read_bytes()
            layout = parse_map_layout(source_bytes)
            try:
                object_contract = build_contract(
                    map_path,
                    ettu_root,
                    fallout2_master,
                    resolver=resolver,
                )
            except Exception as error:
                raise Fo1ProfileError(f"{map_path.name}: {error}") from error
            if object_contract.get("schema") != OBJECT_CONTRACT_SCHEMA:
                raise Fo1ProfileError(
                    f"Fallout object contract schema drifted for {map_path.name}"
                )
            source_hash = object_contract["source"]["map"]["sha256"]
            if source_hash in seen_source_hashes:
                raise Fo1ProfileError(
                    f"Fallout campaign contains duplicate MAP bytes: {map_path.name}"
                )
            seen_source_hashes.add(source_hash)
            configured_row = configured.get(layout.header.mapIndex)
            initialization_scripts = compile_map_int_initialization(
                asdict(layout.header),
                object_contract["map"]["scriptLists"],
                resolver,
            )
            document = {
                "schema": MAP_SCHEMA,
                "status": "transported",
                "id": map_id,
                "source": object_contract["source"],
                "header": asdict(layout.header),
                "entry": {
                    "tile": layout.header.enteringTile,
                    "elevation": layout.header.enteringElevation,
                    "rotation": layout.header.enteringRotation,
                    "source": "MAP header entry metadata; script overrides are transported but not executed",
                },
                "mapsTxt": None
                if configured_row is None
                else {
                    "index": layout.header.mapIndex,
                    "mapName": configured_row["map_name"],
                    "lookupName": configured_row.get("lookup_name"),
                    "music": configured_row.get("music"),
                },
                "layout": map_layout_manifest(layout),
                "objectGraph": object_contract["map"],
                "initializationScripts": initialization_scripts,
                "exitGrids": exit_grid_records(
                    layout.header.mapIndex,
                    layout.header.name,
                    source_hash,
                    object_contract["map"]["objects"],
                ),
                "resources": object_contract["resources"],
                "promotion": {
                    "state": "transported",
                    "rendered": False,
                    "interactive": False,
                    "parityReviewed": False,
                    "headsetAccepted": False,
                },
                "unsupported": [
                    "INT execution and script-authored entry overrides",
                    "3D presentation mapping",
                    "Godot entity creation",
                    "turn, AP, RNG, dialogue, quest, and world-map simulation",
                    "retail visual, behavioral, or OpenXR parity",
                ],
                "retailOrDerivedAssetsPackaged": False,
            }
            relative_path = f"maps/{map_id}.json"
            digest = write_payload(staging / relative_path, document)
            map_rows.append(map_summary(map_id, relative_path, digest, document))
            if (
                configured_row is not None
                and str(configured_row["map_name"]).casefold() == map_id
            ):
                join_maps.append(
                    {
                        "mapIndex": layout.header.mapIndex,
                        "mapName": layout.header.name,
                        "mapSha256": source_hash,
                        "exitGrids": document["exitGrids"],
                    }
                )

        resources = [
            {
                "logicalPath": resource.logical_path,
                "source": resource.source,
                "sha256": resource.sha256,
                "bytes": len(resource.data),
            }
            for resource in sorted(
                resolver.resources.values(),
                key=lambda item: item.logical_path,
            )
        ]
        map_joins = reciprocal_map_joins(join_maps)
        map_documents = [
            json.loads((staging / row["path"]).read_text(encoding="utf-8"))
            for row in map_rows
        ]
        programs_by_sha256: dict[str, dict[str, Any]] = {}
        random_procedures: dict[str, int] = {}
        random_ranges: dict[str, int] = {}
        random_operand_kinds: dict[str, int] = {}
        random_expression_statuses: dict[str, int] = {}
        random_unsupported: dict[str, int] = {}
        branch_kinds: dict[str, int] = {}
        branch_target_kinds: dict[str, int] = {}
        branch_expression_statuses: dict[str, int] = {}
        branch_unsupported: dict[str, int] = {}
        source_expression_random_sites = 0
        for document in map_documents:
            initialization = document["initializationScripts"]
            programs = [
                row["program"] for row in initialization["liveScriptSlots"]
            ]
            if initialization["mapHeader"]["program"] is not None:
                programs.insert(0, initialization["mapHeader"]["program"])
            for program in programs:
                programs_by_sha256.setdefault(program["sha256"], program)
            for site in initialization["randomSites"]:
                random_procedures[site["procedure"]] = (
                    random_procedures.get(site["procedure"], 0) + 1
                )
                operand_kind = site["operandKind"]
                random_operand_kinds[operand_kind] = (
                    random_operand_kinds.get(operand_kind, 0) + 1
                )
                expression_status = site["expressionStatus"]
                random_expression_statuses[expression_status] = (
                    random_expression_statuses.get(expression_status, 0) + 1
                )
                if site["unsupported"] is not None:
                    key = site["unsupported"].split(" at ", 1)[0]
                    random_unsupported[key] = random_unsupported.get(key, 0) + 1
                if site["operandKind"] == "literal-inclusive-range":
                    key = f"{site['minimum']}..{site['maximum']}"
                    random_ranges[key] = random_ranges.get(key, 0) + 1
                else:
                    source_expression_random_sites += 1
        for program in programs_by_sha256.values():
            for procedure in program["inventory"]["procedures"]:
                for branch in procedure["branches"]:
                    branch_kinds[branch["kind"]] = (
                        branch_kinds.get(branch["kind"], 0) + 1
                    )
                    target_kind = branch["targetKind"]
                    branch_target_kinds[target_kind] = (
                        branch_target_kinds.get(target_kind, 0) + 1
                    )
                    expression_status = branch["expressionStatus"]
                    branch_expression_statuses[expression_status] = (
                        branch_expression_statuses.get(expression_status, 0) + 1
                    )
                    if branch["unsupported"] is not None:
                        key = branch["unsupported"].split(" at ", 1)[0]
                        branch_unsupported[key] = branch_unsupported.get(key, 0) + 1
        campaign = {
            "schema": CAMPAIGN_SCHEMA,
            "status": "transported-not-rendered",
            "source": {
                "mapsDirectory": str(maps_dir),
                "mapsTxt": str(maps_txt),
                "mapsTxtSha256": sha256_path(maps_txt),
                "fallout2Master": str(fallout2_master),
                "fallout2MasterSha256": sha256_path(fallout2_master),
                "ettuOverrideRoot": str(resolver.override_root),
            },
            "coverage": {
                "mapFiles": len(map_rows),
                "mapContracts": len(map_rows),
                "presentElevations": sum(len(row["presentElevations"]) for row in map_rows),
                "topLevelObjects": sum(row["topLevelObjects"] for row in map_rows),
                "allObjects": sum(row["allObjects"] for row in map_rows),
                "nestedInventoryObjects": sum(
                    row["allObjects"] - row["topLevelObjects"] for row in map_rows
                ),
                "doors": sum(row["doors"] for row in map_rows),
                "liveScripts": sum(row["liveScripts"] for row in map_rows),
                "mapHeaderIntPrograms": sum(
                    row["headerIntPrograms"] for row in map_rows
                ),
                "uniqueIntPrograms": len(programs_by_sha256),
                "uniqueIntProcedures": sum(
                    len(program["inventory"]["procedures"])
                    for program in programs_by_sha256.values()
                ),
                "sourceInstanceRandomSites": sum(
                    row["randomSites"] for row in map_rows
                ),
                "literalRangeRandomSites": sum(
                    row["randomSites"] for row in map_rows
                ) - source_expression_random_sites,
                "sourceExpressionRandomSites": source_expression_random_sites,
                "randomOperandKindHistogram": dict(
                    sorted(random_operand_kinds.items())
                ),
                "randomExpressionStatusHistogram": dict(
                    sorted(random_expression_statuses.items())
                ),
                "randomUnsupportedHistogram": dict(sorted(random_unsupported.items())),
                "randomProcedureHistogram": dict(sorted(random_procedures.items())),
                "randomRangeHistogram": dict(sorted(random_ranges.items())),
                "branchKindHistogram": dict(sorted(branch_kinds.items())),
                "branchTargetKindHistogram": dict(
                    sorted(branch_target_kinds.items())
                ),
                "branchExpressionStatusHistogram": dict(
                    sorted(branch_expression_statuses.items())
                ),
                "branchUnsupportedHistogram": dict(sorted(branch_unsupported.items())),
                "exitGrids": sum(row["exitGrids"] for row in map_rows),
                "reciprocalMapJoins": len(map_joins),
                "uniqueResources": len(resources),
                "mapsTxtRows": len(configured),
                "configuredMaps": sum(row["mapsTxt"] is not None for row in map_rows),
            },
            "promotion": {
                "transportedMaps": len(map_rows),
                "renderedMaps": 0,
                "interactiveMaps": 0,
                "questExecutableMaps": 0,
                "firstPersonReadyMaps": 0,
                "openXrAcceptedMaps": 0,
            },
            "maps": map_rows,
            "mapJoins": map_joins,
            "resources": resources,
            "unsupported": [
                "script execution and quest state",
                "map transitions and world-map travel",
                "generic 3D presentation and collision",
                "campaign gameplay, saves, autoplay, and OpenXR",
            ],
            "retailOrDerivedAssetsPackaged": False,
        }
        campaign_sha256 = write_payload(staging / "campaign.json", campaign)
        manifest = {
            "schema": CACHE_SCHEMA,
            "status": "prepared-owned-data",
            "campaign": str((output_root / "campaign.json").resolve()),
            "campaignSha256": campaign_sha256,
            "maps": len(map_rows),
            "elevations": campaign["coverage"]["presentElevations"],
            "objects": campaign["coverage"]["topLevelObjects"],
            "doors": campaign["coverage"]["doors"],
            "reciprocalMapJoins": campaign["coverage"]["reciprocalMapJoins"],
            "resources": len(resources),
            "retailOrDerivedAssetsPackaged": False,
        }
        write_payload(staging / "campaign-cache-manifest.json", manifest)
        os.replace(staging, output_root)
        return manifest
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--maps-dir", type=Path, required=True)
    parser.add_argument("--maps-txt", type=Path, required=True)
    parser.add_argument("--ettu-root", type=Path, required=True)
    parser.add_argument("--fallout2-master", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--map-file")
    args = parser.parse_args()
    try:
        manifest = build_campaign_transport(
            args.maps_dir,
            args.maps_txt,
            args.ettu_root,
            args.fallout2_master,
            args.output_root,
            args.map_file,
        )
    except Exception as error:
        print(f"OPENNV_FO1_CAMPAIGN_TRANSPORT_ERROR {error}")
        return 2
    print("OPENNV_FO1_CAMPAIGN_TRANSPORT " + json.dumps(manifest, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
