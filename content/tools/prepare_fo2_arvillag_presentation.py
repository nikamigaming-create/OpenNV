#!/usr/bin/env python3
"""Build a disposable owned ARVILLAG MAP/FRM presentation cache."""

from __future__ import annotations

import argparse
from functools import partial
import json
import sys
from pathlib import Path
from typing import Any

from PIL import Image

from fo1_profile import Fo1ProfileError
from fo2_frm_relief import RELIEF_MODE, RELIEF_SCHEMA, derive_relief
from plugin_stack import file_sha256
from prepare_fo2_temple_presentation import prepare_fo2_map_presentation


SOURCE_SCHEMA = "opennv-fo2-owned-map-slice/v1"
CACHE_SCHEMA = "opennv-fo2-arvillag-presentation-cache/v1"
CACHE_MANIFEST_NAME = "fo2-arvillag-presentation-cache.json"
RELIEF_RECIPE_SCHEMA = "opennv-fo2-arvillag-relief-recipe/v1"
RELIEF_CACHE_SCHEMA = "opennv-fo2-arvillag-object-relief-cache/v1"
RELIEF_STATUS = "source-frm-alpha-derived-closed-relief"
FLOOR_MATERIAL_SCHEMA = "opennv-fo2-arvillag-floor-material-depth-cache/v1"
FLOOR_MATERIAL_STATUS = "source-projected-floor-frm-luma-normal-material-depth"
DEFAULT_RELIEF_RECIPE = (
    Path(__file__).resolve().parents[1]
    / "recipes"
    / "fo2-arvillag-relief-v1.json"
)
ROLE_BY_OBJECT_TYPE = {
    0: "item",
    1: "critter",
    2: "scenery",
    3: "wall",
    4: "tileObject",
    5: "misc",
}


def _top_level_objects(source: dict[str, Any]) -> dict[int, dict[str, Any]]:
    return {
        int(value["serial"]): value
        for elevation in source["map"]["objects"]["elevations"]
        for value in elevation["objects"]
    }


