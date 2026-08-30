"""Compile one explicit, hash-bound Fallout 1 MAP exit-grid transition."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from fo1_profile import Fo1ProfileError, sha256_path

SCHEMA = "opennv-fo1-exit-grid-transition-recipe/v1"
OUTPUT_SCHEMA = "opennv-fo1-exit-grid-transition/v1"


def _read(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def _objects(contract: dict) -> list[dict]:
    return [obj for elevation in contract["map"]["objects"]["elevations"] for obj in elevation["objects"]]


def build(recipe_path: Path, source_object_contract_path: Path, destination_object_contract_path: Path,
          maps_header_path: Path, destination_map_path: Path, maps_txt_path: Path, output_path: Path) -> dict:
    if output_path.exists():
        raise Fo1ProfileError(f"refusing to overwrite Fallout exit-grid descriptor: {output_path}")
    recipe = _read(recipe_path)
    if recipe.get("schema") != SCHEMA:
        raise Fo1ProfileError("unexpected Fallout exit-grid recipe")
    inputs = recipe["inputs"]
    for name, path in {
        "sourceObjectContractSha256": source_object_contract_path,
        "destinationObjectContractSha256": destination_object_contract_path,
        "mapsHeaderSha256": maps_header_path,
        "destinationMapSha256": destination_map_path,
        "mapsTxtSha256": maps_txt_path,
    }.items():
        if sha256_path(path) != inputs[name]:
            raise Fo1ProfileError(f"Fallout exit-grid input hash drift: {name}")
    source = _read(source_object_contract_path)
    destination = _read(destination_object_contract_path)
    expected_source = recipe["sourceMap"]
    source_header = source["map"]["header"]
    if source_header["mapIndex"] != expected_source["mapIndex"] or source_header["name"] != expected_source["name"] or source["source"]["map"]["sha256"] != expected_source["sha256"]:
        raise Fo1ProfileError("source MAP identity drift")
    expected_destination = recipe["destination"]
    destination_header = destination["map"]["header"]
    if destination_header["mapIndex"] != expected_destination["mapIndex"] or destination_header["name"] != expected_destination["name"] or destination["source"]["map"]["sha256"] != expected_destination["mapSha256"]:
        raise Fo1ProfileError("destination MAP identity drift")
    maps_header = maps_header_path.read_text(encoding="utf-8")
    maps_txt = maps_txt_path.read_text(encoding="utf-8")
    if expected_source["mapSymbol"] not in maps_header or expected_destination["mapSymbol"] not in maps_header or expected_destination["worldSection"] not in maps_txt:
        raise Fo1ProfileError("MAP/world source names are absent")
    expected_trigger = recipe["trigger"]
    rows = [obj for obj in _objects(source) if obj["pid"] == expected_trigger["pid"] and obj["fid"] == expected_trigger["fid"] and obj["artFilename"] == expected_trigger["artFilename"] and obj["prototype"]["sha256"] == expected_trigger["prototypeSha256"] and obj["instanceValues"] == expected_destination["instanceValues"]]
    if not rows:
        raise Fo1ProfileError("source MAP has no exact exit grid for the destination")
    reciprocal = [obj for obj in _objects(destination) if obj["pid"] == expected_trigger["pid"] and obj["fid"] == expected_trigger["fid"] and obj["artFilename"] == expected_trigger["artFilename"] and obj["prototype"]["sha256"] == expected_trigger["prototypeSha256"] and obj["instanceValues"] == recipe["reciprocalInstanceValues"]]
    if not reciprocal:
        raise Fo1ProfileError("destination MAP has no reciprocal exit grid")
    document = {
        "schema": OUTPUT_SCHEMA,
        "status": "compiled-owned-map-world-transition",
        "recipe": {"id": recipe["id"], "sha256": sha256_path(recipe_path)},
        "sourceMap": {**expected_source, "objectContractSha256": sha256_path(source_object_contract_path)},
        "destination": {**expected_destination, "objectContractSha256": sha256_path(destination_object_contract_path)},
        "triggers": [{"serial": row["serial"], "tile": row["tile"], "pid": row["pid"], "prototypeSha256": row["prototype"]["sha256"], "instanceValues": row["instanceValues"]} for row in rows],
        "reciprocalTriggers": [{"serial": row["serial"], "tile": row["tile"], "instanceValues": row["instanceValues"]} for row in reciprocal],
        "world": {"mapsHeaderSha256": sha256_path(maps_header_path), "mapsTxtSha256": sha256_path(maps_txt_path)},
        "destinationScenePolicy": "require-explicit-hash-bound-cache",
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(document, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return document


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--source-object-contract", type=Path, required=True)
    parser.add_argument("--destination-object-contract", type=Path, required=True)
    parser.add_argument("--maps-header", type=Path, required=True)
    parser.add_argument("--destination-map", type=Path, required=True)
    parser.add_argument("--maps-txt", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(build(
        args.recipe, args.source_object_contract, args.destination_object_contract,
        args.maps_header, args.destination_map, args.maps_txt, args.output), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
