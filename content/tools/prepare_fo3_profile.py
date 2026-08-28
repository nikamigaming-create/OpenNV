#!/usr/bin/env python3
"""Register a legal Fallout 3 installation as a hash-bound local profile."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import struct
import sys
from io import BytesIO
from pathlib import Path

from actor_catalog import scan_actor_catalog
from bsa_archive import BsaArchive, canonical_member_path
from facegen import compose_facegen_coordinates
from opening_catalog import _prepare_runtime_video
from plugin_records import iter_plugin_records, iter_subrecords, zstring
from plugin_stack import build_plugin_stack, file_sha256, find_case_insensitive_file
from prepare_fo3_opening_slice import (
    compile_opening_slice,
    default_recipe_path as default_opening_slice_recipe_path,
)
from runtime_configuration import load_runtime_configuration
from texture_pipeline import decode_dds


RECIPE_SCHEMA = "opennv-fo3-owned-profile-recipe/v1"
PROFILE_SCHEMA = "opennv-owned-game-profile/v1"
PROFILE_STATUS = "registered-owned-profile"
PROFILE_ID_HEX_CHARACTERS = 20
FORM_ID_HEX_CHARACTERS = 8
FORM_ID_BYTES = 4
FORM_ID_RADIX = 16
PNG_MAXIMUM_COMPRESSION_LEVEL = 9
STAGE_INDEX_BYTES = frozenset({2, 4})
QUEST_RECORD = "QUST"
SCRIPT_RECORD = "SCPT"
MESSAGE_RECORD = "MESG"
PLUGIN_HEADER_RECORD = "TES4"
PACKAGE_RECORD = "PACK"
IDLE_RECORD = "IDLE"
PLAY_BINK_PATTERN = re.compile(r'\bplayBink\s+"(?P<path>[^"]+\.bik)"', re.IGNORECASE)
SEX_CHANGE_PATTERN = re.compile(
    r"\b(?:if|elseif)\s+button\s*==\s*(?P<index>\d+)\s+"
    r"player\.sexChange\s+(?P<sex>male|female)\s+1\b",
    re.IGNORECASE | re.DOTALL,
)
ADD_SCRIPT_PACKAGE_PATTERN = re.compile(
    r"\bplayer\.addScriptPackage\s+(?P<package>[A-Za-z_][A-Za-z0-9_]*)\b",
    re.IGNORECASE,
)
CG00_NEXT_STAGE_PATTERN = re.compile(
    r"\bif\s+getStage\s+CG00\s*>=\s*(?P<source>\d+)\s*&&\s*"
    r"GetStageDone\s+CG00\s+(?P<target>\d+)\s*==\s*0\b"
    r"(?P<body>.*?)\bendif\b",
    re.IGNORECASE | re.DOTALL,
)
SET_CG00_STAGE_PATTERN = re.compile(r"\bsetstage\s+CG00\s+(?P<stage>\d+)\b", re.IGNORECASE)
MATCH_RACE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.MatchRace\s+player$",
    re.IGNORECASE,
)
MATCH_FACE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.MatchFaceGeometry\s+"
    r"player\s+(?P<template>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
PACKAGE_DATA_BYTES = 12
PACKAGE_LOCATION_BYTES = 12
PACKAGE_IDLE_FLAG_BYTES = frozenset({1, 4})
PACKAGE_IDLE_COUNT_BYTES = frozenset({1, 4})
PACKAGE_IDLE_TIMER_BYTES = 4
PACKAGE_EVENT_NAMES = {"POBA": "begin", "POEA": "end", "POCA": "change"}
RACE_DATA_BYTES = 36
RACE_FLAGS_OFFSET = 32
RACE_PLAYABLE_FLAG = 0x01
HAIR_PLAYABLE_FLAG = 0x01
HAIR_FEMALE_FLAG = 0x02
HAIR_MALE_FLAG = 0x04
FACEGEN_SYMMETRIC_GEOMETRY_FLOATS = 50
FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS = 30
FACEGEN_SYMMETRIC_TEXTURE_FLOATS = 50


def default_recipe_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    candidates = []
    for path in (root / "recipes").glob("*.json"):
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if document.get("schema") == RECIPE_SCHEMA and document.get("campaign") == "Fallout3":
            candidates.append(path)
    if len(candidates) != 1:
        raise ValueError(
            "Expected exactly one Fallout 3 owned-profile recipe, "
            f"found {len(candidates)}"
        )
    return candidates[0]


def atomic_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    os.replace(temporary, path)


def atomic_bytes(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def _case_insensitive_directory(root: Path, expected_name: str) -> Path | None:
    matches = [
        path
        for path in root.iterdir()
        if path.is_dir() and path.name.casefold() == expected_name.casefold()
    ]
    if len(matches) > 1:
        raise ValueError(f"Fallout 3 installation contains duplicate {expected_name} directories")
    return matches[0] if matches else None


def resolve_installation(selected_root: Path, recipe: dict[str, object]) -> tuple[Path, Path]:
    selected = selected_root.resolve()
    if not selected.is_dir():
        raise FileNotFoundError(f"Selected Fallout 3 folder does not exist: {selected}")
    install = dict(recipe["install"])
    master_name = str(install["masterFile"])
    try:
        find_case_insensitive_file(selected, master_name)
        return selected.parent, selected
    except FileNotFoundError:
        data_root = _case_insensitive_directory(selected, str(install["dataDirectoryName"]))
        if data_root is None:
            raise FileNotFoundError(
                "Select the Fallout 3 installation folder or its Data folder; "
                f"{master_name} was not found."
            )
        find_case_insensitive_file(data_root, master_name)
        return selected, data_root


def load_recipe(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != RECIPE_SCHEMA or document.get("id") != path.stem:
        raise ValueError(f"Unexpected Fallout 3 owned-profile recipe: {path}")
    install = document.get("install")
    menu = document.get("mainMenu")
    opening = document.get("opening")
    if (
        document.get("campaign") != "Fallout3"
        or not isinstance(install, dict)
        or not isinstance(menu, dict)
        or not isinstance(opening, dict)
        or not isinstance(install.get("requiredArchives"), list)
        or not install["requiredArchives"]
        or not isinstance(opening.get("quests"), list)
        or not opening["quests"]
    ):
        raise ValueError(f"Fallout 3 owned-profile recipe is incomplete: {path}")
    return document


def _file_row(path: Path, *, role: str | None = None) -> dict[str, object]:
    row: dict[str, object] = {
        "file": path.name,
        "source": str(path.resolve()),
        "bytes": path.stat().st_size,
        "sha256": file_sha256(path),
    }
    if role is not None:
        row["role"] = role
    return row


def _editor_id(record: object) -> str | None:
    return next(
        (
            zstring(subrecord.data)
            for subrecord in iter_subrecords(record)
            if subrecord.signature == "EDID"
        ),
        None,
    )


def _text_values(record: object, signature: str) -> list[str]:
    return [
        zstring(subrecord.data)
        for subrecord in iter_subrecords(record)
        if subrecord.signature == signature
    ]


def _form_id(value: int) -> str:
    return f"{value:0{FORM_ID_HEX_CHARACTERS}x}"


def _single_subrecord(record: object, signature: str) -> bytes:
    matches = [
        subrecord.data
        for subrecord in iter_subrecords(record)
        if subrecord.signature == signature
    ]
    if len(matches) != 1:
        raise ValueError(
            f"Fallout 3 {record.signature} {_form_id(record.form_id)} "
            f"has {len(matches)} {signature} values"
        )
    return matches[0]


def _form_id_list(record: object, signature: str) -> list[int]:
    payload = _single_subrecord(record, signature)
    if not payload or len(payload) % FORM_ID_BYTES:
        raise ValueError(
            f"Fallout 3 {record.signature} {_form_id(record.form_id)} "
            f"has an invalid {signature} list"
        )
    return list(struct.unpack(f"<{len(payload) // FORM_ID_BYTES}I", payload))


def _float_contract(values: tuple[float, ...], expected_count: int) -> dict[str, object]:
    if len(values) != expected_count or not all(math.isfinite(value) for value in values):
        raise ValueError("Fallout 3 FaceGen default coordinates are incomplete")
    payload = struct.pack(f"<{len(values)}f", *values)
    return {
        "count": len(values),
        "values": list(values),
        "sha256": hashlib.sha256(payload).hexdigest(),
    }


def _extract_profile_texture(
    archive: BsaArchive,
    archive_sha256: str,
    logical_path: str,
    profile_root: Path,
    cache: dict[str, dict[str, object]],
) -> dict[str, object]:
    canonical = canonical_member_path(logical_path)
    if not canonical.startswith("textures\\"):
        canonical = f"textures\\{canonical}"
    if canonical in cache:
        return cache[canonical]
    member = archive.extract(canonical)
    suffix = Path(canonical.replace("\\", "/")).suffix.casefold()
    if not suffix:
        raise ValueError(f"Fallout 3 appearance texture has no extension: {canonical}")
    output = (
        profile_root.resolve()
        / "generated"
        / "fallout3"
        / "appearance"
        / "textures"
        / f"{member.sha256[:PROFILE_ID_HEX_CHARACTERS]}{suffix}"
    )
    if output.exists():
        if not output.is_file() or file_sha256(output) != member.sha256:
            raise ValueError(f"Fallout 3 appearance texture cache differs: {output}")
    else:
        atomic_bytes(output, member.data)
    preview = output.with_suffix(".png")
    image = decode_dds(member.data, False)
    encoded = BytesIO()
    image.save(encoded, format="PNG", compress_level=PNG_MAXIMUM_COMPRESSION_LEVEL)
    preview_payload = encoded.getvalue()
    preview_sha256 = hashlib.sha256(preview_payload).hexdigest()
    if preview.exists():
        if not preview.is_file() or file_sha256(preview) != preview_sha256:
            raise ValueError(f"Fallout 3 appearance preview cache differs: {preview}")
    else:
        atomic_bytes(preview, preview_payload)
    row = {
        "logicalPath": member.logical_path,
        "sourceArchive": archive.archive.name,
        "sourceArchiveSha256": archive_sha256,
        "sourceBytes": len(member.data),
        "sourceSha256": member.sha256,
        "output": str(output),
        "outputBytes": output.stat().st_size,
        "outputSha256": file_sha256(output),
        "previewOutput": str(preview),
        "previewOutputBytes": preview.stat().st_size,
        "previewOutputSha256": file_sha256(preview),
        "previewWidth": image.width,
        "previewHeight": image.height,
    }
    cache[canonical] = row
    return row


def _appearance_ui_contract(
    recipe: dict[str, object],
    menu_members: dict[str, object],
    texture_archive: BsaArchive,
    texture_archive_sha256: str,
    profile_root: Path,
    texture_cache: dict[str, dict[str, object]],
) -> dict[str, object]:
    definition = dict(dict(recipe["opening"])["appearanceUi"])
    document_path = canonical_member_path(str(definition["document"]))
    member = menu_members.get(document_path)
    if member is None:
        raise ValueError("Fallout 3 appearance menu XML was not admitted")
    text = member.data.decode("cp1252")
    menu_name = str(definition["menuName"])
    panel_name = str(definition["panelName"])
    if f'<menu name="{menu_name}">' not in text:
        raise ValueError("Fallout 3 appearance menu identity differs")
    panel = re.search(
        rf'<rect\s+name="{re.escape(panel_name)}">(?P<body>.*?)<image\s+name="RSM_Background">',
        text,
        re.DOTALL,
    )
    list_item = re.search(
        r'<template\s+name="RSM_list_item_template">(?P<body>.*?)</template>',
        text,
        re.DOTALL,
    )
    if panel is None or list_item is None:
        raise ValueError("Fallout 3 appearance menu layout owners are absent")

    def dimension(body: str, name: str) -> int:
        match = re.search(rf'<{name}>\s*(?P<value>\d+)\s*</{name}>', body)
        if match is None:
            raise ValueError(f"Fallout 3 appearance menu {name} is absent")
        return int(match.group("value"))

    observed = {
        "panelWidth": dimension(panel.group("body"), "width"),
        "panelHeight": dimension(panel.group("body"), "height"),
        "listItemWidth": dimension(list_item.group("body"), "width"),
        "listItemHeight": dimension(list_item.group("body"), "height"),
    }
    for key, value in observed.items():
        if value != int(definition[key]):
            raise ValueError(
                f"Fallout 3 appearance menu {key} differs: "
                f"expected={definition[key]} actual={value}"
            )
    background_path = canonical_member_path(str(definition["backgroundTexture"]))
    if background_path.removeprefix("textures\\") not in text.casefold():
        raise ValueError("Fallout 3 appearance menu background identity differs")
    return {
        "document": document_path,
        "documentSha256": member.sha256,
        "menuName": menu_name,
        "panelName": panel_name,
        **observed,
        "backgroundTexture": _extract_profile_texture(
            texture_archive,
            texture_archive_sha256,
            background_path,
            profile_root,
            texture_cache,
        ),
    }


def _appearance_inventory(
    master: Path,
    recipe: dict[str, object],
    character_selection: dict[str, object],
    menu_member_payloads: dict[str, object],
    texture_archive: BsaArchive,
    texture_archive_sha256: str,
    profile_root: Path,
) -> dict[str, object]:
    selection_recipe = dict(dict(recipe["opening"])["characterSelection"])
    player_form_id = int(str(selection_recipe["playerBaseFormId"]), FORM_ID_RADIX)
    player_editor_id = str(selection_recipe["playerEditorId"])
    catalog = scan_actor_catalog(master)
    player = catalog.actors.get(player_form_id)
    if player is None or player.editor_id != player_editor_id:
        raise ValueError("Fallout 3 player appearance source identity differs")
    records = list(iter_plugin_records(master, frozenset({"RACE", "HAIR", "EYES"})))
    record_by_form = {record.form_id: record for record in records}
    texture_cache: dict[str, dict[str, object]] = {}

    def appearance_option(form_id: int, expected_type: str) -> dict[str, object]:
        record = record_by_form.get(form_id)
        part = catalog.parts.get(form_id)
        if record is None or record.signature != expected_type or part is None:
            raise ValueError(
                f"Fallout 3 appearance part does not resolve: {expected_type} {_form_id(form_id)}"
            )
        if part.texture_path is None:
            raise ValueError(
                f"Fallout 3 appearance part has no texture: {expected_type} {_form_id(form_id)}"
            )
        return {
            "formId": _form_id(form_id),
            "recordType": expected_type,
            "editorId": part.editor_id,
            "label": part.name,
            "modelPath": part.model_path,
            "recordSha256": catalog.record_data_sha256[expected_type][form_id],
            "texture": _extract_profile_texture(
                texture_archive,
                texture_archive_sha256,
                part.texture_path,
                profile_root,
                texture_cache,
            ),
        }

    races = []
    for record in records:
        if record.signature != "RACE":
            continue
        data = _single_subrecord(record, "DATA")
        if len(data) != RACE_DATA_BYTES:
            raise ValueError(f"Fallout 3 RACE DATA size differs: {_form_id(record.form_id)}")
        flags = struct.unpack_from("<I", data, RACE_FLAGS_OFFSET)[0]
        if not flags & RACE_PLAYABLE_FLAG:
            continue
        race = catalog.races.get(record.form_id)
        if race is None:
            raise ValueError(f"Fallout 3 playable RACE was not decoded: {_form_id(record.form_id)}")
        hair_form_ids = _form_id_list(record, "HNAM")
        eye_form_ids = _form_id_list(record, "ENAM")
        younger_form_id = struct.unpack("<I", _single_subrecord(record, "YNAM"))[0]
        younger = record_by_form.get(younger_form_id)
        if younger is None or younger.signature != "RACE":
            raise ValueError("Fallout 3 playable race has no child-race join")
        sex_contracts: dict[str, object] = {}
        for sex, sex_flag in (("male", HAIR_MALE_FLAG), ("female", HAIR_FEMALE_FLAG)):
            hair_options = []
            for form_id in hair_form_ids:
                part_record = record_by_form.get(form_id)
                if part_record is None or part_record.signature != "HAIR":
                    raise ValueError("Fallout 3 race HNAM does not resolve to HAIR")
                part_flags = _single_subrecord(part_record, "DATA")
                if len(part_flags) != 1:
                    raise ValueError("Fallout 3 HAIR DATA size differs")
                if part_flags[0] & HAIR_PLAYABLE_FLAG and part_flags[0] & sex_flag:
                    hair_options.append(appearance_option(form_id, "HAIR"))
            eye_options = []
            for form_id in eye_form_ids:
                part_record = record_by_form.get(form_id)
                if part_record is None or part_record.signature != "EYES":
                    raise ValueError("Fallout 3 race ENAM does not resolve to EYES")
                part_flags = _single_subrecord(part_record, "DATA")
                if len(part_flags) != 1:
                    raise ValueError("Fallout 3 EYES DATA size differs")
                if part_flags[0] & HAIR_PLAYABLE_FLAG:
                    eye_options.append(appearance_option(form_id, "EYES"))
            if not hair_options or not eye_options:
                raise ValueError("Fallout 3 playable race has no sex-aware hair or eye defaults")
            baseline_symmetric = (
                race.male_face_symmetric_geometry
                if sex == "male"
                else race.female_face_symmetric_geometry
            )
            baseline_asymmetric = (
                race.male_face_asymmetric_geometry
                if sex == "male"
                else race.female_face_asymmetric_geometry
            )
            head_textures = (
                race.male_head_textures if sex == "male" else race.female_head_textures
            )
            if not head_textures or head_textures[0] is None:
                raise ValueError("Fallout 3 playable race has no sex-aware head texture")
            composed_symmetric = compose_facegen_coordinates(
                player.face_symmetric_geometry,
                baseline_symmetric,
            )
            composed_asymmetric = compose_facegen_coordinates(
                player.face_asymmetric_geometry,
                baseline_asymmetric,
            )
            sex_contracts[sex] = {
                "headTexture": _extract_profile_texture(
                    texture_archive,
                    texture_archive_sha256,
                    str(head_textures[0]),
                    profile_root,
                    texture_cache,
                ),
                "hairOptions": hair_options,
                "eyeOptions": eye_options,
                "defaultHairFormId": hair_options[0]["formId"],
                "defaultEyesFormId": eye_options[0]["formId"],
                "faceGenDefaults": {
                    "symmetricGeometry": _float_contract(
                        composed_symmetric,
                        FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
                    ),
                    "asymmetricGeometry": _float_contract(
                        composed_asymmetric,
                        FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
                    ),
                    "symmetricTexture": _float_contract(
                        player.face_symmetric_texture,
                        FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
                    ),
                },
            }
        races.append(
            {
                "formId": _form_id(record.form_id),
                "editorId": _editor_id(record),
                "label": _text_values(record, "FULL")[0],
                "flags": flags,
                "childRaceFormId": _form_id(younger_form_id),
                "recordSha256": catalog.record_data_sha256["RACE"][record.form_id],
                "sex": sex_contracts,
            }
        )
    if not races or player.race_form_id not in {
        int(row["formId"], FORM_ID_RADIX) for row in races
    }:
        raise ValueError("Fallout 3 player default does not join the playable race set")
    default_race = next(
        row
        for row in races
        if int(row["formId"], FORM_ID_RADIX) == player.race_form_id
    )
    male_default = dict(default_race["sex"])["male"]
    if (
        male_default["defaultHairFormId"] != _form_id(player.hair_form_id or 0)
        or male_default["defaultEyesFormId"] != _form_id(player.eyes_form_id or 0)
    ):
        raise ValueError("Fallout 3 source-order appearance defaults differ from Player NPC_")
    appearance = dict(character_selection["appearance"])
    return {
        **appearance,
        "schema": "opennv-fo3-cg00-appearance/v1",
        "status": "source-backed-default-selection",
        "player": {
            "formId": _form_id(player.form_id),
            "editorId": player.editor_id,
            "recordSha256": catalog.record_data_sha256["NPC_"][player.form_id],
            "defaultRaceFormId": _form_id(player.race_form_id or 0),
            "defaultHairColorRgba": list(player.hair_color_rgba),
            "defaultHairLength": player.hair_length,
        },
        "ui": _appearance_ui_contract(
            recipe,
            menu_member_payloads,
            texture_archive,
            texture_archive_sha256,
            profile_root,
            texture_cache,
        ),
        "races": races,
        "preview": "owned-head-hair-eye-source-textures-not-a-3d-face-render",
    }


def _script_source(record: object) -> str:
    sources = _text_values(record, "SCTX")
    if len(sources) != 1:
        raise ValueError(f"Fallout 3 script {_form_id(record.form_id)} has ambiguous source")
    return sources[0]


def _source_commands(source: str) -> list[str]:
    return [
        command
        for raw_line in source.splitlines()
        if (command := raw_line.split(";", 1)[0].strip())
    ]


def _compile_cg00_section4_transition(
    records: tuple[object, ...],
    selection: dict[str, object],
    accepted_stage: int,
    accepted_source: str,
    accepted_stage_sources: dict[int, list[str]],
) -> dict[str, object]:
    package_names = [
        match.group("package")
        for match in ADD_SCRIPT_PACKAGE_PATTERN.finditer(accepted_source)
    ]
    if len(package_names) != 1:
        raise ValueError("Fallout 3 owned appearance acceptance package is ambiguous")
    package_name = package_names[0]
    package_recipe = dict(selection["section4Package"])
    if package_name.casefold() != str(package_recipe["editorId"]).casefold():
        raise ValueError("Fallout 3 CG00 Section 4 package identity differs")

    package_records = [
        record
        for record in records
        if record.signature == PACKAGE_RECORD
        and (_editor_id(record) or "").casefold() == package_name.casefold()
    ]
    if len(package_records) != 1:
        raise ValueError("Fallout 3 CG00 Section 4 package does not resolve uniquely")
    package = package_records[0]
    expected_package_form = int(str(package_recipe["formId"]), FORM_ID_RADIX)
    if package.form_id != expected_package_form:
        raise ValueError("Fallout 3 CG00 Section 4 package FormID differs")

    package_data = _single_subrecord(package, "PKDT")
    if len(package_data) != PACKAGE_DATA_BYTES:
        raise ValueError("Fallout 3 CG00 Section 4 PKDT layout is unsupported")
    flags, package_type, _unused, procedure_flags, type_flags, _unknown = struct.unpack(
        "<IBBHHH", package_data
    )
    location_data = _single_subrecord(package, "PLDT")
    if len(location_data) != PACKAGE_LOCATION_BYTES:
        raise ValueError("Fallout 3 CG00 Section 4 PLDT layout is unsupported")
    location_type, location_form_id, radius = struct.unpack("<III", location_data)
    expected_location_form = int(
        str(package_recipe["locationReferenceFormId"]), FORM_ID_RADIX
    )
    if location_form_id != expected_location_form:
        raise ValueError("Fallout 3 CG00 Section 4 location reference differs")

    idle_flags_data = _single_subrecord(package, "IDLF")
    idle_count_data = _single_subrecord(package, "IDLC")
    idle_timer_data = _single_subrecord(package, "IDLT")
    if (
        len(idle_flags_data) not in PACKAGE_IDLE_FLAG_BYTES
        or len(idle_count_data) not in PACKAGE_IDLE_COUNT_BYTES
        or len(idle_timer_data) != PACKAGE_IDLE_TIMER_BYTES
    ):
        raise ValueError("Fallout 3 CG00 Section 4 idle layout is unsupported")
    idle_flags = int.from_bytes(idle_flags_data, "little")
    idle_count = int.from_bytes(idle_count_data, "little")
    idle_timer = struct.unpack("<f", idle_timer_data)[0]
    if not math.isfinite(idle_timer):
        raise ValueError("Fallout 3 CG00 Section 4 idle timer is invalid")
    idle_form_ids = _form_id_list(package, "IDLA")
    if idle_count != len(idle_form_ids) or idle_count == 0:
        raise ValueError("Fallout 3 CG00 Section 4 idle count differs")

    idle_records = {
        record.form_id: record for record in records if record.signature == IDLE_RECORD
    }

    def idle_row(form_id: int) -> dict[str, object]:
        idle = idle_records.get(form_id)
        if idle is None:
            raise ValueError(
                f"Fallout 3 CG00 Section 4 IDLE does not resolve: {_form_id(form_id)}"
            )
        models = _text_values(idle, "MODL")
        if len(models) != 1 or not models[0].casefold().endswith(".kf"):
            raise ValueError("Fallout 3 CG00 Section 4 IDLE model is unsupported")
        return {
            "formId": _form_id(form_id),
            "editorId": _editor_id(idle),
            "modelPath": canonical_member_path(f"meshes\\{models[0]}"),
            "recordSha256": hashlib.sha256(idle.data).hexdigest(),
        }

    events: dict[str, dict[str, object] | None] = {}
    pending_event: str | None = None
    for subrecord in iter_subrecords(package):
        if subrecord.signature in PACKAGE_EVENT_NAMES:
            pending_event = PACKAGE_EVENT_NAMES[subrecord.signature]
            if pending_event in events:
                raise ValueError("Fallout 3 CG00 Section 4 package event is duplicated")
        elif subrecord.signature == "INAM" and pending_event is not None:
            if len(subrecord.data) != FORM_ID_BYTES:
                raise ValueError("Fallout 3 CG00 Section 4 package event IDLE is invalid")
            event_form_id = struct.unpack("<I", subrecord.data)[0]
            events[pending_event] = idle_row(event_form_id) if event_form_id else None
            pending_event = None
    if pending_event is not None or set(events) != set(PACKAGE_EVENT_NAMES.values()):
        raise ValueError("Fallout 3 CG00 Section 4 package events are incomplete")

    trigger_matches: list[tuple[object, re.Match[str]]] = []
    trigger_threshold_stage = int(selection["appearanceStage"])
    for script in (record for record in records if record.signature == SCRIPT_RECORD):
        for match in CG00_NEXT_STAGE_PATTERN.finditer(_script_source(script)):
            if int(match.group("source")) != trigger_threshold_stage:
                continue
            stage_commands = [
                int(stage_match.group("stage"))
                for stage_match in SET_CG00_STAGE_PATTERN.finditer(match.group("body"))
            ]
            if stage_commands == [int(match.group("target"))]:
                trigger_matches.append((script, match))
    if len(trigger_matches) != 1:
        raise ValueError("Fallout 3 CG00 post-appearance stage trigger is ambiguous")
    trigger_script, trigger = trigger_matches[0]
    target_stage = int(trigger.group("target"))
    target_sources = accepted_stage_sources.get(target_stage, [])
    if not target_sources:
        raise ValueError("Fallout 3 CG00 post-appearance stage result is absent")
    target_source = "\n".join(target_sources)
    unsupported_commands = []
    for command in _source_commands(target_source):
        if match := MATCH_RACE_PATTERN.fullmatch(command):
            unsupported_commands.append(
                {"kind": "matchRace", "subject": match.group("subject"), "target": "player"}
            )
            continue
        if match := MATCH_FACE_PATTERN.fullmatch(command):
            unsupported_commands.append(
                {
                    "kind": "matchFaceGeometry",
                    "subject": match.group("subject"),
                    "target": "player",
                    "template": match.group("template"),
                }
            )
            continue
        raise ValueError(
            f"Fallout 3 CG00 stage {target_stage} uses an unsupported command: {command}"
        )
    if not unsupported_commands:
        raise ValueError("Fallout 3 CG00 post-appearance stage result is empty")

    return {
        "schema": "opennv-fo3-cg00-player-package-transition/v1",
        "status": "source-backed-package-activation",
        "sourceStage": accepted_stage,
        "command": f"player.addScriptPackage {package_name}",
        "package": {
            "formId": _form_id(package.form_id),
            "editorId": package_name,
            "recordSha256": hashlib.sha256(package.data).hexdigest(),
            "flags": flags,
            "type": package_type,
            "procedureFlags": procedure_flags,
            "typeSpecificFlags": type_flags,
            "location": {
                "type": location_type,
                "referenceFormId": _form_id(location_form_id),
                "referenceEditorId": str(package_recipe["locationReferenceEditorId"]),
                "radius": radius,
            },
            "idleSelection": {
                "flags": idle_flags,
                "count": idle_count,
                "timerSeconds": idle_timer,
                "idles": [idle_row(form_id) for form_id in idle_form_ids],
            },
            "events": events,
        },
        "nextStageTrigger": {
            "scriptEditorId": _editor_id(trigger_script),
            "scriptFormId": _form_id(trigger_script.form_id),
            "scriptSourceSha256": hashlib.sha256(
                _script_source(trigger_script).encode("cp1252")
            ).hexdigest(),
            "condition": (
                f"getStage CG00 >= {trigger_threshold_stage} && "
                f"GetStageDone CG00 {target_stage} == 0"
            ),
            "thresholdStage": trigger_threshold_stage,
            "command": f"setstage CG00 {target_stage}",
            "targetStage": target_stage,
        },
        "nextStageResult": {
            "stage": target_stage,
            "stageSourceSha256": hashlib.sha256(target_source.encode("cp1252")).hexdigest(),
            "commands": unsupported_commands,
            "runtimeReady": False,
            "blocker": "fo3-cg00-stage-65-parent-race-face-runtime-not-implemented",
        },
    }


def _bind_cg00_package_animations(
    transition: dict[str, object],
    meshes_archive: BsaArchive,
    meshes_archive_sha256: str,
) -> None:
    package = dict(transition["package"])
    idle_selection = dict(package["idleSelection"])
    events = dict(package["events"])
    idle_rows = [
        *list(idle_selection["idles"]),
        *(row for row in events.values() if row is not None),
    ]
    assets: dict[str, dict[str, object]] = {}
    for row in idle_rows:
        idle = dict(row)
        form_id = str(idle["formId"])
        if form_id in assets:
            continue
        member = meshes_archive.extract(str(idle["modelPath"]))
        assets[form_id] = {
            **idle,
            "sourceArchive": meshes_archive.archive.name,
            "sourceArchiveSha256": meshes_archive_sha256,
            "sourceBytes": len(member.data),
            "sourceSha256": member.sha256,
        }
    package["animationSources"] = list(assets.values())
    transition["package"] = package


def _quest_inventory(master: Path, opening: dict[str, object]) -> tuple[list[dict[str, object]], dict[str, object]]:
    records = tuple(
        iter_plugin_records(
            master,
            frozenset(
                {
                    QUEST_RECORD,
                    SCRIPT_RECORD,
                    MESSAGE_RECORD,
                    PLUGIN_HEADER_RECORD,
                    PACKAGE_RECORD,
                    IDLE_RECORD,
                }
            ),
        )
    )
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id is not None:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    quest_rows = []
    scripts_by_quest: dict[str, tuple[str, dict[int, list[str]]]] = {}
    for expected in opening["quests"]:
        definition = dict(expected)
        editor_id = str(definition["editorId"])
        matches = [
            record
            for record in by_editor.get(editor_id.casefold(), [])
            if record.signature == QUEST_RECORD
        ]
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 opening quest does not resolve uniquely: {editor_id}")
        quest = matches[0]
        expected_form_id = str(definition["formId"]).casefold()
        if _form_id(quest.form_id) != expected_form_id:
            raise ValueError(
                f"Fallout 3 opening quest FormID differs: {editor_id} "
                f"expected={expected_form_id} actual={_form_id(quest.form_id)}"
            )
        subrecords = tuple(iter_subrecords(quest))
        script_links = [
            struct.unpack_from("<I", value.data)[0]
            for value in subrecords
            if value.signature == "SCRI" and len(value.data) >= FORM_ID_BYTES
        ]
        if len(script_links) != 1 or script_links[0] not in by_form:
            raise ValueError(f"Fallout 3 opening quest script is ambiguous: {editor_id}")
        quest_script = by_form[script_links[0]]
        if quest_script.signature != SCRIPT_RECORD:
            raise ValueError(f"Fallout 3 opening quest script has the wrong record type: {editor_id}")
        quest_script_source = _script_source(quest_script)

        stage_sources: dict[int, list[str]] = {}
        stage = None
        for subrecord in subrecords:
            if subrecord.signature == "INDX":
                if len(subrecord.data) not in STAGE_INDEX_BYTES:
                    raise ValueError(f"Fallout 3 opening stage index has an unexpected size: {editor_id}")
                stage = int.from_bytes(subrecord.data, "little")
            elif subrecord.signature == "SCTX" and stage is not None:
                stage_sources.setdefault(stage, []).append(zstring(subrecord.data))

        all_stage_source = "\n".join(
            source
            for stage_number in sorted(stage_sources)
            for source in stage_sources[stage_number]
        )
        next_quest = definition.get("nextQuest")
        if next_quest is not None and re.search(
            rf"\bsetstage\s+{re.escape(str(next_quest))}\s+0\b",
            all_stage_source,
            re.IGNORECASE,
        ) is None:
            raise ValueError(f"Fallout 3 opening quest transition is absent: {editor_id}->{next_quest}")
        transition_video = definition.get("transitionVideo")
        authored_videos = sorted(
            {match.group("path") for match in PLAY_BINK_PATTERN.finditer(all_stage_source)},
            key=str.casefold,
        )
        if transition_video is not None and not any(
            value.casefold() == str(transition_video).casefold()
            for value in authored_videos
        ):
            raise ValueError(f"Fallout 3 opening transition movie is absent: {editor_id}")

        title = _text_values(quest, "FULL")
        script_source_bytes = quest_script_source.encode("cp1252")
        stage_source_bytes = all_stage_source.encode("cp1252")
        quest_rows.append(
            {
                "editorId": editor_id,
                "formId": _form_id(quest.form_id),
                "title": title[0] if len(title) == 1 else "",
                "script": {
                    "editorId": _editor_id(quest_script),
                    "formId": _form_id(quest_script.form_id),
                    "sourceSha256": hashlib.sha256(script_source_bytes).hexdigest(),
                },
                "stages": sorted(stage_sources),
                "stageSourceSha256": hashlib.sha256(stage_source_bytes).hexdigest(),
                "nextQuest": next_quest,
                "transitionVideos": authored_videos,
            }
        )
        scripts_by_quest[editor_id.casefold()] = (quest_script_source, stage_sources)

    selection = dict(opening["characterSelection"])
    selection_quest = str(selection["questEditorId"])
    quest_script_source, stage_sources = scripts_by_quest[selection_quest.casefold()]
    sex_message_id = str(selection["sexMessageEditorId"])
    sex_messages = [
        record
        for record in by_editor.get(sex_message_id.casefold(), [])
        if record.signature == MESSAGE_RECORD
    ]
    if len(sex_messages) != 1 or re.search(
        rf"\bShowMessage\s+{re.escape(sex_message_id)}\b",
        quest_script_source,
        re.IGNORECASE,
    ) is None:
        raise ValueError("Fallout 3 owned sex-selection contract does not resolve")
    sex_title = _text_values(sex_messages[0], "FULL")
    sex_choices = _text_values(sex_messages[0], "ITXT")
    if len(sex_title) != 1 or not sex_choices:
        raise ValueError("Fallout 3 owned sex-selection message has no choices")
    sex_mapping = {
        int(match.group("index")): match.group("sex").casefold()
        for match in SEX_CHANGE_PATTERN.finditer(quest_script_source)
    }
    if (
        set(sex_mapping) != set(range(len(sex_choices)))
        or set(sex_mapping.values()) != {"male", "female"}
    ):
        raise ValueError("Fallout 3 owned sex choices do not map uniquely to engine sexes")

    name_stage = int(selection["nameStage"])
    appearance_stage = int(selection["appearanceStage"])
    appearance_entered_stage = int(selection["appearanceMenuEnteredStage"])
    appearance_accepted_stage = int(selection["appearanceAcceptedStage"])
    if re.search(
        r"\bGetPlayerName\b",
        "\n".join(stage_sources.get(name_stage, [])),
        re.IGNORECASE,
    ) is None:
        raise ValueError("Fallout 3 owned name-selection command is absent")
    if re.search(
        r"\bShowRaceMenu\b",
        "\n".join(stage_sources.get(appearance_stage, [])),
        re.IGNORECASE,
    ) is None:
        raise ValueError("Fallout 3 owned appearance-selection command is absent")
    if not all(
        stage in stage_sources
        for stage in (appearance_entered_stage, appearance_accepted_stage)
    ):
        raise ValueError("Fallout 3 owned appearance menu convergence stages are absent")
    accepted_source = "\n".join(stage_sources[appearance_accepted_stage])
    transition = _compile_cg00_section4_transition(
        records,
        selection,
        appearance_accepted_stage,
        accepted_source,
        stage_sources,
    )

    character_selection = {
        "questEditorId": selection_quest,
        "sex": {
            "messageEditorId": sex_message_id,
            "messageFormId": _form_id(sex_messages[0].form_id),
            "choiceCount": len(sex_choices),
            "title": sex_title[0],
            "choices": [
                {"label": label, "engineSex": sex_mapping[index]}
                for index, label in enumerate(sex_choices)
            ],
        },
        "name": {"stage": name_stage, "command": "GetPlayerName"},
        "appearance": {
            "stage": appearance_stage,
            "command": "ShowRaceMenu",
            "menuEnteredStage": appearance_entered_stage,
            "acceptedStage": appearance_accepted_stage,
            "acceptedStageCommand": str(transition["command"]),
            "stageSourceSha256": {
                str(stage): hashlib.sha256(
                    "\n".join(stage_sources[stage]).encode("cp1252")
                ).hexdigest()
                for stage in (
                    appearance_stage,
                    appearance_entered_stage,
                    appearance_accepted_stage,
                )
            },
        },
        "section4Transition": transition,
    }
    return quest_rows, character_selection


def _ini_settings(path: Path, rows: list[object]) -> list[dict[str, str]]:
    section = ""
    values: dict[tuple[str, str], list[str]] = {}
    for raw_line in path.read_text(encoding="cp1252").splitlines():
        line = raw_line.strip()
        if not line or line.startswith((";", "#")):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip()
            continue
        if "=" not in line:
            continue
        key, value = (part.strip() for part in line.split("=", 1))
        values.setdefault((section.casefold(), key.casefold()), []).append(value)
    result = []
    for raw in rows:
        row = dict(raw)
        section = str(row["section"])
        key = str(row["key"])
        matches = values.get((section.casefold(), key.casefold()), [])
        if len(matches) != 1 or not matches[0]:
            raise ValueError(f"Fallout 3 INI setting does not resolve uniquely: {section}.{key}")
        result.append({"section": section, "key": key, "value": matches[0]})
    return result


def _case_insensitive_descendant(root: Path, relative_path: str) -> Path:
    current = root.resolve()
    for part in relative_path.replace("\\", "/").split("/"):
        matches = [
            candidate
            for candidate in current.iterdir()
            if candidate.name.casefold() == part.casefold()
        ]
        if len(matches) != 1:
            raise FileNotFoundError(
                f"Fallout 3 loose path does not resolve uniquely: {current} / {part}"
            )
        current = matches[0]
    if not current.is_file():
        raise FileNotFoundError(f"Fallout 3 loose asset is not a file: {current}")
    return current


def prepare_profile(data_root: Path, profile_root: Path, recipe_path: Path) -> dict[str, object]:
    recipe = load_recipe(recipe_path)
    configuration = load_runtime_configuration()
    install_root, resolved_data_root = resolve_installation(data_root, recipe)
    install = dict(recipe["install"])
    master = find_case_insensitive_file(resolved_data_root, str(install["masterFile"]))
    default_ini = find_case_insensitive_file(install_root, str(install["defaultIniFile"]))

    base_stack = build_plugin_stack(resolved_data_root, [master.name])
    if len(base_stack) != 1 or base_stack[0].masters:
        raise ValueError("Fallout 3 master identity is invalid")

    archives = []
    archive_by_role: dict[str, Path] = {}
    for raw in install["requiredArchives"]:
        row = dict(raw)
        role = str(row["role"])
        archive = find_case_insensitive_file(resolved_data_root, str(row["file"]))
        BsaArchive(archive)
        archives.append(_file_row(archive, role=role))
        archive_by_role[role] = archive

    dlc_rows = []
    plugin_names = [master.name]
    for raw in install["optionalDlc"]:
        definition = dict(raw)
        names = [str(definition["plugin"]), *(str(value) for value in definition["archives"])]
        present = []
        for name in names:
            try:
                present.append(find_case_insensitive_file(resolved_data_root, name))
            except FileNotFoundError:
                pass
        if present and len(present) != len(names):
            raise ValueError(f"Fallout 3 DLC installation is partial: {definition['id']}")
        if not present:
            dlc_rows.append({"id": definition["id"], "available": False})
            continue
        plugin_names.append(present[0].name)
        for archive in present[1:]:
            BsaArchive(archive)
        dlc_rows.append(
            {
                "id": definition["id"],
                "available": True,
                "plugin": _file_row(present[0]),
                "archives": [_file_row(path) for path in present[1:]],
            }
        )
    plugin_stack = build_plugin_stack(resolved_data_root, plugin_names)

    menu = dict(recipe["mainMenu"])
    ui_archive = BsaArchive(archive_by_role[str(menu["uiArchiveRole"])])
    menu_members = []
    menu_member_payloads: dict[str, object] = {}
    for logical_path in menu["requiredMembers"]:
        canonical = canonical_member_path(str(logical_path))
        member = ui_archive.extract(canonical)
        menu_member_payloads[member.logical_path] = member
        menu_members.append(
            {
                "logicalPath": member.logical_path,
                "bytes": len(member.data),
                "sha256": member.sha256,
                "sourceArchive": archive_by_role[str(menu["uiArchiveRole"])].name,
            }
        )
    texture_archive = BsaArchive(archive_by_role[str(menu["textureArchiveRole"])])
    menu_textures = []
    for logical_path in menu["requiredTextureMembers"]:
        member = texture_archive.extract(canonical_member_path(str(logical_path)))
        menu_textures.append(
            {
                "logicalPath": member.logical_path,
                "bytes": len(member.data),
                "sha256": member.sha256,
                "sourceArchive": archive_by_role[str(menu["textureArchiveRole"])].name,
            }
        )
    ini_rows = _ini_settings(default_ini, list(menu["iniSettings"]))
    music_setting = dict(menu["musicSetting"])
    music_values = [
        row["value"]
        for row in ini_rows
        if row["section"].casefold() == str(music_setting["section"]).casefold()
        and row["key"].casefold() == str(music_setting["key"]).casefold()
    ]
    if len(music_values) != 1:
        raise ValueError("Fallout 3 main-menu music setting is ambiguous")
    music_root = _case_insensitive_directory(resolved_data_root, str(menu["musicDirectoryName"]))
    if music_root is None:
        raise FileNotFoundError("Fallout 3 Music directory is absent")
    main_menu_music = _file_row(
        _case_insensitive_descendant(music_root, music_values[0]),
        role="mainMenuMusic",
    )

    opening = dict(recipe["opening"])
    quests, character_selection = _quest_inventory(master, opening)
    meshes_role = "meshes"
    meshes_archive_path = archive_by_role[meshes_role]
    section4_transition = dict(character_selection["section4Transition"])
    _bind_cg00_package_animations(
        section4_transition,
        BsaArchive(meshes_archive_path),
        next(str(row["sha256"]) for row in archives if row["role"] == meshes_role),
    )
    character_selection["section4Transition"] = section4_transition
    appearance_contract = _appearance_inventory(
        master,
        recipe,
        character_selection,
        menu_member_payloads,
        texture_archive,
        next(
            str(row["sha256"])
            for row in archives
            if row["role"] == str(menu["textureArchiveRole"])
        ),
        profile_root,
    )
    character_selection["appearance"] = appearance_contract
    video_root = _case_insensitive_directory(resolved_data_root, str(opening["videoDirectoryName"]))
    if video_root is None:
        raise FileNotFoundError("Fallout 3 opening Video directory is absent")
    video_names = [
        str(opening["introVideo"]),
        *(
            str(dict(row)["transitionVideo"])
            for row in opening["quests"]
            if "transitionVideo" in dict(row)
        ),
    ]
    videos = [_file_row(find_case_insensitive_file(video_root, name)) for name in video_names]
    runtime_intro_video = _prepare_runtime_video(
        Path(str(videos[0]["source"])),
        profile_root,
        configuration,
    )

    source_rows = [
        _file_row(master, role="master"),
        _file_row(default_ini, role="defaultIni"),
        *archives,
        main_menu_music,
        *videos,
    ]
    identity_payload = json.dumps(
        [(row["file"], row["sha256"]) for row in source_rows],
        separators=(",", ":"),
    ).encode("utf-8")
    profile_id = hashlib.sha256(identity_payload).hexdigest()[:PROFILE_ID_HEX_CHARACTERS]
    registrar_path = Path(sys.executable) if getattr(sys, "frozen", False) else Path(__file__)
    opening_slice_compiler_path = (
        Path(sys.executable)
        if getattr(sys, "frozen", False)
        else Path(compile_opening_slice.__code__.co_filename)
    )
    opening_slice_result = compile_opening_slice(
        resolved_data_root,
        profile_root,
        default_opening_slice_recipe_path(),
    )
    opening_slice = opening_slice_result["manifest"]
    manifest = {
        "schema": PROFILE_SCHEMA,
        "status": PROFILE_STATUS,
        "campaign": recipe["campaign"],
        "profileId": profile_id,
        "recipe": {"id": recipe["id"], "sha256": file_sha256(recipe_path)},
        "registrar": {
            "name": "OpenNV Fallout 3 owned-profile registrar v1",
            "sha256": file_sha256(registrar_path),
            "openingSliceCompilerSha256": file_sha256(opening_slice_compiler_path),
            "runtimeConfiguration": configuration.manifest(),
        },
        "install": {
            "root": str(install_root),
            "dataRoot": str(resolved_data_root),
            "master": source_rows[0],
            "defaultIni": source_rows[1],
            "archives": archives,
            "pluginStack": [
                {
                    "file": context.name,
                    "bytes": context.bytes,
                    "sha256": context.sha256,
                    "masters": list(context.masters),
                }
                for context in plugin_stack
            ],
            "dlc": dlc_rows,
        },
        "mainMenu": {
            "members": menu_members,
            "textures": menu_textures,
            "music": main_menu_music,
            "iniSettings": ini_rows,
        },
        "opening": {
            "introVideo": {**videos[0], "runtime": runtime_intro_video},
            "transitionVideos": videos[1:],
            "quests": quests,
            "characterSelection": character_selection,
            "birthSlice": {
                "schema": opening_slice["schema"],
                "output": opening_slice_result["output"],
                "sha256": opening_slice_result["outputSha256"],
                "cellFormId": opening_slice["cell"]["formId"],
                "playerSpawnReferenceFormId": opening_slice["startGraph"][
                    "playerSpawn"
                ]["formId"],
                "doctorActorReferenceFormId": opening_slice["doctorActor"][
                    "reference"
                ]["formId"],
            },
        },
        "capabilities": {
            "profileSelectable": True,
            "mainMenuInputsResolved": True,
            "mainMenuRuntimeReady": True,
            "introVideoRuntimeReady": True,
            "openingQuestInventoryResolved": True,
            "characterSelectionContractResolved": True,
            "cg00SexAndNameRuntimeReady": True,
            "cg00AppearanceRuntimeReady": True,
            "cg00Section4PackageRuntimeReady": True,
            "vault101BirthGraphCompiled": True,
            "runtimeBootReady": True,
        },
        "blockers": [
            "fo3-cg00-stage-65-parent-race-face-runtime-not-implemented",
            "fo3-opening-command-interpreter-after-cg00-not-implemented",
            "fo3-vault101-godot-scene-not-compiled",
        ],
    }
    output = profile_root.resolve() / "fallout3-profile.json"
    atomic_json(output, manifest)
    return {"output": str(output), "manifest": manifest}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--profile-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        result = prepare_profile(
            args.data_root.resolve(),
            args.profile_root.resolve(),
            args.recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_FO3_PROFILE_ERROR {error}", file=sys.stderr)
        return 2
    manifest = result["manifest"]
    print(
        "OPENNV_FO3_PROFILE "
        + json.dumps(
            {
                "profile": result["output"],
                "profileId": manifest["profileId"],
                "dlc": [row["id"] for row in manifest["install"]["dlc"] if row["available"]],
                "runtimeBootReady": manifest["capabilities"]["runtimeBootReady"],
                "vault101BirthGraphCompiled": manifest["capabilities"][
                    "vault101BirthGraphCompiled"
                ],
                "openingSlice": manifest["opening"]["birthSlice"]["output"],
                "blockers": manifest["blockers"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