def _enrich_arvillag_cache(
    staging: Path,
    cache: dict[str, Any],
    source: dict[str, Any],
    *,
    relief_recipe_path: Path,
) -> None:
    recipe_path = relief_recipe_path.resolve()
    recipe = json.loads(recipe_path.read_text(encoding="utf-8"))
    map_contract = recipe.get("map", {})
    relief_recipe = recipe.get("objectRelief3d", {})
    depth_by_type = relief_recipe.get("depthMetersByObjectType", {})
    if (
        recipe.get("schema") != RELIEF_RECIPE_SCHEMA
        or recipe.get("id") != recipe_path.stem
        or recipe.get("campaign") != "Fallout2"
        or map_contract
        != {
            "index": 4,
            "name": "ARVILLAG.MAP",
            "logicalPath": "maps\\arvillag.map",
            "sha256": source["map"]["sha256"],
        }
        or relief_recipe.get("schema") != RELIEF_SCHEMA
        or relief_recipe.get("mode") != RELIEF_MODE
        or set(depth_by_type) != {str(value) for value in ROLE_BY_OBJECT_TYPE}
        or any(float(value) <= 0.0 for value in depth_by_type.values())
        or not 0.0 <= float(relief_recipe.get("sideRoughness", -1.0)) <= 1.0
        or recipe.get("roofCutaway", {}).get("rendered") is not False
        or recipe.get("policy", {}).get("distributionAllowed") is not False
    ):
        raise Fo1ProfileError("Fallout 2 ARVILLAG relief recipe drifted")

    top_level = _top_level_objects(source)
    artifacts = {row["id"]: row for row in cache["artifacts"]}
    relief_artifacts: dict[str, dict[str, Any]] = {}
    placements: list[dict[str, Any]] = []
    transparent_serials: list[int] = []
    for binding in cache["objectBindings"]:
        artifact = artifacts[str(binding["artifactId"])]
        admitted = [
            top_level[int(row["serial"])]
            for row in binding["placements"]
            if int(row["serial"]) in top_level
            and int(top_level[int(row["serial"])]["elevation"]) == 0
        ]
        if not admitted:
            continue
        image = Image.open(staging / artifact["png"]).convert("RGBA")
        if image.getchannel("A").getbbox() is None:
            transparent_serials.extend(int(row["serial"]) for row in admitted)
            continue
        relief = relief_artifacts.setdefault(
            artifact["id"],
            {
                "artifactId": artifact["id"],
                "logicalPath": artifact["logicalPath"],
                "sourceSha256": artifact["sourceSha256"],
                "pngSha256": artifact["pngSha256"],
                "relief": derive_relief(
                    staging,
                    artifact,
                    relief_recipe,
                    output_folder="arvillag-object-relief3d",
                ),
            },
        )
        if relief["pngSha256"] != artifact["pngSha256"]:
            raise Fo1ProfileError("Fallout 2 ARVILLAG relief artifact drifted")
        for row in admitted:
            object_type = int(row["prototype"]["object_type"])
            placements.append(
                {
                    "serial": int(row["serial"]),
                    "tile": int(row["tile"]),
                    "elevation": int(row["elevation"]),
                    "rotation": int(row["rotation"]),
                    "frame": int(row["frame"]),
                    "pixelOffset": [int(value) for value in row["pixelOffset"]],
                    "fid": row["fid"],
                    "pid": row["pid"],
                    "objectType": object_type,
                    "logicalPath": artifact["logicalPath"],
                    "artifactId": artifact["id"],
                    "role": ROLE_BY_OBJECT_TYPE[object_type],
                    "depthMeters": float(depth_by_type[str(object_type)]),
                }
            )
    placement_serials = {int(row["serial"]) for row in placements}
    transparent = set(transparent_serials)
    if (
        placement_serials & transparent
        or placement_serials | transparent != set(top_level)
        or len(placement_serials) != len(placements)
    ):
        raise Fo1ProfileError("Fallout 2 ARVILLAG relief placement closure failed")
    cache["objectRelief3d"] = {
        "schema": RELIEF_CACHE_SCHEMA,
        "status": RELIEF_STATUS,
        "mode": RELIEF_MODE,
        "recipe": {
            "file": str(recipe_path),
            "sha256": file_sha256(recipe_path),
        },
        "sideRoughness": float(relief_recipe["sideRoughness"]),
        "artifacts": [relief_artifacts[key] for key in sorted(relief_artifacts)],
        "placements": sorted(placements, key=lambda row: int(row["serial"])),
        "transparentSourceSerials": sorted(transparent),
        "counts": {
            "reliefArtifacts": len(relief_artifacts),
            "reliefPlacements": len(placements),
            "transparentSourcePlacements": len(transparent),
            "topLevelSourcePlacements": len(top_level),
        },
        "visualParity": False,
    }
    floor_artifacts: list[dict[str, Any]] = []
    for binding in sorted(cache["tileBindings"], key=lambda row: int(row["id"])):
        tile_id = int(binding["id"])
        if tile_id == 1 or not any(
            row["role"] == "floor"
            and int(row["elevation"]) == 0
            and int(row["count"]) > 0
            for row in binding["uses"]
        ):
            continue
        artifact = artifacts[str(binding["artifactId"])]
        floor_artifacts.append(
            {
                "tileId": tile_id,
                "artifactId": artifact["id"],
                "logicalPath": artifact["logicalPath"],
                "sourceSha256": artifact["sourceSha256"],
                "pngSha256": artifact["pngSha256"],
                "relief": derive_relief(
                    staging,
                    artifact,
                    relief_recipe,
                    output_folder="arvillag-floor-material-depth3d",
                ),
            }
        )
    expected_floor_ids = {
        int(value) & 0x0FFF
        for value in source["map"]["layout"]["elevations"][0]["rawEntries"]
        if int(value) & 0x0FFF != 1
    }
    if {row["tileId"] for row in floor_artifacts} != expected_floor_ids:
        raise Fo1ProfileError("Fallout 2 ARVILLAG floor material closure failed")
    cache["floorMaterialDepth3d"] = {
        "schema": FLOOR_MATERIAL_SCHEMA,
        "status": FLOOR_MATERIAL_STATUS,
        "mode": RELIEF_MODE,
        "normalScale": 1.0,
        "artifacts": floor_artifacts,
        "counts": {
            "sourceFloorTileIds": len(expected_floor_ids),
            "materialDepthArtifacts": len(floor_artifacts),
        },
        "visualParity": False,
    }
    cache["counts"]["objectRelief3dArtifacts"] = len(relief_artifacts)
    cache["counts"]["objectRelief3dPlacements"] = len(placements)
    cache["counts"]["floorMaterialDepth3dArtifacts"] = len(floor_artifacts)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Build a disposable owned Fallout 2 ARVILLAG presentation cache."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--source-manifest", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--archive-recipe", type=Path, default=None)
    parser.add_argument(
        "--relief-recipe",
        type=Path,
        default=DEFAULT_RELIEF_RECIPE,
    )
    args = parser.parse_args()
    try:
        document = prepare_fo2_map_presentation(
            args.profile,
            args.source_manifest,
            args.output_root,
            args.archive_recipe,
            source_schema=SOURCE_SCHEMA,
            source_status="transported-owned-map-source-and-presentation-graph",
            source_slice="ArroyoVillage",
            cache_schema=CACHE_SCHEMA,
            cache_manifest_name=CACHE_MANIFEST_NAME,
            map_index=4,
            map_name="ARVILLAG.MAP",
            map_logical_path="maps\\arvillag.map",
            map_label="Arroyo Village",
            cache_enricher=partial(
                _enrich_arvillag_cache,
                relief_recipe_path=args.relief_recipe,
            ),
        )
    except Exception as error:
        print(f"OPENNV_FO2_ARVILLAG_PRESENTATION_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_ARVILLAG_PRESENTATION "
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
