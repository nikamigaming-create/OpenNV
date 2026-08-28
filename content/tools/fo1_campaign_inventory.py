#!/usr/bin/env python3
"""Inventory every locally owned Fallout 1 map and record honest promotion gates."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import tempfile
from collections import Counter
from dataclasses import asdict
from pathlib import Path

from fo1_profile import Fo1ProfileError, parse_map_layout, sha256_path
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
FO1_CAMPAIGN_INVENTORY_FORMAT_CONTRACT_HEX_0FFF = 0x0FFF
FO1_CAMPAIGN_INVENTORY_FORMAT_CONTRACT_INTEGER_16 = 16



SCHEMA = "opennv-fo1-campaign-inventory/v1"
MAP_SECTION = re.compile(r"^\[Map\s+([0-9]+)\](?:\s*#.*)?$", re.IGNORECASE)


def parse_maps_txt(text: str) -> dict[int, dict[str, str]]:
    rows: dict[int, dict[str, str]] = {}
    current: dict[str, str] | None = None
    for source_line in text.splitlines():
        line = source_line.split(";", 1)[0].strip()
        if not line:
            continue
        section = MAP_SECTION.match(line)
        if section:
            index = int(section.group(1))
            if index in rows:
                raise Fo1ProfileError(f"duplicate Maps.txt index {index}")
            current = {"index": str(index)}
            rows[index] = current
            continue
        if current is None or "=" not in line:
            continue
        key, value = (part.strip() for part in line.split("=", 1))
        if not key or key.casefold() in current:
            raise Fo1ProfileError(f"invalid or duplicate Maps.txt key {key!r}")
        current[key.casefold()] = value
    missing = [index for index, row in rows.items() if not row.get("map_name")]
    if missing:
        raise Fo1ProfileError(f"Maps.txt rows have no map_name: {missing}")
    return rows


def _canonical_names(values: list[str]) -> set[str]:
    result = {value.strip().casefold() for value in values if value.strip()}
    if len(result) != len(values):
        raise Fo1ProfileError("campaign promotion map names must be non-empty and unique")
    return result


def build_inventory(
    maps_dir: Path,
    maps_txt: Path,
    object_maps: list[str],
    presentation_maps: list[str],
    tactical_maps: list[str],
) -> dict[str, object]:
    maps_dir = maps_dir.resolve()
    maps_txt = maps_txt.resolve()
    if not maps_dir.is_dir() or not maps_txt.is_file():
        raise FileNotFoundError(maps_dir if not maps_dir.is_dir() else maps_txt)
    configured = parse_maps_txt(maps_txt.read_text(encoding="cp1252"))
    map_paths = sorted(maps_dir.glob("*.MAP"), key=lambda path: path.name.casefold())
    if not map_paths:
        raise Fo1ProfileError(f"Fallout campaign contains no MAP files: {maps_dir}")
    object_names = _canonical_names(object_maps)
    presentation_names = _canonical_names(presentation_maps)
    tactical_names = _canonical_names(tactical_maps)
    if not tactical_names.issubset(presentation_names) or not presentation_names.issubset(object_names):
        raise Fo1ProfileError(
            "campaign promotion must be monotonic: tactical ⊆ presentation ⊆ object"
        )

    rows = []
    file_stems: set[str] = set()
    total_non_default_floor = 0
    total_non_default_roof = 0
    elevation_count = 0
    for path in map_paths:
        stem = path.stem.casefold()
        if stem in file_stems:
            raise Fo1ProfileError(f"duplicate case-insensitive Fallout MAP name: {path.name}")
        file_stems.add(stem)
        payload = path.read_bytes()
        layout = parse_map_layout(payload)
        configured_row = configured.get(layout.header.mapIndex)
        elevations = []
        for elevation in layout.elevations:
            floor_ids = [entry & FO1_CAMPAIGN_INVENTORY_FORMAT_CONTRACT_HEX_0FFF for entry in elevation.entries]
            roof_ids = [(entry >> FO1_CAMPAIGN_INVENTORY_FORMAT_CONTRACT_INTEGER_16) & FO1_CAMPAIGN_INVENTORY_FORMAT_CONTRACT_HEX_0FFF for entry in elevation.entries]
            floor_count = sum(value != 1 for value in floor_ids)
            roof_count = sum(value != 1 for value in roof_ids)
            total_non_default_floor += floor_count
            total_non_default_roof += roof_count
            elevation_count += 1
            elevations.append(
                {
                    "elevation": elevation.elevation,
                    "rawSha256": elevation.raw_sha256,
                    "uniqueFloorIds": len(set(floor_ids)),
                    "uniqueRoofIds": len(set(roof_ids)),
                    "nonDefaultFloorEntries": floor_count,
                    "nonDefaultRoofEntries": roof_count,
                }
            )
        list_name = None if configured_row is None else configured_row["map_name"]
        rows.append(
            {
                "file": path.name,
                "bytes": len(payload),
                "sha256": hashlib.sha256(payload).hexdigest(),
                "header": asdict(layout.header),
                "elevations": elevations,
                "mapsTxt": None
                if configured_row is None
                else {
                    "index": layout.header.mapIndex,
                    "mapName": list_name,
                    "lookupName": configured_row.get("lookup_name"),
                    "music": configured_row.get("music"),
                },
                "identity": {
                    "headerMatchesFilename": layout.header.name.casefold() == path.name.casefold(),
                    "mapsTxtMatchesFilename": (
                        list_name is not None and list_name.casefold() == path.stem.casefold()
                    ),
                },
                "promotion": {
                    "layoutInventoried": True,
                    "objectGraphTransported": stem in object_names,
                    "owned3dPresentation": stem in presentation_names,
                    "tacticalPlayable": stem in tactical_names,
                    "questScriptsExecutable": False,
                    "autoplayAgentReady": False,
                    "firstPersonModeReady": False,
                    "openXrAccepted": False,
                },
            }
        )

    configured_stems = {row["map_name"].casefold() for row in configured.values()}
    missing_files = sorted(configured_stems - file_stems)
    unconfigured_files = sorted(file_stems - configured_stems)
    named_rows = [row for row in rows if row["mapsTxt"] is not None]
    result = {
        "schema": SCHEMA,
        "status": "inventoried-not-campaign-ready",
        "source": {
            "mapsDirectory": str(maps_dir),
            "mapsTxt": str(maps_txt),
            "mapsTxtSha256": sha256_path(maps_txt),
        },
        "coverage": {
            "mapFiles": len(rows),
            "mapsTxtRows": len(configured),
            "parsedLayouts": len(rows),
            "presentElevations": elevation_count,
            "versions": dict(sorted(Counter(row["header"]["version"] for row in rows).items())),
            "totalNonDefaultFloorEntries": total_non_default_floor,
            "totalNonDefaultRoofEntries": total_non_default_roof,
            "headerFilenameMatches": sum(
                row["identity"]["headerMatchesFilename"] for row in rows
            ),
            "mapsTxtFilenameMatches": sum(
                row["identity"]["mapsTxtMatchesFilename"] for row in named_rows
            ),
            "configuredRowsWithoutMapFile": missing_files,
            "mapFilesWithoutConfiguredRow": unconfigured_files,
        },
        "promotion": {
            "layoutInventoriedMaps": len(rows),
            "objectGraphTransportedMaps": sum(
                row["promotion"]["objectGraphTransported"] for row in rows
            ),
            "owned3dPresentationMaps": sum(
                row["promotion"]["owned3dPresentation"] for row in rows
            ),
            "tacticalPlayableMaps": sum(
                row["promotion"]["tacticalPlayable"] for row in rows
            ),
            "questScriptsExecutableMaps": 0,
            "autoplayAgentReadyMaps": 0,
            "firstPersonModeReadyMaps": 0,
            "openXrAcceptedMaps": 0,
        },
        "maps": rows,
        "retailOrDerivedAssetsPackaged": False,
    }
    if any(name not in file_stems for name in object_names | presentation_names | tactical_names):
        raise Fo1ProfileError("campaign promotion references a MAP file that is absent")
    return result


def write_inventory(path: Path, document: dict[str, object]) -> str:
    digest_path = path.with_suffix(path.suffix + ".sha256")
    if path.exists() or digest_path.exists():
        raise Fo1ProfileError(
            f"refusing to overwrite Fallout campaign inventory or digest: {path}"
        )
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    digest = hashlib.sha256(payload).hexdigest()
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(payload)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)
    digest_path.write_text(
        f"{digest}  {path.name}\n",
        encoding="ascii",
    )
    return digest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--maps-dir", type=Path, required=True)
    parser.add_argument("--maps-txt", type=Path, required=True)
    parser.add_argument("--object-map", action="append", default=[])
    parser.add_argument("--presentation-map", action="append", default=[])
    parser.add_argument("--tactical-map", action="append", default=[])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = build_inventory(
            args.maps_dir,
            args.maps_txt,
            args.object_map,
            args.presentation_map,
            args.tactical_map,
        )
        digest = write_inventory(args.output.resolve(), result)
    except Exception as error:
        print(f"OPENNV_FO1_CAMPAIGN_INVENTORY_ERROR {error}")
        return 2
    print(
        "OPENNV_FO1_CAMPAIGN_INVENTORY "
        + json.dumps(
            {
                "output": str(args.output.resolve()),
                "sha256": digest,
                **result["coverage"],
                **result["promotion"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
