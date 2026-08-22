"""Prepare one recipe-pinned, data-driven interior cell scene."""

from __future__ import annotations

import hashlib
import json
import math
import os
import sys
from pathlib import Path

from bsa_archive import BsaArchive
from cell_catalog import BaseObject, CellCatalog, PlacedReference, Transform, scan_cell_catalog
from export_static_nif_gltf import export_static_nif


CELL_SCENE_SCHEMA = "opennv-cell-scene/v1"
CELL_RECIPE_SCHEMA = "opennv-cell-recipe/v1"


def form_id(value: int) -> str:
    return f"{value:08x}"


def recipe_path(recipe_id: str) -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / f"{recipe_id}.json"


def load_recipe(recipe_id: str) -> dict[str, object]:
    path = recipe_path(recipe_id)
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != CELL_RECIPE_SCHEMA or document.get("id") != recipe_id:
        raise ValueError(f"Invalid OpenNV cell recipe: {path}")
    return document


def selected_reference(base: BaseObject, recipe: dict[str, object]) -> bool:
    selection = recipe["selection"]
    prefixes = tuple(str(value).lower() for value in selection["modelPrefixes"])
    record_types = {str(value) for value in selection["includeBaseRecordTypes"]}
    return bool(base.model_path) and (base.model_path.startswith(prefixes) or base.record_type in record_types)


def yaw_only(transform: Transform) -> bool:
    return math.isclose(transform.rotation_radians[0], 0.0, abs_tol=1.0e-5) and math.isclose(
        transform.rotation_radians[1], 0.0, abs_tol=1.0e-5
    )


def godot_position(position: tuple[float, float, float], origin: tuple[float, float, float]) -> list[float]:
    delta = tuple(value - anchor for value, anchor in zip(position, origin))
    return [delta[0], delta[2], -delta[1]]


