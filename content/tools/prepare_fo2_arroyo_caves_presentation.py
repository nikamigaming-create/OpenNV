#!/usr/bin/env python3
"""Build a disposable local PNG cache for the owned Fallout 2 Arroyo Caves graph."""

from __future__ import annotations

import argparse
from functools import partial
import json
import sys
from pathlib import Path

from fo2_arroyo_classic_hud import (
    DEFAULT_HUD_RECIPE,
    enrich_arroyo_cache_with_classic_hud,
)
from fo2_arroyo_molded_surface import enrich_arroyo_cache_with_molded_surface
from prepare_fo2_temple_presentation import prepare_fo2_map_presentation


SOURCE_SCHEMA = "opennv-fo2-owned-map-slice/v1"
CACHE_SCHEMA = "opennv-fo2-arroyo-caves-presentation-cache/v2"
CACHE_MANIFEST_NAME = "fo2-arroyo-caves-presentation-cache.json"
DEFAULT_MOLDED_SURFACE_RECIPE = (
    Path(__file__).resolve().parents[1]
    / "recipes"
    / "fo2-arroyo-caves-molded-surface-v1.json"
)


def _enrich_arroyo_cache(
    staging: Path,
    document: dict,
    source_manifest: dict,
    *,
    profile_path: Path,
    archive_recipe_path: Path | None,
    molded_surface_recipe_path: Path,
    classic_hud_recipe_path: Path,
) -> None:
    enrich_arroyo_cache_with_molded_surface(
        staging,
        document,
        source_manifest,
        recipe_path=molded_surface_recipe_path,
    )
    enrich_arroyo_cache_with_classic_hud(
        staging,
        document,
        source_manifest,
        profile_path=profile_path,
        archive_recipe_path=archive_recipe_path,
        hud_recipe_path=classic_hud_recipe_path,
    )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Build a disposable local Fallout 2 Arroyo Caves PNG presentation cache."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--source-manifest", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=None)
    parser.add_argument(
        "--molded-surface-recipe",
        type=Path,
        default=DEFAULT_MOLDED_SURFACE_RECIPE,
    )
    parser.add_argument(
        "--classic-hud-recipe",
        type=Path,
        default=DEFAULT_HUD_RECIPE,
    )
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
            cache_enricher=partial(
                _enrich_arroyo_cache,
                profile_path=args.profile,
                archive_recipe_path=args.recipe,
                molded_surface_recipe_path=args.molded_surface_recipe,
                classic_hud_recipe_path=args.classic_hud_recipe,
            ),
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
