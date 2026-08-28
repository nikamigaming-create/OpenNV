#!/usr/bin/env python3
"""Compile the owned Fallout 2 Temple of Trials source graph.

The emitted JSON is asset-free: it contains authored numeric placement data and
hash-bound identities, never DAT2 member payloads or decoded images.
"""

from __future__ import annotations

import argparse
from dataclasses import asdict
import json
import sys
from pathlib import Path
from typing import Any

from corpus_io import atomic_json
from fo1_frm import decode_frm
from fo1_map_objects import (
    FO1_MAP_OBJECTS_FORMAT_CONTRACT_HEX_01000000,
    OBJECT_TYPE_NAMES,
    TYPE_DIRECTORIES,
    Fo1ResourceResolver,
    parse_map_objects,
    parse_script_section,
)
from fo1_profile import Fo1ProfileError, map_layout_manifest, parse_map_layout
from plugin_stack import file_sha256


SCHEMA = "opennv-fo2-first-slice/v1"
RECIPE_SCHEMA = "opennv-fo2-first-slice-recipe/v1"
PROFILE_SCHEMA = "opennv-fo2-owned-profile/v1"
FORM_ID_RADIX = 16
FRM_PALETTE_SIZE = 256
MAP_WIDTH_TILES = 200


def default_recipe_path() -> Path:
    recipes = Path(__file__).resolve().parents[1] / "recipes"
    matches = []
    for path in recipes.glob("*.json"):
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if document.get("schema") == RECIPE_SCHEMA:
            matches.append(path)
    if len(matches) != 1:
        raise Fo1ProfileError(f"Expected one Fallout 2 first-slice recipe, found {len(matches)}")
    return matches[0]


def _load_json(path: Path) -> dict[str, Any]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise Fo1ProfileError(f"JSON document is not an object: {path}")
    return document


def _load_recipe(path: Path) -> dict[str, Any]:
    recipe = _load_json(path)
    if recipe.get("schema") != RECIPE_SCHEMA or recipe.get("id") != path.stem:
        raise Fo1ProfileError(f"unexpected Fallout 2 first-slice recipe: {path}")
    if recipe.get("campaign") != "Fallout2" or recipe.get("sourceProfileSchema") != PROFILE_SCHEMA:
        raise Fo1ProfileError("Fallout 2 first-slice recipe identity changed")
    overlay = recipe.get("overlayOrderHighToLow")
    if overlay != ["patch000.dat", "critter.dat", "master.dat"]:
        raise Fo1ProfileError("Fallout 2 DAT2 overlay order changed")
    if not isinstance(recipe.get("unsupported"), list) or not recipe["unsupported"]:
        raise Fo1ProfileError("Fallout 2 first-slice unsupported boundary is missing")
    return recipe


def _archive_paths(profile: dict[str, Any], recipe: dict[str, Any]) -> list[Path]:
    if (
        profile.get("schema") != PROFILE_SCHEMA
        or profile.get("campaign") != "Fallout2"
        or profile.get("status") != "registered-owned-install"
    ):
        raise Fo1ProfileError("Fallout 2 owned profile is not a registered source profile")
    install = profile.get("install")
    if not isinstance(install, dict):
        raise Fo1ProfileError("Fallout 2 owned profile has no install binding")
    root = Path(str(install.get("root", ""))).resolve()
    rows = install.get("archives")
    if not root.is_dir() or not isinstance(rows, list):
        raise Fo1ProfileError("Fallout 2 owned install binding is unavailable")
    by_name = {str(row.get("file", "")).casefold(): row for row in rows if isinstance(row, dict)}
    if set(by_name) != {"master.dat", "critter.dat", "patch000.dat"}:
        raise Fo1ProfileError("Fallout 2 owned profile archive set changed")
    resolved = []
    for name in recipe["overlayOrderHighToLow"]:
        row = by_name[name.casefold()]
        source = Path(str(row.get("source", ""))).resolve()
        if source.parent != root or source.name.casefold() != name.casefold() or not source.is_file():
            raise Fo1ProfileError(f"Fallout 2 archive binding escapes the registered root: {name}")
        expected_bytes = row.get("bytes")
        if source.stat().st_size != expected_bytes:
            raise Fo1ProfileError(f"Fallout 2 archive byte size drift: {name}")
        expected_hash = str(row.get("sha256", "")).casefold()
        actual_hash = file_sha256(source)
        if actual_hash != expected_hash:
            raise Fo1ProfileError(
                f"Fallout 2 archive SHA-256 drift for {name}: expected {expected_hash}, got {actual_hash}"
            )
        resolved.append(source)
    return resolved