def _atomic_bytes(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def _atomic_json(path: Path, document: object) -> None:
    _atomic_bytes(path, (json.dumps(document, indent=2, sort_keys=True) + "\n").encode())


def _find_cell(catalog: CellCatalog, editor_id: str) -> int:
    matches = [cell.form_id for cell in catalog.cells.values() if cell.editor_id == editor_id]
    if len(matches) != 1:
        raise ValueError(f"Expected one CELL with editor ID {editor_id!r}, found {len(matches)}")
    return matches[0]


def _arrival_transform(catalog: CellCatalog, target_door_form_id: int) -> tuple[int, Transform]:
    incoming = [
        reference
        for reference in catalog.references
        if reference.teleport_destination_form_id == target_door_form_id
        and reference.teleport_destination_transform is not None
    ]
    if len(incoming) != 1:
        raise ValueError(
            f"Expected one incoming XTEL for door {target_door_form_id:08x}, found {len(incoming)}"
        )
    return incoming[0].form_id, incoming[0].teleport_destination_transform


def prepare_cell_scene(
    master_path: Path,
    meshes_path: Path,
    cache_root: Path,
    recipe: dict[str, object],
    master_sha256: str,
) -> dict[str, object]:
    expected_master = str(recipe["master"]["sha256"])
    if master_sha256 != expected_master:
        raise ValueError(f"Cell recipe master hash mismatch: expected={expected_master} actual={master_sha256}")

    catalog = scan_cell_catalog(master_path)
    cell_form_id = _find_cell(catalog, str(recipe["cellEditorId"]))
    cell = catalog.cells[cell_form_id]
    entry_door = int(str(recipe["entryDoorReferenceFormId"]), 16)
    source_door, arrival = _arrival_transform(catalog, entry_door)
    origin = arrival.position

    selected: list[tuple[PlacedReference, BaseObject]] = []
    skipped_non_yaw: list[str] = []
    for reference in catalog.references_for(cell_form_id):
        base = catalog.base_objects.get(reference.base_form_id)
        if base is None or not selected_reference(base, recipe):
            continue
        if bool(recipe["selection"]["yawOnly"]) and not yaw_only(reference.transform):
            skipped_non_yaw.append(form_id(reference.form_id))
            continue
        selected.append((reference, base))
    if not selected:
        raise ValueError(f"Cell recipe selected no references: {recipe['id']}")

    archive = BsaArchive(meshes_path)
    assets: dict[str, dict[str, object]] = {}
    compiler: dict[str, str] | None = None
    models = sorted({base.model_path for _, base in selected if base.model_path})
    for model_path in models:
        logical_path = "meshes\\" + model_path
        asset_id = hashlib.sha256(logical_path.encode()).hexdigest()[:20]
        member = archive.extract(logical_path)
        source_path = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
        _atomic_bytes(source_path, member.data)
        output_root = cache_root / "generated" / "cells" / str(recipe["id"]) / "assets"
        gltf_path = output_root / f"{asset_id}.gltf"
        sidecar_path = output_root / f"{asset_id}.opennv.json"
        sidecar = export_static_nif(source_path, member.logical_path, gltf_path, sidecar_path, strict=False)
        if compiler is None:
            compiler = sidecar["compiler"]
        elif compiler != sidecar["compiler"]:
            raise ValueError("Cell assets were produced by different compilers")
        assets[model_path] = {
            "id": asset_id,
            "logicalPath": member.logical_path,
            "sourceSha256": member.sha256,
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "surfaces": sidecar["coverage"]["surfaces"],
        }

    references = []
    for reference, base in selected:
        asset = assets[base.model_path]
        references.append(
            {
                "formId": form_id(reference.form_id),
                "baseFormId": form_id(reference.base_form_id),
                "baseRecordType": base.record_type,
                "baseEditorId": base.editor_id,
                "assetId": asset["id"],
                "positionGameUnits": list(reference.transform.position),
                "positionGodotUnits": godot_position(reference.transform.position, origin),
                "yawRadians": reference.transform.rotation_radians[2],
                "initiallyDisabled": bool(reference.flags & 0x00000800),
                "teleportDestinationFormId": (
                    form_id(reference.teleport_destination_form_id)
                    if reference.teleport_destination_form_id is not None
                    else None
                ),
            }
        )

    output_path = cache_root / "generated" / "cells" / str(recipe["id"]) / "cell-scene.json"
    document = {
        "schema": CELL_SCENE_SCHEMA,
        "status": "geometry-structure",
        "recipe": str(recipe["id"]),
        "source": {"master": master_path.name, "masterSha256": master_sha256},
        "compiler": compiler,
        "cell": {
            "formId": form_id(cell.form_id),
            "editorId": cell.editor_id,
            "interior": cell.interior,
        },
        "coordinates": {
            "source": "Gamebryo X-right/Y-forward/Z-up, radians",
            "target": "Godot X-right/Y-up/-Z-forward",
            "unitsToMeters": recipe["unitsToMeters"],
            "originGameUnits": list(origin),
        },
        "spawn": {
            "sourceDoorReferenceFormId": form_id(source_door),
            "targetDoorReferenceFormId": form_id(entry_door),
            "positionGameUnits": list(arrival.position),
            "positionGodotUnits": [0.0, 0.0, 0.0],
            "yawRadians": arrival.rotation_radians[2],
        },
        "proof": {
            "doorReferenceFormId": str(recipe["portalProofDoorReferenceFormId"]),
            "visibilityModel": "whole-cell-no-portal-culling",
        },
        "assets": sorted(assets.values(), key=lambda value: value["id"]),
        "references": references,
        "coverage": {
            "selectedReferences": len(selected),
            "exportedAssets": len(assets),
            "doors": sum(1 for _, base in selected if base.record_type == "DOOR"),
            "skippedNonYawReferences": skipped_non_yaw,
            "collision": "runtime-trimesh",
            "textures": "not-exported",
        },
    }
    _atomic_json(output_path, document)
    document["output"] = str(output_path.resolve())
    return document
