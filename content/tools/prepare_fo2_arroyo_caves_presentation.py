#!/usr/bin/env python3
"""Build a disposable local PNG cache for the owned Fallout 2 Arroyo Caves graph."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from prepare_fo2_temple_presentation import prepare_fo2_map_presentation


SOURCE_SCHEMA = "opennv-fo2-owned-map-slice/v1"
CACHE_SCHEMA = "opennv-fo2-arroyo-caves-presentation-cache/v1"
CACHE_MANIFEST_NAME = "fo2-arroyo-caves-presentation-cache.json"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Build a disposable local Fallout 2 Arroyo Caves PNG presentation cache."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--source-manifest", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=None)
    args = parser.parse_args()
    try:
        document = prepare_fo2_map_presentation(
            args.profile,
            args.source_manifest,
            args.output_root,
            args.recipe,
            source_schema=SOURCE_SCHEMA,
            source_status="transported-owned-map-source-and-presentation-graph",
            source_slice="ArroyoCaves",
            cache_schema=CACHE_SCHEMA,
            cache_manifest_name=CACHE_MANIFEST_NAME,
            map_index=3,
            map_name="ARCAVES.MAP",
            map_logical_path="maps\\arcaves.map",
            map_label="Arroyo Caves",
        )
    except Exception as error:
        print(f"OPENNV_FO2_ARROYO_CAVES_PRESENTATION_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_ARROYO_CAVES_PRESENTATION "
        + json.dumps(
            {
                "cache": str(args.output_root.resolve()),
                **document["counts"],
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