def _maps_section(data: bytes, section_name: str) -> dict[str, str]:
    try:
        text = data.decode("cp1252")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("Fallout 2 maps.txt is not cp1252") from error
    wanted = section_name.casefold()
    active = False
    found = False
    values: dict[str, str] = {}
    for raw_line in text.replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        line = raw_line.strip()
        if not line or line.startswith(("#", ";")):
            continue
        if line.startswith("[") and line.endswith("]"):
            active = line[1:-1].strip().casefold() == wanted
            found = found or active
            continue
        if active and "=" in line:
            key, value = line.split("=", 1)
            normalized = key.strip().casefold()
            if normalized in values:
                raise Fo1ProfileError(f"duplicate maps.txt key in [{section_name}]: {key.strip()}")
            values[normalized] = value.strip()
    if not found:
        raise Fo1ProfileError(f"Fallout 2 maps.txt section is missing: [{section_name}]")
    return values


def _flatten_objects(objects: dict[str, Any]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []

    def add(obj: dict[str, Any]) -> None:
        rows.append(obj)
        for inventory in obj["inventory"]:
            add(inventory["object"])

    for elevation in objects["elevations"]:
        for obj in elevation["objects"]:
            add(obj)
    return rows


def _frm_structure(decoded: dict[str, object]) -> dict[str, object]:
    return {
        "version": decoded["version"],
        "storedFps": decoded["storedFps"],
        "fps": decoded["fps"],
        "actionFrame": decoded["actionFrame"],
        "framesPerDirection": decoded["framesPerDirection"],
        "frameAreaSize": decoded["frameAreaSize"],
        "directions": [
            {
                "rotation": direction["rotation"],
                "xOffset": direction["xOffset"],
                "yOffset": direction["yOffset"],
                "dataOffset": direction["dataOffset"],
                "frames": [
                    {
                        "index": frame["index"],
                        "width": frame["width"],
                        "height": frame["height"],
                        "x": frame["x"],
                        "y": frame["y"],
                    }
                    for frame in direction["frames"]
                ],
            }
            for direction in decoded["directions"]
        ],
    }


def compile_fo2_first_slice(
    profile_path: Path,
    recipe_path: Path | None = None,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    recipe_path = (recipe_path or default_recipe_path()).resolve()
    profile = _load_json(profile_path)
    recipe = _load_recipe(recipe_path)
    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])

    with resolver.access_scope() as accessed:
        registry_resource = resolver.read(recipe["mapRegistry"]["logicalPath"])
        registry_values = _maps_section(registry_resource.data, recipe["mapRegistry"]["section"])
        expected_registry = {
            "lookup_name": recipe["mapRegistry"]["lookupName"],
            "map_name": recipe["mapRegistry"]["mapName"],
        }
        for key, expected in expected_registry.items():
            if registry_values.get(key) != expected:
                raise Fo1ProfileError(
                    f"Fallout 2 maps.txt {key} drift: expected {expected!r}, got {registry_values.get(key)!r}"
                )

        map_resource = resolver.read(recipe["map"]["logicalPath"])
        if map_resource.sha256 != recipe["map"]["sha256"]:
            raise Fo1ProfileError("Fallout 2 Temple MAP SHA-256 drift")
        layout = parse_map_layout(map_resource.data)
        header = asdict(layout.header)
        if header != recipe["map"]["header"]:
            raise Fo1ProfileError(f"Fallout 2 Temple MAP header drift: {header}")
        if [row.elevation for row in layout.elevations] != recipe["map"]["presentElevations"]:
            raise Fo1ProfileError("Fallout 2 Temple MAP elevation presence drift")
        scripts, objects_offset = parse_script_section(map_resource.data, layout.next_offset)
        objects, end_offset = parse_map_objects(
            map_resource.data,
            objects_offset,
            layout.header.version,
            resolver,
        )
        if end_offset != len(map_resource.data):
            raise Fo1ProfileError(
                f"Fallout 2 Temple object graph leaves {len(map_resource.data) - end_offset} trailing bytes"
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

    return {
        "schema": SCHEMA,
        "status": "transported-source-manifest",
        "campaign": "Fallout2",
        "slice": "TempleOfTrials",
        "declaredRole": recipe["declaredRole"],
        "sourceProfile": {
            "file": str(profile_path),
            "sourceProfileId": profile["sourceProfileId"],
            "saveCompatibilityId": profile["saveCompatibilityId"],
            "sha256": file_sha256(profile_path),
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
            "section": recipe["mapRegistry"]["section"],
            "values": registry_values,
        },
        "newGameStart": {
            "mapIndex": layout.header.mapIndex,
            "lookupName": registry_values["lookup_name"],
            "mapName": registry_values["map_name"],
            "playerEntry": {
                "source": "MAP header",
                "tile": layout.header.enteringTile,
                "tileX": layout.header.enteringTile % MAP_WIDTH_TILES,
                "tileY": layout.header.enteringTile // MAP_WIDTH_TILES,
                "elevation": layout.header.enteringElevation,
                "rotation": layout.header.enteringRotation,
                "placedPlayerObject": any(
                    int(obj["pid"], FORM_ID_RADIX)
                    == FO1_MAP_OBJECTS_FORMAT_CONTRACT_HEX_01000000
                    for obj in flat_objects
                ),
            },
            "selectionAuthority": "declared recipe role; executable-owned new-game selection is not transported",
        },
        "map": {
            "logicalPath": map_resource.logical_path,
            "source": map_resource.source,
            "bytes": len(map_resource.data),
            "sha256": map_resource.sha256,
            "header": header,
            "layout": map_layout_manifest(layout),
            "scriptLists": scripts,
            "objectsOffset": objects_offset,
            "endOffset": end_offset,
            "objects": objects,
            "allObjectCount": len(flat_objects),
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
            "rendered": False,
            "interactive": False,
            "parityReviewed": False,
            "headsetAccepted": False,
        },
        "runtimeCompatibility": {
            "ready": False,
            "presentations": {
                "hex-tactical": False,
                "first-person": False,
                "openxr": False,
            },
            "firstSliceBlocker": (
                "The exact Temple MAP/PRO/FRM source graph is transported, but no Godot runtime "
                "consumes it and character creation, script execution, gameplay, and save state are absent."
            ),
        },
        "nextRuntimeOwner": (
            "A bounded Godot Fallout 2 Temple loader over this manifest and one shared Chosen One "
            "gameplay/save state; no runtime owner is implemented yet."
        ),
        "unsupported": recipe["unsupported"],
        "retailOrDerivedAssetsPackaged": False,
        "generatedCaches": [],
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Compile an asset-free Fallout 2 Temple of Trials source manifest."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=None)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        profile = _load_json(args.profile.resolve())
        install_root = Path(str(profile.get("install", {}).get("root", ""))).resolve()
        if output.is_relative_to(install_root):
            raise Fo1ProfileError("Fallout 2 first-slice output must be outside the owned install")
        document = compile_fo2_first_slice(args.profile, args.recipe)
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_FO2_FIRST_SLICE_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_FIRST_SLICE "
        + json.dumps(
            {
                "manifest": str(output),
                "map": document["map"]["logicalPath"],
                "topLevelObjects": document["map"]["objects"]["totalTopLevelObjects"],
                "allObjects": document["map"]["allObjectCount"],
                "prototypes": len(document["prototypes"]),
                "frms": len(document["frms"]),
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
