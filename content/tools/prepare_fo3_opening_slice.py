#!/usr/bin/env python3
"""Compile the owned Fallout 3 CG00/Vault 101 birth graph into a local contract."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import struct
import sys
from collections import Counter
from pathlib import Path

from actor_catalog import (
    ActorCatalog,
    ActorReference,
    HumanoidActor,
    resolve_actor_outfit_form_ids,
    scan_actor_catalog,
)
from actor_parity_graph import NPC_USE_TEMPLATE_TRAITS_ACTOR_FLAG, TEMPLATE_CATEGORY_FLAGS
from bsa_archive import BsaArchive, canonical_member_path
from cell_catalog import INITIALLY_DISABLED_RECORD_FLAG, cell_parent_form_id
from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring
from plugin_stack import file_sha256, find_case_insensitive_file


RECIPE_SCHEMA = "opennv-fo3-opening-slice-recipe/v1"
OUTPUT_SCHEMA = "opennv-fo3-opening-slice/v1"
OUTPUT_NAME = "fo3-vault101-cg00-birth.json"
FORM_ID_BYTES = 4
FORM_ID_RADIX = 16
REFERENCE_TRANSFORM_BYTES = 24
REFERENCE_TRANSFORM_FLOATS = 6
DEFAULT_REFERENCE_SCALE = 1.0
REFERENCE_SCALE_BYTES = 4
CELL_RECORD = "CELL"
QUEST_RECORD = "QUST"
REFERENCE_RECORD_TYPES = frozenset({"REFR", "ACHR", "ACRE"})


def default_recipe_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    matches = []
    for path in (root / "recipes").glob("*.json"):
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if document.get("schema") == RECIPE_SCHEMA:
            matches.append(path)
    if len(matches) != 1:
        raise ValueError(f"Expected one Fallout 3 opening-slice recipe, found {len(matches)}")
    return matches[0]


def _atomic_bytes(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def _atomic_json(path: Path, document: object) -> str:
    payload = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    _atomic_bytes(path, payload)
    digest = hashlib.sha256(payload).hexdigest()
    _atomic_bytes(path.with_suffix(path.suffix + ".sha256"), f"{digest}  {path.name}\n".encode("ascii"))
    return digest


def _load_recipe(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if (
        document.get("schema") != RECIPE_SCHEMA
        or document.get("id") != path.stem
        or document.get("campaign") != "Fallout3"
        or not isinstance(document.get("source"), dict)
        or not isinstance(document.get("cell"), dict)
        or not isinstance(document.get("openingQuest"), dict)
        or not isinstance(document["openingQuest"].get("expectedCommands"), list)
        or not document["openingQuest"]["expectedCommands"]
        or not isinstance(document.get("startGraph"), dict)
    ):
        raise ValueError(f"Invalid Fallout 3 opening-slice recipe: {path}")
    return document


def _resolve_data_root(selected: Path, master_name: str) -> Path:
    root = selected.resolve()
    if not root.is_dir():
        raise FileNotFoundError(f"Fallout 3 installation does not exist: {root}")
    try:
        find_case_insensitive_file(root, master_name)
        return root
    except FileNotFoundError:
        data_directories = [
            path for path in root.iterdir() if path.is_dir() and path.name.casefold() == "data"
        ]
        if len(data_directories) != 1:
            raise FileNotFoundError("Select the Fallout 3 installation or its Data directory")
        find_case_insensitive_file(data_directories[0], master_name)
        return data_directories[0]


def _verify_source(data_root: Path, definition: object) -> Path:
    row = dict(definition)
    path = find_case_insensitive_file(data_root, str(row["file"]))
    actual = file_sha256(path)
    expected = str(row["sha256"]).lower()
    if actual != expected:
        raise ValueError(
            f"Fallout 3 source hash differs for {path.name}: expected={expected} actual={actual}"
        )
    return path


def _values(record: Record) -> dict[str, list[bytes]]:
    result: dict[str, list[bytes]] = {}
    for subrecord in iter_subrecords(record):
        result.setdefault(subrecord.signature, []).append(subrecord.data)
    return result


def _one(values: dict[str, list[bytes]], signature: str, record: Record) -> bytes:
    matches = values.get(signature, [])
    if len(matches) != 1:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} must contain one {signature}"
        )
    return matches[0]


def _editor_id(values: dict[str, list[bytes]]) -> str:
    matches = values.get("EDID", [])
    return zstring(matches[0]) if len(matches) == 1 else ""


def _form_id(data: bytes, description: str) -> int:
    if len(data) != FORM_ID_BYTES:
        raise ValueError(f"{description} must contain one FormID")
    return struct.unpack("<I", data)[0]


def _form_text(value: int) -> str:
    return f"{value:08x}"


def _transform(values: dict[str, list[bytes]], record: Record) -> dict[str, object]:
    raw = _one(values, "DATA", record)
    if len(raw) != REFERENCE_TRANSFORM_BYTES:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} has malformed DATA transform"
        )
    unpacked = struct.unpack(f"<{REFERENCE_TRANSFORM_FLOATS}f", raw)
    scales = values.get("XSCL", [])
    if len(scales) > 1 or (scales and len(scales[0]) != REFERENCE_SCALE_BYTES):
        raise ValueError(f"{record.signature} {record.form_id:08x} has malformed XSCL")
    scale = struct.unpack("<f", scales[0])[0] if scales else DEFAULT_REFERENCE_SCALE
    if scale <= 0:
        raise ValueError(f"{record.signature} {record.form_id:08x} has invalid scale")
    return {
        "positionGameUnits": list(unpacked[:3]),
        "rotationRadians": list(unpacked[3:]),
        "scale": scale,
    }


def _canonical_resource(path: str, root: str) -> str:
    logical = canonical_member_path(path)
    prefix = root + "\\"
    return logical if logical.startswith(prefix) else prefix + logical


def _resource_row(archive: BsaArchive, path: str, root: str) -> dict[str, object]:
    logical = _canonical_resource(path, root)
    member = archive.extract(logical)
    return {
        "logicalPath": member.logical_path,
        "bytes": len(member.data),
        "sha256": member.sha256,
        "sourceArchive": archive.archive.name,
    }


def _scan_cell(
    master: Path,
    cell_definition: dict[str, object],
) -> tuple[dict[str, object], list[dict[str, object]], dict[int, dict[str, object]]]:
    cell_form_id = int(str(cell_definition["formId"]), FORM_ID_RADIX)
    cell_matches = []
    references = []
    for record in iter_plugin_records(master, frozenset({CELL_RECORD, *REFERENCE_RECORD_TYPES})):
        if record.signature == CELL_RECORD:
            if record.form_id == cell_form_id:
                cell_matches.append(record)
            continue
        if cell_parent_form_id(record) != cell_form_id:
            continue
        values = _values(record)
        base_form_id = _form_id(_one(values, "NAME", record), "reference NAME")
        references.append(
            {
                "recordType": record.signature,
                "formId": _form_text(record.form_id),
                "editorId": _editor_id(values),
                "baseFormId": _form_text(base_form_id),
                "flags": record.flags,
                "initiallyDisabled": bool(record.flags & INITIALLY_DISABLED_RECORD_FLAG),
                "transform": _transform(values, record),
            }
        )
    if len(cell_matches) != 1:
        raise ValueError(f"Expected one Fallout 3 CELL {_form_text(cell_form_id)}")
    cell_record = cell_matches[0]
    cell_values = _values(cell_record)
    editor_id = _editor_id(cell_values)
    if editor_id != str(cell_definition["editorId"]):
        raise ValueError(f"Fallout 3 opening CELL differs: {editor_id}")
    xcll = _one(cell_values, "XCLL", cell_record)
    if len(xcll) != int(cell_definition["xcllBytes"]):
        raise ValueError("Fallout 3 opening CELL XCLL size differs")
    if hashlib.sha256(xcll).hexdigest() != str(cell_definition["xcllSha256"]):
        raise ValueError("Fallout 3 opening CELL XCLL identity differs")
    counts = Counter(row["recordType"] for row in references)
    expected_counts = {str(key): int(value) for key, value in cell_definition["expectedReferenceTypes"].items()}
    if len(references) != int(cell_definition["expectedReferences"]) or dict(sorted(counts.items())) != dict(sorted(expected_counts.items())):
        raise ValueError(
            f"Fallout 3 opening CELL reference inventory differs: total={len(references)} types={dict(counts)}"
        )

    base_ids = {int(row["baseFormId"], FORM_ID_RADIX) for row in references}
    base_records: dict[int, dict[str, object]] = {}
    for record in iter_plugin_records(master):
        if record.form_id not in base_ids:
            continue
        values = _values(record)
        models = []
        for signature in ("MODL", "MOD2", "MOD3", "MOD4"):
            for raw in values.get(signature, []):
                path = zstring(raw).replace("/", "\\").lstrip("\\").lower()
                if path.endswith(".nif"):
                    models.append({"field": signature, "path": path})
        base_records[record.form_id] = {
            "recordType": record.signature,
            "formId": _form_text(record.form_id),
            "editorId": _editor_id(values),
            "recordDataSha256": hashlib.sha256(record.data).hexdigest(),
            "models": models,
        }
    expected_unique = int(cell_definition["expectedUniqueBaseFormIds"])
    expected_resolved = int(cell_definition["expectedResolvedBaseRecords"])
    if len(base_ids) != expected_unique or len(base_records) != expected_resolved:
        raise ValueError(
            "Fallout 3 opening CELL base inventory differs: "
            f"unique={len(base_ids)} resolved={len(base_records)}"
        )
    implicit = {_form_text(value) for value in base_ids - set(base_records)}
    expected_implicit = {str(value).lower() for value in cell_definition["implicitEngineBaseFormIds"]}
    if implicit != expected_implicit:
        raise ValueError(f"Fallout 3 opening CELL implicit base inventory differs: {sorted(implicit)}")
    return (
        {
            "formId": _form_text(cell_form_id),
            "editorId": editor_id,
            "name": zstring(_one(cell_values, "FULL", cell_record)),
            "flags": cell_record.flags,
            "interior": bool(_one(cell_values, "DATA", cell_record)[0] & 1),
            "xcll": {
                "bytes": len(xcll),
                "sha256": hashlib.sha256(xcll).hexdigest(),
            },
        },
        sorted(references, key=lambda row: int(row["formId"], FORM_ID_RADIX)),
        base_records,
    )


def _quest_start(master: Path, definition: dict[str, object]) -> dict[str, object]:
    form_id = int(str(definition["formId"]), FORM_ID_RADIX)
    matches = [
        record
        for record in iter_plugin_records(master, frozenset({QUEST_RECORD}))
        if record.form_id == form_id
    ]
    if len(matches) != 1:
        raise ValueError(f"Expected one Fallout 3 opening quest {_form_text(form_id)}")
    record = matches[0]
    values = _values(record)
    if _editor_id(values) != str(definition["editorId"]):
        raise ValueError("Fallout 3 opening quest identity differs")
    stage_number = None
    stage_sources = []
    for subrecord in iter_subrecords(record):
        if subrecord.signature == "INDX":
            stage_number = int.from_bytes(subrecord.data, "little")
        elif subrecord.signature == "SCTX" and stage_number == int(definition["stage"]):
            stage_sources.append(subrecord.data)
    if len(stage_sources) != 1:
        raise ValueError("Fallout 3 opening quest stage zero is ambiguous")
    source = stage_sources[0]
    digest = hashlib.sha256(source).hexdigest()
    if digest != str(definition["stageSourceSha256"]):
        raise ValueError("Fallout 3 opening quest stage zero identity differs")
    return {
        "editorId": str(definition["editorId"]),
        "formId": _form_text(form_id),
        "stage": int(definition["stage"]),
        "stageSourceSha256": digest,
        "commands": definition["expectedCommands"],
    }


def _require_actor_reference(
    catalog: ActorCatalog,
    reference_id: int,
    cell_id: int,
) -> tuple[ActorReference, HumanoidActor]:
    matches = [
        reference
        for reference in catalog.references_for(cell_id)
        if reference.form_id == reference_id and reference.record_type == "ACHR"
    ]
    if len(matches) != 1 or matches[0].actor_form_id not in catalog.actors:
        raise ValueError(f"Fallout 3 opening actor reference does not resolve: {reference_id:08x}")
    return matches[0], catalog.actors[matches[0].actor_form_id]


def _category_source(catalog: ActorCatalog, actor: HumanoidActor, category: str) -> HumanoidActor:
    category_flag = TEMPLATE_CATEGORY_FLAGS[category]
    current = actor
    seen = set()
    while current.template_form_id is not None:
        if current.form_id in seen:
            raise ValueError(f"Fallout 3 actor template cycle at {current.form_id:08x}")
        seen.add(current.form_id)
        delegates = bool(current.template_flags & category_flag)
        if category == "traits":
            delegates = delegates and bool(current.actor_flags & NPC_USE_TEMPLATE_TRAITS_ACTOR_FLAG)
        if not delegates:
            return current
        target = catalog.actors.get(current.template_form_id)
        if target is None:
            raise ValueError(f"Fallout 3 actor template is unresolved: {current.template_form_id:08x}")
        current = target
    return current


def _actor_record_row(catalog: ActorCatalog, actor: HumanoidActor) -> dict[str, object]:
    return {
        "formId": _form_text(actor.form_id),
        "editorId": actor.editor_id,
        "name": actor.name,
        "recordDataSha256": catalog.record_data_sha256["NPC_"][actor.form_id],
        "templateFormId": _form_text(actor.template_form_id) if actor.template_form_id else None,
        "templateFlags": actor.template_flags,
        "actorFlags": actor.actor_flags,
    }


def _doctor_graph(
    catalog: ActorCatalog,
    definition: dict[str, object],
    cell_id: int,
    meshes: BsaArchive,
    textures: BsaArchive,
) -> dict[str, object]:
    reference_id = int(str(definition["referenceFormId"]), FORM_ID_RADIX)
    expected_base_id = int(str(definition["baseFormId"]), FORM_ID_RADIX)
    reference, actor = _require_actor_reference(catalog, reference_id, cell_id)
    if actor.form_id != expected_base_id or actor.editor_id != str(definition["baseEditorId"]):
        raise ValueError("Fallout 3 doctor actor base identity differs")
    category_sources = {
        category: _category_source(catalog, actor, category)
        for category in TEMPLATE_CATEGORY_FLAGS
    }
    traits = category_sources["traits"]
    model = category_sources["model"]
    inventory = category_sources["inventory"]
    race = catalog.races.get(traits.race_form_id or 0)
    hair = catalog.parts.get(traits.hair_form_id or 0)
    eyes = catalog.parts.get(traits.eyes_form_id or 0)
    head_parts = [catalog.parts.get(value) for value in traits.head_part_form_ids]
    if (
        race is None
        or hair is None
        or eyes is None
        or model.skeleton_path is None
        or any(part is None for part in head_parts)
    ):
        raise ValueError("Fallout 3 doctor actor appearance graph is incomplete")
    head_models = race.female_head_models if traits.female else race.male_head_models
    body_models = race.female_body_models if traits.female else race.male_body_models
    head_textures = race.female_head_textures if traits.female else race.male_head_textures
    body_textures = race.female_body_textures if traits.female else race.male_body_textures
    outfit_ids = resolve_actor_outfit_form_ids(catalog, inventory)
    outfits = []
    model_paths = {model.skeleton_path}
    texture_paths = set()
    model_paths.update(value for value in head_models if value and value.lower().endswith(".nif"))
    model_paths.update(value for value in body_models if value and value.lower().endswith(".nif"))
    texture_paths.update(
        texture
        for model_path, texture in zip(head_models, head_textures)
        if model_path and model_path.lower().endswith(".nif") and texture
    )
    texture_paths.update(
        texture
        for model_path, texture in zip(body_models, body_textures)
        if model_path and model_path.lower().endswith(".nif") and texture
    )
    if hair.model_path:
        model_paths.add(hair.model_path)
    if hair.texture_path:
        texture_paths.add(hair.texture_path)
    if eyes.model_path:
        model_paths.add(eyes.model_path)
    if eyes.texture_path:
        texture_paths.add(eyes.texture_path)
    for part in head_parts:
        assert part is not None
        if part.model_path:
            model_paths.add(part.model_path)
        if part.texture_path:
            texture_paths.add(part.texture_path)
    for form_id in outfit_ids:
        armor = catalog.armor.get(form_id)
        if armor is None:
            raise ValueError(f"Fallout 3 doctor outfit does not resolve: {form_id:08x}")
        paths = [
            armor.male_model_path,
            armor.male_ground_model_path,
            armor.female_model_path,
            armor.female_ground_model_path,
        ]
        model_paths.update(value for value in paths if value)
        outfits.append(
            {
                "formId": _form_text(form_id),
                "editorId": armor.editor_id,
                "name": armor.name,
                "recordDataSha256": catalog.record_data_sha256["ARMO"][form_id],
                "modelPaths": [value for value in paths if value],
            }
        )
    return {
        "reference": {
            "formId": _form_text(reference.form_id),
            "editorId": str(definition["referenceEditorId"]),
            "cellFormId": _form_text(reference.cell_form_id),
            "flags": reference.flags,
            "initiallyDisabled": reference.initially_disabled,
            "transform": {
                "positionGameUnits": list(reference.position),
                "rotationRadians": list(reference.rotation_radians),
                "scale": reference.scale,
            },
        },
        "base": _actor_record_row(catalog, actor),
        "templateChain": [
            _actor_record_row(catalog, catalog.actors[value])
            for value in [actor.form_id, actor.template_form_id]
            if value is not None
        ],
        "categorySources": {
            category: _form_text(source.form_id)
            for category, source in category_sources.items()
        },
        "appearance": {
            "female": traits.female,
            "race": {
                "formId": _form_text(race.form_id),
                "editorId": race.editor_id,
                "recordDataSha256": catalog.record_data_sha256["RACE"][race.form_id],
            },
            "hair": {
                "formId": _form_text(hair.form_id),
                "editorId": hair.editor_id,
                "recordDataSha256": catalog.record_data_sha256[hair.record_type][hair.form_id],
            },
            "eyes": {
                "formId": _form_text(eyes.form_id),
                "editorId": eyes.editor_id,
                "recordDataSha256": catalog.record_data_sha256[eyes.record_type][eyes.form_id],
            },
            "headParts": [
                {
                    "formId": _form_text(part.form_id),
                    "editorId": part.editor_id,
                    "recordType": part.record_type,
                    "recordDataSha256": catalog.record_data_sha256[part.record_type][part.form_id],
                }
                for part in head_parts
                if part is not None
            ],
            "faceGen": {
                "symmetricGeometryCount": len(traits.face_symmetric_geometry),
                "symmetricGeometrySha256": hashlib.sha256(struct.pack(f"<{len(traits.face_symmetric_geometry)}f", *traits.face_symmetric_geometry)).hexdigest(),
                "asymmetricGeometryCount": len(traits.face_asymmetric_geometry),
                "asymmetricGeometrySha256": hashlib.sha256(struct.pack(f"<{len(traits.face_asymmetric_geometry)}f", *traits.face_asymmetric_geometry)).hexdigest(),
                "symmetricTextureCount": len(traits.face_symmetric_texture),
                "symmetricTextureSha256": hashlib.sha256(struct.pack(f"<{len(traits.face_symmetric_texture)}f", *traits.face_symmetric_texture)).hexdigest(),
            },
            "outfits": outfits,
        },
        "resources": {
            "models": [_resource_row(meshes, path, "meshes") for path in sorted(model_paths)],
            "textures": [_resource_row(textures, path, "textures") for path in sorted(texture_paths)],
        },
    }


def _require_named_reference(
    references: list[dict[str, object]],
    form_id: str,
    editor_id: str,
) -> dict[str, object]:
    matches = [row for row in references if row["formId"] == form_id.lower()]
    if len(matches) != 1 or matches[0]["editorId"] != editor_id:
        raise ValueError(f"Fallout 3 opening reference does not resolve: {editor_id}/{form_id}")
    return matches[0]


def compile_opening_slice(
    data_root: Path,
    output_root: Path,
    recipe_path: Path,
) -> dict[str, object]:
    recipe = _load_recipe(recipe_path)
    source = dict(recipe["source"])
    resolved_data_root = _resolve_data_root(data_root, str(dict(source["master"])["file"]))
    master = _verify_source(resolved_data_root, source["master"])
    meshes_path = _verify_source(resolved_data_root, source["meshesArchive"])
    textures_path = _verify_source(resolved_data_root, source["texturesArchive"])
    meshes = BsaArchive(meshes_path)
    textures = BsaArchive(textures_path)
    cell_definition = dict(recipe["cell"])
    cell, references, bases = _scan_cell(master, cell_definition)
    cell_model_paths = sorted(
        {
            str(model["path"])
            for base in bases.values()
            for model in base["models"]
        }
    )
    if len(cell_model_paths) != int(cell_definition["expectedModelResources"]):
        raise ValueError(
            f"Fallout 3 opening CELL model inventory differs: {len(cell_model_paths)}"
        )
    cell_resources = [_resource_row(meshes, path, "meshes") for path in cell_model_paths]

    start_definition = dict(recipe["startGraph"])
    player_spawn = _require_named_reference(
        references,
        str(start_definition["playerSpawnReferenceFormId"]),
        "CG00PlayerStartMarker",
    )
    actor_catalog = scan_actor_catalog(master)
    start_actors = []
    doctor_definition = None
    for raw in start_definition["actors"]:
        definition = dict(raw)
        actor_reference = _require_named_reference(
            references,
            str(definition["referenceFormId"]),
            str(definition["referenceEditorId"]),
        )
        marker = _require_named_reference(
            references,
            str(definition["startMarkerFormId"]),
            str(definition["startMarkerEditorId"]),
        )
        if actor_reference["baseFormId"] != str(definition["baseFormId"]).lower():
            raise ValueError(f"Fallout 3 opening actor base differs: {definition['role']}")
        actor_base = actor_catalog.actors.get(
            int(str(definition["baseFormId"]), FORM_ID_RADIX)
        )
        if actor_base is None or actor_base.editor_id != str(definition["baseEditorId"]):
            raise ValueError(f"Fallout 3 opening actor identity differs: {definition['role']}")
        start_actors.append(
            {
                "role": definition["role"],
                "reference": actor_reference,
                "startMarker": marker,
            }
        )
        if definition["role"] == "doctor":
            doctor_definition = definition
    if doctor_definition is None:
        raise ValueError("Fallout 3 opening recipe has no doctor actor")
    doctor = _doctor_graph(
        actor_catalog,
        doctor_definition,
        int(str(cell_definition["formId"]), FORM_ID_RADIX),
        meshes,
        textures,
    )
    document = {
        "schema": OUTPUT_SCHEMA,
        "status": "transported",
        "recipe": {
            "id": recipe["id"],
            "sha256": file_sha256(recipe_path),
        },
        "source": {
            "dataRoot": str(resolved_data_root),
            "master": {
                "file": master.name,
                "bytes": master.stat().st_size,
                "sha256": file_sha256(master),
            },
            "meshesArchive": {
                "file": meshes_path.name,
                "bytes": meshes_path.stat().st_size,
                "sha256": file_sha256(meshes_path),
            },
            "texturesArchive": {
                "file": textures_path.name,
                "bytes": textures_path.stat().st_size,
                "sha256": file_sha256(textures_path),
            },
        },
        "promotion": {
            "transported": True,
            "rendered": False,
            "interactive": False,
            "parityReviewed": False,
            "headsetAccepted": False,
        },
        "questStart": _quest_start(master, dict(recipe["openingQuest"])),
        "cell": cell,
        "startGraph": {
            "playerSpawn": player_spawn,
            "actors": start_actors,
        },
        "doctorActor": doctor,
        "cellGraph": {
            "references": references,
            "bases": [bases[value] for value in sorted(bases)],
            "implicitEngineBaseFormIds": cell_definition["implicitEngineBaseFormIds"],
            "modelResources": cell_resources,
        },
        "coverage": {
            "references": len(references),
            "referenceTypes": dict(sorted(Counter(row["recordType"] for row in references).items())),
            "uniqueBaseFormIds": len({row["baseFormId"] for row in references}),
            "resolvedBaseRecords": len(bases),
            "cellModelResources": len(cell_resources),
            "doctorModelResources": len(doctor["resources"]["models"]),
            "doctorTextureResources": len(doctor["resources"]["textures"]),
        },
        "blockers": [
            "fo3-vault101-godot-scene-not-compiled",
            "fo3-opening-command-interpreter-not-implemented",
        ],
    }
    output = output_root.resolve() / "generated" / "fallout3" / OUTPUT_NAME
    digest = _atomic_json(output, document)
    return {
        "output": str(output),
        "outputSha256": digest,
        "manifest": document,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        result = compile_opening_slice(
            args.data_root.resolve(),
            args.output_root.resolve(),
            args.recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_FO3_OPENING_SLICE_ERROR {error}", file=sys.stderr)
        return 2
    manifest = result["manifest"]
    print(
        "OPENNV_FO3_OPENING_SLICE "
        + json.dumps(
            {
                "output": result["output"],
                "outputSha256": result["outputSha256"],
                "cell": manifest["cell"]["editorId"],
                "references": manifest["coverage"]["references"],
                "models": manifest["coverage"]["cellModelResources"],
                "doctor": manifest["doctorActor"]["base"]["editorId"],
                "blockers": manifest["blockers"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
