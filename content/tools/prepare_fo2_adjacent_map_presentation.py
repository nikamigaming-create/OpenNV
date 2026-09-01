#!/usr/bin/env python3
"""Decode one owned Fallout 2 adjacent MAP into a disposable presentation cache."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from fo1_profile import Fo1ProfileError
from prepare_fo2_temple_presentation import prepare_fo2_map_presentation


def prepare_adjacent_map(
    profile: Path,
    catalog: Path,
    output: Path,
    map_index: int,
) -> dict[str, object]:
    source = json.loads(catalog.read_text(encoding="utf-8"))
    maps = [row for row in source.get("maps", []) if row.get("mapIndex") == map_index]
    if len(maps) != 1:
        raise Fo1ProfileError("adjacent Fallout 2 MAP selection is absent or duplicated")
    selected = maps[0]
    name = str(selected["mapName"])
    logical_path = str(selected["logicalPath"])
    return prepare_fo2_map_presentation(
        profile,
        catalog,
        output,
        Path(str(source["recipe"]["file"])),
        source_schema="opennv-fo2-adjacent-map-catalog/v1",
        source_status="compiled-owned-reciprocal-adjacent-maps",
        source_slice="AdjacentMaps",
        cache_schema="opennv-fo2-adjacent-map-presentation-cache/v1",
        cache_manifest_name="manifest.json",
        map_index=map_index,
        map_name=name,
        map_logical_path=logical_path,
        map_label=name,
        source_map_index=map_index,
        recipe_schema=str(source["recipe"]["schema"]),
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, required=True)
    parser.add_argument("--map-index", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        document = prepare_adjacent_map(
            args.profile.resolve(),
            args.catalog.resolve(),
            args.output.resolve(),
            args.map_index,
        )
    except Exception as error:
        print(f"OPENNV_FO2_ADJACENT_PRESENTATION_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_ADJACENT_PRESENTATION "
        + json.dumps(
            {
                "manifest": str(args.output.resolve() / "manifest.json"),
                "mapIndex": args.map_index,
                "artifacts": len(document["artifacts"]),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
