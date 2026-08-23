"""Emit a neutral, hash-pinned Fallout et Tu opening source contract.

This module deliberately stops at source identity and donor-asset relationships.
It does not extract retail assets, decode placed Fallout MAP objects, create a
Godot node, or claim that the opening is rendered or interactive.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import struct
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path, PurePosixPath
from typing import Any

from actor_catalog import ActorCatalog, scan_actor_catalog
from cell_catalog import BaseObject, CellCatalog, scan_cell_catalog


MAP_HEADER_SIZE = 0xEC
RECIPE_SCHEMA = "opennv-fo1-profile-recipe/v1"
CONTRACT_SCHEMA = "opennv-fo1-opening-source-contract/v1"


class Fo1ProfileError(ValueError):
    """Fail-closed recipe, source, or relationship error."""


@dataclass(frozen=True)
class MapHeader:
    version: int
    name: str
    enteringTile: int
    enteringElevation: int
    enteringRotation: int
    localVariables: int
    scriptIndex: int
    flags: int
    darkness: int
    globalVariables: int
    mapIndex: int
    lastVisitTime: int


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def parse_map_header(data: bytes) -> MapHeader:
    if len(data) < MAP_HEADER_SIZE:
        raise Fo1ProfileError(f"MAP header requires {MAP_HEADER_SIZE} bytes, got {len(data)}")

    version = struct.unpack_from(">i", data, 0x00)[0]
    if version not in {19, 20}:
        raise Fo1ProfileError(f"unsupported Fallout MAP version {version}")

    stored_name = data[0x04:0x14].split(b"\0", 1)[0]
    try:
        name = stored_name.decode("ascii")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("MAP name is not ASCII") from error
    if not name or not name.casefold().endswith(".map"):
        raise Fo1ProfileError(f"invalid MAP header name {name!r}")

    values = struct.unpack_from(">10i", data, 0x14)
    header = MapHeader(version, name, *values)
    if not 0 <= header.enteringTile < 40000:
        raise Fo1ProfileError(f"entering tile is outside the 200x200 hex grid: {header.enteringTile}")
    if not 0 <= header.enteringElevation <= 2:
        raise Fo1ProfileError(f"invalid entering elevation {header.enteringElevation}")
    if not 0 <= header.enteringRotation <= 5:
        raise Fo1ProfileError(f"invalid entering rotation {header.enteringRotation}")
    if header.localVariables < 0 or header.globalVariables < 0:
        raise Fo1ProfileError("MAP variable counts cannot be negative")
    if header.flags & ~0x0F:
        raise Fo1ProfileError(f"unsupported MAP flag bits 0x{header.flags & ~0x0F:08x}")
    if header.mapIndex < 0:
        raise Fo1ProfileError(f"invalid MAP index {header.mapIndex}")
    return header


def read_map_header(path: Path) -> MapHeader:
    with path.open("rb") as stream:
        return parse_map_header(stream.read(MAP_HEADER_SIZE))


def load_recipe(path: Path) -> dict[str, Any]:
    recipe = json.loads(path.read_text(encoding="utf-8"))
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise Fo1ProfileError(f"unsupported recipe schema {recipe.get('schema')!r}")
    if not recipe.get("id"):
        raise Fo1ProfileError("recipe id is required")
    if recipe.get("targetState") != "transported":
        raise Fo1ProfileError("the v1 profile can only target the transported state")
    if recipe.get("expectedActors") != []:
        raise Fo1ProfileError("actors are unsupported until the Fallout MAP object graph is decoded")
    return recipe


def resolve_owned_path(root: Path, relative_path: str) -> Path:
    normalized = relative_path.replace("\\", "/")
    parsed = PurePosixPath(normalized)
    if parsed.is_absolute() or any(part in {"", ".", ".."} for part in parsed.parts):
        raise Fo1ProfileError(f"owned-data path is not a safe relative path: {relative_path!r}")
    resolved_root = root.resolve()
    resolved = resolved_root.joinpath(*parsed.parts).resolve()
    if not resolved.is_relative_to(resolved_root):
        raise Fo1ProfileError(f"owned-data path escapes its root: {relative_path!r}")
    if not resolved.is_file():
        raise Fo1ProfileError(f"owned-data file does not exist: {resolved}")
    return resolved


def parse_form_id(value: str, label: str) -> int:
    if len(value) != 8:
        raise Fo1ProfileError(f"{label} must be exactly eight hexadecimal digits")
    try:
        return int(value, 16)
    except ValueError as error:
        raise Fo1ProfileError(f"{label} is not hexadecimal: {value!r}") from error


def verify_hash(path: Path, expected: str, label: str) -> str:
    actual = sha256_path(path)
    if actual != expected.casefold():
        raise Fo1ProfileError(f"{label} SHA-256 mismatch: expected {expected}, got {actual}")
    return actual


def _base_manifest(base: BaseObject) -> dict[str, Any]:
    return {
        "formId": f"{base.form_id:08x}",
        "recordType": base.record_type,
        "editorId": base.editor_id,
        "modelPath": base.model_path,
    }


def resolve_donor_cells(recipe: dict[str, Any], catalog: CellCatalog) -> list[dict[str, Any]]:
    resolved = []
    for donor in recipe["assetSource"]["donorCells"]:
        form_id = parse_form_id(donor["cellFormId"], f"{donor['role']} cellFormId")
        cell = catalog.cells.get(form_id)
        if cell is None:
            raise Fo1ProfileError(f"donor CELL {form_id:08x} was not found")
        if cell.editor_id != donor["cellEditorId"]:
            raise Fo1ProfileError(
                f"donor CELL {form_id:08x} EDID mismatch: expected {donor['cellEditorId']!r}, "
                f"got {cell.editor_id!r}"
            )

        prefixes = tuple(prefix.replace("/", "\\").casefold() for prefix in donor["modelPrefixes"])
        if not prefixes or any(not prefix for prefix in prefixes):
            raise Fo1ProfileError(f"donor CELL {cell.editor_id} has an empty model prefix")

        placements = []
        bases: dict[int, BaseObject] = {}
        prefix_placement_counts = {prefix: 0 for prefix in prefixes}
        for reference in catalog.references_for(form_id):
            base = catalog.base_objects.get(reference.base_form_id)
            if base is None or base.model_path is None:
                continue
            matched_prefix = next(
                (prefix for prefix in prefixes if base.model_path.casefold().startswith(prefix)), None
            )
            if matched_prefix is None:
                continue
            prefix_placement_counts[matched_prefix] += 1
            bases[base.form_id] = base
            placements.append(
                {
                    "referenceFormId": f"{reference.form_id:08x}",
                    "baseFormId": f"{base.form_id:08x}",
                    "modelPath": base.model_path,
                }
            )

        expected_placements = donor["expectedPlacementCount"]
        expected_bases = donor["expectedUniqueBaseCount"]
        if len(placements) != expected_placements:
            raise Fo1ProfileError(
                f"donor CELL {cell.editor_id} placement drift: expected {expected_placements}, "
                f"got {len(placements)}"
            )
        if len(bases) != expected_bases:
            raise Fo1ProfileError(
                f"donor CELL {cell.editor_id} base drift: expected {expected_bases}, got {len(bases)}"
            )

        resolved.append(
            {
                "role": donor["role"],
                "cellEditorId": cell.editor_id,
                "cellFormId": f"{cell.form_id:08x}",
                "modelPrefixes": list(prefixes),
                "placementCount": len(placements),
                "uniqueBaseCount": len(bases),
                "prefixPlacementCounts": prefix_placement_counts,
                "bases": [_base_manifest(base) for base in sorted(bases.values(), key=lambda item: item.form_id)],
                "placements": sorted(placements, key=lambda item: item["referenceFormId"]),
            }
        )
    return resolved


def resolve_explicit_bases(recipe: dict[str, Any], catalog: CellCatalog) -> list[dict[str, Any]]:
    resolved = []
    for expected in recipe["assetSource"].get("explicitBases", []):
        form_id = parse_form_id(expected["formId"], f"{expected['role']} formId")
        base = catalog.base_objects.get(form_id)
        if base is None:
            raise Fo1ProfileError(f"explicit base {form_id:08x} was not found")
        actual = _base_manifest(base)
        for key in ("recordType", "editorId", "modelPath"):
            if actual[key] != expected[key]:
                raise Fo1ProfileError(
                    f"explicit base {form_id:08x} {key} mismatch: expected {expected[key]!r}, "
                    f"got {actual[key]!r}"
                )
        resolved.append({"role": expected["role"], **actual})
    return resolved


def resolve_actor_mappings(recipe: dict[str, Any], catalog: ActorCatalog) -> dict[str, Any]:
    creatures = []
    for expected in recipe["assetSource"].get("creatureMappings", []):
        form_id = parse_form_id(expected["formId"], f"{expected['role']} formId")
        creature = catalog.creatures.get(form_id)
        if creature is None:
            raise Fo1ProfileError(f"creature base {form_id:08x} was not found")
        if creature.editor_id != expected["editorId"] or creature.skeleton_path != expected["skeletonPath"]:
            raise Fo1ProfileError(f"creature identity drift for {form_id:08x}")
        creatures.append(
            {
                "role": expected["role"],
                "formId": f"{form_id:08x}",
                "editorId": creature.editor_id,
                "name": creature.name,
                "skeletonPath": creature.skeleton_path,
            }
        )

    armor = []
    for expected in recipe["assetSource"].get("armorMappings", []):
        form_id = parse_form_id(expected["formId"], f"{expected['role']} formId")
        item = catalog.armor.get(form_id)
        if item is None:
            raise Fo1ProfileError(f"armor base {form_id:08x} was not found")
        if (
            item.editor_id != expected["editorId"]
            or item.male_model_path != expected["maleModelPath"]
            or item.female_model_path != expected["femaleModelPath"]
        ):
            raise Fo1ProfileError(f"armor identity drift for {form_id:08x}")
        armor.append(
            {
                "role": expected["role"],
                "formId": f"{form_id:08x}",
                "editorId": item.editor_id,
                "name": item.name,
                "maleModelPath": item.male_model_path,
                "femaleModelPath": item.female_model_path,
                "identityDelta": expected["identityDelta"],
            }
        )
    return {"armor": armor, "creatures": creatures}


def build_contract(recipe_path: Path, ettu_root: Path, fnv_data_root: Path) -> dict[str, Any]:
    recipe = load_recipe(recipe_path)

    source_map_recipe = recipe["source"]["map"]
    source_map = resolve_owned_path(ettu_root, source_map_recipe["relativePath"])
    source_map_hash = verify_hash(source_map, source_map_recipe["sha256"], "Et Tu source MAP")
    header = read_map_header(source_map)
    expected_header = source_map_recipe["header"]
    actual_header = asdict(header)
    if actual_header != expected_header:
        raise Fo1ProfileError(f"Et Tu source MAP header drift: expected {expected_header}, got {actual_header}")

    master_recipe = recipe["assetSource"]["master"]
    master_path = resolve_owned_path(fnv_data_root, master_recipe["file"])
    master_hash = verify_hash(master_path, master_recipe["sha256"], "FalloutNV.esm")

    archives = []
    for archive_recipe in recipe["assetSource"]["archives"]:
        archive_path = resolve_owned_path(fnv_data_root, archive_recipe["file"])
        archives.append(
            {
                "file": archive_recipe["file"],
                "bytes": archive_path.stat().st_size,
                "sha256": verify_hash(archive_path, archive_recipe["sha256"], archive_recipe["file"]),
            }
        )

    cell_catalog = scan_cell_catalog(master_path)
    actor_catalog = scan_actor_catalog(master_path)
    contract = {
        "schema": CONTRACT_SCHEMA,
        "recipe": {
            "id": recipe["id"],
            "file": recipe_path.name,
            "sha256": sha256_path(recipe_path),
        },
        "promotion": {
            "state": "transported",
            "rendered": False,
            "interactive": False,
            "parityReviewed": False,
            "headsetAccepted": False,
        },
        "source": {
            "package": recipe["source"]["package"],
            "map": {
                "relativePath": source_map_recipe["relativePath"],
                "bytes": source_map.stat().st_size,
                "sha256": source_map_hash,
                "header": actual_header,
            },
            "entry": recipe["entry"],
            "expectedActors": recipe["expectedActors"],
        },
        "assetSource": {
            "master": {
                "file": master_recipe["file"],
                "bytes": master_path.stat().st_size,
                "sha256": master_hash,
            },
            "archives": archives,
            "donorCells": resolve_donor_cells(recipe, cell_catalog),
            "explicitBases": resolve_explicit_bases(recipe, cell_catalog),
            **resolve_actor_mappings(recipe, actor_catalog),
        },
        "supported": recipe["supported"],
        "unsupported": recipe["unsupported"],
    }
    return contract


def write_contract(output: Path, contract: dict[str, Any]) -> tuple[str, Path]:
    sidecar = output.with_suffix(output.suffix + ".sha256")
    if output.exists() or sidecar.exists():
        raise Fo1ProfileError(f"refusing to overwrite existing proof output: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(contract, indent=2, sort_keys=True) + "\n").encode("utf-8")
    digest = hashlib.sha256(payload).hexdigest()

    with tempfile.NamedTemporaryFile(dir=output.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(payload)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(output)
    sidecar.write_text(f"{digest}  {output.name}\n", encoding="ascii")
    return digest, sidecar


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--ettu-root", type=Path, required=True)
    parser.add_argument("--fnv-data-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    contract = build_contract(
        args.recipe.resolve(),
        args.ettu_root.resolve(),
        args.fnv_data_root.resolve(),
    )
    digest, sidecar = write_contract(args.output.resolve(), contract)
    print(
        json.dumps(
            {
                "schema": contract["schema"],
                "output": str(args.output.resolve()),
                "outputSha256": digest,
                "sha256Sidecar": str(sidecar),
                "donorCells": len(contract["assetSource"]["donorCells"]),
                "eligibleUniqueBases": sum(
                    donor["uniqueBaseCount"] for donor in contract["assetSource"]["donorCells"]
                ),
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

